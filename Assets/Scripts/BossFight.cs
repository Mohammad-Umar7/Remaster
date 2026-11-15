using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The last room: the Captain of the guards stands between you and BUILD & RUN.
/// He is new in the remake, so the old game shows an empty hall.
///
/// The camera pulls out of your head and you see yourself for the first time: the 1989 game's little
/// hero, in 3D. First a scene, shot like a film (he drops in, the two of you have words, voiced; his
/// mouth moves to the sound of his own voice). The remake never modelled a sword, so you draw the old
/// game's one. Then the fight, with the view locked on him:
///   left click   a three-hit combo (slash, slash, overhead smash)
///   space        a dodge roll (nothing can hit you while you roll)
/// He blocks while he is ready, so you dodge first and hit him while he is stuck or dizzy.
/// His health comes in three parts and each part has its own attack. Where each one will land is
/// drawn on the floor in red before it does:
///   the charge  a red lane: step out of it
///   the spin    a red circle round him: back away
///   the leap    a red circle where he will come down: run or roll out
/// Every coin from the old game takes a little off his health. When it is gone he comes apart, and
/// every piece tumbles away under real physics.
/// </summary>
public class BossFight
{
    public enum St { Asleep, Drop, Talk, Stalk, ThrustWind, ThrustDash, Recover, SpinWind, Spin, LeapWind, LeapAir, Dizzy, Dying, Gone }
    public St st = St.Asleep;
    public bool defeated;
    public int hp, maxHp;
    public Vector3 pos;                 // his feet, world space
    public float yaw;

    public const float Scale = 1.9f;    // he is nearly twice a guard
    const float Reach = 3.6f;           // how far your sword reaches him (metres, centre to centre)
    const float DashLength = 5.6f, DashHit = 1.4f, SpinHit = 2.9f, ShockRadius = 2.9f, Body = 1.25f;
    const float ThrustLock = 0.26f, ThrustWindup = 0.62f;

    GameManager gm;
    Room room;
    GuardRig rig;
    HeroRig hero;
    Transform shock, mouth, lane, disc, ring, slash;
    Material shockMat, laneMat, discMat, ringMat, slashMat;
    public readonly List<GameObject> debris = new List<GameObject>();
    // him
    float t, cooldown, flinch, blockT, walk, talkT, talkHold, spinHurt, shockT = -1f, starClock, roundT, steamClock, mouthOpen;
    int line = -1, attackCount, lastPhase = 1, resets;
    bool introSeen, dashHit, saidLast, newMove;
    Vector3 dashDir, leapFrom, leapTo, home;
    // you
    float heroYaw, runPhase, atkT = -1f, comboClock, dodgeT = -1f, dodgeCool, hurtT, swordShow, slashT = -1f, winT = -1f;
    int combo, slashKind, lastHits;
    bool atkHit, atkQueued, swordDrawn;
    // the camera
    float tp;                           // 0 = through your eyes, 1 = over your shoulder
    bool camHeld;
    Vector3 camPos;
    int shot = -99;

    struct Line { public bool captain; public string text, voice; }
    static readonly Line[] Lines =
    {
        new Line { captain = true,  text = "HALT! Nobody ships an unfinished game on MY watch!", voice = "boss_line_1" },
        new Line { captain = false, text = "It's 3:47 in the morning. I'm shipping it.", voice = "dev_line_1" },
        new Line { captain = true,  text = "With WHAT? You never even modelled a sword! HA HA HA!", voice = "boss_line_2" },
        new Line { captain = false, text = "I don't need one. The old game still has its sword. And every coin I found makes it sharper.", voice = "dev_line_2" },
        new Line { captain = true,  text = "Then come and TAKE your build!", voice = "boss_line_3" },
    };
    static readonly string[] HitWords = { "SLASH!", "WHAM!", "CLONK!", "BAM!" };
    static readonly Color YouColor = Pal.Hex("1f8f84");

