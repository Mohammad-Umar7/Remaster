using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Slime
{
    public SlimeDef def;
    public float d, total, hopClock, hitFlash;
    public bool hopped;
    public Vector2 pos;     // tile units, centre of the slime
    public Vector2 dir = Vector2.right;     // which way it is heading (tile units, y points down the screen)
    public float hopPeriod = 0.44f;         // seconds per hop
    public int steps;                       // hops so far (left foot, right foot)
    public bool gone;                       // its floor was deleted under it
    readonly float[] seg;

    public Slime(SlimeDef def)
    {
        this.def = def;
        int n = def.loop.Length;
        seg = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector2Int a = def.loop[i], b = def.loop[(i + 1) % n];
            seg[i] = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
            total += seg[i];
        }
        Reset();
    }

    public void Reset()
    {
        d = def.phase * total; hopClock = 0f; hitFlash = 0f; steps = 0; gone = false;
        pos = PosAtDistance(d);
        Vector2 ahead = PosAtDistance((d + 0.05f) % total) - pos;
        if (ahead.sqrMagnitude > 1e-6f) dir = ahead.normalized;
    }

    public void Tick(float dt)
    {
        d = (d + def.speed * dt) % total;
        Vector2 before = pos;
        pos = PosAtDistance(d);
        Vector2 moved = pos - before;
        if (moved.sqrMagnitude > 1e-8f) dir = moved.normalized;
        hopped = false;
        hopClock += dt;
        if (hopClock >= hopPeriod) { hopClock -= hopPeriod; hopped = true; steps++; }
        if (hitFlash > 0f) hitFlash -= dt;
    }

    /// <summary>Where this slime will be in <paramref name="seconds"/> (it follows a fixed loop).</summary>
    public Vector2 PosIn(float seconds) { return PosAtDistance((d + def.speed * Mathf.Max(0f, seconds)) % total); }

    Vector2 PosAtDistance(float dist)
    {
        float left = dist;
        int n = def.loop.Length;
        for (int i = 0; i < n; i++)
        {
            if (left <= seg[i] || i == n - 1)
            {
                Vector2 a = def.loop[i], b = def.loop[(i + 1) % n];
                float t = seg[i] > 0f ? Mathf.Clamp01(left / seg[i]) : 0f;
                Vector2 p = Vector2.Lerp(a, b, t);
                return new Vector2(p.x + 0.5f, p.y + 0.5f);
            }
            left -= seg[i];
        }
        return pos;
    }
}

public class RoomState
{
    public bool[,] coinTaken = new bool[Maps.W, Maps.H];
    public bool[,] revealed = new bool[Maps.W, Maps.H];
    public bool[,] fakeFound = new bool[Maps.W, Maps.H];
    public bool[,] broken = new bool[Maps.W, Maps.H];           // one-use planks that are gone
    public float[,] breakIn = new float[Maps.W, Maps.H];        // > 0: this plank snaps in that many seconds
    public float[] rowCrumble = new float[Maps.H];              // -1 intact, 0..1 crumbling, 2 deleted
    public List<Slime> slimes = new List<Slime>();
    public List<Slime> guards = new List<Slime>();             // new in the remake: only the 3D world shows them
    public bool keyTaken;                                       // the exit is locked until this is true
    public bool cleared, waveActive;
    public float waveClock;
    public int waveRow;
    public float clock;                 // seconds spent in this room since the last (re)start: drives flicker and lasers
    public int flickerPhase = -1;       // which flicker group is solid right now (-1 = not applied yet)
    public int warnTicks;               // warning ticks already played in this flicker phase
    public float[] hotUntil;            // per laser volley: room-clock time until which its beams hurt
}

/// <summary>The rules of REMASTER: the old game is the truth.</summary>
public class GameManager : MonoBehaviour
{
    public static GameManager I;
    public enum State { Boot, Title, Intro, Playing, Ending, Results }
    public State state = State.Boot;

    public Room[] rooms;
    public RoomState[] rs;
    public int cur;
    public const int MaxHearts = 3;
    public int hearts = MaxHearts, coins, totalCoins, falls, hits, clearedCount;
    public float time, invuln, corridorT;
    public int corridorFrom = -1;
    public bool paused;
    public int tileX, tileR;

    public PlayerController player;
    public World3D world;
    public PixelGame pixel;
    public Hud hud;
    public AudioDirector sound;
    public IntroDirector intro;
    public Fx fx;
    public Camera screenCam;

    public const float WaveInterval = 1.8f, CrumbleTime = 0.75f, PlankBreakDelay = 0.32f;
    int lastTileX = -99, lastTileR = -99;
    bool firstBridge, firstFake, firstWrap, firstCoin, firstSlime, firstFall, firstBreak, firstFlicker, firstLaser, chipFoot;
    bool firstKey, firstLocked, firstGuardSeen, firstGuardHit;
    float lockedCooldown;
    int hitStopId;
    bool[] hasOneUse;
    static readonly string[] LayerNames = { "drums", "bass", "strings", "bells" };

    void Awake()
    {
        I = this;
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = 120;
    }

