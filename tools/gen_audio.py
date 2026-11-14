#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
REMASTER - audio generator
==========================
Every sound in the game is synthesized by this script (numpy + scipy only).
No samples, no downloads. Deterministic: same script -> same files.

    python gen_audio.py [out_dir] [manifest_path] [preview_dir]

Defaults: out_dir = ../Assets/Resources/Audio , manifest = ./audio_manifest.md
preview_dir (optional) also writes mixdowns for auditioning: chip only, full, and the build-up.

Design idea
-----------
Two worlds, two sound palettes.
  * The OLD PIXEL GAME is a sound chip: pulse waves, a triangle bass, noise drums.
  * The UNFINISHED 3D REMAKE is paper and pencil: soft taps, scribbles, and
    warmer "HD" instruments.
The music is ONE tune in two arrangements that stack. The four chip stems play
from the start; the four HD stems are added one by one as rooms are cleared,
so finishing the game finishes the soundtrack. All eight stems are the same
length (16 bars at 132 BPM) and are pre-balanced: play them all at volume 1.0.

To edit the tune, change the tables in the "MUSIC DATA" section.
"""
import math
import os
import sys
import wave
import zlib

import numpy as np
from scipy import signal

# --------------------------------------------------------------------------
# Globals
# --------------------------------------------------------------------------
SR = 44100
SEED = 1989
BPM = 132.0
BEAT = 60.0 / BPM              # seconds per beat
SPB = SR * BEAT                # samples per beat (float)
BARS = 16
N = int(round(BARS * 4 * SPB))  # loop length in samples (1282909)
TAIL = int(4.0 * SR)           # scratch space for note tails, folded back to the start

NOTE_INDEX = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
C_MAJOR = {0, 2, 4, 5, 7, 9, 11}


def midi(name):
    """'C4' -> 60. Accepts '#' and 'b'."""
    if isinstance(name, (int, float)):
        return float(name)
    letter = name[0].upper()
    i = 1
    acc = 0
    while name[i] in '#b':
        acc += 1 if name[i] == '#' else -1
        i += 1
    return 12 * (int(name[i:]) + 1) + NOTE_INDEX[letter] + acc


def hz(name):
    return 440.0 * 2.0 ** ((midi(name) - 69.0) / 12.0)


def rng(tag):
    """Deterministic random generator per sound."""
    return np.random.default_rng([SEED, zlib.crc32(tag.encode('utf-8'))])


def db(x):
    return 20.0 * math.log10(max(float(x), 1e-12))


def from_db(d):
    return 10.0 ** (d / 20.0)


# --------------------------------------------------------------------------
# Envelopes and small helpers
# --------------------------------------------------------------------------
def ramp_in(x, ms=1.5):
    n = min(len(x), max(2, int(SR * ms / 1000.0)))
    r = np.linspace(0.0, 1.0, n)
    if x.ndim == 2:
        r = r[:, None]
    x[:n] *= r
    return x


def ramp_out(x, ms=6.0):
    n = min(len(x), max(2, int(SR * ms / 1000.0)))
    r = np.linspace(1.0, 0.0, n)
    if x.ndim == 2:
        r = r[:, None]
    x[-n:] *= r
    return x


def adsr(n_on, a=0.004, d=0.06, s=0.8, r=0.03):
    """Attack / exponential decay to sustain / release. Length = n_on + release."""
    na = max(2, int(a * SR))
    nr = max(2, int(r * SR))
    n_on = max(int(n_on), na + 2)
    e = np.empty(n_on + nr)
    e[:na] = np.linspace(0.0, 1.0, na, endpoint=False)
    k = np.arange(n_on - na)
    e[na:n_on] = s + (1.0 - s) * np.exp(-k / max(1.0, d * SR / 3.0))
    e[n_on:] = e[n_on - 1] * np.linspace(1.0, 0.0, nr) ** 1.5
    return e


def expdecay(n, tau, attack_ms=1.5):
    t = np.arange(n) / SR
    e = np.exp(-t / tau)
    return ramp_in(e, attack_ms)


def phase(freq):
    return 2.0 * np.pi * np.cumsum(np.asarray(freq, dtype=float)) / SR


def const(f, n):
    return np.full(int(n), float(f))


def glide(f0, f1, n, curve='exp'):
    u = np.linspace(0.0, 1.0, int(n))
    if curve == 'exp':
        return f0 * (f1 / f0) ** u
    return f0 + (f1 - f0) * u


# --------------------------------------------------------------------------
# Oscillators (band-limited additive synthesis: no aliasing, soft top end)
# --------------------------------------------------------------------------
def osc_pulse(freq, duty=0.25, fcut=9500.0, soft=6000.0, kmax=48):
    """Pulse wave. Harmonics stop at fcut and roll off gently above `soft`."""
    freq = np.asarray(freq, dtype=float)
    ph = phase(freq)
    K = int(max(1, min(kmax, fcut // float(freq.max()))))
    k = np.arange(1, K + 1)[:, None]
    amp = (2.0 / (k * np.pi)) * np.sin(k * np.pi * duty)
    roll = 1.0 / np.sqrt(1.0 + (k * float(freq.mean()) / soft) ** 4)
    x = np.sum(amp * roll * np.cos(k * ph[None, :]), axis=0)
    return x / (1.0 - duty)


def osc_tri(freq, drive=0.0, fcut=6000.0, kmax=14):
    """Triangle wave. `drive` adds a little tanh grit so it reads on small speakers."""
    freq = np.asarray(freq, dtype=float)
    ph = phase(freq)
    J = int(max(1, min(kmax, (fcut / float(freq.max()) + 1) // 2)))
    j = np.arange(J)[:, None]
    k = 2 * j + 1
    amp = (8.0 / np.pi ** 2) * ((-1.0) ** j) / (k ** 2)
    x = np.sum(amp * np.sin(k * ph[None, :]), axis=0)
    if drive > 0:
        x = np.tanh(drive * x) / np.tanh(drive)
    return x


def osc_saw(freq, fc=1200.0, fcut=4000.0, kmax=40, ph0=0.0):
    """Sawtooth with a built-in 2-pole style lowpass at fc (shaped harmonics)."""
    freq = np.asarray(freq, dtype=float)
    ph = phase(freq) + ph0
    K = int(max(1, min(kmax, fcut // float(freq.max()))))
    k = np.arange(1, K + 1)[:, None]
    amp = (2.0 / np.pi) * ((-1.0) ** (k + 1)) / k
    roll = 1.0 / np.sqrt(1.0 + (k * float(freq.mean()) / fc) ** 4)
    return np.sum(amp * roll * np.sin(k * ph[None, :]), axis=0)


def osc_sine(freq):
    return np.sin(phase(freq))


def fm_bell(f, dur, tau=0.33, bright=1.0):
    """Soft FM bell / pluck: bright for an instant, then nearly a sine."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    idx_peak = 2.0 * bright * min(1.0, 800.0 / f)     # less FM up high: no harshness
    idx = idx_peak * np.exp(-t / 0.07) + 0.15 * min(1.0, 800.0 / f)
    x = np.sin(2 * np.pi * f * t + idx * np.sin(2 * np.pi * 2.0 * f * t))
    if 3.0 * f < 9000.0:
        x += 0.16 * np.sin(2 * np.pi * 3.0 * f * t) * np.exp(-t / 0.05)
    x *= np.exp(-t / tau)
    ramp_in(x, 2.0)
    ramp_out(x, 25.0)
    return x


# --------------------------------------------------------------------------
# Noise and filters
# --------------------------------------------------------------------------
def white(n, r):
    return r.standard_normal(int(n))


def sh_noise(n, rate, r):
    """Sample-and-hold noise: the lo-fi hiss of a sound chip's noise channel."""
    n = int(n)
    idx = np.floor(np.arange(n) * (rate / SR)).astype(int)
    vals = r.uniform(-1.0, 1.0, int(idx.max()) + 2)
    return vals[idx]


def _sos(kind, f, order=2):
    return signal.butter(order, f, btype=kind, fs=SR, output='sos')


def lp(x, f, order=2):
    return signal.sosfilt(_sos('low', f, order), x, axis=0)


def hp(x, f, order=2):
    return signal.sosfilt(_sos('high', f, order), x, axis=0)


def bp(x, lo, hi, order=2):
    return signal.sosfilt(_sos('band', [lo, hi], order), x, axis=0)


def svf_band(x, fc, q=1.2):
    """Time-varying band-pass (zero-delay-feedback state variable filter)."""
    n = len(x)
    fc = np.clip(np.broadcast_to(np.asarray(fc, dtype=float), (n,)), 30.0, 0.45 * SR)
    g = np.tan(np.pi * fc / SR)
    k = 1.0 / q
    a1 = 1.0 / (1.0 + g * (g + k))
    a2 = g * a1
    a3 = g * a2
    out = np.empty(n)
    ic1 = 0.0
    ic2 = 0.0
    xl = x.tolist()
    a1l, a2l, a3l = a1.tolist(), a2.tolist(), a3.tolist()
    for i in range(n):
        v3 = xl[i] - ic2
        v1 = a1l[i] * ic1 + a2l[i] * v3
        v2 = ic2 + a2l[i] * ic1 + a3l[i] * v3
        ic1 = 2.0 * v1 - ic1
        ic2 = 2.0 * v2 - ic2
        out[i] = v1 * k          # unity gain at the centre frequency
    return out


def norm_peak(x, peak_db):
    p = float(np.max(np.abs(x)))
    if p <= 0:
        return x
    return x * (from_db(peak_db) / p)