    public bool Visible { get { return st != St.Asleep && st != St.Gone; } }
    public bool Fighting { get { return st >= St.Stalk && st <= St.Dizzy; } }
    public bool InScene { get { return st == St.Drop || st == St.Talk || (st == St.Dying && t > 0.9f); } }
    bool Open { get { return st == St.Recover || st == St.Dizzy; } }      // he can be hurt

    // ------------------------------------------------------------------ building it
    public void Build(GameManager gm, Room room)
    {
        this.gm = gm; this.room = room;
        home = room.TileCenter(Maps.W / 2, 4);
        var holder = new GameObject("Captain").transform;
        holder.SetParent(gm.world.root, false);
        rig = GuardRig.Build(holder, Scale);
        // the Captain wears dark iron where his guards wear grey, and a crown on his helmet
        rig.Tint(Pal.Metal, Pal.Hex("4a4666"));
        for (int i = 0; i < 6; i++)
        {
            float a = (i * 60f + 30f) * Mathf.Deg2Rad;
            Bit(rig.Head, new Vector3(Mathf.Sin(a) * 0.25f, 0.42f, Mathf.Cos(a) * 0.25f), new Vector3(0.075f, 0.2f, 0.075f), Pal.Yellow, i * 60f + 30f);
        }
        // gritted teeth under the angry eyes: this is the mouth that moves when he talks
        mouth = Bit(rig.Head, new Vector3(0f, 0.1f, 0.352f), new Vector3(0.2f, 0.03f, 0.03f), Pal.White, 0f);
        rig.Show(false);

        // you: the old game's hero, built out of blocks, with the old game's sword
        hero = HeroRig.Build(holder);

        // red on the floor, wherever his next blow is going to land
        lane = Flat(holder, TexLib.white, Pal.A(Pal.Red, 0.3f), out laneMat);
        disc = Flat(holder, TexLib.dot, Pal.A(Pal.Red, 0.25f), out discMat);
        ring = Flat(holder, TexLib.ring, Pal.A(Pal.Red, 0.9f), out ringMat);
        shock = Flat(holder, TexLib.ring, Pal.Red, out shockMat);

        // the white sweep your sword leaves in the air
        var sg = new GameObject("slash");
        sg.transform.SetParent(holder, false);
        sg.AddComponent<MeshFilter>().sharedMesh = ArcMesh();
        var smr = sg.AddComponent<MeshRenderer>();
        slashMat = TexLib.DecalMat(TexLib.white, Pal.White);
        smr.sharedMaterial = slashMat;
        smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        smr.receiveShadows = false;
        slash = sg.transform;
        sg.SetActive(false);
        Reset();
    }

    /// <summary>A small coloured block stuck on one of his parts (crown spikes, teeth).</summary>
    static Transform Bit(Transform parent, Vector3 local, Vector3 size, Color color, float yawDeg)
    {
        var go = new GameObject("bit");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = TexLib.Cube;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = TexLib.Mat(color, 0, 0f, null, false, false, 0.9f);
        return go.transform;
    }

    /// <summary>A flat drawing on the floor (a danger marker, the shockwave).</summary>
    static Transform Flat(Transform parent, Texture tex, Color color, out Material mat)
    {
        var go = new GameObject("floor_mark");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = TexLib.QuadUp;
        var mr = go.AddComponent<MeshRenderer>();
        mat = TexLib.DecalMat(tex, color);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.SetActive(false);
        return go.transform;
    }