    void Start()
    {
        TexLib.Build();
        PixelArt.Load();

        // a camera that only clears the real screen: everything visible is UI showing render textures
        var sc = new GameObject("ScreenCamera");
        screenCam = sc.AddComponent<Camera>();
        screenCam.clearFlags = CameraClearFlags.SolidColor;
        screenCam.backgroundColor = Pal.PaperDark;
        screenCam.cullingMask = 0;
        screenCam.depth = -50;
        screenCam.allowHDR = false; screenCam.allowMSAA = false;

        // the sun: one directional light whose shadows the sketch shader draws as hatching
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        Vector3 toSun = new Vector3(-0.60f, 0.62f, -0.15f).normalized;
        sun.transform.rotation = Quaternion.LookRotation(-toSun);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;
        sun.shadowBias = 0.06f; sun.shadowNormalBias = 0.5f;
        sun.intensity = 1f; sun.color = Color.white;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowDistance = 62f;
        QualitySettings.shadowCascades = 2;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;
        Shader.SetGlobalVector("_RM_LightDir", new Vector4(toSun.x, toSun.y, toSun.z, 0));

        rooms = new Room[Maps.Defs.Length];
        rs = new RoomState[rooms.Length];
        for (int i = 0; i < rooms.Length; i++)
        {
            rooms[i] = new Room(Maps.Defs[i], i);
            rs[i] = new RoomState();
            for (int r = 0; r < Maps.H; r++) rs[i].rowCrumble[r] = -1f;
            foreach (var sd in Maps.Defs[i].slimes) rs[i].slimes.Add(new Slime(sd));
            foreach (var gd in Maps.Defs[i].guards)
            {
                // a faster guard takes quicker steps, so you can hear how fast it is
                var g = new Slime(gd);
                g.hopPeriod = Mathf.Clamp(0.55f / Mathf.Max(0.1f, gd.speed), 0.27f, 0.8f);
                rs[i].guards.Add(g);
            }
            if (Maps.Defs[i].lasers != null) rs[i].hotUntil = new float[Maps.Defs[i].lasers.Length];
            totalCoins += rooms[i].coins.Count;
        }
        hasOneUse = new bool[rooms.Length];
        for (int i = 0; i < rooms.Length; i++)
            for (int r = 0; r < Maps.H; r++)
                for (int x = 0; x < Maps.W; x++)
                    if (rooms[i].IsOneUse(x, r)) hasOneUse[i] = true;

        world = new World3D();
        world.Build(rooms);

        player = PlayerController.Create();
        player.onStep = OnStep;
        player.Teleport(rooms[0].TileCenter(rooms[0].SpawnTile), 0f);

        fx = new GameObject("Fx").AddComponent<Fx>();
        fx.world = world;
        fx.SetCamera(player.cam.transform);

        sound = new GameObject("Audio").AddComponent<AudioDirector>();
        pixel = new PixelGame();
        hud = new GameObject("Hud").AddComponent<Hud>();
        hud.Build(this);
        intro = new GameObject("Intro").AddComponent<IntroDirector>();
        intro.Build(this);

        if (Boot.skipIntro) StartGame(Boot.startRoom);
        else GoTitle();
    }

    /// <summary>How many remastered music layers you have earned after clearing this many rooms.</summary>
    int LayersFor(int cleared) { return Mathf.Clamp(cleared * 4 / Mathf.Max(1, rooms.Length - 1), 0, 4); }

    // ------------------------------------------------------------------ flow
    void GoTitle()
    {
        state = State.Title;
        player.controlEnabled = false;
        hud.SetLayout(true);
        intro.ShowTitle();
    }

    void BeginIntro()
    {
        state = State.Intro;
        intro.PlayIntro(() => StartGame(0));
    }

    public void StartGame(int roomIndex)
    {
        state = State.Playing;
        paused = false; Time.timeScale = 1f;
        intro.Hide();
        hud.SetLayout(false);
        hud.ShowPlayUi(true);
        if (roomIndex == 0) { time = 0f; coins = 0; falls = 0; hits = 0; }
        clearedCount = roomIndex;
        for (int i = 0; i < rooms.Length; i++) { rs[i].cleared = i < roomIndex; rs[i].keyTaken = i < roomIndex; }
        world.SnapDoors(this);
        hearts = MaxHearts;
        player.controlEnabled = true;
        Respawn(roomIndex, false);
        pixel.bootTimer = 1.0f;
        sound.Play("jingle_boot", 0.8f);
        sound.StartMusic();
        sound.SetLayers(LayersFor(clearedCount));
        sound.SetMusicLevel(1f);
        hud.RefreshTodo();
        hud.ShowRoomTitle(cur);
        hud.ShowControlsHint();
    }

    void Finish()
    {
        state = State.Ending;
        player.controlEnabled = false;
        sound.SetHurry(false);
        if (!rs[cur].cleared) { rs[cur].cleared = true; clearedCount++; }
        sound.SetLayers(4);
        hud.RefreshTodo();
        hud.ShowPlayUi(false);
        intro.PlayEnding(() =>
        {
            state = State.Results;
            hud.ShowResults(time, coins, totalCoins, falls, hits);
        });
    }

    public void JumpToRoom(int i)
    {
        if (i < 0 || i >= rooms.Length) return;
        if (state == State.Results || state == State.Ending) return;
        if (state != State.Playing) { intro.Abort(); StartGame(i); return; }
        clearedCount = i;
        for (int k = 0; k < rooms.Length; k++) { rs[k].cleared = k < i; rs[k].keyTaken = k < i; }
        world.SnapDoors(this);
        sound.SetLayers(LayersFor(clearedCount));
        hearts = MaxHearts;
        Respawn(i, true);
        hud.RefreshTodo();
        hud.ShowRoomTitle(i);
    }