def finish(x, in_ms=1.5, out_ms=8.0, trim=True):
    """Common SFX finishing: remove DC, guard both ends, peak = 1.0.
    The final level is set in main() from the SOUNDS loudness table."""
    x = np.asarray(x, dtype=float)
    if trim:                               # drop dead air at the end
        env = np.abs(x) if x.ndim == 1 else np.abs(x).max(axis=1)
        live = np.nonzero(env > 0.004 * env.max())[0]
        x = x[:min(len(x), int(live[-1]) + int(0.012 * SR))]
    x = x - np.mean(x, axis=0)
    ramp_in(x, in_ms)
    ramp_out(x, out_ms)
    w = np.hanning(len(x))                 # take out the last bit of DC with a window
    w = w / w.mean()                       # that is zero at both ends
    x = x - (np.mean(x, axis=0) * (w if x.ndim == 1 else w[:, None]))
    return norm_peak(x, 0.0)


def soft_ceiling(x, ceiling):
    """Round off peaks above `ceiling` (linear); quiet material is left alone."""
    c = ceiling * 1.15
    return c * np.tanh(x / c)


_AW = None


def a_weight(x):
    """A-weighting filter (how loud things sound to an ear, roughly)."""
    global _AW
    if _AW is None:
        f1, f2, f3, f4 = 20.598997, 107.65265, 737.86223, 12194.217
        nums = [(2 * np.pi * f4) ** 2 * 10 ** (1.9997 / 20.0), 0, 0, 0, 0]
        dens = np.polymul([1, 4 * np.pi * f4, (2 * np.pi * f4) ** 2],
                          [1, 4 * np.pi * f1, (2 * np.pi * f1) ** 2])
        dens = np.polymul(np.polymul(dens, [1, 2 * np.pi * f3]), [1, 2 * np.pi * f2])
        _AW = signal.bilinear(nums, dens, SR)
    return signal.lfilter(_AW[0], _AW[1], x, axis=0)


def loudness_db(x, win=0.125):
    """A-weighted level of the loudest `win` seconds (125 ms = the "fast" setting of a
    sound level meter). A sound shorter than `win` is averaged over `win`, because very
    short sounds are heard as quieter."""
    y = a_weight(np.asarray(x, dtype=float))
    p = y ** 2 if y.ndim == 1 else np.mean(y ** 2, axis=1)
    w = int(win * SR)
    if len(p) <= w:
        e = float(p.sum()) / w
    else:
        c = np.cumsum(np.concatenate([[0.0], p]))
        e = float((c[w:] - c[:-w]).max()) / w
    return 10.0 * math.log10(max(e, 1e-20))


def integrated_db(x):
    """A-weighted level over the whole file (used for the music)."""
    y = a_weight(np.asarray(x, dtype=float))
    return 10.0 * math.log10(max(float(np.mean(y ** 2)), 1e-20))


# --------------------------------------------------------------------------
# Loop buffer (notes may ring past the end; the overflow wraps to the start)
# --------------------------------------------------------------------------
def pan_gains(p):
    a = (p + 1.0) * np.pi / 4.0
    return math.cos(a) * math.sqrt(2.0), math.sin(a) * math.sqrt(2.0)   # centre = 1,1


class Loop:
    def __init__(self):
        self.a = np.zeros((N + TAIL, 2))

    def add(self, start, x, gain=1.0, pan=0.0):
        start = int(start)
        n = len(x)
        assert 0 <= start and start + n <= N + TAIL, 'note runs past the scratch tail'
        if x.ndim == 1:
            gl, gr = pan_gains(pan)
            self.a[start:start + n, 0] += x * (gain * gl)
            self.a[start:start + n, 1] += x * (gain * gr)
        else:
            self.a[start:start + n] += x * gain

    def fold(self):
        out = self.a[:N].copy()
        out[:TAIL] += self.a[N:N + TAIL]
        return out


def pos(bar, beat):
    """Sample position of (bar 1..16, beat 0..4)."""
    return int(round(((bar - 1) * 4 + beat) * SPB))


def cconv(x, ir):
    """Circular convolution: a reverb whose tail wraps around the loop point."""
    X = np.fft.rfft(x, axis=0)
    H = np.fft.rfft(ir, n=len(x), axis=0)
    return np.fft.irfft(X * H, n=len(x), axis=0)


# ==========================================================================
# MUSIC DATA  (edit here)  - key of C major, 132 BPM, 16 bars: A = 1-8, B = 9-16
# ==========================================================================
CHORDS = ['C', 'G', 'Am', 'F',   'C', 'G', 'F', 'G',      # A
          'F', 'G', 'Em', 'Am',  'F', 'G', 'C', 'C']      # B

# Lead melody: bar -> [(beat, length_in_beats, note)]
# Hook = bars 1-2 (rhythm: quarter, two eighths, quarter, quarter).
MELODY = {
    1:  [(0, 1, 'G5'), (1, .5, 'E5'), (1.5, .5, 'F5'), (2, 1, 'G5'), (3, 1, 'C6')],
    2:  [(0, 1, 'B5'), (1, .5, 'G5'), (1.5, .5, 'A5'), (2, 1, 'B5'), (3, 1, 'D6')],
    3:  [(0, 1.5, 'C6'), (1.5, .5, 'B5'), (2, 1, 'A5'), (3, 1, 'E5')],
    4:  [(0, 1, 'F5'), (1, .5, 'A5'), (1.5, .5, 'G5'), (2, 2, 'F5')],
    5:  [(0, 1, 'G5'), (1, .5, 'E5'), (1.5, .5, 'F5'), (2, 1, 'G5'), (3, 1, 'C6')],
    6:  [(0, 1, 'B5'), (1, .5, 'G5'), (1.5, .5, 'A5'), (2, 1, 'B5'), (3, 1, 'G5')],
    7:  [(0, 1.5, 'A5'), (1.5, .5, 'G5'), (2, 1, 'F5'), (3, 1, 'A5')],
    8:  [(0, 1, 'G5'), (1, .5, 'A5'), (1.5, .5, 'B5'), (2, 2, 'D6')],
    9:  [(0, 1, 'C6'), (1, .5, 'C6'), (1.5, .5, 'D6'), (2, 1, 'C6'), (3, 1, 'A5')],
    10: [(0, 1, 'D6'), (1, .5, 'D6'), (1.5, .5, 'E6'), (2, 1, 'D6'), (3, 1, 'B5')],
    11: [(0, 1.5, 'E6'), (1.5, .5, 'D6'), (2, 1, 'B5'), (3, 1, 'G5')],
    12: [(0, 1, 'A5'), (1, .5, 'C6'), (1.5, .5, 'B5'), (2, 2, 'A5')],
    13: [(0, 1, 'C6'), (1, .5, 'C6'), (1.5, .5, 'D6'), (2, 1, 'C6'), (3, 1, 'A5')],
    14: [(0, 1, 'B5'), (1, .5, 'D6'), (1.5, .5, 'E6'), (2, 1, 'D6'), (3, 1, 'B5')],
    15: [(0, 1, 'C6'), (1, .5, 'G5'), (1.5, .5, 'E5'), (2, 1, 'G5'), (3, 1, 'C6')],
    16: [(0, 2, 'C6'), (3, .5, 'E5'), (3.5, .5, 'F5')],     # pickup back into bar 1
}

# HD bell: A section = soft chord-tone pings + answering fills while the lead holds;
# B section = doubles the lead (same notes) so the tune "lifts".
PLUCK = {
    1:  [(0, 2, 'E6'), (2, 2, 'G6')],
    2:  [(0, 2, 'D6'), (2, 2, 'G6')],
    3:  [(0, 2, 'E6'), (2, 2, 'C6')],
    4:  [(0, 2, 'C6'), (2, .5, 'A5'), (2.5, .5, 'C6'), (3, .5, 'F6'), (3.5, .5, 'C6')],
    5:  [(0, 2, 'E6'), (2, 2, 'G6')],
    6:  [(0, 2, 'D6'), (2, 2, 'G6')],
    7:  [(0, 2, 'C6'), (2, 2, 'F6')],
    8:  [(0, 2, 'B5'), (2, .5, 'G5'), (2.5, .5, 'B5'), (3, .5, 'D6'), (3.5, .5, 'G6')],
    9:  'LEAD', 10: 'LEAD', 11: 'LEAD', 12: 'LEAD', 13: 'LEAD', 14: 'LEAD', 15: 'LEAD',
    16: [(0, 1, 'C6'), (1, .5, 'E6'), (1.5, .5, 'G6'), (2, 1, 'C7')],
}

# Arpeggio voicings (low -> high) and the 16-step order they are played in.
ARP = {
    'C':  ['C4', 'E4', 'G4', 'C5'],
    'G':  ['B3', 'D4', 'G4', 'B4'],
    'Am': ['A3', 'C4', 'E4', 'A4'],
    'F':  ['C4', 'F4', 'A4', 'C5'],
    'Em': ['B3', 'E4', 'G4', 'B4'],
}
ARP_ORDER = [0, 1, 2, 3,  2, 1, 2, 3,  0, 1, 2, 3,  2, 3, 2, 1]

# Bass: chord -> (root, fifth, octave). One bar = 8 eighth-note slots.
BASS = {
    'C':  ('C3', 'G3', 'C4'),
    'G':  ('G2', 'D3', 'G3'),
    'Am': ('A2', 'E3', 'A3'),
    'F':  ('F2', 'C3', 'F3'),
    'Em': ('E2', 'B2', 'E3'),
}
BASS_SLOTS = ['R', None, 'R', 'O', 'F', None, 'F', 'O']     # bouncy: boom . boom bip
# HD bass only: walking notes that lead into the next bar's root. bar -> {slot: note}
BASS_PASSING = {
    1:  {7: 'A2'},  3:  {7: 'G2'},  4:  {7: 'G2'},  5:  {7: 'A2'},
    10: {7: 'F2'},  11: {7: 'G2'},  12: {7: 'G2'},  14: {6: 'A2', 7: 'B2'},
    16: {7: 'G2'},
}

