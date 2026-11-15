using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The last room: the Captain of the guards stands between you and BUILD & RUN.
/// He is new in the remake, so the old game shows an empty hall.
///
/// The camera pulls out of your head (the handheld is put away) and you see yourself for the first
/// time: the 1989 game's little hero, in 3D. First a scene, shot like a film (he drops in, the two of you have words, voiced; his
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

    // where the scene has got to (read by the test scripts that take its pictures)
    public int LineNow { get { return line; } }
    public float TalkClock { get { return talkT; } }
    public float StateClock { get { return t; } }

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
        maxHp = 12 - Mathf.Min(3, gm.coins / 5);            // the old game's coins sharpen the old game's sword
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

    // ------------------------------------------------------------------ rules
    public void Tick(float dt, Vector3 p)
    {
        if (st == St.Gone) return;
        int startedAt = resets;
        Vector3 toYou = p - pos; toYou.y = 0f;
        float dist = toYou.magnitude;
        Vector3 dirYou = dist > 0.01f ? toYou / dist : Vector3.back;          // from him to you
        float yawYou = Mathf.Atan2(dirYou.x, dirYou.z) * Mathf.Rad2Deg;
        t += dt;
        if (cooldown > 0f) cooldown -= dt;
        if (flinch > 0f) flinch -= dt;
        if (blockT > 0f) blockT -= dt;
        if (spinHurt > 0f) spinHurt -= dt;
        if (roundT > 0f) roundT -= dt;

        // from the moment he appears the view is locked on him: forward is toward him, sideways circles him
        var pl = gm.player;
        pl.lookLocked = st != St.Asleep;
        if (pl.lookLocked && dist > 0.9f)
        {
            pl.yaw = Mathf.MoveTowardsAngle(pl.yaw, yawYou + 180f, (Fighting ? 300f : 420f) * dt);
            pl.pitch = Mathf.MoveTowards(pl.pitch, 6f, 60f * dt);
        }
        // he is solid: you cannot walk through him
        if (Fighting && st != St.ThrustDash && st != St.LeapAir && dist < Body && dist > 0.01f)
            pl.Shift(dirYou * Mathf.Min(Body - dist, 7f * dt));

        switch (st)
        {
            case St.Asleep:
                {
                    int x, r;
                    room.WorldToTile(p, out x, out r);
                    if (r <= 7 && p.z >= room.MinZ) Begin();
                    break;
                }
            case St.Drop:
                {
                    pl.controlEnabled = false;
                    float u = Mathf.Clamp01((t - 0.55f) / 0.55f);       // a beat to look up, then he comes down
                    pos = home + Vector3.up * (16f * (1f - u * u));
                    if (u >= 1f)
                    {
                        pos = home;
                        pl.AddTrauma(0.8f);
                        gm.sound.Play("guard_bonk", 1f, 0f, 0.6f);
                        gm.sound.Play("tile_delete", 0.9f, 0f, 0.7f);
                        for (int i = 0; i < 8; i++) gm.fx.Puff(pos + Dir(i * 45f) * 1.7f + Vector3.up * 0.2f, Pal.A(Pal.Ink, 0.7f), 1.5f, 0.6f);
                        shockT = 0f; shock.position = pos + Vector3.up * 0.05f; shock.gameObject.SetActive(true);
                        st = St.Talk; t = 0f; line = -1; talkT = 0f; talkHold = 0.7f;      // a breath before the first word
                    }
                    break;
                }
            case St.Talk:
                {
                    pl.controlEnabled = false;
                    yaw = Mathf.MoveTowardsAngle(yaw, yawYou, 200f * dt);
                    talkT += dt;
                    bool skip = line >= 0 && talkT > 0.35f && (GameInput.ConfirmPressed || GameInput.AttackPressed || GameInput.overrideActive);
                    if (talkT > talkHold || skip) NextLine();
                    // line four: you reach into the old game and the sword comes out
                    if (line == 3 && !swordDrawn && talkT > 1.0f) DrawSword(p);
                    break;
                }
            case St.Stalk:
                {
                    yaw = Mathf.MoveTowardsAngle(yaw, yawYou, 210f * dt);
                    if (dist > 2.9f)
                    {
                        float sp = 2.3f + 0.35f * (Phase - 1);
                        pos = Clamp(pos + Dir(yaw) * sp * dt);
                        walk += sp * dt;
                    }
                    if (cooldown <= 0f && roundT <= 0f)
                    {
                        int k = attackCount;
                        bool near = dist < 5.0f, far = dist > 9f;
                        if (Phase == 1) { if (near) Go(St.ThrustWind); }
                        else if (Phase == 2)
                        {
                            // angry: the first thing he does is his new move, the spin; after that he mixes
                            if (newMove) { if (dist < 5.8f) { newMove = false; Go(St.SpinWind); } }
                            else if (far) Go(St.LeapWind);
                            else if (near) Go(k % 2 == 0 ? St.ThrustWind : St.SpinWind);
                        }
                        else
                        {
                            // furious: he opens with the leap, from wherever he stands
                            if (newMove) { newMove = false; Go(St.LeapWind); }
                            else if (far || k % 3 == 0) Go(St.LeapWind);
                            else if (near) Go(k % 3 == 1 ? St.ThrustWind : St.SpinWind);
                        }
                    }
                    break;
                }
            case St.ThrustWind:
                {
                    // he lines you up, then commits: from there the red lane is where he will go
                    if (t < ThrustLock) yaw = Mathf.MoveTowardsAngle(yaw, yawYou, 420f * dt);
                    if (t >= ThrustWindup)
                    {
                        dashDir = Dir(yaw); dashHit = false;
                        gm.sound.PlayAt("guard_swing", pos + Vector3.up * 2f, 1f, 0f, 0.75f, 0.2f);
                        st = St.ThrustDash; t = 0f;
                    }
                    break;
                }
            case St.ThrustDash:
                {
                    pos = Clamp(pos + dashDir * (DashLength / 0.3f) * dt);
                    walk += DashLength / 0.3f * dt;
                    if (((int)(t * 30f)) != ((int)((t - dt) * 30f))) gm.fx.Puff(pos + Vector3.up * 0.25f, Pal.A(Pal.Ink, 0.5f), 1.1f, 0.4f);
                    if (t >= 0.3f) { st = St.Recover; t = 0f; gm.fx.Puff(pos + Vector3.up * 0.2f, Pal.A(Pal.Ink, 0.6f), 1.6f, 0.5f); }
                    if (!dashHit && dist < DashHit) { dashHit = true; Hit(dirYou, 9.5f); }
                    break;
                }
            case St.Recover:
                if (t >= 1.5f) AfterOpening(dist, dirYou);
                break;
            case St.SpinWind:
                yaw = Mathf.MoveTowardsAngle(yaw, yawYou, 300f * dt);
                if (t >= 0.7f) { st = St.Spin; t = 0f; spinHurt = 0f; gm.sound.PlayAt("guard_swing", pos + Vector3.up * 2f, 1f, 0f, 0.6f, 0.2f); }
                break;
            case St.Spin:
                {
                    yaw += 820f * dt;
                    pos = Clamp(pos + dirYou * 1.8f * dt);
                    if (((int)(t * 5f)) != ((int)((t - dt) * 5f))) gm.sound.PlayAt("guard_swing", pos + Vector3.up * 2f, 0.6f, 0.1f, 0.9f, 0.3f);
                    if (t >= 1.5f) { st = St.Dizzy; t = 0f; }
                    else if (spinHurt <= 0f && dist < SpinHit) { spinHurt = 0.75f; Hit(dirYou, 10f); }
                    break;
                }
            case St.LeapWind:
                yaw = Mathf.MoveTowardsAngle(yaw, yawYou, 300f * dt);
                if (t >= 0.6f)
                {
                    leapFrom = pos; leapTo = Clamp(p);
                    gm.sound.Play("fall", 0.6f, 0f, 1.3f);
                    st = St.LeapAir; t = 0f;
                }
                break;
            case St.LeapAir:
                {
                    float u = Mathf.Clamp01(t / 0.9f);
                    pos = Vector3.Lerp(leapFrom, leapTo, u) + Vector3.up * (5f * Mathf.Sin(u * Mathf.PI));
                    if (u >= 1f)
                    {
                        pos = leapTo;
                        st = St.Dizzy; t = 0f;
                        shockT = 0f;
                        shock.position = pos + Vector3.up * 0.05f;
                        shock.gameObject.SetActive(true);
                        pl.AddTrauma(0.75f);
                        gm.sound.Play("guard_bonk", 1f, 0f, 0.55f);
                        gm.sound.Play("tile_delete", 0.9f, 0f, 0.6f);
                        for (int i = 0; i < 10; i++) gm.fx.Puff(pos + Dir(i * 36f) * 2.2f + Vector3.up * 0.2f, Pal.A(Pal.Ink, 0.7f), 1.6f, 0.6f);
                        Vector3 d2 = p - pos; d2.y = 0f;
                        if (d2.magnitude < ShockRadius) Hit(d2.sqrMagnitude > 0.01f ? d2.normalized : Vector3.back, 11f);
                    }
                    break;
                }
            case St.Dizzy:
                if (t >= 2.0f) AfterOpening(dist, dirYou);
                break;
            case St.Dying:
                {
                    KeepPiecesIn();
                    if (t > 0.9f) pl.controlEnabled = false;
                    if (!saidLast && t > 2.0f)
                    {
                        // his head lies on the floor, and still has the last word
                        saidLast = true;
                        gm.hud.SetCinema(true);
                        gm.hud.SetEyelids(0.1f);
                        var c = gm.sound.Clip("boss_line_4");
                        float len = c != null ? c.length : 3.2f;
                        gm.hud.Dialogue("THE CAPTAIN", Pal.Purple, "Fine... ship it. But fix that bridge.", false, len);
                        gm.sound.PlayVoice("boss_line_4", 1f, 1f);
                        talkHold = 2.0f + len + 0.6f;
                    }
                    if (saidLast && (t > talkHold || (t > 3.2f && (GameInput.ConfirmPressed || GameInput.AttackPressed || GameInput.overrideActive))))
                    {
                        defeated = true;
                        st = St.Gone;
                        gm.hud.Hint("", 0f);
                        gm.sound.StopVoice();
                        gm.hud.HideDialogue();
                        gm.hud.SetEyelids(0f);
                        gm.hud.SetCinema(false);
                        pl.zoom = 0f;
                        pl.lookLocked = false;
                        pl.controlEnabled = true;
                        gm.OnBossDefeated();
                    }
                    break;
                }
        }
        if (resets != startedAt) return;        // you lost your last heart in there: the fight has been put back

        // the "he has changed gear" moment
        if (Fighting && Phase != lastPhase)
        {
            lastPhase = Phase;
            gm.hud.Toast(Phase == 2 ? "the Captain is ANGRY:     now he SPINS" : "the Captain is FURIOUS:     now he LEAPS");
            gm.sound.PlayVoice("guard_say_stop", 1f, 0.72f);
            pl.AddTrauma(0.35f);
            steamClock = 0f;
            // that was the end of this opening: he shakes it off, throws you back, and comes at you with a new move
            newMove = true;
            st = St.Stalk; t = 0f; cooldown = 0.9f;
            blockT = 0.35f;
            if (dist < 4.5f && !GameInput.overrideActive) pl.Knock(dirYou * 11f, 3f);
            SketchCamera.Flash(0.25f, Pal.Red);
            for (int i = 0; i < 8; i++) gm.fx.Puff(Ground + Dir(i * 45f) * 1.9f + Vector3.up * 0.3f, Pal.A(Pal.Red, 0.8f), 1.3f, 0.5f);
        }
        if (Fighting) Combat(dt, p, dist, dirYou);
    }

    /// <summary>His pieces bounce off the edge of the arena instead of sailing over the wall.</summary>
    void KeepPiecesIn()
    {
        float m = Maps.TS + 0.5f;
        foreach (var d in debris)
        {
            if (d == null) continue;
            var rb = d.GetComponent<Rigidbody>();
            if (rb == null) continue;
            Vector3 q = rb.position, v = rb.linearVelocity;
            bool hit = false;
            if (q.x < room.MinX + m) { q.x = room.MinX + m; v.x = Mathf.Abs(v.x) * 0.4f; hit = true; }
            else if (q.x > room.MaxX - m) { q.x = room.MaxX - m; v.x = -Mathf.Abs(v.x) * 0.4f; hit = true; }
            if (q.z < room.MinZ + m) { q.z = room.MinZ + m; v.z = Mathf.Abs(v.z) * 0.4f; hit = true; }
            else if (q.z > room.MaxZ - m) { q.z = room.MaxZ - m; v.z = -Mathf.Abs(v.z) * 0.4f; hit = true; }
            if (hit) { rb.position = q; rb.linearVelocity = v; }
        }
    }

    void Go(St next)
    {
        st = next; t = 0f; attackCount++;
        gm.sound.PlayAt("guard_windup", pos + Vector3.up * 2f, 1f, 0f, 0.7f, 0.2f);
        if (next == St.ThrustWind) gm.sound.PlayVoice("guard_say_hyah", 0.95f, 0.78f);
    }

    void AfterOpening(float dist, Vector3 dirYou)
    {
        st = St.Stalk; t = 0f;
        cooldown = Mathf.Lerp(1.0f, 0.55f, (Phase - 1) / 2f);
        if (dist < 2.8f && !GameInput.overrideActive)
        {
            // he shoves you off with the shield: no harm done, but the opening is over
            blockT = 0.35f;
            gm.player.Knock(dirYou * 9f, 2.6f);
            gm.sound.Play("gate", 0.8f);
            WordAt("BACK OFF!", Pal.Hex("c8c2e6"), Pal.Ink, HeadPos + Vector3.up * 1.2f, 0.45f);
        }
    }

    void Begin()
    {
        rig.Show(true);
        gm.sound.SetHurry(false);
        lastHits = gm.hits;
        if (introSeen)
        {
            pos = home; yaw = 180f;
            st = St.Stalk; t = 0f; cooldown = 1.3f; roundT = 1.3f;
            swordShow = 1f;
            gm.hud.ShowHandheld(false);
            gm.hud.SetFightUi(true);
            gm.hud.ShowBoss(true);
            gm.hud.SetBoss(hp, maxHp);
            gm.hud.Toast(hp < maxHp ? "again!     he is still hurt" : "round two!");
            gm.sound.SetHurry(true);
            return;
        }
        st = St.Drop; t = 0f;
        pos = home + Vector3.up * 16f; yaw = 180f;
        gm.player.controlEnabled = false;
        gm.hud.SetCinema(true);
        gm.hud.SetEyelids(0.1f);
        gm.sound.Duck(0.3f, 40f);
        gm.sound.Play("fall", 0.8f);
    }

    void NextLine()
    {
        line++;
        talkT = 0f;
        if (line >= Lines.Length) { StartFight(); return; }
        Line l = Lines[line];
        var c = gm.sound.Clip(l.voice);
        float len = c != null ? c.length : 3f;
        gm.hud.Dialogue(l.captain ? "THE CAPTAIN" : "YOU", l.captain ? (Color)Pal.Purple : YouColor, l.text, l.captain, len);
        gm.sound.PlayVoice(l.voice, 1f, 1f);
        talkHold = len + 0.45f;
        steamClock = 0f;
        if (l.captain) { gm.player.AddTrauma(line == 0 ? 0.5f : 0.3f); SketchCamera.Flash(line == 0 ? 0.3f : 0.16f, Pal.Red); }
        if (line == 4) { gm.sound.Play("tile_delete", 0.8f, 0f, 0.7f); gm.player.AddTrauma(0.45f); }
    }

    /// <summary>The remake has no sword, so you take the old game's: it comes out of the old game in a flash.</summary>
    void DrawSword(Vector3 p)
    {
        swordDrawn = true;
        gm.sound.Play("key_get", 0.8f);
        gm.sound.Play("coin", 0.6f, 0f, 1.3f);
        SketchCamera.Flash(0.4f, Pal.Teal);
        gm.player.AddTrauma(0.25f);
        Vector2 tile = room.WorldToTileF(p);
        gm.pixel.AddFx("sparkle", 3, 0.5f, tile, room.index);
        for (int i = 0; i < 6; i++) gm.fx.Star(p + Vector3.up * (1.6f + i * 0.22f) + Dir(i * 60f) * 0.5f, i % 2 == 0 ? (Color)Pal.Teal : (Color)Pal.White, 0.7f, 0.6f);
    }

    void StartFight()
    {
        introSeen = true;
        if (!swordDrawn) { swordDrawn = true; }
        swordShow = Mathf.Max(swordShow, 0.01f);
        gm.sound.StopVoice();
        gm.hud.HideDialogue();
        gm.hud.SetEyelids(0f);
        gm.hud.SetCinema(false);
        gm.hud.ShowHandheld(false);
        gm.player.zoom = 0f;
        gm.player.controlEnabled = true;
        st = St.Stalk; t = 0f; cooldown = 1.1f;
        gm.sound.Duck(1f, 0.1f);
        gm.sound.SetHurry(true);
        gm.hud.SetFightUi(true);
        gm.hud.ShowBoss(true);
        gm.hud.SetBoss(hp, maxHp);
        gm.hud.Word("FIGHT!", Pal.Yellow, Pal.Red, 0f, 0.9f);
        gm.hud.PlaceWord(new Vector2(0.5f, 0.72f), 0.9f);
        gm.sound.Play("jingle_hurry", 0.7f);
        gm.hud.Hint("LEFT CLICK  sword combo       SPACE  dodge roll       he BLOCKS until he is stuck or dizzy", 18f);
        if (maxHp < 12) gm.hud.Toast("old-game coins:  " + gm.coins + "     the Captain starts  " + (12 - maxHp) + (12 - maxHp == 1 ? "  hit down" : "  hits down"));
    }

    /// <summary>His weapon found you.</summary>
    void Hit(Vector3 away, float shove)
    {
        if (GameInput.overrideActive) return;       // the test driver checks the fight's flow, not whether it can dodge
        int before = gm.hits;
        gm.HitPlayer(away, shove);
        if (gm.hits != before) PlaceWord(gm.player.transform.position + Vector3.up * 2.3f, 0.5f);
    }

    /// <summary>A comic word pops where the blow landed (small, so it never hides the two of you).</summary>
    void WordAt(string text, Color fill, Color ink, Vector3 world, float scale)
    {
        gm.hud.Word(text, fill, ink, 0f, scale);
        PlaceWord(world, scale);
    }

    void PlaceWord(Vector3 world, float scale)
    {
        Vector3 v = gm.player.cam.WorldToViewportPoint(world);
        if (v.z > 0f) gm.hud.PlaceWord(new Vector2(v.x, v.y), scale);
    }

    // ------------------------------------------------------------------ you: sword and dodge
    float AttackLength { get { return combo == 3 ? 0.5f : 0.34f; } }
    float AttackHitAt { get { return combo == 3 ? 0.19f : 0.12f; } }

    void Combat(float dt, Vector3 p, float dist, Vector3 dirYou)
    {
        var pl = gm.player;
        Vector3 toBoss = -dirYou;

        // the dodge roll: a quick dive the way you are moving (straight back if you are standing still)
        if (dodgeCool > 0f) dodgeCool -= dt;
        if (dodgeT < 0f && dodgeCool <= 0f && GameInput.DodgePressed)
        {
            Vector2 mv = GameInput.Move;
            Vector3 dir = mv.sqrMagnitude > 0.05f ? (pl.transform.forward * mv.y + pl.transform.right * mv.x).normalized : -pl.transform.forward;
            dodgeT = 0f; dodgeCool = 0.7f;
            atkT = -1f; atkQueued = false;
            pl.Knock(dir * 12.5f, 0f);
            gm.invuln = Mathf.Max(gm.invuln, 0.45f);        // nothing touches you mid-roll
            gm.sound.Play("wrap", 0.45f, 0.05f, 1.6f);
            gm.fx.Puff(p + Vector3.up * 0.2f, Pal.A(Pal.Pencil, 0.8f), 1.0f, 0.4f);
        }
        if (dodgeT >= 0f) { dodgeT += dt; if (dodgeT > 0.45f) dodgeT = -1f; }

        // the sword: clicks are remembered for a moment, so a quick second and third click chain into the combo
        if (GameInput.AttackPressed) atkQueued = true;
        if (atkT >= 0f)
        {
            atkT += dt;
            if (!atkHit && atkT >= AttackHitAt)
            {
                atkHit = true;
                slashT = 0f; slashKind = combo;
                if (dist < Reach) Land(dirYou, combo == 3 ? 2 : 1);
            }
            // the next click cuts the follow-through short
            if (atkT >= AttackLength || (atkHit && atkQueued && combo < 3 && atkT >= AttackLength * 0.7f)) { atkT = -1f; comboClock = 0.55f; }
        }
        if (atkT < 0f)
        {
            if (comboClock > 0f) { comboClock -= dt; if (comboClock <= 0f) combo = 0; }
            if (atkQueued && dodgeT < 0f && swordShow > 0.5f)
            {
                atkQueued = false;
                combo = combo >= 3 ? 1 : combo + 1;
                atkT = 0f; atkHit = false;
                gm.sound.Play("guard_swing", 0.75f, 0.04f, combo == 3 ? 0.95f : 1.3f + combo * 0.12f);
                // a step in toward him with every swing
                if (dist > 2.3f && dist < 7f) pl.Knock(toBoss * (combo == 3 ? 7.5f : 5.5f), 0f);
            }
        }
    }

    void Land(Vector3 dirYou, int damage)
    {
        Vector3 spot = Ground + dirYou * (0.6f * Scale) + Vector3.up * (0.85f * Scale);
        if (!Open)
        {
            // clang: he was ready for that, and the block throws you back
            blockT = 0.3f;
            atkQueued = false; combo = 0;
            gm.sound.Play("gate", 0.85f, 0.05f, 1.2f);
            gm.fx.Star(spot, Pal.White, 1.2f, 0.3f);
            gm.fx.Star(spot + Vector3.up * 0.3f, Pal.Yellow, 0.7f, 0.25f);
            WordAt("CLANG!", Pal.Hex("c8c2e6"), Pal.Ink, spot + Vector3.up * 1.6f, 0.42f);
            if (!GameInput.overrideActive) gm.player.Knock(dirYou * 7f, 2f);
            gm.player.AddTrauma(0.2f);
            return;
        }
        hp = Mathf.Max(0, hp - damage);
        flinch = 0.3f;
        pos = Clamp(pos - dirYou * (damage > 1 ? 1.1f : 0.6f));
        gm.sound.Play("guard_bonk", 0.95f, 0.05f, damage > 1 ? 0.9f : 1.3f);
        gm.sound.Play("coin", 0.5f, 0f, 0.8f);
        gm.fx.Star(spot, Pal.Yellow, damage > 1 ? 1.7f : 1.1f, 0.45f);
        gm.fx.Star(spot, Pal.White, damage > 1 ? 1.0f : 0.7f, 0.3f);
        gm.fx.Puff(spot, Pal.A(Pal.Purple, 0.9f), 1.4f, 0.45f);
        for (int i = 0; i < (damage > 1 ? 6 : 3); i++)
            gm.fx.Star(spot + new Vector3(Random.Range(-1f, 1f), Random.Range(-0.4f, 1.2f), Random.Range(-1f, 1f)), Pal.Yellow, 0.45f, 0.4f);
        WordAt(damage > 1 ? "SMASH!" : HitWords[(maxHp - hp) % HitWords.Length], damage > 1 ? (Color)Pal.Yellow : (Color)Pal.Hex("7bd65a"), damage > 1 ? (Color)Pal.Red : (Color)Pal.Ink,
            HeadPos + Vector3.up * 1.4f, damage > 1 ? 0.62f : 0.45f);
        gm.hud.SetBoss(hp, maxHp);
        gm.hud.BossPunch();
        gm.player.AddTrauma(damage > 1 ? 0.45f : 0.26f);
        gm.Freeze(damage > 1 ? 0.13f : 0.06f);
        if (damage > 1) SketchCamera.Flash(0.22f, Pal.White);
        if (hp <= 0) Die(dirYou);
    }

    void Die(Vector3 dirYou)
    {
        st = St.Dying; t = 0f;
        winT = 0f;
        atkT = -1f; atkQueued = false;
        gm.sound.SetHurry(false);
        gm.sound.Play("jingle_room", 0.9f);
        gm.sound.Play("plank_break", 1f, 0f, 0.7f);
        gm.hud.Word("K.O.!", Pal.Yellow, Pal.Red, 0f, 1.0f);
        gm.hud.PlaceWord(new Vector2(0.5f, 0.74f), 1.0f);
        SketchCamera.Flash(0.8f, Pal.White);
        gm.player.AddTrauma(0.8f);
        gm.Freeze(0.25f);
        // he comes apart: every part is thrown clear and tumbles with real physics
        debris.AddRange(rig.Shatter(gm.world.root, pos + dirYou * 1.2f + Vector3.up * 0.4f, 7f, gm.player.GetComponent<CharacterController>()));
        rig.Show(false);
        for (int i = 0; i < 10; i++)
            gm.fx.Star(pos + Vector3.up * (1f + i * 0.25f) + Dir(i * 36f) * 1.2f, i % 2 == 0 ? (Color)Pal.Yellow : (Color)Pal.White, 0.8f, 0.7f);
    }

    // ------------------------------------------------------------------ how it looks (every frame)
    public void Animate(float dt)
    {
        if (rig == null) return;
        float clock = Time.time;
        var pl = gm.player;
        bool here = gm.state == GameManager.State.Playing && gm.cur == room.index;
        if (st == St.Dying && t > 1.2f && gm.hud != null) { gm.hud.ShowBoss(false); gm.hud.SetFightUi(false); }

        // ---- the camera goes first: everything that faces the viewer needs to know where it is
        Cameras(dt, here);
        Vector3 eye = pl.cam.transform.position;

        // ---- him
        if (Visible && st != St.Dying)
        {
            var p = new GuardPose();
            p.visible = true; p.squash = 1f; p.feet = pos; p.yaw = yaw;
            float stride = Mathf.Cos(walk / 1.25f * Mathf.PI);
            float rise = 1f - Mathf.Abs(stride);
            // on guard by default: spear levelled at you, shield up
            p.legL = stride * -30f; p.legR = stride * 30f; p.bob = rise * 0.05f; p.roll = stride * 3f; p.twist = stride * 6f;
            p.spear = 40f; p.shield = -20f; p.shieldOut = 14f; p.lean = 6f;
            float e, roar = 0f;
            bool hisLine = st == St.Talk && line >= 0 && line < Lines.Length && Lines[line].captain;
            switch (st)
            {
                case St.Drop:
                    p.squash = 1.15f; p.spear = -40f; p.legL = -20f; p.legR = -20f;
                    break;
                case St.Talk:
                    Act(ref p, hisLine);
                    break;
                case St.ThrustWind:
                    e = Out(t / ThrustWindup); roar = 0.7f;
                    p.squash = 1f - 0.2f * e; p.lean = -18f * e; p.spear = Mathf.Lerp(40f, -70f, e); p.shieldOut = -8f;
                    p.bang = t < 0.12f ? t / 0.12f : (t < 0.5f ? 1f : Mathf.Clamp01(1f - (t - 0.5f) / 0.12f));
                    break;
                case St.ThrustDash:
                    roar = 1f;
                    p.squash = 1.18f; p.lean = 16f; p.spear = 82f; p.spearAim = 20f; p.legL = 26f; p.legR = -30f; p.shieldOut = 34f;
                    break;
                case St.Recover:
                    p.squash = 0.9f + 0.04f * Mathf.Sin(t * 14f); p.lean = 30f; p.spear = 100f; p.headPitch = 22f; p.legL = 14f; p.legR = -14f; p.bob = 0f;
                    break;
                case St.SpinWind:
                    e = Out(t / 0.7f); roar = 0.7f;
                    p.squash = 1f - 0.14f * e; p.spear = Mathf.Lerp(40f, 92f, e); p.spearAim = -70f * e; p.shieldOut = 60f * e; p.bob = 0.2f * e;
                    p.bang = t < 0.12f ? t / 0.12f : 1f;
                    break;
                case St.Spin:
                    roar = 1f;
                    p.squash = 1.06f; p.spear = 92f; p.spearAim = -70f; p.shieldOut = 60f; p.lean = 0f; p.legL = 20f; p.legR = -20f; p.bob = 0.12f;
                    break;
                case St.LeapWind:
                    e = Out(t / 0.6f); roar = 0.7f;
                    p.squash = 1f - 0.3f * e; p.lean = 12f * e; p.spear = Mathf.Lerp(40f, -45f, e); p.legL = -25f * e; p.legR = -25f * e;
                    p.bang = t < 0.12f ? t / 0.12f : 1f;
                    break;
                case St.LeapAir:
                    {
                        float u = Mathf.Clamp01(t / 0.9f); roar = 1f;
                        p.squash = 1.16f; p.lean = Mathf.Lerp(-22f, 38f, u); p.spear = Mathf.Lerp(-72f, 70f, u); p.legL = -42f; p.legR = -42f; p.bob = 0f;
                        break;
                    }
                case St.Dizzy:
                    p.squash = 1f - 0.3f * Mathf.Exp(-7f * t) * Mathf.Cos(t * 18f) - 0.05f;
                    p.roll = Mathf.Sin(t * 6f) * 12f; p.lean = 10f + Mathf.Sin(t * 4.3f) * 6f; p.headYaw = Mathf.Sin(t * 7f) * 38f; p.headPitch = 12f;
                    p.spear = 18f; p.shield = 10f; p.shieldOut = 40f; p.bob = 0f;
                    roar = 0.35f + 0.25f * Mathf.Sin(t * 9f);
                    break;
            }
            if (flinch > 0f)
            {
                float k = flinch / 0.3f;
                p.squash *= 1f - 0.24f * k; p.lean -= 28f * k; p.roll += 11f * k; p.headPitch -= 24f * k;
                p.shieldOut += 40f * k; p.spear -= 30f * k;
                roar = 1f;
            }
            if (blockT > 0f)
            {
                float k = blockT / 0.35f;
                p.shieldOut = Mathf.Lerp(p.shieldOut, -34f, k); p.shield = Mathf.Lerp(p.shield, -50f, k); p.lean -= 6f * k;
            }
            rig.Apply(p, Vector3.zero, eye);

            // his mouth: it follows the sound of his own voice while he talks, and gapes when he roars
            float want = hisLine ? Mathf.Clamp01(gm.sound.VoiceLevel() * 1.6f) : roar;
            mouthOpen = Mathf.Lerp(mouthOpen, want, 1f - Mathf.Exp(-(want > mouthOpen ? 40f : 16f) * dt));
            float mh = 0.03f + mouthOpen * 0.085f;
            mouth.localScale = new Vector3(0.2f - mouthOpen * 0.03f, mh, 0.03f);
            mouth.localPosition = new Vector3(0f, 0.115f - mh * 0.5f, 0.352f);

            // steam: he is so angry it comes out of his ears (while he shouts, and when he changes gear)
            steamClock += dt;
            bool steaming = hisLine || (Fighting && Phase > 1 && steamClock < 1.6f);
            if (steaming && ((int)(steamClock / 0.16f)) != ((int)((steamClock - dt) / 0.16f)))
            {
                float side = ((int)(steamClock / 0.16f)) % 2 == 0 ? 1f : -1f;
                Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                gm.fx.Puff(HeadPos + right * (side * 0.75f) + Vector3.up * 0.45f, Pal.A(Pal.Red, 0.85f), 0.5f, 0.5f);
            }

            // stars round his head while he can be hurt
            if (Open)
            {
                starClock -= dt;
                if (starClock <= 0f)
                {
                    starClock = 0.11f;
                    gm.fx.Star(HeadPos + Dir(clock * 420f) * 1.1f + Vector3.up * 0.8f, Pal.Yellow, 0.6f, 0.45f);
                }
            }
        }

        Marks(dt, clock);
        Hero(dt, clock, here);
        if (gm.hud != null && Fighting) gm.hud.SetBoss(hp, maxHp);
    }

    /// <summary>Red on the floor where his next blow will land, the ring of his landing, and the sweep of your sword.</summary>
    void Marks(float dt, float clock)
    {
        float pulse = 0.75f + 0.25f * Mathf.Sin(clock * 22f);
        Vector3 g = Ground;

        // the charge: a lane from him along the line he has chosen (it swings with him until he commits)
        bool showLane = st == St.ThrustWind || st == St.ThrustDash;
        if (lane.gameObject.activeSelf != showLane) lane.gameObject.SetActive(showLane);
        if (showLane)
        {
            Vector3 along = st == St.ThrustDash ? dashDir : Dir(yaw);
            float len = st == St.ThrustDash ? Mathf.Max(0.5f, DashLength * (1f - t / 0.3f)) + 1.2f : DashLength + 1.2f;
            lane.position = g + along * (len * 0.5f) + Vector3.up * 0.04f;
            lane.rotation = Quaternion.LookRotation(along, Vector3.up);
            lane.localScale = new Vector3(DashHit * 2f, 1f, len);
            bool committed = st == St.ThrustDash || t >= ThrustLock;
            laneMat.color = Pal.A(Pal.Red, (committed ? 0.42f : 0.2f) * pulse);
        }

        // the spin and the leap: a circle
        bool spin = st == St.SpinWind || st == St.Spin, leap = st == St.LeapWind || st == St.LeapAir;
        bool showDisc = spin || leap;
        if (disc.gameObject.activeSelf != showDisc) { disc.gameObject.SetActive(showDisc); ring.gameObject.SetActive(showDisc); }
        if (showDisc)
        {
            float radius = spin ? SpinHit : ShockRadius;
            Vector3 at = spin ? g : (st == St.LeapAir ? leapTo : gm.player.transform.position);
            at.y = room.FloorY;
            // while he is still winding up the circle grows in, so you see it coming
            float grow = st == St.SpinWind ? Out(t / 0.7f) : (st == St.LeapWind ? Out(t / 0.6f) : 1f);
            disc.position = at + Vector3.up * 0.04f;
            ring.position = at + Vector3.up * 0.045f;
            float ds = radius * 2f * grow, rs = radius / 0.42f * grow;
            disc.localScale = new Vector3(ds, 1f, ds);
            ring.localScale = new Vector3(rs, 1f, rs);
            ring.rotation = Quaternion.Euler(0f, Mathf.Floor(clock * 8f) * 19f, 0f);
            discMat.color = Pal.A(Pal.Red, 0.3f * pulse);
            ringMat.color = Pal.A(Pal.Red, 0.95f);
        }

        // the shockwave ring races outward and fades
        if (shockT >= 0f)
        {
            shockT += dt;
            float u = Mathf.Clamp01(shockT / 0.5f);
            float size = Mathf.Lerp(1f, ShockRadius / 0.42f, 1f - (1f - u) * (1f - u));
            shock.localScale = new Vector3(size, 1f, size);
            shockMat.color = Pal.A(Pal.Red, 1f - u);
            if (u >= 1f) { shockT = -1f; shock.gameObject.SetActive(false); }
        }

        // your sword's sweep hangs in the air for a moment
        bool showSlash = slashT >= 0f;
        if (slash.gameObject.activeSelf != showSlash) slash.gameObject.SetActive(showSlash);
        if (showSlash)
        {
            slashT += dt;
            float u = Mathf.Clamp01(slashT / 0.2f);
            Vector3 at = gm.player.transform.position + Vector3.up * 1.15f;
            slash.position = at;
            if (slashKind == 3) slash.rotation = Quaternion.Euler(0f, heroYaw, 0f) * Quaternion.Euler(0f, 0f, 90f);
            else slash.rotation = Quaternion.Euler(0f, heroYaw, slashKind == 1 ? -16f : 16f);
            float s = (slashKind == 3 ? 1.25f : 1f) * (0.85f + 0.3f * u);
            slash.localScale = new Vector3(slashKind == 2 ? -s : s, s, s);
            slashMat.color = Pal.A(Color.Lerp(Pal.White, Pal.Teal, u), 0.95f * (1f - u * u));
            if (u >= 1f) slashT = -1f;
        }
    }

    // ------------------------------------------------------------------ you (every frame)
    void Hero(float dt, float clock, bool here)
    {
        var pl = gm.player;
        var h = new HeroPose();
        // you only see yourself once the camera is far enough out of your head; hurt, you blink
        bool blink = hurtT > 0f && dodgeT < 0f && ((int)(Time.unscaledTime * 16f)) % 2 == 0;
        h.visible = here && tp > 0.3f && !blink;
        if (gm.hits != lastHits) { lastHits = gm.hits; hurtT = 0.9f; atkT = -1f; atkQueued = false; combo = 0; }
        if (hurtT > 0f) hurtT -= dt;
        if (swordDrawn && swordShow < 1f) swordShow = Mathf.Min(1f, swordShow + dt / 0.22f);
        if (winT >= 0f) winT += dt;
        if (!h.visible) { hero.Apply(h); return; }

        Vector3 hp3 = pl.transform.position;
        Vector3 toBoss = Ground - hp3; toBoss.y = 0f;
        float bossYaw = toBoss.sqrMagnitude > 0.01f ? Mathf.Atan2(toBoss.x, toBoss.z) * Mathf.Rad2Deg : pl.yaw;
        heroYaw = Mathf.MoveTowardsAngle(heroYaw, st == St.Gone ? pl.yaw : bossYaw, 720f * dt);
        h.feet = new Vector3(hp3.x, hp3.y - 0.04f, hp3.z);
        h.yaw = heroYaw;
        h.squash = 1f;

        // --- running: one stride per 1.9 m, arms and legs in opposite pairs
        float speed = pl.speedNow;
        float run = Mathf.Clamp01(speed / 3.2f);
        runPhase += speed * dt / 1.9f * Mathf.PI * 2f;
        float sw = Mathf.Sin(runPhase);
        h.legL = sw * 44f * run; h.legR = -sw * 44f * run;
        h.armL = -sw * 36f * run; h.armLOut = 8f;
        h.bob = Mathf.Abs(Mathf.Cos(runPhase)) * 0.06f * run + Mathf.Sin(clock * 2.3f) * 0.012f * (1f - run);
        h.lean = 10f * run;
        h.roll = sw * 3f * run;
        // --- on guard: the sword upright in front of him
        float readyArm = -40f + sw * 7f * run, readyYaw = 14f, readySword = 58f;
        h.armR = readyArm; h.armRYaw = readyYaw; h.sword = readySword;
        h.swordShow = swordShow;
        h.headPitch = -6f;       // he looks up at the Captain

        // --- a swing
        if (atkT >= 0f)
        {
            float a = atkT, hitAt = AttackHitAt, len = AttackLength;
            float wind = Mathf.Clamp01(a / (hitAt * 0.55f));
            float cut = a < hitAt * 0.55f ? 0f : Out((a - hitAt * 0.55f) / (hitAt * 0.9f));
            float back = Smooth((a - hitAt * 1.5f) / Mathf.Max(0.01f, len - hitAt * 1.5f));
            float arm, armYaw, swordTilt, twist, lean;
            if (combo == 3)
            {
                // the smash: both feet off the ground, the sword from behind his head down to the floor
                arm = Mathf.Lerp(Mathf.Lerp(readyArm, -182f, wind), -22f, cut); armYaw = 0f; swordTilt = Mathf.Lerp(150f, 178f, cut);
                twist = 0f; lean = Mathf.Lerp(-14f * wind, 30f, cut);
                h.bob += Mathf.Sin(Mathf.Clamp01(a / (hitAt * 1.3f)) * Mathf.PI) * 0.42f;
                h.squash = 1f + 0.14f * wind * (1f - cut) - 0.2f * cut * (1f - back);
                h.legL = Mathf.Lerp(h.legL, -34f, 1f - back); h.legR = Mathf.Lerp(h.legR, 30f, 1f - back);
            }
            else
            {
                // a flat cut, right to left, then back again
                float dir = combo == 1 ? 1f : -1f;
                armYaw = Mathf.Lerp(Mathf.Lerp(readyYaw, 96f * dir, wind), -84f * dir, cut);
                arm = -86f; swordTilt = 176f;
                twist = armYaw * 0.5f; lean = 13f;
                h.legL = Mathf.Lerp(h.legL, 24f * dir, 1f - back); h.legR = Mathf.Lerp(h.legR, -24f * dir, 1f - back);
            }
            h.armR = Mathf.Lerp(arm, readyArm, back);
            h.armRYaw = Mathf.Lerp(armYaw, readyYaw, back);
            h.sword = Mathf.Lerp(swordTilt, readySword, back);
            h.twist = twist * (1f - back);
            h.lean = Mathf.Lerp(lean, h.lean, back);
            h.armL = Mathf.Lerp(30f, h.armL, back); h.armLOut = Mathf.Lerp(34f, 8f, back);
        }

        // --- the dodge: tucked up, one whole forward roll
        if (dodgeT >= 0f)
        {
            float u = Mathf.Clamp01(dodgeT / 0.45f);
            h.tumble = 360f * Out(u);
            h.squash = 0.72f + 0.28f * Smooth((u - 0.7f) / 0.3f);
            h.legL = -70f * (1f - u); h.legR = -70f * (1f - u);
            h.armL = -60f; h.armR = -60f; h.sword = 120f;
            h.bob = -0.12f * Mathf.Sin(u * Mathf.PI);
        }

        // --- hit: thrown back on his heels for a moment
        if (hurtT > 0.55f)
        {
            float k = (hurtT - 0.55f) / 0.35f;
            h.lean = Mathf.Lerp(h.lean, -30f, k); h.squash *= 1f - 0.16f * k;
            h.armL = Mathf.Lerp(h.armL, -70f, k); h.armLOut = Mathf.Lerp(h.armLOut, 60f, k);
            h.armR = Mathf.Lerp(h.armR, -70f, k); h.headPitch = -22f * k;
        }

        // --- the scene: he acts too
        if (st == St.Drop)
        {
            // something very large is coming down: he looks up and takes a step back
            float k = Smooth(t / 0.5f);
            h.headPitch = -30f * k; h.lean = -10f * k; h.armL = -20f * k; h.armLOut = 40f * k; h.armR = -20f * k; h.armRYaw = 40f * k;
        }
        else if (st == St.Talk)
        {
            bool mine = line >= 0 && line < Lines.Length && !Lines[line].captain;
            float talk = mine ? Mathf.Clamp01(gm.sound.VoiceLevel() * 1.6f) : 0f;
            h.headPitch = -14f + talk * 8f;
            h.squash = 1f + talk * 0.03f;
            if (line == 0) { h.lean = -8f; h.armLOut = 26f; }                                   // HALT: he leans away from the spear
            else if (line == 1)
            {
                // "I'm shipping it": a tired shrug
                float k = Smooth(talkT / 0.4f) * (1f - Smooth((talkT - 2.6f) / 0.5f));
                h.armL = -24f * k; h.armLOut = 46f * k; h.armR = -24f * k; h.armRYaw = 48f * k; h.roll = 5f * k; h.headYaw = 12f * k; h.headPitch = -4f;
            }
            else if (line == 2) { h.headPitch = -18f; h.roll = Mathf.Sin(talkT * 3f) * 2f; }       // being laughed at
            else if (line == 3)
            {
                // he reaches up, and the old game's sword is in his hand
                float up = Smooth((talkT - 0.5f) / 0.5f), down = Smooth((talkT - 2.6f) / 0.7f);
                h.armR = Mathf.Lerp(Mathf.Lerp(readyArm, -176f, up), readyArm, down);
                h.armRYaw = Mathf.Lerp(readyYaw, 0f, up * (1f - down));
                h.sword = Mathf.Lerp(readySword, 176f, up * (1f - down));
                h.headPitch = Mathf.Lerp(-10f, -34f, up * (1f - down));
                h.bob += 0.06f * up * (1f - down);
            }
            else if (line == 4) { h.lean = 8f; h.armLOut = 20f; h.legL = 14f; h.legR = -14f; }   // TAKE your build: he sets his feet
        }

        // --- he won
        if (winT >= 0f && st != St.Gone)
        {
            float k = Smooth((winT - 0.3f) / 0.4f);
            h.armR = Mathf.Lerp(h.armR, -176f, k); h.armRYaw = Mathf.Lerp(h.armRYaw, 0f, k); h.sword = Mathf.Lerp(h.sword, 176f, k);
            h.bob += Mathf.Abs(Mathf.Sin(winT * 6f)) * 0.14f * k * Mathf.Clamp01(2.2f - winT);
            h.armL = Mathf.Lerp(h.armL, -150f, k * 0.8f); h.armLOut = Mathf.Lerp(h.armLOut, 30f, k);
        }
        hero.Apply(h);
    }

    // ------------------------------------------------------------------ the camera
    /// <summary>Which set-up the camera is on: -1 the fight camera, 0 and up the shots of the scene.</summary>
    int ShotNow()
    {
        if (st == St.Drop) return 0;
        if (st == St.Talk) return 1 + Mathf.Clamp(line, 0, Lines.Length - 1);
        if (st == St.Dying && saidLast) return 6;
        return -1;
    }

    void Cameras(float dt, bool here)
    {
        var pl = gm.player;
        Transform ct = pl.cam.transform;
        // out of your head from the moment he appears until his last word
        bool want = here && Visible;
        tp = Mathf.MoveTowards(tp, want ? 1f : 0f, dt / 0.75f);
        if (tp <= 0f)
        {
            if (camHeld) { ct.localPosition = Vector3.zero; ct.localRotation = Quaternion.identity; camHeld = false; shot = -99; }
            return;
        }

        Vector3 you = pl.transform.position;
        Vector3 g = Ground;
        Vector3 toBoss = g - you; toBoss.y = 0f;
        float d = toBoss.magnitude;
        Vector3 fwd = d > 0.2f ? toBoss / d : Dir(pl.yaw);
        Vector3 side = Vector3.Cross(Vector3.up, fwd);              // to the right of the line from you to him
        Vector3 up = Vector3.up;
        Vector3 wantPos, focus;
        float zoom = 0f;
        int now = ShotNow();
        switch (now)
        {
            case 0:     // he lands: from behind you, low, looking up at where he comes down
                wantPos = you - fwd * 3.4f + side * 1.5f + up * 1.2f;
                focus = Vector3.Lerp(you + up * 1.4f, home + up * 2.4f, 0.8f);
                break;
            case 1:     // HALT!  low and close, up his spear at his face
                wantPos = g - fwd * (4.4f - 0.5f * Out(talkT / 3f)) + side * 1.25f + up * 1.0f;
                focus = HeadPos - up * 0.3f; zoom = 12f;
                break;
            case 2:     // you, from in front
                wantPos = you + fwd * (3.0f - 0.25f * Out(talkT / 3f)) + side * 1.0f + up * 1.55f;
                focus = you + up * 0.7f; zoom = 10f;
                break;
            case 3:     // he laughs: both of you from the side, small you and enormous him
                wantPos = (you + g) * 0.5f - side * (d * 0.55f + 4.6f) + up * 1.3f;
                focus = (you + g) * 0.5f + up * 1.9f; zoom = 2f;
                break;
            case 4:     // you draw the old sword: from low down, looking up at it
                wantPos = you + fwd * 3.3f - side * 1.1f + up * 0.55f;
                focus = you + up * 1.25f; zoom = 0f;
                break;
            case 5:     // TAKE your build!  his face again, from the other side, closer
                wantPos = g - fwd * 3.8f - side * 1.2f + up * 0.9f;
                focus = HeadPos - up * 0.25f; zoom = 14f;
                break;
            case 6:     // his head, on the floor, has the last word
                {
                    Vector3 head = debris.Count > 1 && debris[1] != null ? debris[1].transform.position : g;
                    head.y = room.FloorY;
                    Vector3 away = you - head; away.y = 0f;
                    away = away.sqrMagnitude > 0.05f ? away.normalized : Vector3.back;
                    wantPos = head + away * 2.7f + Vector3.Cross(up, away) * 0.7f + up * 0.85f;
                    focus = head + up * 0.3f; zoom = 14f;
                    break;
                }
            default:    // the fight: over your shoulder, far enough back to keep both of you in the picture
                {
                    float far = Mathf.InverseLerp(3f, 13f, d);
                    wantPos = you - fwd * Mathf.Lerp(5.2f, 7.4f, far) + side * 2.6f + up * Mathf.Lerp(4.3f, 5.2f, far);
                    focus = Vector3.Lerp(you + up * 1.0f, new Vector3(pos.x, Mathf.Lerp(room.FloorY, pos.y, 0.5f) + 1.7f, pos.z), 0.55f);
                    break;
                }
        }
        // keep the camera inside the walls
        float m = Maps.TS + 0.45f;
        wantPos.x = Mathf.Clamp(wantPos.x, room.MinX + m, room.MaxX - m);
        wantPos.z = Mathf.Clamp(wantPos.z, room.MinZ + m, room.MaxZ - m);

        if (!camHeld || now != shot)
        {
            // a new set-up is a cut (the fight camera eases in from wherever the scene left it)
            if (now >= 0 || !camHeld) camPos = wantPos;
            shot = now;
        }
        camPos = Vector3.Lerp(camPos, wantPos, 1f - Mathf.Exp(-(now < 0 ? 6f : 3f) * dt));
        camHeld = true;
        if (InScene || st == St.Drop) pl.zoom = zoom; else if (Fighting) pl.zoom = 0f;

        Quaternion rot = Quaternion.LookRotation((focus - camPos).normalized, up);
        // hits shake this camera too
        float sh = pl.trauma * pl.trauma;
        float n = Time.unscaledTime * 28f;
        rot *= Quaternion.Euler((Mathf.PerlinNoise(n, 1.3f) - 0.5f) * sh * 7f, (Mathf.PerlinNoise(n, 7.7f) - 0.5f) * sh * 7f, (Mathf.PerlinNoise(n, 13.1f) - 0.5f) * sh * 5f);
        float e = Smooth(tp);
        ct.position = Vector3.Lerp(pl.pivot.position, camPos, e);
        ct.rotation = Quaternion.Slerp(pl.pivot.rotation, rot, e);
    }

    /// <summary>His acting during the talk: every line gets its own body language.</summary>
    void Act(ref GuardPose p, bool hisLine)
    {
        float tt = talkT;
        float talk = hisLine ? mouthOpen : 0f;          // his whole head nods to his own voice
        p.legL = 0f; p.legR = 0f; p.bob = 0f; p.roll = 0f; p.twist = 0f;
        p.headPitch = 10f + talk * 9f;                  // he looks down at you
        p.squash = 1f + talk * 0.04f;
        switch (line)
        {
            case -1:    // just landed: he rises out of the crouch
                p.squash = 1f - 0.25f * Mathf.Exp(-6f * tt) * Mathf.Cos(tt * 16f); p.spear = -10f;
                break;
            case 0:     // HALT!  spear straight at your face, leaning in
                p.spear = Mathf.Lerp(-30f, 78f, Out(tt / 0.25f)); p.spearAim = 18f; p.lean = 14f + talk * 5f; p.shieldOut = 30f;
                p.bang = tt < 0.9f ? Mathf.Clamp01(tt / 0.1f) : Mathf.Clamp01(1f - (tt - 0.9f) / 0.2f);
                p.roll = Mathf.Sin(tt * 43f) * 1.6f;                // he is shaking with rage
                break;
            case 1:     // you talk: he tilts his head and taps a foot
                p.spear = -4f; p.roll = 7f; p.headYaw = 14f; p.lean = 2f; p.legR = -Mathf.Abs(Mathf.Sin(tt * 7f)) * 16f;
                break;
            case 2:     // he laughs at you, head thrown back, belly shaking
                {
                    float laugh = Smooth((tt - 2.4f) / 0.3f);
                    p.lean = Mathf.Lerp(8f, -16f, laugh) + Mathf.Sin(tt * 19f) * 5f * laugh;
                    p.headPitch = Mathf.Lerp(10f + talk * 9f, -24f, laugh);
                    p.squash = 1f + Mathf.Sin(tt * 19f) * 0.06f * laugh + talk * 0.04f;
                    p.spear = Mathf.Lerp(52f, -8f, laugh) + Mathf.Sin(tt * 19f) * 6f * laugh; p.spearAim = 14f * (1f - laugh);
                    p.shield = -30f * laugh; p.shieldOut = 50f * laugh;
                    break;
                }
            case 3:     // you draw the sword: he starts back
                {
                    float jolt = tt > 1.0f ? Out((tt - 1.0f) / 0.2f) : 0f;
                    p.lean = -14f * jolt; p.spear = Mathf.Lerp(-4f, 30f, jolt); p.shieldOut = -20f * jolt; p.shield = -40f * jolt; p.headPitch = 10f - 16f * jolt;
                    p.bang = tt > 1.0f && tt < 2.1f ? Mathf.Clamp01((tt - 1.0f) / 0.1f) : 0f;
                    break;
                }
            case 4:     // TAKE your build!  a stomp, the spear shaken overhead, then levelled at you
                {
                    float hop = tt < 0.4f ? Mathf.Sin(tt / 0.4f * Mathf.PI) : 0f;
                    p.feet += Vector3.up * hop * 0.9f;
                    p.squash = tt < 0.4f ? 1.1f : 1f - 0.22f * Mathf.Exp(-7f * (tt - 0.4f)) * Mathf.Cos((tt - 0.4f) * 18f) + talk * 0.04f;
                    float shake = tt < 1.5f ? 1f : 0f;
                    p.spear = Mathf.Lerp(74f, -34f + Mathf.Sin(tt * 30f) * 9f, shake); p.spearAim = 18f * (1f - shake); p.lean = Mathf.Lerp(14f, -6f, shake); p.shieldOut = 40f;
                    p.roll = Mathf.Sin(tt * 43f) * 2f;
                    break;
                }
        }
    }

    static float Out(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