    // ------------------------------------------------------------------ frame
    void Update()
    {
        float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        SketchCamera.flash = Mathf.MoveTowards(SketchCamera.flash, 0f, udt * 2.6f);

        if (GameInput.F11Pressed) Screen.fullScreen = !Screen.fullScreen;
        if (GameInput.TabPressed && state == State.Playing) { hud.ToggleFloatSize(); sound.Play("ui_move", 0.6f); }
        // room-jump keys are for demos: only from the title or the pause menu, never by accident mid-run
        int num = GameInput.NumberPressed;
        if (num > 0 && num <= rooms.Length && (state == State.Title || (state == State.Playing && paused))) { if (paused) SetPaused(false); JumpToRoom(num - 1); }
        else num = 0;

        switch (state)
        {
            case State.Title:
                if (GameInput.AnyKeyPressed && num == 0) BeginIntro();
                break;
            case State.Playing:
                if (GameInput.PausePressed) SetPaused(!paused);
                if (paused)
                {
                    if (GameInput.RestartPressed) { SetPaused(false); RestartRoom(); }
                    else if (GameInput.QuitPressed) { SetPaused(false); Reload(); }
                    break;
                }
                TickPlaying(Time.deltaTime);
                break;
            case State.Results:
                if (GameInput.ConfirmPressed) Reload();
                break;
        }

        bool wantLock = (state == State.Playing && !paused) || state == State.Intro || state == State.Ending;
        if (wantLock && Application.isFocused) { if (Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; } }
        else if (!wantLock && Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }

    void LateUpdate()
    {
        if (world != null && state != State.Boot) world.Animate(this, Time.deltaTime);
        if (pixel != null) pixel.Render(this, Time.deltaTime);
    }

    void SetPaused(bool p)
    {
        paused = p;
        Time.timeScale = p ? 0f : 1f;
        hud.ShowPause(p);
        sound.SetMusicLevel(p ? 0.35f : 1f);
        sound.Play("ui_pause", 0.6f);
    }

    public void Reload()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    void TickPlaying(float dt)
    {
        time += dt;
        if (invuln > 0f) invuln -= dt;
        int last = rooms.Length - 1;
        Vector3 p = player.transform.position;

        if (cur == last && p.z >= world.portalTriggerZ) { Finish(); return; }

        // which room are we in?
        if (cur < last && p.z >= rooms[cur + 1].MinZ) EnterRoom(cur + 1, true);
        else if (cur > 0 && p.z < rooms[cur - 1].MaxZ) EnterRoom(cur - 1, false);
        Room room = rooms[cur];
        RoomState s = rs[cur];

        // between rooms: tell the old game how far to scroll
        if (p.z >= room.MaxZ && cur < last) { corridorFrom = cur; corridorT = Mathf.InverseLerp(room.MaxZ, rooms[cur + 1].MinZ, p.z); }
        else if (p.z < room.MinZ && cur > 0) { corridorFrom = cur - 1; corridorT = Mathf.InverseLerp(rooms[cur - 1].MaxZ, room.MinZ, p.z); }
        else corridorFrom = -1;

        if (p.y < room.FloorY - 5f) { Fall(); return; }

        bool inRoom = p.z >= room.MinZ && p.z < room.MaxZ;
        if (!inRoom)
        {
            // stepping out of the room off a one-use plank still snaps it
            if (lastTileX != -99 && room.IsOneUse(lastTileX, lastTileR) && !s.broken[lastTileX, lastTileR] && s.breakIn[lastTileX, lastTileR] <= 0f)
                s.breakIn[lastTileX, lastTileR] = PlankBreakDelay;
            lastTileX = -99;
            TickPlanks(room, s, dt);
            return;
        }

        if (room.def.wrap)
        {
            if (p.x < room.MinX) { DoWrap(room, 1); p = player.transform.position; }
            else if (p.x >= room.MaxX) { DoWrap(room, -1); p = player.transform.position; }
        }

        s.clock += dt;
        room.WorldToTile(p, out tileX, out tileR);
        if (tileX != lastTileX || tileR != lastTileR)
        {
            int px = lastTileX, pr = lastTileR;
            lastTileX = tileX; lastTileR = tileR;
            OnLeaveTile(room, s, px, pr);
            OnEnterTile(room, s, tileX, tileR);
        }

        // coins: they only exist in the old game, so you find them by walking where it says
        if (room.HasCoin(tileX, tileR) && !s.coinTaken[tileX, tileR] && s.rowCrumble[tileR] < 2f)
        {
            Vector3 c = room.TileCenter(tileX, tileR);
            if (new Vector2(p.x - c.x, p.z - c.z).magnitude < Maps.TS * 0.45f) Collect(room, s, tileX, tileR);
        }

        TickKey(room, s, p, dt);
        TickGuards(room, s, p, dt);
        TickSlimes(room, s, p, dt);
        TickWave(room, s, dt);
        TickFlicker(room, s);
        TickPlanks(room, s, dt);
        TickLasers(room, s, dt);
        CheckStranded(room, s, dt);
    }

    void EnterRoom(int i, bool forward)
    {
        int prev = cur;
        cur = i;
        lastTileX = -99;
        if (forward)
        {
            if (!rs[prev].cleared)
            {
                rs[prev].cleared = true;
                int before = LayersFor(clearedCount);
                clearedCount++;
                int after = LayersFor(clearedCount);
                sound.Play("jingle_room", 0.8f);
                sound.SetLayers(after);
                if (after > before)
                {
                    StartCoroutine(After(0.9f, () => sound.Play("jingle_layer", 0.6f)));
                    hud.Toast("checkpoint!     soundtrack remastered:  + " + LayerNames[Mathf.Clamp(after - 1, 0, 3)]);
                }
                else hud.Toast("checkpoint!");
                hud.RefreshTodo();
            }
            hearts = MaxHearts;
            ResetRoomDynamic(i);
            hud.ShowRoomTitle(i);
        }
        else hud.ShowRoomTitle(i);
        hud.RefreshKey();
    }

    void OnLeaveTile(Room room, RoomState s, int x, int r)
    {
        if (!room.In(x, r)) return;
        // a one-use plank snaps a moment after you step off it
        if (room.IsOneUse(x, r) && !s.broken[x, r] && s.breakIn[x, r] <= 0f) s.breakIn[x, r] = PlankBreakDelay;
    }

    void OnEnterTile(Room room, RoomState s, int x, int r)
    {
        if (!room.In(x, r)) return;
        if (room.IsBridge(x, r) && !s.revealed[x, r] && IsSolidNow(cur, x, r))
        {
            s.revealed[x, r] = true;
            fx.RevealBridge(world.BridgeRenderers(cur, x, r));
            sound.Play((x + r) % 2 == 0 ? "scribble_1" : "scribble_2", 0.5f, 0.06f);
            if (!firstBridge) { firstBridge = true; hud.Popup("It holds! The old game says there's a bridge here.", 3.4f); }
        }
        if (room.IsFake(x, r) && !s.fakeFound[x, r])
        {
            s.fakeFound[x, r] = true;
            world.MarkFakeFound(cur, x, r);
            sound.Play("ghostwall", 0.8f);
            SketchCamera.Flash(0.30f, Pal.Paper);
            player.AddTrauma(0.22f);
            if (!firstFake) { firstFake = true; hud.Popup("No collision! That wall was only decoration.", 3.4f); }
        }
        if (room.def.deleteWave && !s.waveActive && r <= 7) StartWave(s);
    }

    void Collect(Room room, RoomState s, int x, int r)
    {
        s.coinTaken[x, r] = true;
        coins++;
        sound.Play("coin", 0.75f);
        pixel.AddFx("sparkle", 3, 0.35f, new Vector2(x + 0.5f, r + 0.5f), cur);
        Vector3 c = room.TileCenter(x, r);
        fx.Star(c + Vector3.up * 0.55f, Pal.Yellow, 0.55f, 0.7f);
        hud.CoinPulse();
        if (!firstCoin) { firstCoin = true; hud.Popup("A coin! They only exist in the old game.", 2.8f); }
    }

    // ------------------------------------------------------------------ new in the remake: keys and guards
    /// <summary>Is this room's exit open? It is locked until you hold the room's key.</summary>
    public bool ExitOpen(int ri) { return !rooms[ri].HasKey || rs[ri].keyTaken; }

    /// <summary>
    /// The key exists only in the 3D remake: the old game has no idea, so you have to look for it.
    /// It also keeps the exit shut, and a locked exit says so when you walk into it.
    /// </summary>
    void TickKey(Room room, RoomState s, Vector3 p, float dt)
    {
        if (!room.HasKey) return;
        if (lockedCooldown > 0f) lockedCooldown -= dt;
        if (s.keyTaken) return;

        Vector3 kc = room.TileCenter(room.def.key);
        if (new Vector2(p.x - kc.x, p.z - kc.z).magnitude < Maps.KeyReach && Mathf.Abs(p.y - room.FloorY) < 1.2f)
        {
            s.keyTaken = true;
            sound.Play("key_get", 0.85f);
            StartCoroutine(After(0.5f, () => sound.Play("door_unlock", 0.8f)));
            fx.Star(kc + Vector3.up * 1.15f, Pal.Yellow, 0.9f, 0.7f);
            fx.Star(kc + Vector3.up * 1.15f, Pal.White, 0.5f, 0.45f);
            SketchCamera.Flash(0.22f, Pal.Yellow);
            hud.KeyPulse();
            hud.RefreshKey();
            if (!firstKey) { firstKey = true; hud.Popup("Got the key! The exit is open. Keys are NEW: the old game has none.", 4.0f); }
            else hud.Toast("key found!     the exit is open");
            return;
        }

        // walking into the locked exit
        Vector3 gate = world.GatePos(room.index);
        if (lockedCooldown <= 0f && Mathf.Abs(p.x - gate.x) < Maps.TS * 0.5f && p.z > gate.z - 1.5f && p.z < gate.z + 0.4f)
        {
            lockedCooldown = 2.6f;
            sound.Play("gate", 0.8f);
            player.AddTrauma(0.25f);
            world.RattleGate(room.index);
            hud.KeyPulse();
            if (!firstLocked) { firstLocked = true; hud.Popup("Locked! The key is NEW in the remake: look for it in the 3D world.", 4.2f); }
            else hud.Popup("Locked. Find the key first: only the 3D world shows it.", 2.6f);
        }
    }

    /// <summary>Guards are new in the remake too, so the old game cannot see them: watch the 3D world.</summary>
    void TickGuards(Room room, RoomState s, Vector3 p, float dt)
    {
        if (s.guards.Count == 0) return;
        Vector2 pf = room.WorldToTileF(p);
        foreach (var g in s.guards)
        {
            if (g.gone) continue;
            g.Tick(dt);
            Vector3 wp = room.TileFToWorld(g.pos);
            // a guard whose floor has been deleted drops out of the level
            int gx = Mathf.FloorToInt(g.pos.x), gr = Mathf.FloorToInt(g.pos.y);
            if (room.In(gx, gr) && s.rowCrumble[gr] >= 2f)
            {
                g.gone = true;
                fx.Puff(wp + Vector3.up * 0.8f, Pal.A(Pal.Magenta, 0.9f), 1.2f, 0.5f);
                continue;
            }
            float dist = Vector2.Distance(pf, g.pos);
            if (g.hopped && dist < 6.5f)
            {
                // you hear a guard before you see it; the quick ones only clank on every other step
                // so a room with two of them does not turn into a drum roll
                if (g.hopPeriod >= 0.42f || g.steps % 2 == 0) sound.PlayAt("guard_step", wp + Vector3.up * 0.4f, 0.8f, 0.07f);
                fx.Puff(wp + Vector3.up * 0.06f, Pal.A(Pal.Pencil, 0.6f), 0.4f, 0.35f);
            }
            if (!firstGuardSeen && dist < 4.2f)
            {
                firstGuardSeen = true;
                hud.Popup("A guard! Guards are NEW too: the old game cannot see them. Watch the 3D world.", 4.4f);
            }
            if (invuln <= 0f && dist < Maps.GuardHit)
            {
                g.hitFlash = 0.4f;
                Hurt("Caught by a guard! The old game cannot see them: keep an eye on the 3D world.", ref firstGuardHit);
            }
        }
    }

    // ------------------------------------------------------------------ hazards
    void TickSlimes(Room room, RoomState s, Vector3 p, float dt)
    {
        if (s.slimes.Count == 0) return;
        Vector2 pf = room.WorldToTileF(p);
        foreach (var sl in s.slimes)
        {
            sl.Tick(dt);
            Vector3 wp = room.TileFToWorld(sl.pos);
            if (sl.hopped && Vector3.Distance(wp, p) < 11f)
            {
                // you cannot see them in 3D, but you can hear where they are and see the dust they kick up
                sound.PlayAt("slime_hop", wp + Vector3.up * 0.3f, 0.75f, 0.08f);
                fx.Puff(wp + Vector3.up * 0.1f, Pal.A(Pal.Pencil, 0.75f), 0.5f, 0.4f);
            }
            if (invuln <= 0f && Vector2.Distance(pf, sl.pos) < 0.62f)
            {
                sl.hitFlash = 0.35f;
                Hurt("Ouch! Something un-modelled bit you. Watch the old game.", ref firstSlime);
            }
        }
    }

    /// <summary>Sprite flicker: plank groups A and B take turns existing. You can hear each switch.</summary>
    void TickFlicker(Room room, RoomState s)
    {
        bool any = false;
        for (int r = 0; r < Maps.H && !any; r++)
            for (int x = 0; x < Maps.W; x++)
                if (room.IsFlicker(x, r)) { any = true; break; }
        if (!any) return;

        float period = room.def.flickerPeriod;
        int phase = Mathf.FloorToInt(s.clock / period) % 2;
        float left = period - (s.clock % period);
        if (phase != s.flickerPhase)
        {
            bool first = s.flickerPhase < 0;
            s.flickerPhase = phase;
            s.warnTicks = 0;
            ApplyFlicker(room, s);
            if (!first)
            {
                sound.Play(phase == 0 ? "flick_a" : "flick_b", 0.7f);
                if (!firstFlicker) { firstFlicker = true; hud.Popup("They blink! Cross while a plank is solid. Listen for the beat.", 3.6f); }
            }
        }
        // two quick ticks before the planks swap: an audible countdown
        int want = left < 0.28f ? 2 : (left < 0.62f ? 1 : 0);
        if (want > s.warnTicks) { s.warnTicks = want; sound.Play("flick_warn", 0.55f, 0f, want == 2 ? 1.18f : 1f); }
    }

    void ApplyFlicker(Room room, RoomState s)
    {
        for (int r = 0; r < Maps.H; r++)
            for (int x = 0; x < Maps.W; x++)
            {
                if (!room.IsFlicker(x, r)) continue;
                bool solid = room.FlickerSolidAt(room.c[x, r], s.clock);
                world.SetTileSolid(room.index, x, r, solid);
                var rds = world.BridgeRenderers(room.index, x, r);
                if (rds != null) foreach (var rd in rds) world.SetReveal(rd, (solid && s.revealed[x, r]) ? 1f : 0f);
            }
    }

    /// <summary>One-use planks: they snap a moment after you leave them.</summary>
    void TickPlanks(Room room, RoomState s, float dt)
    {
        for (int r = 0; r < Maps.H; r++)
            for (int x = 0; x < Maps.W; x++)
            {
                if (s.breakIn[x, r] <= 0f) continue;
                s.breakIn[x, r] -= dt;
                if (s.breakIn[x, r] > 0f) continue;
                s.breakIn[x, r] = 0f;
                s.broken[x, r] = true;
                world.SetTileSolid(room.index, x, r, false);
                var rds = world.BridgeRenderers(room.index, x, r);
                if (rds != null) foreach (var rd in rds) world.SetReveal(rd, 0f);
                Vector3 c = room.TileCenter(x, r);
                sound.PlayAt("plank_break", c, 0.9f, 0.06f);
                fx.Puff(c + Vector3.up * 0.1f, Pal.A(Pal.Magenta, 0.9f), 0.9f, 0.5f);
                pixel.AddFx("poof", 3, 0.3f, new Vector2(x + 0.5f, r + 0.5f), room.index);
                if (!firstBreak) { firstBreak = true; hud.Popup("It snapped behind you. No going back: solve the maze in the old game first.", 4.2f); }
            }
    }

    // ------------------------------------------------------------------ nobody gets soft-locked in the maze
    float strandClock, strandTimer;
    bool strandWarned;

    /// <summary>Is there still a way from this tile to the exit over planks that have not snapped?</summary>
    bool ExitReachable(Room room, RoomState s, int x0, int r0)
    {
        var seen = new bool[Maps.W, Maps.H];
        var q = new Queue<Vector2Int>();
        q.Enqueue(new Vector2Int(x0, r0)); seen[x0, r0] = true;
        int[] dx = { 0, 1, 0, -1 }, dr = { -1, 0, 1, 0 };
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (c == room.exit) return true;
            for (int i = 0; i < 4; i++)
            {
                int nx = c.x + dx[i], nr = c.y + dr[i];
                if (room.def.wrap) { if (nx < 0) nx = Maps.W - 1; else if (nx >= Maps.W) nx = 0; }
                if (!room.In(nx, nr) || seen[nx, nr] || !room.TruthWalkable(nx, nr)) continue;
                if (s.broken[nx, nr] || s.breakIn[nx, nr] > 0f || s.rowCrumble[nr] >= 2f) continue;
                seen[nx, nr] = true;
                q.Enqueue(new Vector2Int(nx, nr));
            }
        }
        return false;
    }