# Pad voicings (close position, little movement between chords).
PAD = {
    'C':  ['G3', 'C4', 'E4'],
    'G':  ['G3', 'B3', 'D4'],
    'Am': ['A3', 'C4', 'E4'],
    'F':  ['A3', 'C4', 'F4'],
    'Em': ['G3', 'B3', 'E4'],
}

FILL_BARS = (8, 16)          # snare fill in the last beat
# Balanced mix at volume 1.0.
#   ('rms', dBFS)          plain RMS level (the lead is the anchor; basses sit beside it)
#   ('a_int_rel', dB)      A-weighted level over the whole loop, relative to the lead
#   ('a_max_rel', dB)      A-weighted loudest 125 ms, relative to the lead (drums)
# A global trim is then applied to every stem if the 8-stem sum would pass the ceiling.
STEM_LEVEL = {
    'mus_chip_lead':  ('rms', -19.0),
    'mus_chip_arp':   ('a_int_rel', -10.0),
    'mus_chip_bass':  ('rms', -21.0),
    'mus_chip_drums': ('a_max_rel', -6.5),
    'mus_hd_drums':   ('a_max_rel', -6.5),
    'mus_hd_bass':    ('rms', -24.5),
    'mus_hd_pad':     ('a_int_rel', -10.0),
    'mus_hd_pluck':   ('a_int_rel', -8.5),
}
DRUM_PEAK_DB = -8.0          # drum stems are soft-rounded above this before the trim
MIX_CEILING_DB = -1.1        # all eight stems summed must stay under this


def check_tables_in_key():
    names = []
    for notes in MELODY.values():
        names += [n for _, _, n in notes]
    for notes in PLUCK.values():
        if notes != 'LEAD':
            names += [n for _, _, n in notes]
    for v in list(ARP.values()) + list(PAD.values()) + [list(b) for b in BASS.values()]:
        names += list(v)
    for d in BASS_PASSING.values():
        names += list(d.values())
    for n in names:
        assert int(midi(n)) % 12 in C_MAJOR, 'note outside C major: %s' % n


# ==========================================================================
# MUSIC STEMS
# ==========================================================================
def stem_chip_lead():
    """25% pulse lead, light vibrato on long notes, one quiet echo."""
    b = Loop()
    for bar, notes in MELODY.items():
        for beat, ln, name in notes:
            f0 = hz(name)
            e = adsr(ln * SPB * 0.86, a=0.004, d=0.09, s=0.80, r=0.035)
            n = len(e)
            t = np.arange(n) / SR
            f = const(f0, n)
            if ln >= 1.5:
                vr = np.clip((t - 0.20) / 0.22, 0.0, 1.0)
                f = f0 * (1.0 + 0.0065 * vr * np.sin(2 * np.pi * 6.0 * t))
            b.add(pos(bar, beat), osc_pulse(f, 0.25) * e)
    out = b.fold()
    out += 0.17 * np.roll(out, int(round(0.75 * SPB)), axis=0)     # dotted-eighth echo
    return out


def stem_chip_arp():
    """12.5% pulse, 16th-note arpeggios of each bar's chord."""
    b = Loop()
    for bar in range(1, BARS + 1):
        notes = ARP[CHORDS[bar - 1]]
        for step in range(16):
            f0 = hz(notes[ARP_ORDER[step]])
            e = adsr(0.25 * SPB * 0.72, a=0.002, d=0.035, s=0.55, r=0.012)
            x = osc_pulse(const(f0, len(e)), 0.125) * e
            b.add(pos(bar, step * 0.25), x, gain=1.0 if step % 4 == 0 else 0.8)
    return b.fold()


def bass_line(bar, hd):
    root, fifth, octave = BASS[CHORDS[bar - 1]]
    pick = {'R': root, 'F': fifth, 'O': octave}
    line = [(i, pick[s]) for i, s in enumerate(BASS_SLOTS) if s]
    if hd and bar in BASS_PASSING:
        slots = dict(line)
        slots.update(BASS_PASSING[bar])
        line = sorted(slots.items())
    return line


def stem_chip_bass():
    """Triangle bass: root / fifth / octave bounce."""
    b = Loop()
    for bar in range(1, BARS + 1):
        for slot, name in bass_line(bar, hd=False):
            e = adsr(0.5 * SPB * 0.80, a=0.003, d=0.03, s=0.92, r=0.02)
            b.add(pos(bar, slot * 0.5), osc_tri(const(hz(name), len(e)), drive=1.5) * e)
    return b.fold()


def drum_hits(bar):
    """16 steps per bar. Returns {'kick': [...], 'snare': [(step, vel)], 'hat': [...]}"""
    fill = bar in FILL_BARS
    kick = [0, 8] + ([10] if bar % 2 == 0 and not fill else [])
    if fill:
        snare = [(4, 1.0), (12, 0.6), (13, 0.7), (14, 0.85), (15, 1.0)]
        hats = [s for s in range(0, 12, 2)]
    else:
        snare = [(4, 1.0), (12, 1.0)]
        hats = [s for s in range(0, 16, 2)]
    return kick, snare, hats


def chip_kick():
    n = int(0.10 * SR)
    t = np.arange(n) / SR
    f = 62.0 + 170.0 * np.exp(-t / 0.016)
    x = (2.0 / np.pi) * np.arcsin(np.sin(phase(f))) * np.exp(-t / 0.032)
    return ramp_out(ramp_in(x, 0.6), 12.0)


def chip_snare(r):
    n = int(0.14 * SR)
    t = np.arange(n) / SR
    x = lp(sh_noise(n, 8200.0, r), 9000.0) * np.exp(-t / 0.042)
    return ramp_out(ramp_in(x, 0.8), 10.0)


def chip_hat(r, accent):
    n = int(0.045 * SR)
    t = np.arange(n) / SR
    x = hp(sh_noise(n + 128, 20000.0, r), 5200.0)[128:] * np.exp(-t / (0.016 if accent else 0.009))
    return ramp_out(ramp_in(x, 0.5), 5.0)


def stem_chip_drums():
    """Noise hats + noise snare + triangle-sweep kick."""
    b = Loop()
    r = rng('chip_drums')
    for bar in range(1, BARS + 1):
        kick, snare, hats = drum_hits(bar)
        for s in kick:
            b.add(pos(bar, s * 0.25), chip_kick(), gain=0.55)
        for s, v in snare:
            b.add(pos(bar, s * 0.25), chip_snare(r), gain=0.80 * v)
        for s in hats:
            accent = (s % 4 == 2)                     # off-beat eighths are the bright ones
            b.add(pos(bar, s * 0.25), chip_hat(r, accent), gain=0.38 if accent else 0.26)
    return b.fold()


def hd_kick(r):
    n = int(0.24 * SR)
    t = np.arange(n) / SR
    x = np.sin(phase(52.0 + 130.0 * np.exp(-t / 0.022))) * np.exp(-t / 0.075)
    nc = int(0.004 * SR)
    x[:nc] += 0.30 * hp(white(nc, r), 2500.0) * np.exp(-np.arange(nc) / (0.001 * SR))
    x = np.tanh(1.6 * x) / np.tanh(1.6)
    return ramp_out(ramp_in(x, 0.5), 20.0)


def hd_snare(r):
    n = int(0.26 * SR)
    t = np.arange(n) / SR
    tone = np.sin(phase(178.0 + 60.0 * np.exp(-t / 0.012))) * np.exp(-t / 0.045)
    nz = bp(white(n, r), 1100.0, 7500.0) * np.exp(-t / 0.07)
    return ramp_out(ramp_in(0.55 * tone + 0.50 * nz, 0.8), 15.0)


def hd_hat(r):
    n = int(0.05 * SR)
    t = np.arange(n) / SR
    x = lp(hp(white(n + 256, r), 7000.0, 4), 12000.0)[256:] * np.exp(-t / 0.012)
    return ramp_out(ramp_in(x, 0.5), 5.0)


def hd_clap(r):
    n = int(0.28 * SR)
    t = np.arange(n) / SR
    out = np.zeros((n, 2))
    for ch in range(2):                               # different noise per side = width
        nz = bp(white(n, r), 900.0, 3400.0)
        env = np.zeros(n)
        for t0, g in ((0.0, 0.7), (0.011, 0.7), (0.022, 0.8)):
            m = t >= t0
            env[m] += g * np.exp(-(t[m] - t0) / 0.004)
        m = t >= 0.033
        env[m] += np.exp(-(t[m] - 0.033) / 0.055)
        out[:, ch] = nz * env
    return ramp_out(ramp_in(out, 0.5), 15.0)


def hd_crash(r):
    n = int(1.8 * SR)
    t = np.arange(n) / SR
    out = np.zeros((n, 2))
    for ch in range(2):
        out[:, ch] = bp(white(n, r), 2500.0, 9000.0) * np.exp(-t / 0.42)
    return ramp_out(ramp_in(out, 2.0), 60.0)


def room_ir(r, length=0.26, tau=0.045):
    n = int(length * SR)
    t = np.arange(n) / SR
    ir = np.zeros((n, 2))
    for ch in range(2):
        ir[:, ch] = bp(white(n, r), 400.0, 6000.0) * np.exp(-t / tau)
    ir[: int(0.004 * SR)] = 0.0                       # short pre-delay
    return ir / np.sqrt(np.sum(ir ** 2, axis=0, keepdims=True))