    /// <summary>A brush stroke bent into an arc, lying flat, bulging toward +Z: thin at both ends, fat in the middle.</summary>
    static Mesh ArcMesh()
    {
        const int n = 22;
        const float r0 = 0.8f, r1 = 2.15f;
        var v = new Vector3[(n + 1) * 2];
        var uv = new Vector2[v.Length];
        var tri = new int[n * 12];
        for (int i = 0; i <= n; i++)
        {
            float u = i / (float)n;
            float a = Mathf.Lerp(-80f, 80f, u) * Mathf.Deg2Rad;
            float fat = 0.12f + 0.88f * Mathf.Sin(u * Mathf.PI);
            Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            float mid = (r0 + r1) * 0.5f, half = (r1 - r0) * 0.5f * fat;
            v[i * 2] = d * (mid - half); v[i * 2 + 1] = d * (mid + half);
            uv[i * 2] = new Vector2(u, 0f); uv[i * 2 + 1] = new Vector2(u, 1f);
        }
        for (int i = 0; i < n; i++)
        {
            // both faces, so the stroke shows from above and from below
            int a0 = i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3, k = i * 12;
            tri[k] = a0; tri[k + 1] = a1; tri[k + 2] = b0; tri[k + 3] = b0; tri[k + 4] = a1; tri[k + 5] = b1;
            tri[k + 6] = a0; tri[k + 7] = b0; tri[k + 8] = a1; tri[k + 9] = b0; tri[k + 10] = b1; tri[k + 11] = a1;
        }
        var m = new Mesh { name = "slash" };
        m.vertices = v; m.uv = uv; m.triangles = tri;
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    /// <summary>A new run (or a jump to a room): he is alive again and the scene will play again.</summary>
    public void NewRun()
    {
        defeated = false;
        introSeen = false;
        swordDrawn = false;
        Reset();
    }

    /// <summary>Back to the start of the fight (you lost your hearts, restarted the room, or jumped here).</summary>
    public void Reset()
    {
        bool wasScene = InScene, wasActive = Visible;
        // a retry keeps the thirds of his health you had already beaten: he starts again from the top of this one
        int keepPhase = (introSeen && !defeated) ? Mathf.Clamp(lastPhase, 1, 3) : 1;
        resets++;
        foreach (var d in debris) if (d != null) Object.Destroy(d);
        debris.Clear();
        maxHp = 12 - Mathf.Min(3, gm.coins / 7);            // the old game's coins sharpen the old game's sword
        hp = keepPhase == 1 ? maxHp : (keepPhase == 2 ? maxHp * 2 / 3 : maxHp / 3);
        st = defeated ? St.Gone : St.Asleep;
        t = 0f; cooldown = 0f; flinch = 0f; blockT = 0f; shockT = -1f; line = -1; attackCount = 0;
        lastPhase = keepPhase; saidLast = false; newMove = keepPhase > 1;
        mouthOpen = 0f;
        atkT = -1f; atkQueued = false; combo = 0; comboClock = 0f; dodgeT = -1f; dodgeCool = 0f; hurtT = 0f; slashT = -1f; winT = -1f;
        swordShow = swordDrawn ? 1f : 0f;
        lastHits = gm.hits;
        pos = home; yaw = 180f;
        if (rig != null) rig.Show(false);
        foreach (var mark in new[] { shock, lane, disc, ring, slash }) if (mark != null) mark.gameObject.SetActive(false);
        if (!wasActive) return;
        // put back whatever the scene or the fight had changed
        if (gm.hud != null) { gm.hud.ShowBoss(false); gm.hud.SetFightUi(false); gm.hud.HideDialogue(); gm.hud.SetEyelids(0f); gm.hud.SetCinema(false); }
        if (gm.sound != null) { gm.sound.SetHurry(false); gm.sound.Duck(1f, 0.1f); gm.sound.StopVoice(); }
        if (gm.player != null) { gm.player.zoom = 0f; gm.player.lookLocked = false; if (wasScene) gm.player.controlEnabled = true; }
    }

    static Vector3 Dir(float yawDeg) { float a = yawDeg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }

    Vector3 Clamp(Vector3 p)
    {
        const float m = Maps.TS + 1.4f;
        p.x = Mathf.Clamp(p.x, room.MinX + m, room.MaxX - m);
        p.z = Mathf.Clamp(p.z, room.MinZ + m, room.MaxZ - m);
        p.y = room.FloorY;
        return p;
    }

    // his health comes in three equal parts, and each part has its own attack
    int Phase { get { return hp * 3 > maxHp * 2 ? 1 : (hp * 3 > maxHp ? 2 : 3); } }
    Vector3 HeadPos { get { return pos + Vector3.up * (1.32f * Scale); } }
    Vector3 Ground { get { return new Vector3(pos.x, room.FloorY, pos.z); } }

}