    /// <summary>
    /// One-use planks can leave you with no way forward. On a plank that is a dead end: you are told.
    /// Standing safely on real floor with every route gone would be a silent soft-lock, so the room resets itself.
    /// </summary>
    void CheckStranded(Room room, RoomState s, float dt)
    {
        if (!hasOneUse[room.index]) return;
        strandClock += dt;
        if (strandClock < 0.35f) return;
        float step = strandClock; strandClock = 0f;
        if (!room.In(tileX, tileR) || !IsSolidNow(room.index, tileX, tileR)) return;
        if (ExitReachable(room, s, tileX, tileR)) { strandWarned = false; strandTimer = 0f; return; }
        if (room.IsOneUse(tileX, tileR))
        {
            if (!strandWarned) { strandWarned = true; hud.Popup("Dead end. Step off the plank to try the maze again.", 3.6f); }
        }
        else
        {
            if (!strandWarned) { strandWarned = true; hud.Popup("No route left. The old game resets the room...", 2.2f); }
            strandTimer += step;
            if (strandTimer >= 1.7f)
            {
                strandTimer = 0f; strandWarned = false;
                Respawn(cur, true);
                sound.Play("respawn", 0.6f);
            }
        }
    }

    /// <summary>Lasers: the old game is three seconds ahead. It shows the line first; the remake fires it late.</summary>
    void TickLasers(Room room, RoomState s, float dt)
    {
        var vols = room.def.lasers;
        if (vols == null) return;
        float P = room.def.laserPeriod;
        float now = s.clock, before = s.clock - dt;
        for (int i = 0; i < vols.Length; i++)
        {
            var v = vols[i];
            // the old game fires it now (a small "pew" in the window)...
            if (Crossed(before + Maps.LaserLead, now + Maps.LaserLead, v.t, P))
            {
                sound.Play("laser_old", 0.6f, 0.03f);
                if (!firstLaser) { firstLaser = true; hud.Popup("Red line in the old game = a laser there in 3 seconds.", 4.2f); }
            }
            // ...and the remake fires it three seconds later
            if (Crossed(before, now, v.t, P))
            {
                s.hotUntil[i] = now + Maps.LaserHot;
                sound.Play("laser_fire", 0.85f, 0.04f);
                world.FireLasers(room, v, fx);
                player.AddTrauma(0.12f);
            }
            if (now < s.hotUntil[i] && invuln <= 0f && v.Hits(tileX, tileR))
            {
                bool dummy = true;
                SketchCamera.Flash(0.5f, Pal.Red);
                Hurt(null, ref dummy);
            }
        }
    }