def stem_hd_drums():
    """Fuller kit on the same pattern as the chip drums, so they reinforce each other."""
    dry = Loop()
    send = Loop()                                     # snare + clap go to a short room
    r = rng('hd_drums')
    for bar in range(1, BARS + 1):
        kick, snare, _ = drum_hits(bar)
        fill = bar in FILL_BARS
        for s in kick:
            dry.add(pos(bar, s * 0.25), hd_kick(r), gain=0.58)
        for s, v in snare:
            x = hd_snare(r)
            dry.add(pos(bar, s * 0.25), x, gain=1.0 * v)
            send.add(pos(bar, s * 0.25), x, gain=1.0 * v)
        for s in range(16):
            if fill and s >= 12:
                continue
            g = 0.24 if s % 2 == 0 else 0.10          # eighths + ghosted sixteenths
            dry.add(pos(bar, s * 0.25), hd_hat(r), gain=g, pan=0.25)
        if bar >= 9 and not fill:                     # claps on beat 4 through the B section
            c = hd_clap(r)
            dry.add(pos(bar, 3.0), c, gain=0.50)
            send.add(pos(bar, 3.0), c, gain=0.50)
        if bar in (1, 9):
            dry.add(pos(bar, 0.0), hd_crash(r), gain=0.18)
    out = dry.fold()
    out += 0.20 * cconv(send.fold(), room_ir(rng('room')))
    return out


def stem_hd_bass():
    """Round bass: sine + soft saw, with a few walking notes."""
    b = Loop()
    for bar in range(1, BARS + 1):
        for slot, name in bass_line(bar, hd=True):
            e = adsr(0.5 * SPB * 0.88, a=0.006, d=0.10, s=0.66, r=0.05)
            f = const(hz(name), len(e))
            x = 0.50 * osc_sine(f) + 0.55 * osc_saw(f, fc=700.0, fcut=3000.0)
            b.add(pos(bar, slot * 0.5), x * e)
    return b.fold()


def stem_hd_pad():
    """Warm stereo pad: three detuned saws per note through a gentle lowpass."""
    b = Loop()
    r = rng('pad')
    na = int(0.30 * SR)
    nr = int(0.55 * SR)
    n_on = int(4 * SPB)
    env = np.ones(n_on + nr)
    env[:na] = 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, na))
    env[n_on:] = 0.5 + 0.5 * np.cos(np.linspace(0.0, np.pi, nr))
    for bar in range(1, BARS + 1):
        for name in PAD[CHORDS[bar - 1]]:
            for cents, pan in ((-9.0, -0.7), (0.0, 0.0), (9.0, 0.7)):
                f = const(hz(name) * 2.0 ** (cents / 1200.0), len(env))
                x = osc_saw(f, fc=1250.0, fcut=3600.0, ph0=r.uniform(0, 2 * np.pi))
                b.add(pos(bar, 0), x * env, pan=pan)
    return b.fold()


def stem_hd_pluck():
    """Soft FM bell: pings and fills in A, doubles the lead in B. Ping-pong echo."""
    b = Loop()
    for bar, notes in PLUCK.items():
        if notes == 'LEAD':
            notes = MELODY[bar]
        for beat, ln, name in notes:
            dur = min(1.3, max(0.45, ln * BEAT * 1.6))
            b.add(pos(bar, beat), fm_bell(hz(name), dur, tau=0.30))
    dry = b.fold()
    mono = dry[:, 0].copy()
    d = int(round(0.75 * SPB))
    out = dry.copy()
    out[:, 1] += 0.42 * np.roll(mono, d)              # right
    out[:, 0] += 0.24 * np.roll(mono, 2 * d)          # left
    out[:, 1] += 0.12 * np.roll(mono, 3 * d)          # right
    return out


STEMS = [
    ('mus_chip_lead',  stem_chip_lead,  'Chip: pulse-wave lead melody (the tune).'),
    ('mus_chip_arp',   stem_chip_arp,   'Chip: fast pulse arpeggios of the chords.'),
    ('mus_chip_bass',  stem_chip_bass,  'Chip: bouncy triangle bass.'),
    ('mus_chip_drums', stem_chip_drums, 'Chip: noise hats + snare, triangle kick.'),
    ('mus_hd_drums',   stem_hd_drums,   'HD remaster layer: fuller kit with claps and room.'),
    ('mus_hd_bass',    stem_hd_bass,    'HD remaster layer: round bass with walking notes.'),
    ('mus_hd_pad',     stem_hd_pad,     'HD remaster layer: warm stereo pad of the chords.'),
    ('mus_hd_pluck',   stem_hd_pluck,   'HD remaster layer: soft bell, pings then doubles the lead.'),
]


# ==========================================================================
# JINGLES
# ==========================================================================
def seq(notes, duty=0.25, gate=0.9, a=0.003, d=0.05, s=0.75, r=0.03, tail=0.0):
    """Render [(start_s, dur_s, note, gain)] with the pulse oscillator. Mono."""
    end = max(st + du for st, du, _, _ in notes) + r + tail + 0.01
    out = np.zeros(int(end * SR) + 8)
    for st, du, name, g in notes:
        e = adsr(du * SR * gate, a=a, d=d, s=s, r=r)
        x = osc_pulse(const(hz(name), len(e)), duty) * e * g
        i = int(st * SR)
        out[i:i + len(x)] += x
    return out


def place(out, x, t, gain=1.0):
    i = int(t * SR)
    n = min(len(x), len(out) - i)
    if n > 0:
        out[i:i + n] += x[:n] * gain
    return out


def jingle_boot():
    """Console power-on: a quick sweep, four rising notes, a bright chord. Pixel window pops up."""
    out = np.zeros(int(1.22 * SR))
    n = int(0.16 * SR)
    place(out, osc_tri(glide(110.0, 660.0, n)) * ramp_out(ramp_in(np.ones(n), 4), 30), 0.0, 0.5)
    place(out, seq([(0.16, 0.10, 'C5', 0.8), (0.26, 0.10, 'G5', 0.8),
                    (0.36, 0.10, 'C6', 0.85), (0.46, 0.10, 'E6', 0.9)]), 0.0)
    for name, duty, g in (('G6', 0.125, 0.75), ('E6', 0.25, 0.40), ('C6', 0.25, 0.40)):
        m = int(0.64 * SR)
        place(out, osc_pulse(const(hz(name), m), duty) * expdecay(m, 0.20, 3.0), 0.56, g)
    return finish(out, out_ms=30.0)


def jingle_room():
    """Room clear: four rising notes of the C chord, the last one rings."""
    out = np.zeros(int(1.0 * SR))
    place(out, seq([(0.00, 0.10, 'G5', 0.8), (0.10, 0.10, 'C6', 0.85), (0.20, 0.10, 'E6', 0.9)]), 0.0)
    m = int(0.68 * SR)
    for name, duty, g in (('G6', 0.25, 0.85), ('E6', 0.125, 0.35), ('C6', 0.125, 0.35)):
        place(out, osc_pulse(const(hz(name), m), duty) * expdecay(m, 0.26, 3.0), 0.30, g)
    place(out, osc_tri(const(hz('C4'), m), drive=1.4) * expdecay(m, 0.22, 3.0), 0.30, 0.6)
    return finish(out, out_ms=40.0)


def jingle_win():
    """Final fanfare: the hook, a turn through G, then a held C chord with both palettes. Stereo."""
    q = 0.28                                           # one beat of the fanfare
    total = int(3.5 * SR)
    mono = np.zeros(total)
    lead = [(0.0, q, 'G5', 1.0), (q, q / 2, 'E5', 1.0), (1.5 * q, q / 2, 'F5', 1.0),
            (2 * q, q, 'G5', 1.0), (3 * q, q, 'C6', 1.0),
            (4 * q, q / 2, 'D6', 1.0), (4.5 * q, q / 2, 'E6', 1.0),
            (5 * q, q / 2, 'D6', 1.0), (5.5 * q, q / 2, 'B5', 1.0)]
    place(mono, seq(lead, duty=0.25), 0.0, 0.9)
    t_end = 6 * q                                      # 1.68 s: the final chord lands here
    hold = int(1.62 * SR)
    t = np.arange(hold) / SR
    vib = 1.0 + 0.006 * np.clip((t - 0.25) / 0.25, 0, 1) * np.sin(2 * np.pi * 6.0 * t)
    env = ramp_out(ramp_in(np.ones(hold), 4.0), 500.0)
    place(mono, osc_pulse(hz('C6') * vib, 0.25) * env, t_end, 0.9)
    place(mono, osc_pulse(const(hz('E5'), hold), 0.125) * env, t_end, 0.32)
    place(mono, osc_pulse(const(hz('G5'), hold), 0.125) * env, t_end, 0.32)
    # triangle bass: C, then G under the turn, then a held C
    bass = [(0.0, 'C3'), (q, 'C4'), (2 * q, 'G3'), (3 * q, 'C4'), (4 * q, 'G2'), (5 * q, 'G3')]
    for st, name in bass:
        e = adsr(q * SR * 0.8, a=0.003, d=0.03, s=0.9, r=0.02)
        place(mono, osc_tri(const(hz(name), len(e)), drive=1.5) * e, st, 0.8)
    place(mono, osc_tri(const(hz('C3'), hold), drive=1.5) * env, t_end, 0.8)
    # chip snare run into the chord, then a soft noise splash
    r = rng('win')
    for i in range(4):
        place(mono, chip_snare(r), 5 * q + i * q / 4, 0.35 + 0.1 * i)
    place(mono, chip_kick(), t_end, 0.9)
    out = np.stack([mono, mono], axis=1)
    cr = hd_crash(r)
    n = min(len(cr), total - int(t_end * SR))
    out[int(t_end * SR):int(t_end * SR) + n] += 0.20 * cr[:n]
    # HD bells sparkle up the C chord during the hold, alternating sides
    for i, name in enumerate(['C6', 'E6', 'G6', 'C7', 'G6', 'E6', 'G6', 'C7']):
        bell = fm_bell(hz(name), 0.9, tau=0.28)
        gl, gr = pan_gains(-0.6 if i % 2 == 0 else 0.6)
        i0 = int((t_end + 0.14 * i) * SR)
        n = min(len(bell), total - i0)
        out[i0:i0 + n, 0] += 0.30 * gl * bell[:n]
        out[i0:i0 + n, 1] += 0.30 * gr * bell[:n]
    return finish(out, out_ms=120.0)


