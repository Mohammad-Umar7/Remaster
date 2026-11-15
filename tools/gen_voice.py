"""
REMASTER - the guard's voice.

The five lines in tools/voice_src/*.mp3 were generated with ElevenLabs text-to-speech
(voice "Harry - Fierce Warrior", model Eleven v4) during the jam:

    halt.mp3    "[shouting] HALT!"
    hey.mp3     "[shouting] HEY! You there!"
    stop.mp3    "[shouting] Stop right there!"
    hyah.mp3    "[shouting] HYAH!"
    gotcha.mp3  "[smug] Gotcha!"

The words before the last fight were generated the same way. The Captain uses the guard's voice;
the dev ("YOU") is the voice "Will - Relaxed Optimist":

    boss_line_1.mp3  "[shouting, angry] HALT! Nobody ships an unfinished game on MY watch!"
    dev_line_1.mp3   "[tired, deadpan] It's three forty-seven in the morning. I'm shipping it."
    boss_line_2.mp3  "[mocking, shouting] With WHAT? You never even modelled a sword! [laughs loudly]"
    dev_line_2.mp3   "[calm, confident] I don't need one. The old game still has its sword. And every coin I found makes it sharper."
    boss_line_3.mp3  "[shouting, furious] Then come and TAKE your build!"
    boss_line_4.mp3  "[defeated, grumbling] Fine... ship it. But fix that bridge."

This script turns them into the kind of speech sample a 1989 game could play, so the voice sits with
the chip sounds instead of on top of them:

    trim the silence -> band-limit -> 11 kHz -> 8 bits -> back to 44.1 kHz with the steps left in

Run (needs ffmpeg on the PATH or at C:/ffmpeg/bin, plus numpy and scipy):
    python tools/gen_voice.py
Writes Assets/Resources/Audio/guard_say_<name>.wav for the guard's shouts and
Assets/Resources/Audio/<name>.wav for the lines of the last scene.
"""
import os
import shutil
import subprocess
import sys
import wave

import numpy as np
from scipy import signal

SR = 44100
CRUSH_RATE = 11025          # the old machine's sample rate
BITS = 8
LOUDNESS_DB = -16.0         # every line is brought to this RMS level...
CEILING = 0.89              # ...and its peaks are rounded off below this
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "voice_src")
OUT = os.path.normpath(os.path.join(HERE, "..", "Assets", "Resources", "Audio"))
# source file (without .mp3) -> name of the .wav the game loads
LINES = [("halt", "guard_say_halt"), ("hey", "guard_say_hey"), ("stop", "guard_say_stop"),
         ("hyah", "guard_say_hyah"), ("gotcha", "guard_say_gotcha"),
         ("boss_line_1", "boss_line_1"), ("dev_line_1", "dev_line_1"), ("boss_line_2", "boss_line_2"),
         ("dev_line_2", "dev_line_2"), ("boss_line_3", "boss_line_3"), ("boss_line_4", "boss_line_4")]


def ffmpeg_exe():
    exe = shutil.which("ffmpeg")
    if exe:
        return exe
    for guess in (r"C:\ffmpeg\bin\ffmpeg.exe", "/c/ffmpeg/bin/ffmpeg"):
        if os.path.exists(guess):
            return guess
    raise SystemExit("ffmpeg not found")


def decode(path):
    """MP3 -> mono float samples at 44.1 kHz."""
    raw = subprocess.run([ffmpeg_exe(), "-v", "error", "-i", path, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype="<f4").astype(np.float64)


def trim(x, floor=0.02, pad_ms=12):
    """Cut the silence off both ends, keeping a few milliseconds of air."""
    level = np.abs(x)
    loud = np.nonzero(level > floor * level.max())[0]
    if len(loud) == 0:
        return x
    pad = int(SR * pad_ms / 1000)
    return x[max(0, loud[0] - pad):min(len(x), loud[-1] + pad)]


def crush(x):
    """11 kHz, 8 bits, and the stair-steps kept on the way back up."""
    step = SR // CRUSH_RATE
    lp = signal.butter(6, (CRUSH_RATE * 0.45) / (SR / 2), output="sos")
    x = signal.sosfiltfilt(lp, x)
    x = x / max(1e-9, np.abs(x).max())
    low = x[::step]
    levels = 2 ** (BITS - 1)
    low = np.round(low * (levels - 1)) / (levels - 1)
    held = np.repeat(low, step)                     # zero-order hold: this is the grit
    soften = signal.butter(2, 7000.0 / (SR / 2), output="sos")
    return signal.sosfilt(soften, held)


def fades(x, in_ms=3, out_ms=18):
    n_in, n_out = int(SR * in_ms / 1000), int(SR * out_ms / 1000)
    x = x.copy()
    x[:n_in] *= np.linspace(0.0, 1.0, n_in)
    x[-n_out:] *= np.linspace(1.0, 0.0, n_out)
    return x


def write_wav(path, x):
    data = np.clip(x, -1.0, 1.0)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((data * 32767.0).astype("<i2").tobytes())


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, out_name in LINES:
        x = decode(os.path.join(SRC, name + ".mp3"))
        x = fades(crush(trim(x)))
        x *= 10.0 ** (LOUDNESS_DB / 20.0) / max(1e-9, np.sqrt(np.mean(x ** 2)))
        x = np.tanh(x / CEILING) * CEILING
        out = os.path.join(OUT, out_name + ".wav")
        write_wav(out, x)
        rms = 20.0 * np.log10(max(1e-9, np.sqrt(np.mean(x ** 2))))
        print("%-22s %.2f s   peak %.1f dB   rms %.1f dB" % (os.path.basename(out), len(x) / SR, 20.0 * np.log10(np.abs(x).max()), rms))


if __name__ == "__main__":
    sys.exit(main())