    /// <summary>Did a looping clock pass time <paramref name="t"/> between <paramref name="a"/> and <paramref name="b"/>?</summary>
    static bool Crossed(float a, float b, float t, float period)
    {
        if (b <= a) return false;
        float k = Mathf.Floor((a - t) / period) + 1f;      // first loop index whose fire time is after a
        float fire = t + k * period;
        return fire > a && fire <= b;
    }

    /// <summary>Seconds until this volley next fires in the 3D remake.</summary>
    public float LaserCountdown(Room room, RoomState s, LaserVolley v)
    {
        float P = room.def.laserPeriod;
        float d = (v.t - s.clock) % P;
        if (d < 0f) d += P;
        return d;
    }

    void Hurt(string firstTimeText, ref bool firstFlag)
    {
        hearts--;
        hits++;
        invuln = 1.7f;
        sound.Play("hurt", 0.85f);
        player.AddTrauma(0.5f);
        SketchCamera.Flash(0.42f, Pal.Red);
        pixel.Shake(10);
        hud.HurtFlash();
        StartCoroutine(HitStop(0.06f));
        if (firstTimeText != null && !firstFlag) { firstFlag = true; hud.Popup(firstTimeText, 3.4f); }
        if (hearts <= 0)
        {
            sound.Play("jingle_gameover", 0.8f);
            hud.Popup("GAME OVER...   just kidding. Back to the door.", 3f);
            hearts = MaxHearts;
            Respawn(cur, true);
        }
    }

