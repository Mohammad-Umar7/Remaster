using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sound with a point: the old game sounds like a chip, the unfinished remake sounds like paper.
/// The music is one tune in stacked layers; every room you clear adds a "remastered" instrument,
/// so finishing the game finishes its soundtrack.
/// </summary>
public class AudioDirector : MonoBehaviour
{
    static readonly string[] Stems = { "mus_chip_lead", "mus_chip_arp", "mus_chip_bass", "mus_chip_drums", "mus_hd_drums", "mus_hd_bass", "mus_hd_pad", "mus_hd_pluck" };
    readonly AudioSource[] music = new AudioSource[8];
    readonly float[] target = new float[8];
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    readonly List<AudioSource> pool = new List<AudioSource>();
    AudioSource tone;
    int layers;
    bool musicStarted;
    public float musicMaster = 1f;      // 0..1, eased
    float musicMasterTarget = 1f, duck = 1f, duckTarget = 1f, pitch = 1f, pitchTarget = 1f;
    public float masterVolume = 0.72f;
    public int LayerCount => layers;

    void Awake()
    {
        for (int i = 0; i < music.Length; i++)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = true; s.spatialBlend = 0f; s.volume = 0f; s.priority = 10;
            music[i] = s;
        }
        for (int i = 0; i < 12; i++)
        {
            var go = new GameObject("sfx" + i);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 0f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 2f; s.maxDistance = 22f;
            pool.Add(s);
        }
        tone = gameObject.AddComponent<AudioSource>();
        tone.playOnAwake = false; tone.loop = true; tone.spatialBlend = 0f; tone.volume = 0f;
        AudioListener.volume = masterVolume;
    }

    public AudioClip Clip(string name)
    {
        AudioClip c;
        if (clips.TryGetValue(name, out c)) return c;
        c = Resources.Load<AudioClip>("Audio/" + name);
        clips[name] = c;
        return c;
    }

    // ---------------------------------------------------------------- music
    public void StartMusic()
    {
        if (musicStarted) return;
        bool any = false;
        double t0 = AudioSettings.dspTime + 0.15;
        for (int i = 0; i < Stems.Length; i++)
        {
            var c = Clip(Stems[i]);
            if (c == null) continue;
            music[i].clip = c;
            music[i].volume = 0f;
            music[i].PlayScheduled(t0);
            any = true;
        }
        musicStarted = any;
        ApplyLayers();
    }

    public void StopMusic()
    {
        foreach (var s in music) s.Stop();
        musicStarted = false;
    }

    /// <summary>0 = pure chip tune. Each step adds one remastered instrument.</summary>
    public void SetLayers(int n)
    {
        layers = Mathf.Clamp(n, 0, 4);
        ApplyLayers();
    }

    void ApplyLayers()
    {
        // lead, arp, bass, drums | hd drums, hd bass, hd pad, hd pluck
        // the stems are already balanced against each other, so active ones play at the same level
        target[0] = 1f; target[1] = 1f; target[2] = 1f; target[3] = 1f;
        target[4] = layers >= 1 ? 1f : 0f;
        target[5] = layers >= 2 ? 1f : 0f;
        target[6] = layers >= 3 ? 1f : 0f;
        target[7] = layers >= 4 ? 1f : 0f;
        if (layers >= 1) target[3] = 0.5f;      // real drums take over from the noise drums
        if (layers >= 2) target[2] = 0.55f;     // and the round bass from the triangle
    }

    public void SetMusicLevel(float level) { musicMasterTarget = level; }
    public void SetHurry(bool on) { pitchTarget = on ? 1.12f : 1f; }
    public void Duck(float to, float seconds) { duckTarget = to; duckRelease = Time.unscaledTime + seconds; }
    float duckRelease;

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (Time.unscaledTime > duckRelease) duckTarget = 1f;
        duck = Mathf.MoveTowards(duck, duckTarget, dt * 3.5f);
        musicMaster = Mathf.MoveTowards(musicMaster, musicMasterTarget, dt * 1.2f);
        pitch = Mathf.MoveTowards(pitch, pitchTarget, dt * 0.35f);
        for (int i = 0; i < music.Length; i++)
        {
            float v = target[i] * musicMaster * duck * 0.9f;
            music[i].volume = Mathf.MoveTowards(music[i].volume, v, dt * 0.9f);
            music[i].pitch = pitch;
        }
    }

    // ---------------------------------------------------------------- one-shots
    AudioSource Free()
    {
        foreach (var s in pool) if (!s.isPlaying) return s;
        return pool[0];
    }

    public void Play(string name, float volume = 1f, float pitchJitter = 0f, float basePitch = 1f)
    {
        var c = Clip(name);
        if (c == null) return;
        var s = Free();
        s.transform.localPosition = Vector3.zero;
        s.spatialBlend = 0f;
        s.pitch = basePitch + (pitchJitter > 0f ? Random.Range(-pitchJitter, pitchJitter) : 0f);
        s.volume = volume;
        s.clip = c;
        s.Play();
    }

    /// <summary>A sound that comes from a place in the 3D room (used for things you cannot see).</summary>
    public void PlayAt(string name, Vector3 pos, float volume = 1f, float pitchJitter = 0f)
    {
        var c = Clip(name);
        if (c == null) return;
        var s = Free();
        s.transform.position = pos;
        s.spatialBlend = 1f;
        s.pitch = 1f + (pitchJitter > 0f ? Random.Range(-pitchJitter, pitchJitter) : 0f);
        s.volume = volume;
        s.clip = c;
        s.Play();
    }

    /// <summary>
    /// A placed sound with its own pitch. <paramref name="spatial"/> 0..1: how much it comes from its place
    /// (below 1 some of it is always heard, so the caller can make "nearer = louder" with the volume).
    /// </summary>
    public void PlayAt(string name, Vector3 pos, float volume, float pitchJitter, float basePitch, float spatial)
    {
        var c = Clip(name);
        if (c == null) return;
        var s = Free();
        s.transform.position = pos;
        s.spatialBlend = Mathf.Clamp01(spatial);
        s.pitch = basePitch + (pitchJitter > 0f ? Random.Range(-pitchJitter, pitchJitter) : 0f);
        s.volume = volume;
        s.clip = c;
        s.Play();
    }

    public void LoopTone(string name, float volume)
    {
        var c = Clip(name);
        if (c == null) { tone.Stop(); return; }
        if (tone.clip != c) { tone.clip = c; tone.Play(); }
        else if (!tone.isPlaying) tone.Play();
        tone.volume = volume;
    }

    public void StopTone() { tone.Stop(); }
}