def jingle_gameover():
    """Comic 'oops': a major arpeggio tumbles down, slides, and ends on a silly boing."""
    out = np.zeros(int(1.6 * SR))
    place(out, seq([(0.00, 0.14, 'E5', 0.9), (0.16, 0.14, 'C5', 0.9), (0.32, 0.14, 'G4', 0.9)],
                   duty=0.5), 0.0, 0.7)
    n = int(0.52 * SR)                                 # the slide with a wobble
    t = np.arange(n) / SR
    f = glide(hz('E4'), hz('C4'), n) * (1.0 + 0.035 * np.sin(2 * np.pi * 7.5 * t))
    place(out, osc_pulse(f, 0.5) * ramp_out(ramp_in(np.ones(n), 5.0), 60.0), 0.50, 0.62)
    m = int(0.16 * SR)                                 # the bonk
    place(out, osc_pulse(glide(hz('C3'), hz('C3') * 0.8, m), 0.25) * expdecay(m, 0.05, 2.0), 1.08, 0.85)
    place(out, sh_noise(m, 5000.0, rng('oops')) * expdecay(m, 0.02, 1.0), 1.08, 0.25)
    place(out, sfx_slime_hop(), 1.30, 0.8)             # the boing tag
    return finish(out, out_ms=30.0)


def jingle_hurry():
    """'Hurry up!': three fast arpeggios, each one step higher, then a buzzing top note."""
    notes = []
    t = 0.0
    for chord in (['C5', 'E5', 'G5', 'C6'], ['D5', 'F#5', 'A5', 'D6'], ['E5', 'G#5', 'B5', 'E6']):
        for name in chord:
            notes.append((t, 0.05, name, 0.9))
            t += 0.05
    out = np.zeros(int(0.9 * SR))
    place(out, seq(notes, duty=0.25, gate=0.85, a=0.002, d=0.02, s=0.8, r=0.01), 0.0)
    n = int(0.28 * SR)
    tt = np.arange(n) / SR
    f = hz('E6') * (1.0 + 0.012 * np.sin(2 * np.pi * 15.0 * tt))
    place(out, osc_pulse(f, 0.25) * expdecay(n, 0.12, 2.0), 0.60, 0.9)
    return finish(out, out_ms=25.0)


def jingle_layer():
    """A remaster layer joins: HD bells shimmer up the C chord over a soft pad swell. Stereo."""
    total = int(0.9 * SR)
    out = np.zeros((total, 2))
    for i, (name, g) in enumerate([('C6', 0.9), ('E6', 0.9), ('G6', 0.9), ('C7', 0.8), ('E7', 0.45)]):
        bell = fm_bell(hz(name), 0.62, tau=0.24)
        gl, gr = pan_gains(-0.55 if i % 2 == 0 else 0.55)
        i0 = int(0.07 * i * SR)
        n = min(len(bell), total - i0)
        out[i0:i0 + n, 0] += g * gl * bell[:n]
        out[i0:i0 + n, 1] += g * gr * bell[:n]
    r = rng('layer')
    env = np.sin(np.linspace(0.0, np.pi, total)) ** 1.5
    for name in ('C4', 'E4', 'G4'):
        for cents, ch in ((-8.0, 0), (8.0, 1)):
            f = const(hz(name) * 2.0 ** (cents / 1200.0), total)
            out[:, ch] += 0.16 * osc_saw(f, fc=1300.0, fcut=3600.0, ph0=r.uniform(0, 6.28)) * env
    return finish(out, out_ms=60.0)


# ==========================================================================
# SFX - old pixel game (chip palette)
# ==========================================================================
def sfx_step_chip(note):
    """Footstep on a tile that exists only in the old game: a tiny square blip."""
    n = int(0.060 * SR)
    f = glide(hz(note), hz(note) * 0.93, n, 'lin')
    return finish(osc_pulse(f, 0.5) * expdecay(n, 0.016, 1.0), out_ms=6.0)


def sfx_coin():
    """Coin: a quick three-note sparkle up the C chord."""
    out = np.zeros(int(0.38 * SR))
    place(out, seq([(0.0, 0.045, 'E6', 0.8), (0.045, 0.045, 'G6', 0.85)],
                   duty=0.25, gate=0.95, a=0.002, d=0.02, s=0.9, r=0.004), 0.0)
    n = int(0.29 * SR)
    place(out, osc_pulse(const(hz('C7'), n), 0.25) * expdecay(n, 0.085, 1.5), 0.09, 0.9)
    return finish(out, out_ms=12.0)


def sfx_hurt():
    """Ouch: a wobbly falling square with a puff of noise. Comic, not harsh."""
    n = int(0.30 * SR)
    t = np.arange(n) / SR
    f = glide(480.0, 170.0, n) * (1.0 + 0.06 * np.sin(2 * np.pi * 28.0 * t))
    x = osc_pulse(f, 0.5, soft=4000.0) * ramp_out(ramp_in(np.ones(n), 2.0), 70.0)
    m = int(0.07 * SR)
    x[:m] += 0.45 * sh_noise(m, 6000.0, rng('hurt')) * expdecay(m, 0.02, 1.0)
    return finish(x, out_ms=20.0)


def sfx_slime_hop():
    """Slime hop: a soft low boing (pitch bends up then back)."""
    n = int(0.18 * SR)
    u = np.linspace(0.0, 1.0, n)
    f = 190.0 * 2.0 ** (1.15 * np.sin(np.pi * u) ** 1.3)
    env = np.sin(np.pi * u) ** 0.7
    return finish(osc_tri(f, drive=1.2) * env, out_ms=15.0)


def sfx_wrap():
    """Screen wrap: an arpeggio races up and back down, with a quick echo."""
    names = ['C5', 'E5', 'G5', 'C6', 'E6', 'G6', 'C7', 'G6', 'E6', 'C6', 'G5']
    notes = [(i * 0.030, 0.030, nm, 0.9) for i, nm in enumerate(names)]
    out = np.zeros(int(0.45 * SR))
    dry = seq(notes, duty=0.25, gate=0.92, a=0.002, d=0.02, s=0.85, r=0.006)
    place(out, dry, 0.0)
    place(out, dry, 0.060, 0.33)
    return finish(out, out_ms=25.0)


def sfx_fall():
    """Falling into a pit: a cartoon slide whistle going down."""
    n = int(0.70 * SR)
    t = np.arange(n) / SR
    f = glide(1300.0, 180.0, n) * (1.0 + 0.025 * np.sin(2 * np.pi * 9.0 * t))
    env = ramp_out(ramp_in(np.ones(n), 8.0), 140.0)
    return finish(osc_tri(f, drive=0.8) * env, out_ms=30.0)


def sfx_respawn():
    """Respawn: a little pop and a rising arpeggio."""
    out = np.zeros(int(0.30 * SR))
    m = int(0.04 * SR)
    place(out, osc_sine(glide(200.0, 900.0, m)) * expdecay(m, 0.02, 1.0), 0.0, 0.6)
    place(out, seq([(0.02, 0.045, 'C5', 0.8), (0.065, 0.045, 'G5', 0.85),
                    (0.11, 0.045, 'C6', 0.9)], duty=0.25, gate=0.9, r=0.008), 0.0)
    n = int(0.14 * SR)
    place(out, osc_pulse(const(hz('E6'), n), 0.25) * expdecay(n, 0.05, 1.5), 0.155, 0.9)
    return finish(out, out_ms=15.0)


def sfx_tile_delete():
    """A floor tile deletes itself: a tiny crumble whose grain falls in pitch."""
    n = int(0.12 * SR)
    r = rng('tile_delete')
    rate = glide(14000.0, 1500.0, n)
    ph = np.cumsum(rate) / SR
    idx = np.floor(ph).astype(int)
    vals = r.uniform(-1.0, 1.0, int(idx.max()) + 2)
    return finish(lp(vals[idx], 9000.0) * expdecay(n, 0.035, 1.0), out_ms=10.0)


def sfx_laser_old():
    """The OLD game's laser: a tiny chip 'pew'. You hear it three seconds before the real one."""
    n = int(0.16 * SR)
    f = glide(1500.0, 420.0, n)
    return finish(osc_pulse(f, 0.25) * expdecay(n, 0.05, 1.0), out_ms=10.0)


def sfx_laser_fire():
    """The REMAKE's laser, three seconds late: a fat cartoon zap (falling saw, a square on top, a crackle)."""
    n = int(0.34 * SR)
    t = np.arange(n) / SR
    f = glide(1100.0, 110.0, n) * (1.0 + 0.05 * np.sin(2 * np.pi * 37.0 * t))
    x = osc_saw(f, fc=2600.0) * ramp_out(ramp_in(np.ones(n), 1.5), 120.0)
    x = x + 0.5 * osc_pulse(glide(2200.0, 300.0, n), 0.5, soft=5000.0) * expdecay(n, 0.07, 1.0)
    m = int(0.09 * SR)
    x[:m] += 0.5 * sh_noise(m, 9000.0, rng('laser_fire')) * expdecay(m, 0.03, 1.0)
    return finish(x, out_ms=25.0)


def sfx_flick(note):
    """A flicker plank group switching on: one soft round note. Two pitches for the two groups,
    so the rhythm of the room can be heard as well as seen."""
    n = int(0.11 * SR)
    return finish(osc_tri(const(hz(note), n), drive=0.6) * expdecay(n, 0.035, 1.5), out_ms=10.0)


def sfx_flick_warn():
    """Tick: the flicker planks are about to swap."""
    n = int(0.035 * SR)
    return finish(osc_pulse(const(hz('C7'), n), 0.125) * expdecay(n, 0.009, 0.8), out_ms=4.0)


def sfx_plank_break():
    """A one-use plank snaps behind you: a dry wooden crack and a little falling 'doop'."""
    n = int(0.22 * SR)
    crack = bp(white(n, rng('plank_break')), 700.0, 3800.0) * expdecay(n, 0.018, 0.5)
    m = int(0.16 * SR)
    doop = osc_tri(glide(520.0, 180.0, m), drive=0.8) * expdecay(m, 0.05, 1.0)
    out = np.zeros(n)
    out += crack
    place(out, doop, 0.03, 0.6)
    return finish(out, out_ms=15.0)