    void Fall()
    {
        falls++;
        sound.Play("fall", 0.7f);
        if (!firstFall) { firstFall = true; hud.Popup("Nothing there. Check the old game before you step.", 3.2f); }
        Respawn(cur, true);
        StartCoroutine(After(0.28f, () => sound.Play("respawn", 0.6f)));
    }

    void RestartRoom()
    {
        hearts = MaxHearts;
        Respawn(cur, true);
        sound.Play("respawn", 0.6f);
    }

    void Respawn(int i, bool flash)
    {
        cur = i;
        ResetRoomDynamic(i);
        player.Teleport(rooms[i].TileCenter(rooms[i].SpawnTile), 0f);
        invuln = 1.0f;
        lastTileX = -99;
        corridorFrom = -1;
        if (flash) SketchCamera.Flash(0.95f, Pal.Paper);
    }

    void ResetRoomDynamic(int i)
    {
        var s = rs[i];
        Room room = rooms[i];
        foreach (var sl in s.slimes) sl.Reset();
        foreach (var g in s.guards) g.Reset();
        s.clock = 0f;
        s.flickerPhase = -1;
        s.warnTicks = 0;
        if (s.hotUntil != null) for (int k = 0; k < s.hotUntil.Length; k++) s.hotUntil[k] = -1f;

        bool hideReveals = room.def.deleteWave;
        // one-use planks come back
        for (int r = 0; r < Maps.H; r++)
            for (int x = 0; x < Maps.W; x++)
            {
                s.breakIn[x, r] = 0f;
                if (room.IsOneUse(x, r))
                {
                    s.broken[x, r] = false;
                    world.SetTileSolid(i, x, r, true);
                    hideReveals = true;
                }
            }
        if (room.def.deleteWave)
        {
            s.waveActive = false; s.waveClock = 0f; s.waveRow = Maps.H - 1;
            for (int r = 0; r < Maps.H; r++) s.rowCrumble[r] = -1f;
            world.RestoreRoom(i);
            sound.SetHurry(false);
        }
        if (hideReveals)
        {
            fx.ClearReveals();
            for (int r = 0; r < Maps.H; r++)
                for (int x = 0; x < Maps.W; x++)
                    if (s.revealed[x, r])
                    {
                        s.revealed[x, r] = false;
                        var l = world.BridgeRenderers(i, x, r);
                        if (l != null) foreach (var rd in l) world.SetReveal(rd, 0f);
                    }
        }
        // put flicker planks into their starting state right away
        TickFlicker(room, s);
    }

    void DoWrap(Room room, int dir)
    {
        // the old game's bug: leave one side of the screen, appear on the other
        player.Shift(new Vector3(dir * Maps.W * Maps.TS, 0, 0));
        sound.Play("wrap", 0.8f);
        SketchCamera.Flash(0.28f, Pal.Teal);
        player.AddTrauma(0.2f);
        int r;
        int x;
        room.WorldToTile(player.transform.position, out x, out r);
        pixel.AddFx("warp", 3, 0.4f, new Vector2(dir > 0 ? Maps.W - 0.5f : 0.5f, r + 0.5f), cur);
        pixel.AddFx("warp", 3, 0.4f, new Vector2(dir > 0 ? 0.5f : Maps.W - 0.5f, r + 0.5f), cur);
        lastTileX = -99;
        if (!firstWrap) { firstWrap = true; hud.Popup("SCREEN WRAP! The 1989 bug still works.", 3.2f); }
    }

    void StartWave(RoomState s)
    {
        s.waveActive = true;
        s.waveRow = Maps.H - 1;
        s.waveClock = WaveInterval * 0.5f;
        sound.Play("jingle_hurry", 0.8f);
        sound.SetHurry(true);
        hud.Popup("The unfinished floor is being deleted.  RUN!", 2.6f);
    }

    void TickWave(Room room, RoomState s, float dt)
    {
        if (!s.waveActive) return;
        for (int r = 0; r < Maps.H; r++)
        {
            if (s.rowCrumble[r] >= 0f && s.rowCrumble[r] < 1f)
            {
                s.rowCrumble[r] += dt / CrumbleTime;
                if (s.rowCrumble[r] >= 1f)
                {
                    s.rowCrumble[r] = 2f;
                    world.SetRowDeleted(cur, r, true);
                    sound.Play("tile_delete", 0.55f, 0.12f, 0.8f);
                }
            }
        }
        s.waveClock += dt;
        if (s.waveClock >= WaveInterval && s.waveRow >= 1)
        {
            s.waveClock = 0f;
            s.rowCrumble[s.waveRow] = 0.0001f;
            world.SetRowCrumbling(cur, s.waveRow);
            sound.Play("tile_delete", 0.6f, 0.1f);
            pixel.Shake(3);
            s.waveRow--;
        }
    }