def sfx_ui_move():
    n = int(0.050 * SR)
    return finish(osc_pulse(const(hz('G5'), n), 0.25) * expdecay(n, 0.018, 1.0), out_ms=5.0)


def sfx_ui_ok():
    out = np.zeros(int(0.15 * SR))
    place(out, seq([(0.0, 0.05, 'C6', 0.85)], duty=0.25, gate=0.95, r=0.004), 0.0)
    n = int(0.095 * SR)
    place(out, osc_pulse(const(hz('G6'), n), 0.25) * expdecay(n, 0.04, 1.5), 0.055, 0.9)
    return finish(out, out_ms=8.0)


def sfx_ui_pause():
    out = np.zeros(int(0.25 * SR))
    place(out, seq([(0.0, 0.075, 'G5', 0.9)], duty=0.5, gate=0.95, r=0.005), 0.0)
    n = int(0.165 * SR)
    place(out, osc_pulse(const(hz('D5'), n), 0.5) * expdecay(n, 0.08, 2.0), 0.085, 0.9)
    return finish(out, out_ms=15.0)


def sfx_gate():
    """Gate says no: a small metal clank and two low buzzes."""
    n = int(0.25 * SR)
    t = np.arange(n) / SR
    buzz = 0.5 * (osc_pulse(const(147.0, n), 0.25, soft=2500.0) + osc_pulse(const(156.0, n), 0.25, soft=2500.0))
    env = np.zeros(n)
    for a0, a1 in ((0.0, 0.09), (0.12, 0.245)):
        i0, i1 = int(a0 * SR), int(a1 * SR)
        seg = np.ones(i1 - i0)
        ramp_out(ramp_in(seg, 3.0), 12.0)
        env[i0:i1] = seg
    x = buzz * env
    clank = sum(g * np.sin(2 * np.pi * f * t) for f, g in ((620.0, 1.0), (1130.0, 0.6), (1810.0, 0.35)))
    x += 0.35 * clank * np.exp(-t / 0.03)
    return finish(x, out_ms=10.0)


# ==========================================================================
# SFX - unfinished 3D remake (paper and pencil palette)
# ==========================================================================
def sfx_step_paper(i):
    """Footstep on real 3D floor: a soft paper/pencil tap. Four variants."""
    fc, dur, tau = [(1100.0, 0.090, 0.022), (1400.0, 0.100, 0.026),
                    (950.0, 0.110, 0.030), (1250.0, 0.085, 0.020)][i]
    r = rng('step_paper_%d' % i)
    n = int(dur * SR)
    t = np.arange(n) / SR
    body = bp(white(n, r), fc * 0.6, fc * 1.7) * np.exp(-t / tau)
    rustle = bp(white(n, r), 3000.0, 6500.0) * np.exp(-t / (tau * 1.6)) * 0.22
    thud = 0.55 * np.sin(phase(glide(125.0, 80.0, n))) * np.exp(-t / 0.02)
    return finish(body + rustle + thud, in_ms=2.5, out_ms=14.0)


def _stroke(r, dur, centre, level):
    n = int(dur * SR)
    u = np.linspace(0.0, 1.0, n)
    t = np.arange(n) / SR
    x = bp(white(n, r), centre * 0.6, centre * 1.6)
    env = np.sin(np.pi * u) ** 0.6 * (1.0 + 0.3 * np.sin(2 * np.pi * 45.0 * t + r.uniform(0, 6.28)))
    return x * env * level


def sfx_scribble(i):
    """Pencil scribbling: back-and-forth strokes of band-passed noise."""
    strokes = [
        [(0.09, 2600, 0.8), (0.11, 3400, 1.0), (0.08, 2500, 0.7), (0.12, 3300, 1.0), (0.14, 2700, 0.9)],
        [(0.07, 3000, 0.9), (0.08, 2400, 0.7), (0.07, 3300, 1.0), (0.09, 2500, 0.8),
         (0.07, 3200, 0.9), (0.10, 2600, 0.8)],
    ][i]
    r = rng('scribble_%d' % i)
    parts = []
    for dur, centre, level in strokes:
        parts.append(_stroke(r, dur, float(centre), level))
        parts.append(np.zeros(int(r.uniform(0.006, 0.014) * SR)))
    return finish(lp(np.concatenate(parts), 7000.0), in_ms=3.0, out_ms=20.0)


def sfx_ghostwall():
    """Walking through a fake wall: a papery whoosh with a tiny detuned chip zip inside."""
    n = int(0.45 * SR)
    r = rng('ghostwall')
    u = np.linspace(0.0, 1.0, n)
    fc = np.where(u < 0.45, 600.0 * (3200.0 / 600.0) ** (u / 0.45),
                  3200.0 * (900.0 / 3200.0) ** ((u - 0.45) / 0.55))
    x = svf_band(white(n, r), fc, q=1.2) * np.sin(np.pi * u ** 0.6) ** 1.5
    m = int(0.20 * SR)
    f = glide(1400.0, 350.0, m)
    zip_ = 0.5 * (osc_pulse(f * 1.015, 0.25) + osc_pulse(f * 0.985, 0.25))
    zip_ *= ramp_out(ramp_in(np.ones(m), 4.0), 80.0)
    x = x / np.max(np.abs(x))
    place(x, zip_, 0.06, 0.20)
    return finish(x, in_ms=4.0, out_ms=30.0)


def sfx_note():
    """A dev note appears: two pencil scratches, then a small soft bell."""
    r = rng('note')
    out = np.zeros(int(0.55 * SR))
    place(out, lp(_stroke(r, 0.07, 3000.0, 0.9), 7000.0), 0.0, 0.5)
    place(out, lp(_stroke(r, 0.09, 2500.0, 0.9), 7000.0), 0.085, 0.5)
    sc = float(np.max(np.abs(out)))
    out *= 0.45 / sc
    place(out, fm_bell(hz('E6'), 0.35, tau=0.12), 0.19, 0.9)
    return finish(out, in_ms=3.0, out_ms=25.0)


def sfx_page_flip():
    """A page turns: a rising paper swish and a little flap."""
    n = int(0.35 * SR)
    r = rng('page_flip')
    u = np.linspace(0.0, 1.0, n)
    fc = 900.0 * (5200.0 / 900.0) ** np.clip(u / 0.75, 0, 1)
    env = np.where(u < 0.62, (u / 0.62) ** 0.9, np.exp(-(u - 0.62) / 0.06))
    x = svf_band(white(n, r), fc, q=0.9) * env
    x = x / np.max(np.abs(x))
    m = int(0.05 * SR)
    place(x, bp(white(m, r), 600.0, 2500.0) * expdecay(m, 0.012, 1.0), 0.275, 0.35)
    return finish(x, in_ms=4.0, out_ms=25.0)


# ==========================================================================
# SFX - intro / ending (the real desk)
# ==========================================================================
def sfx_key(i):
    """Mechanical keyboard click: a click, a little thock, a faint return."""
    f_thock, bright, ret = [(310.0, 1.0, 0.020), (265.0, 0.85, 0.024), (350.0, 0.9, 0.018)][i]
    r = rng('key_%d' % i)
    n = int(0.055 * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    m = int(0.006 * SR)
    x[:m] += 0.8 * bright * hp(white(m, r), 4000.0) * np.exp(-np.arange(m) / (0.0015 * SR))
    x += 0.7 * (np.sin(2 * np.pi * f_thock * t) + 0.3 * np.sin(2 * np.pi * 2.3 * f_thock * t)) * np.exp(-t / 0.011)
    x += 0.25 * bp(white(n, r), 800.0, 3000.0) * np.exp(-t / 0.014)
    i0 = int(ret * SR)
    x[i0:i0 + m] += 0.28 * hp(white(m, r), 4500.0) * np.exp(-np.arange(m) / (0.002 * SR))
    return finish(x, in_ms=0.3, out_ms=8.0)


def sfx_room_tone():
    """6 s seamless loop of a quiet PC fan: shaped noise built in the frequency domain."""
    n = int(6.0 * SR)
    r = rng('room_tone')
    freqs = np.fft.rfftfreq(n, 1.0 / SR)
    mag = 1.0 / (1.0 + (freqs / 180.0) ** 2)
    mag *= (freqs / (freqs + 40.0)) ** 2                # no rumble / no DC
    mag[freqs > 3000.0] *= np.exp(-(freqs[freqs > 3000.0] - 3000.0) / 1500.0)
    spec = mag * np.exp(1j * r.uniform(0, 2 * np.pi, len(freqs)))
    spec[0] = 0.0
    x = np.fft.irfft(spec, n=n)
    x /= np.max(np.abs(x))
    t = np.arange(n) / SR
    # hum partials: every frequency is a whole number of cycles in 6 s, so the loop is exact
    x += 0.22 * np.sin(2 * np.pi * 120.0 * t) + 0.08 * np.sin(2 * np.pi * 240.0 * t + 1.0)
    x *= 1.0 + 0.06 * np.sin(2 * np.pi * 0.5 * t)       # slow breathing, 3 cycles per loop
    x -= np.mean(x)
    return x / np.max(np.abs(x))


def sfx_yawn_whoosh():
    """Pulled into the screen: an airy riser that swells, spins up and cuts. Exciting."""
    n = int(1.8 * SR)
    r = rng('whoosh')
    u = np.linspace(0.0, 1.0, n)
    t = np.arange(n) / SR
    swell = np.where(u < 0.93, 0.12 + 0.88 * (u / 0.93) ** 1.7, np.exp(-(u - 0.93) / 0.012))
    fc = 250.0 * (6500.0 / 250.0) ** (u ** 1.3)
    air = svf_band(white(n, r), fc, q=1.4)
    air = air * (6500.0 / fc) ** 0.4          # even out the narrow low band
    air = air / np.max(np.abs(air)) * swell
    f = glide(hz('G3'), hz('G6'), n)
    trem = 1.0 + 0.35 * np.sin(2 * np.pi * np.cumsum(8.0 + 16.0 * u) / SR)
    riser = osc_pulse(f, 0.25, soft=4500.0) * swell * trem
    return finish(air + 0.24 * riser, in_ms=30.0, out_ms=6.0)


def sfx_alarm():
    """Digital alarm clock: four quick beeps, then a pause (so it can loop)."""
    out = np.zeros(int(1.6 * SR))
    n = int(0.14 * SR)
    beep = osc_pulse(const(hz('B6'), n), 0.5, soft=4000.0) * ramp_out(ramp_in(np.ones(n), 3.0), 12.0)
    for i in range(4):
        place(out, beep, i * 0.28)
    return out / np.max(np.abs(out))


# ==========================================================================
# Registry
# ==========================================================================
SOUNDS = [
    # name, function, level, description
    # level = loudness in dB relative to the full 8-stem music mix (A-weighted, loudest 0.3 s),
    #         or ('peak', dBFS) for a fixed peak.
    ('jingle_boot',     jingle_boot,     5.0, 'Console power-on chime. Plays when the pixel window pops up.'),
    ('jingle_room',     jingle_room,     5.0, 'Room clear: four rising notes of the C chord.'),
    ('jingle_win',      jingle_win,      6.0, 'Final fanfare built on the hook of the tune; chip + HD bells. Stereo.'),
    ('jingle_gameover', jingle_gameover, 4.0, 'Comic "oops" when all hearts are lost.'),
    ('jingle_hurry',    jingle_hurry,    5.0, '"Hurry up!" riff when the floor starts deleting.'),
    ('jingle_layer',    jingle_layer,    4.0, 'Shimmer when an HD remaster music layer is added. Stereo.'),
    ('step_chip_1',     lambda: sfx_step_chip('C5'), -5.0, 'Footstep on an old-game-only tile (blip, C).'),
    ('step_chip_2',     lambda: sfx_step_chip('G4'), -5.0, 'Footstep on an old-game-only tile (blip, G).'),
    ('coin',            sfx_coin,        5.0, 'Coin picked up (exists only in the pixel game).'),
    ('hurt',            sfx_hurt,        4.0, 'Touched a slime, lost a heart.'),
    ('slime_hop',       sfx_slime_hop,   -1.0, 'A slime hops nearby.'),
    ('wrap',            sfx_wrap,        5.0, 'Screen-wrap teleport.'),
    ('fall',            sfx_fall,        3.0, 'Fell into a pit.'),
    ('respawn',         sfx_respawn,     4.0, 'Back at the room start.'),
    ('tile_delete',     sfx_tile_delete, -2.0, 'One floor tile deletes itself (room 5).'),
    ('ui_move',         sfx_ui_move,     -1.0, 'Menu cursor move.'),
    ('ui_ok',           sfx_ui_ok,       2.0, 'Menu confirm.'),
    ('ui_pause',        sfx_ui_pause,    2.0, 'Pause / unpause.'),
    ('gate',            sfx_gate,        3.0, 'Bumped the locked gate: denied.'),
    ('step_paper_1',    lambda: sfx_step_paper(0), -8.0, 'Footstep on real 3D floor (paper tap).'),
    ('step_paper_2',    lambda: sfx_step_paper(1), -8.0, 'Footstep on real 3D floor (paper tap).'),
    ('step_paper_3',    lambda: sfx_step_paper(2), -8.0, 'Footstep on real 3D floor (paper tap).'),
    ('step_paper_4',    lambda: sfx_step_paper(3), -8.0, 'Footstep on real 3D floor (paper tap).'),
    ('scribble_1',      lambda: sfx_scribble(0),   -2.0, 'Pencil scribble (something sketches in).'),
    ('scribble_2',      lambda: sfx_scribble(1),   -2.0, 'Pencil scribble, shorter strokes.'),
    ('ghostwall',       sfx_ghostwall,   2.0, 'Walking through a fake 3D wall.'),
    ('note',            sfx_note,        2.0, 'A dev note appears: scratch + soft bell.'),
    ('page_flip',       sfx_page_flip,   -1.0, 'Paper swish (room / screen transition).'),
    ('key_1',           lambda: sfx_key(0), -4.0, 'Keyboard click (intro at the desk).'),
    ('key_2',           lambda: sfx_key(1), -4.0, 'Keyboard click.'),
    ('key_3',           lambda: sfx_key(2), -4.0, 'Keyboard click.'),
    ('room_tone',       sfx_room_tone,   ('peak', -30.0), 'Seamless 6 s loop of a quiet PC fan (intro / ending).'),
    ('yawn_whoosh',     sfx_yawn_whoosh, 6.0, 'Pulled into the screen: rising whoosh that cuts.'),
    ('alarm',           sfx_alarm,       3.0, 'Alarm clock: four beeps then a pause (ending).'),
    ('laser_old',       sfx_laser_old,   1.0, "The old game's laser: chip pew, heard 3 s before the real beam."),
    ('laser_fire',      sfx_laser_fire,  5.0, "The remake's laser beam firing (3 s after the old game showed it)."),
    ('flick_a',         lambda: sfx_flick('E6'), -1.0, 'Flicker planks: group A appears (higher note).'),
    ('flick_b',         lambda: sfx_flick('A5'), -1.0, 'Flicker planks: group B appears (lower note).'),
    ('flick_warn',      sfx_flick_warn,  -4.0, 'Tick before the flicker planks swap.'),
    ('plank_break',     sfx_plank_break, 2.0, 'A one-use plank snaps after you step off it.'),
]
SFX_PEAK_CEILING_DB = -3.0   # no sound effect peaks above this
# Very short or very low sounds would need silly peaks to hit a loudness target; cap them.
SFX_PEAK_CAP = {
    'key_1': -9.0, 'key_2': -9.0, 'key_3': -9.0,
    'step_paper_1': -7.0, 'step_paper_2': -7.0, 'step_paper_3': -7.0, 'step_paper_4': -7.0,
    'step_chip_1': -8.0, 'step_chip_2': -8.0, 'ui_move': -8.0, 'tile_delete': -7.0,
    'slime_hop': -8.0, 'gate': -6.0,
    'flick_a': -7.0, 'flick_b': -7.0, 'flick_warn': -9.0, 'laser_old': -6.0,
}


# ==========================================================================
# Output
# ==========================================================================
def write_wav(path, x):
    x = np.asarray(x, dtype=float)
    assert np.all(np.isfinite(x)), 'NaN/inf in ' + path
    ch = 1 if x.ndim == 1 else x.shape[1]
    pcm = np.round(np.clip(x, -1.0, 1.0) * 32767.0).astype('<i2')
    with wave.open(path, 'wb') as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def stats(x):
    x = np.asarray(x, dtype=float)
    peak = float(np.max(np.abs(x)))
    rms = float(np.sqrt(np.mean(x ** 2)))
    dc = float(np.max(np.abs(np.mean(x, axis=0))))
    mono = x if x.ndim == 1 else x.mean(axis=1)
    spec = np.abs(np.fft.rfft(mono)) ** 2
    fr = np.fft.rfftfreq(len(mono), 1.0 / SR)
    hf = float(spec[fr > 10000.0].sum() / max(spec.sum(), 1e-20))
    return {'peak_db': db(peak), 'rms_db': db(rms), 'dc': dc, 'hf': hf, 'loud': loudness_db(x),
            'dur': len(x) / SR, 'ch': 1 if x.ndim == 1 else x.shape[1], 'n': len(x)}


def loop_jump(x):
    """Size of the step across the loop point compared with normal sample-to-sample motion."""
    step = float(np.max(np.abs(x[0] - x[-1])))
    typical = float(np.percentile(np.abs(np.diff(x, axis=0)), 99.9))
    slope = float(np.max(np.abs((x[0] - x[-1]) - (x[1] - x[0]))))
    return step, typical, slope


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, '..', 'Assets', 'Resources', 'Audio')
    manifest = sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, 'audio_manifest.md')
    preview_dir = sys.argv[3] if len(sys.argv) > 3 else None
    os.makedirs(out_dir, exist_ok=True)
    check_tables_in_key()

    rows = []
    problems = []

    # ---- music stems -------------------------------------------------------
    stems = {}
    for name, fn, desc in STEMS:
        x = fn()
        assert x.shape == (N, 2)
        stems[name] = x - np.mean(x, axis=0)
        print('rendered', name)
    lead = stems['mus_chip_lead']
    lead *= from_db(STEM_LEVEL['mus_chip_lead'][1]) / float(np.sqrt(np.mean(lead ** 2)))
    lead_int, lead_max = integrated_db(lead), loudness_db(lead)
    for name in stems:
        mode, level = STEM_LEVEL[name]
        x = stems[name]
        if mode == 'rms':
            x *= from_db(level) / float(np.sqrt(np.mean(x ** 2)))
        elif mode == 'a_int_rel':
            x *= from_db(lead_int + level - integrated_db(x))
        elif mode == 'a_max_rel':
            x *= from_db(lead_max + level - loudness_db(x))
            if db(np.max(np.abs(x))) > DRUM_PEAK_DB:
                x[:] = soft_ceiling(x, from_db(DRUM_PEAK_DB))
                x *= from_db(lead_max + level - loudness_db(x))     # restore the loudness
                x[:] = soft_ceiling(x, from_db(DRUM_PEAK_DB))
    full = sum(stems.values())
    chip = sum(v for k, v in stems.items() if k.startswith('mus_chip'))
    peak_full = float(np.max(np.abs(full)))
    scale = min(1.0, from_db(MIX_CEILING_DB) / peak_full)
    if scale < 1.0:
        for k in stems:
            stems[k] *= scale
        full *= scale
        chip *= scale
    music_ref = integrated_db(full)                    # what the SFX levels are measured against
    mix = {
        'scale_db': db(scale), 'ref_dba': music_ref, 'chip_dba': integrated_db(chip),
        'full_peak': db(np.max(np.abs(full))), 'full_rms': db(np.sqrt(np.mean(full ** 2))),
        'chip_peak': db(np.max(np.abs(chip))), 'chip_rms': db(np.sqrt(np.mean(chip ** 2))),
    }
    loop_rows = []
    for name, fn, desc in STEMS:
        x = stems[name]
        write_wav(os.path.join(out_dir, name + '.wav'), x)
        s = stats(x)
        rows.append((name, s, desc))
        step, typical, slope = loop_jump(x)
        loop_rows.append((name, step, typical, slope))
        if step > 2.5 * typical + 1e-4:
            problems.append('%s: loop step %.5f vs typical %.5f' % (name, step, typical))
        if s['n'] != N:
            problems.append('%s: wrong length' % name)

    # ---- jingles and sfx ---------------------------------------------------
    capped = []
    for name, fn, level, desc in SOUNDS:
        x = fn()
        if isinstance(level, tuple):
            x = norm_peak(x, level[1])
        else:
            x = x * from_db(music_ref + level - loudness_db(x))
            cap = SFX_PEAK_CAP.get(name, SFX_PEAK_CEILING_DB)
            if db(np.max(np.abs(x))) > cap:
                capped.append(name)
                x = norm_peak(x, cap)
        write_wav(os.path.join(out_dir, name + '.wav'), x)
        s = stats(x)
        rows.append((name, s, desc))
        if name != 'room_tone':
            first = float(np.max(np.abs(x[0])))
            last = float(np.max(np.abs(x[-1])))
            if first > 0.003 or last > 0.003:
                problems.append('%s: does not start/end at silence (%.4f / %.4f)' % (name, first, last))
        else:
            step, typical, slope = loop_jump(x)
            loop_rows.append((name, step, typical, slope))
            if step > 2.5 * typical + 1e-5:
                problems.append('room_tone: loop step %.6f vs typical %.6f' % (step, typical))
        print('rendered', name)

    for name, s, _ in rows:
        if s['peak_db'] > -1.0:
            problems.append('%s: peak %.2f dBFS above -1' % (name, s['peak_db']))
        if s['dc'] > 0.002:
            problems.append('%s: DC offset %.4f' % (name, s['dc']))

    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        write_wav(os.path.join(preview_dir, 'preview_chip.wav'), chip)
        write_wav(os.path.join(preview_dir, 'preview_full.wav'), full)
        order = ['mus_hd_drums', 'mus_hd_bass', 'mus_hd_pad', 'mus_hd_pluck']
        half = pos(9, 0)                                # 8 bars per step of the build-up
        parts = []
        for k in range(len(order) + 1):
            m = chip + sum((stems[o] for o in order[:k]), np.zeros_like(chip))
            parts.append(m[:half] if k % 2 == 0 else m[half:])
        write_wav(os.path.join(preview_dir, 'preview_buildup.wav'), np.concatenate(parts))

    # ---- manifest ----------------------------------------------------------
    L = []
    L.append('# REMASTER audio manifest')
    L.append('')
    L.append('Generated by `tools/gen_audio.py` (numpy + scipy synthesis, seed %d). '
             'No samples, no downloads. 44100 Hz, 16-bit PCM.' % SEED)
    L.append('')
    L.append('Two palettes: the **old pixel game** is a sound chip (pulse, triangle, noise); '
             'the **unfinished 3D remake** is paper and pencil plus warmer "HD" instruments.')
    L.append('')
    L.append('## Music stems')
    L.append('')
    L.append('One tune, C major, %d BPM, %d bars, %d samples (%.4f s) per stem. '
             'All stems loop seamlessly and are pre-balanced: play every active stem at volume 1.0.'
             % (int(BPM), BARS, N, N / SR))
    L.append('')
    L.append('| File | Ch | Duration (s) | Peak dBFS | RMS dBFS | Purpose |')
    L.append('|---|---|---|---|---|---|')
    for name, s, desc in rows[:len(STEMS)]:
        L.append('| %s.wav | %d | %.3f | %.1f | %.1f | %s |' % (name, s['ch'], s['dur'], s['peak_db'], s['rms_db'], desc))
    L.append('')
    L.append('Mix check: four chip stems summed peak %.1f dBFS (RMS %.1f). '
             'All eight stems summed peak %.1f dBFS (RMS %.1f). Global trim applied: %.2f dB.'
             % (mix['chip_peak'], mix['chip_rms'], mix['full_peak'], mix['full_rms'], mix['scale_db']))
    L.append('')
    L.append('Suggested layering: chip stems from the start; add `mus_hd_drums`, `mus_hd_bass`, '
             '`mus_hd_pad`, `mus_hd_pluck` one per cleared room. Start every stem on the same '
             'DSP time so they stay in sync (mute a layer with volume 0 rather than stopping it).')
    L.append('')
    L.append('The stems are balanced against each other, so they do not all peak at the same level: '
             'the lead and drums are the loudest, the arpeggio, pad and bell are deliberately quiet. '
             'Keep every active stem at the same volume.')
    L.append('')
    L.append('Headroom: the full mix peaks at %.1f dBFS and effects peak up to %.1f dBFS, so music plus '
             'an effect can clip at full volume. Run everything at about 0.7 (for example '
             '`AudioListener.volume = 0.7`); that keeps the balance and leaves about 3 dB of room.'
             % (mix['full_peak'], SFX_PEAK_CEILING_DB))
    L.append('')
    L.append('## Jingles and sound effects')
    L.append('')
    L.append('Levels are set by loudness, not by peak: each sound is placed a fixed number of dB '
             'above or below the full music mix (A-weighted, loudest 0.125 s), so a coin is clearly '
             'audible over the music and a footstep sits under it. Music reference: %.1f dBA '
             '(chip stems only: %.1f dBA).' % (mix['ref_dba'], mix['chip_dba']))
    L.append('')
    L.append('| File | Ch | Duration (s) | Peak dBFS | RMS dBFS | Loudness dBA | vs music | Purpose |')
    L.append('|---|---|---|---|---|---|---|---|')
    for name, s, desc in rows[len(STEMS):]:
        L.append('| %s.wav | %d | %.3f | %.1f | %.1f | %.1f | %+.1f | %s |' %
                 (name, s['ch'], s['dur'], s['peak_db'], s['rms_db'], s['loud'], s['loud'] - mix['ref_dba'], desc))
    L.append('')
    if capped:
        L.append('Held at a peak cap (so a little under their loudness target): %s.' % ', '.join(capped))
        L.append('')
    L.append('## Loop points')
    L.append('')
    L.append('Step across the loop point compared with the 99.9th percentile of normal '
             'sample-to-sample motion (a step no bigger than normal motion cannot click).')
    L.append('')
    L.append('| File | Step at loop | Typical step | Slope mismatch |')
    L.append('|---|---|---|---|')
    for name, step, typical, slope in loop_rows:
        L.append('| %s.wav | %.5f | %.5f | %.5f |' % (name, step, typical, slope))
    L.append('')
    L.append('## Quality checks')
    L.append('')
    L.append('- Peak of every file at or below -1 dBFS; no NaN; DC offset below 0.002.')
    L.append('- Every SFX starts and ends at silence (ramped in under 5 ms, faded out) and has no leading gap. '
             '`alarm.wav` keeps a 0.6 s pause at the end on purpose so it can loop; '
             '`room_tone.wav` is a seamless loop.')
    L.append('- All eight stems have exactly %d samples.' % N)
    L.append('- All music notes are checked to be in C major (the "hurry" jingle rises by whole steps on purpose).')
    L.append('- Share of energy above 10 kHz (harshness guard): ' +
             ', '.join('%s %.1f%%' % (n, 100.0 * s['hf']) for n, s, _ in rows if s['hf'] > 0.02) + '.'
             if any(s['hf'] > 0.02 for _, s, _ in rows) else
             '- No file has more than 2% of its energy above 10 kHz.')
    L.append('')
    L.append('Result: ' + ('**all checks passed**' if not problems else '**PROBLEMS**: ' + '; '.join(problems)))
    L.append('')
    L.append('## The tune (lead melody)')
    L.append('')
    L.append('Chords, one per bar: A = ' + ' | '.join(CHORDS[:8]) + ' ; B = ' + ' | '.join(CHORDS[8:]))
    L.append('')
    L.append('| Bar | Chord | Notes (beat:length note) |')
    L.append('|---|---|---|')
    for bar in range(1, BARS + 1):
        L.append('| %d | %s | %s |' % (bar, CHORDS[bar - 1],
                 ', '.join('%g:%g %s' % (b, ln, nm) for b, ln, nm in MELODY[bar])))
    L.append('')
    with open(manifest, 'w', encoding='utf-8') as f:
        f.write('\n'.join(L))

    print('\nMIX', mix)
    for k, v in stems.items():
        print('  %-16s a_int %6.1f  a_max %6.1f' % (k, integrated_db(v), loudness_db(v)))
    for name, s, _ in rows:
        print('%-18s ch%d %7.3fs peak %6.1f rms %6.1f loud %6.1f dc %.5f hf %.3f' %
              (name, s['ch'], s['dur'], s['peak_db'], s['rms_db'], s['loud'], s['dc'], s['hf']))
    for name, step, typical, slope in loop_rows:
        print('LOOP %-16s step %.5f typical %.5f slope %.5f' % (name, step, typical, slope))
    print('CAPPED AT PEAK CEILING:', capped if capped else 'none')
    print('PROBLEMS:', problems if problems else 'none')
    return 0 if not problems else 1


if __name__ == '__main__':
    sys.exit(main())