    // ------------------------------------------------------------------ truth queries (also used by the auto-walker)
    /// <summary>Can you stand on this tile right now, according to the old game?</summary>
    public bool IsSolidNow(int ri, int x, int r)
    {
        Room room = rooms[ri];
        RoomState s = rs[ri];
        if (!room.TruthWalkable(x, r)) return false;
        if (s.rowCrumble[r] >= 2f) return false;
        if (s.broken[x, r]) return false;
        if (room.IsFlicker(x, r) && !room.FlickerSolidAt(room.c[x, r], s.clock)) return false;
        return true;
    }

    /// <summary>Seconds until row <paramref name="r"/> of the deleting floor is gone (infinite if it never goes).</summary>
    float RowGoneIn(RoomState s, int r)
    {
        if (!s.waveActive) return float.PositiveInfinity;
        if (s.rowCrumble[r] >= 2f) return 0f;
        if (s.rowCrumble[r] >= 0f) return (1f - s.rowCrumble[r]) * CrumbleTime;
        if (r < 1 || r > s.waveRow) return float.PositiveInfinity;
        return (WaveInterval - s.waveClock) + (s.waveRow - r) * WaveInterval + CrumbleTime;
    }

    /// <summary>
    /// Will this tile be safe to stand on for the whole window from <paramref name="t0"/> to <paramref name="t1"/>
    /// seconds from now? Every hazard here follows a fixed pattern, so the answer is exact.
    /// </summary>
    public bool SafeBetween(int ri, int x, int r, float t0, float t1)
    {
        Room room = rooms[ri];
        RoomState s = rs[ri];
        if (!room.In(x, r) || !room.TruthWalkable(x, r)) return false;
        if (s.broken[x, r]) return false;
        if (s.breakIn[x, r] > 0f && s.breakIn[x, r] < t1 + 0.1f) return false;
        if (RowGoneIn(s, r) < t1 + 0.35f) return false;
        if (room.IsFlicker(x, r))
        {
            char ch = room.c[x, r];
            float period = room.def.flickerPeriod;
            float a = s.clock + Mathf.Max(0f, t0), b = s.clock + t1 + 0.2f;
            if (!room.FlickerSolidAt(ch, a) || !room.FlickerSolidAt(ch, b)) return false;
            if (Mathf.FloorToInt(a / period) != Mathf.FloorToInt(b / period)) return false;
        }
        var vols = room.def.lasers;
        if (vols != null)
        {
            float P = room.def.laserPeriod;
            for (int i = 0; i < vols.Length; i++)
            {
                if (!vols[i].Hits(x, r)) continue;
                // unsafe if a fire time (plus the time the beam stays hot) overlaps the window
                float lo = s.clock + t0 - Maps.LaserHot - 0.15f, hi = s.clock + t1 + 0.15f;
                float k = Mathf.Ceil((lo - vols[i].t) / P);
                float fire = vols[i].t + k * P;
                if (fire <= hi) return false;
                if (s.clock < s.hotUntil[i] && t0 < Maps.LaserHot) return false;
            }
        }
        if (s.slimes.Count > 0)
        {
            Vector2 c = new Vector2(x + 0.5f, r + 0.5f);
            for (int k = 0; k <= 4; k++)
            {
                float t = Mathf.Lerp(Mathf.Max(0f, t0), t1, k / 4f);
                // a slime bites within 0.62 of a tile; leave a little room for not standing dead centre
                foreach (var sl in s.slimes)
                    if (Vector2.Distance(sl.PosIn(t), c) < 0.86f) return false;
            }
        }
        if (s.guards.Count > 0)
        {
            Vector2 c = new Vector2(x + 0.5f, r + 0.5f);
            for (int k = 0; k <= 5; k++)
            {
                float t = Mathf.Lerp(Mathf.Max(0f, t0), t1, k / 5f);
                foreach (var g in s.guards)
                    if (!g.gone && Vector2.Distance(g.PosIn(t), c) < Maps.GuardClear) return false;
            }
        }
        return true;
    }

    void OnStep(bool sprint)
    {
        if (state != State.Playing) return;
        Room room = rooms[cur];
        Vector3 p = player.transform.position;
        int x, r;
        room.WorldToTile(p, out x, out r);
        bool inRoom = p.z >= room.MinZ && p.z < room.MaxZ;
        if (inRoom && room.IsBridge(x, r))
        {
            // standing on something that only exists in the old game: your steps sound like the old game
            chipFoot = !chipFoot;
            sound.Play(chipFoot ? "step_chip_1" : "step_chip_2", 0.45f);
            fx.Footprint(new Vector3(p.x, room.FloorY, p.z), player.yaw, Pal.A(Pal.Pencil, 0.9f));
        }
        else
        {
            sound.Play("step_paper_" + Random.Range(1, 5), sprint ? 0.5f : 0.38f, 0.07f);
        }
        hud.StepKick(sprint ? 5f : 3f);
    }

    IEnumerator After(float seconds, System.Action act)
    {
        yield return new WaitForSeconds(seconds);
        if (act != null) act();
    }

    IEnumerator HitStop(float seconds)
    {
        int id = ++hitStopId;
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(seconds);
        if (id == hitStopId && !paused) Time.timeScale = 1f;
    }
}
