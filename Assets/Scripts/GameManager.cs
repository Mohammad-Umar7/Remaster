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
    // a guard's behaviour. Its patrol never changes, so the auto-player's maths stay exact:
    // noticing you and striking only change how it looks, sounds, and when the heart is lost.
    public bool alerted;                    // it has noticed you
    public float alertT;                    // 0..1, eased: how far it is into its on-guard stance
    public float bangT = -1f;               // seconds since the "!" popped (-1: not showing)
    public float attackT = -1f;             // seconds into a spear strike (-1: not striking)
    public bool attackSwung, attackLanded;
    public Vector2 attackDir = Vector2.up;  // which way the strike goes, in tile space
    public float attackReach;               // metres it lunges (or hops back) so it ends a spear's length from you
    public Vector2 lookAt;                  // where you are, in tile units (for its head)
    // the chase (guards only): step inside its ring and it leaves its patrol to come after you, for a few
    // tiles or a few seconds. Then it walks back the way it came and carries on where it left off.
    public int mode;                        // 0 on patrol, 1 chasing you, 2 walking back to its patrol
    public float chaseClock, chaseT;        // seconds on this chase; 0..1 eased, for how it looks
    public readonly List<Vector2> trail = new List<Vector2>();     // breadcrumbs: the way back (first = where it left the loop)
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
        alerted = false; alertT = 0f; bangT = -1f; attackT = -1f; attackSwung = false; attackLanded = false;
        mode = 0; chaseClock = 0f; chaseT = 0f; trail.Clear();
        pos = PosAtDistance(d);
        Vector2 ahead = PosAtDistance((d + 0.05f) % total) - pos;
        if (ahead.sqrMagnitude > 1e-6f) dir = ahead.normalized;
    }

    public void Tick(float dt)
    {
        if (mode == 0)
        {
            // on patrol: along the loop. (Off it, the game moves the guard and the loop waits where it was left.)
            d = (d + def.speed * dt) % total;
            Vector2 before = pos;
            pos = PosAtDistance(d);
            Vector2 moved = pos - before;
            if (moved.sqrMagnitude > 1e-8f) dir = moved.normalized;
        }
        hopped = false;
        hopClock += dt * (mode == 1 ? 1.4f : 1f);       // a chasing guard takes quicker steps
        if (hopClock >= hopPeriod) { hopClock -= hopPeriod; hopped = true; steps++; }
        if (hitFlash > 0f) hitFlash -= dt;
    }

    /// <summary>
    /// Where this slime will be in <paramref name="seconds"/> (it follows a fixed loop).
    /// A guard that is off its loop cannot be predicted: it is treated as staying where it is.
    /// </summary>
    public Vector2 PosIn(float seconds)
    {
        if (mode != 0) return pos;
        return PosAtDistance((d + def.speed * Mathf.Max(0f, seconds)) % total);
    }

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
    public BossFight boss;              // the last room's fight (null if no room is marked as the boss room)
    int bossRoom = -1;
    public Camera screenCam;

    public const float WaveInterval = 1.8f, CrumbleTime = 0.75f, PlankBreakDelay = 0.32f;
    int lastTileX = -99, lastTileR = -99;
    bool firstBridge, firstFake, firstWrap, firstSlime, firstFall, firstBreak, firstFlicker, firstLaser, chipFoot;
    bool firstGuardHit, firstChase;
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
        player.canStand = CanStandAt;
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
        for (int i = 0; i < rooms.Length; i++) if (rooms[i].def.boss) bossRoom = i;
        if (bossRoom >= 0) { boss = new BossFight(); boss.Build(this, rooms[bossRoom]); }

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

    bool quitting;

    /// <summary>
    /// The start screen: START or QUIT. Up / down (W S, arrows, d-pad, stick) or the mouse picks one,
    /// Enter / Space / A or a click confirms it. Esc only points at QUIT: it never quits by itself.
    /// </summary>
    void TickTitle()
    {
        if (quitting) return;
        // the test driver has no hands: it just starts
        if (GameInput.overrideActive) { if (GameInput.AnyKeyPressed) BeginIntro(); return; }

        int sel = hud.menuIndex;
        if (GameInput.MenuUpPressed) sel = 0;
        else if (GameInput.MenuDownPressed || GameInput.PausePressed) sel = 1;
        int over = hud.MenuAt(GameInput.PointerPosition);
        if (over >= 0 && GameInput.PointerMoved) sel = over;
        bool click = GameInput.ClickPressed && over >= 0;
        if (click) sel = over;
        if (sel != hud.menuIndex) { hud.SetMenu(sel); sound.Play("ui_move", 0.6f); }

        if (click || GameInput.ConfirmPressed)
        {
            hud.PunchMenu();
            sound.Play("ui_ok", 0.7f);
            if (sel == 0) BeginIntro();
            else StartCoroutine(QuitRoutine());
        }
    }

    /// <summary>QUIT: the dev finally does the "TODO: sleep" on the wall. Lights out, then the game closes.</summary>
    IEnumerator QuitRoutine()
    {
        quitting = true;
        hud.SetCaption("TODO: sleep.   Done.   Goodnight!");
        yield return new WaitForSecondsRealtime(0.75f);
        hud.SetFadeSpeed(2.6f);
        hud.SetFade(1f, Pal.K);
        yield return new WaitForSecondsRealtime(0.6f);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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
        if (boss != null) boss.NewRun();
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
        if (boss != null) boss.NewRun();
        player.controlEnabled = true;
        world.SnapDoors(this);
        sound.SetLayers(LayersFor(clearedCount));
        hearts = MaxHearts;
        Respawn(i, true);
        hud.RefreshTodo();
        hud.ShowRoomTitle(i);
    }

    void SkipTo(int room, string say)
    {
        if (paused) SetPaused(false);
        JumpToRoom(room);
        sound.Play("ui_ok", 0.7f);
        hud.Toast(say);
    }

    // ------------------------------------------------------------------ frame
    void Update()
    {
        float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        SketchCamera.flash = Mathf.MoveTowards(SketchCamera.flash, 0f, udt * 2.6f);

        if (GameInput.F11Pressed) Screen.fullScreen = !Screen.fullScreen;
        if (GameInput.TabPressed && state == State.Playing) { hud.ToggleHandheld(); sound.Play("ui_move", 0.6f); }
        // room-jump keys are for demos: only from the title or the pause menu, never by accident mid-run
        int num = GameInput.NumberPressed;
        if (num > 0 && num <= rooms.Length && (state == State.Title || (state == State.Playing && paused))) { if (paused) SetPaused(false); JumpToRoom(num - 1); }
        else num = 0;

        // skip keys, for anyone who is stuck (a judge has three minutes): N goes to the next room, P back to
        // the previous one, B straight to the last fight. They work while playing and from the pause menu;
        // B from the title too.
        if (num == 0)
        {
            bool playing = state == State.Playing;
            if (playing && GameInput.SkipRoomPressed && cur < rooms.Length - 1) { SkipTo(cur + 1, "skipped to the next room"); num = -1; }
            else if (playing && GameInput.PrevRoomPressed && cur > 0) { SkipTo(cur - 1, "back to the previous room"); num = -1; }
            else if (GameInput.SkipToBossPressed && bossRoom >= 0 && (state == State.Title || (playing && cur != bossRoom))) { SkipTo(bossRoom, "skipped to the last fight"); num = -1; }
        }

        switch (state)
        {
            case State.Title:
                if (num == 0) TickTitle();
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
        if (boss != null && state != State.Boot) boss.Animate(Time.deltaTime);
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

        // the last room: the Captain (he keeps going even if you back out into the doorway)
        if (room.def.boss && boss != null) boss.Tick(dt, p);

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
                    hud.Toast("checkpoint!     + " + LayerNames[Mathf.Clamp(after - 1, 0, 3)]);
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
            if (!firstBridge && !room.IsOneUse(x, r)) { firstBridge = true; hud.Popup("It holds! Trust the old game.", 2.2f); }
        }
        if (room.IsFake(x, r) && !s.fakeFound[x, r])
        {
            s.fakeFound[x, r] = true;
            world.MarkFakeFound(cur, x, r);
            sound.Play("ghostwall", 0.8f);
            SketchCamera.Flash(0.30f, Pal.Paper);
            player.AddTrauma(0.22f);
            if (!firstFake) { firstFake = true; hud.Popup("No collision! Only decoration.", 2.2f); }
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
    }

    // ------------------------------------------------------------------ new in the remake: keys and guards
    /// <summary>Is this room's exit open? It is locked until you hold the room's key.</summary>
    public bool ExitOpen(int ri)
    {
        // the last exit has no key: the Captain stands in front of it, and it opens when he is beaten
        if (rooms[ri].def.boss) return boss == null || boss.defeated;
        return !rooms[ri].HasKey || rs[ri].keyTaken;
    }

    /// <summary>
    /// The key exists only in the 3D remake: the old game has no idea, so you have to look for it.
    /// It also keeps the exit shut, and a locked exit says so when you walk into it.
    /// </summary>
    void TickKey(Room room, RoomState s, Vector3 p, float dt)
    {
        if (!room.HasKey && !room.def.boss) return;
        if (lockedCooldown > 0f) lockedCooldown -= dt;
        if (ExitOpen(room.index)) return;

        Vector3 kc = room.HasKey ? room.TileCenter(room.def.key) : Vector3.zero;
        if (room.HasKey && new Vector2(p.x - kc.x, p.z - kc.z).magnitude < Maps.KeyReach && Mathf.Abs(p.y - room.FloorY) < 1.2f)
        {
            s.keyTaken = true;
            sound.Play("key_get", 0.85f);
            StartCoroutine(After(0.5f, () => sound.Play("door_unlock", 0.8f)));
            fx.Star(kc + Vector3.up * 1.15f, Pal.Yellow, 0.9f, 0.7f);
            fx.Star(kc + Vector3.up * 1.15f, Pal.White, 0.5f, 0.45f);
            SketchCamera.Flash(0.22f, Pal.Yellow);
            hud.KeyPulse();
            hud.RefreshKey();
            hud.Toast("key found!     the exit is open");
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
            if (room.def.boss) hud.Popup("Locked! No key for this one: beat the Captain.", 3f);
            else hud.Popup("Locked. Find the key: only the 3D world shows it.", 2.2f);
        }
    }

    /// <summary>
    /// Guards are new in the remake too, so the old game cannot see them: watch the 3D world.
    /// A guard notices you when you come close (a chirp, a "!", its head turns, the spear comes down).
    /// Step inside its ring and it strikes: it cocks the spear back, lunges, and the heart is lost
    /// when the spear lands, with a shove and a bonk. Its footsteps get louder the nearer it is.
    /// </summary>
    void TickGuards(Room room, RoomState s, Vector3 p, float dt)
    {
        if (s.guards.Count == 0) return;
        Vector2 pf = room.WorldToTileF(p);
        bool striking = false;
        foreach (var g in s.guards) if (!g.gone && g.attackT >= 0f && !g.attackLanded) striking = true;
        if (guardBarkCooldown > 0f) guardBarkCooldown -= dt;
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
            g.lookAt = pf;

            // ---- the chase: off its patrol and after you; then, far enough from its post, back the way it came
            if (g.mode == 1)
            {
                g.chaseClock += dt;
                g.alerted = true;
                bool planted = g.attackT >= 0f && g.attackT < Maps.GuardWindup + Maps.GuardStrike + 0.22f;    // it stands still to strike
                bool tooFar = Vector2.Distance(g.pos, g.trail[0]) > Maps.ChaseLeash;
                if (g.attackT < 0f && (tooFar || g.chaseClock > Maps.ChaseSeconds || dist > Maps.ChaseLose))
                {
                    // that is as far as it goes: it gives up with a grunt and turns back to its post
                    g.mode = 2; g.alerted = false;
                    sound.PlayAt("guard_alert", wp + Vector3.up * 1.6f, 0.5f, 0.02f, 0.66f, 0.35f);
                    fx.Puff(wp + Vector3.up * 2.3f, Pal.A(Pal.Pencil, 0.85f), 0.8f, 0.55f);
                }
                else if (!planted && dist > 0.4f)
                {
                    Vector2 run = (pf - g.pos).normalized;
                    float sp = Mathf.Clamp(g.def.speed * 1.3f, 1.3f, 2.0f) * dt;       // quicker than its patrol, slower than your walk
                    Vector2 full = g.pos + run * sp, alongX = g.pos + new Vector2(run.x, 0f) * sp, alongY = g.pos + new Vector2(0f, run.y) * sp;
                    // it only walks on plain floor, so it slides along walls and stops at the edge of a pit
                    if (GuardCanStand(room, s, full)) g.pos = full;
                    else if (Mathf.Abs(run.x) > 0.15f && GuardCanStand(room, s, alongX)) g.pos = alongX;
                    else if (Mathf.Abs(run.y) > 0.15f && GuardCanStand(room, s, alongY)) g.pos = alongY;
                    g.dir = run;
                    if (Vector2.Distance(g.pos, g.trail[g.trail.Count - 1]) > 0.3f) g.trail.Add(g.pos);
                }
            }
            else if (g.mode == 2)
            {
                Vector2 back = g.trail[g.trail.Count - 1];
                Vector2 to = back - g.pos;
                float sp = Mathf.Max(1.2f, g.def.speed * 1.15f) * dt;
                if (to.magnitude <= sp)
                {
                    g.pos = back;
                    g.trail.RemoveAt(g.trail.Count - 1);
                    if (g.trail.Count == 0) g.mode = 0;     // back on its loop: the patrol carries on from where it stopped
                }
                else { g.dir = to.normalized; g.pos += g.dir * sp; }
            }
            if (g.mode != 0)
            {
                wp = room.TileFToWorld(g.pos);
                dist = Vector2.Distance(pf, g.pos);
            }
            g.chaseT = Mathf.MoveTowards(g.chaseT, g.mode == 1 ? 1f : 0f, dt / 0.25f);

            // ---- it notices you
            if (!g.alerted && g.mode != 2 && dist < Maps.GuardAlert)
            {
                g.alerted = true;
                g.bangT = 0f;
                sound.PlayAt("guard_alert", wp + Vector3.up * 1.6f, 0.6f, 0.03f, 1f, 0.35f);
                sound.Duck(0.55f, 0.5f);
                if (guardBarkCooldown <= 0f)
                {
                    // "HALT!" - it says so out loud, in a crunchy 1989 speech sample
                    guardBarkCooldown = 4.5f;
                    sound.PlayAt(GuardBarks[guardBarkCount++ % GuardBarks.Length], wp + Vector3.up * 1.6f, 1f, 0.03f, 1f, 0.3f);
                }
            }
            else if (g.alerted && g.mode != 1 && dist > Maps.GuardCalm) g.alerted = false;
            g.alertT = Mathf.MoveTowards(g.alertT, g.alerted ? 1f : 0f, dt / 0.22f);
            if (g.bangT >= 0f) { g.bangT += dt; if (g.bangT > 0.9f) g.bangT = -1f; }

            // ---- footsteps: quiet far away, loud and heavy up close, so your ears tell you it is coming
            if (g.hopped && dist < GuardEarshot)
            {
                float near = 1f - Mathf.Clamp01(dist / GuardEarshot);
                // the quick ones only clank on every other step, unless they are right on you
                if (g.hopPeriod >= 0.42f || g.alerted || g.steps % 2 == 0)
                    sound.PlayAt("guard_step", wp + Vector3.up * 0.4f, Mathf.Lerp(0.14f, 1f, near * near), 0.05f, g.alerted ? 0.82f : 1f, 0.55f);
                // while it is on to you the music dips under each stomp, so the steps always cut through
                if (g.alerted) sound.Duck(0.7f, 0.2f);
                fx.Puff(wp + Vector3.up * 0.06f, Pal.A(Pal.Pencil, 0.6f), 0.4f, 0.35f);
            }

            // ---- the strike
            if (g.attackT < 0f && !striking && invuln <= 0f && dist < Maps.GuardHit)
            {
                g.attackT = 0f; g.attackSwung = false; g.attackLanded = false;
                striking = true;
                sound.PlayAt("guard_windup", wp + Vector3.up * 1.2f, 0.7f, 0.04f, 1f, 0.3f);
                sound.PlayAt("guard_say_hyah", wp + Vector3.up * 1.5f, 0.95f, 0.04f, 1f, 0.25f);
                // from now on it is after you: it remembers where it left its patrol, and comes
                if (g.mode == 0) { g.trail.Clear(); g.trail.Add(g.pos); }
                if (g.mode != 1 && !firstChase) { firstChase = true; hud.Toast("the guard is after you!     RUN"); }
                g.mode = 1;
                g.chaseClock = 0f;
            }
            if (g.attackT >= 0f)
            {
                g.attackT += dt;
                // it keeps the spear on you until the moment it lands
                Vector2 toYou = pf - g.pos;
                if (!g.attackLanded && toYou.sqrMagnitude > 0.0004f)
                {
                    g.attackDir = toYou.normalized;
                    g.attackReach = Mathf.Clamp(toYou.magnitude * Maps.TS - 1.05f, -0.3f, 0.75f);
                }
                if (!g.attackSwung && g.attackT >= Maps.GuardWindup)
                {
                    g.attackSwung = true;
                    sound.PlayAt("guard_swing", wp + Vector3.up * 1.2f, 0.9f, 0.05f, 1f, 0.3f);
                }
                if (!g.attackLanded && g.attackT >= Maps.GuardImpactAt)
                {
                    g.attackLanded = true;
                    // the spear only reaches so far: sprint out from under it and it finds nothing but air
                    if (toYou.magnitude <= Maps.GuardReach) GuardImpact(g);
                    else if (invuln <= 0f) hud.Word("MISSED!", Pal.Hex("bfeee8"), Pal.Hex("1f8f84"), 0f, 0.62f);
                }
                if (g.attackT >= Maps.GuardWindup + Maps.GuardStrike + Maps.GuardRecover) g.attackT = -1f;
            }
        }
    }

    const float GuardEarshot = 7f;      // tiles: beyond this you cannot hear a guard's steps
    // the guard's voice: three barks for when it notices you, taken in turn, never two in quick succession
    static readonly string[] GuardBarks = { "guard_say_halt", "guard_say_hey", "guard_say_stop" };
    float guardBarkCooldown;
    int guardBarkCount;

    /// <summary>The spear lands: bonk, a shove away from the guard, the camera snaps back, and a heart goes.</summary>
    void GuardImpact(Slime g)
    {
        if (invuln > 0f) return;        // something else got you first: the spear swishes past
        Vector3 away = new Vector3(g.attackDir.x, 0f, -g.attackDir.y);      // tile space: y points down the screen
        Vector3 p = player.transform.position;
        g.hitFlash = 0.4f;
        sound.Play("guard_bonk", 0.95f);
        sound.Duck(0.4f, 0.45f);
        Vector3 hit = p + Vector3.up * 1.15f - away * 0.4f;
        fx.Star(hit, Pal.Yellow, 1.4f, 0.5f);
        fx.Star(hit, Pal.White, 0.8f, 0.34f);
        fx.Puff(hit, Pal.A(Pal.Ink, 0.8f), 1.1f, 0.45f);
        float side = Vector3.Dot(away, player.transform.right);     // which side of you the blow came from
        player.Knock(away * 7.2f, 3.6f);
        player.Kick(-9f, side * 10f, 9f);
        player.AddTrauma(0.3f);
        hud.Bonk(side);
        // it gloats, unless that was your last heart (the game-over jingle has the floor then)
        if (hearts > 1) StartCoroutine(After(0.55f, () => sound.Play("guard_say_gotcha", 0.9f)));
        Hurt(null, ref firstGuardHit, 0.11f);
    }

    /// <summary>Can a chasing guard stand here (tile units)? Only on plain floor that still exists, a step clear of anything else.</summary>
    static bool GuardCanStand(Room room, RoomState s, Vector2 c)
    {
        const float m = 0.3f;
        for (int k = 0; k < 4; k++)
        {
            int x = Mathf.FloorToInt(c.x + ((k & 1) == 0 ? -m : m)), r = Mathf.FloorToInt(c.y + ((k & 2) == 0 ? -m : m));
            if (!room.GuardFloor(x, r) || s.rowCrumble[r] >= 2f) return false;
        }
        return true;
    }

    /// <summary>The Captain's weapon found you: the same bonk, shove and lost heart as a guard's spear, only harder.</summary>
    public void HitPlayer(Vector3 away, float shove)
    {
        if (invuln > 0f) return;
        Vector3 p = player.transform.position;
        sound.Play("guard_bonk", 1f, 0f, 0.85f);
        sound.Duck(0.4f, 0.45f);
        Vector3 hit = p + Vector3.up * 1.15f - away * 0.4f;
        fx.Star(hit, Pal.Yellow, 1.6f, 0.5f);
        fx.Star(hit, Pal.White, 0.9f, 0.34f);
        fx.Puff(hit, Pal.A(Pal.Ink, 0.8f), 1.2f, 0.45f);
        float side = Vector3.Dot(away, player.transform.right);
        player.Knock(away * shove, 4.2f);
        player.Kick(-11f, side * 12f, 11f);
        hud.Bonk(side);
        bool told = true;
        Hurt(null, ref told, 0.13f);
    }

    /// <summary>The Captain is beaten: the last gate opens.</summary>
    public void OnBossDefeated()
    {
        hearts = MaxHearts;
        sound.Play("door_unlock", 0.9f);
        hud.KeyPulse();
        hud.RefreshKey();
        hud.Toast("the Captain is beaten!     BUILD & RUN is open");
    }

    /// <summary>Hold the whole game still for a moment (a heavy hit).</summary>
    public void Freeze(float seconds) { StartCoroutine(HitStop(seconds)); }

    /// <summary>Is there something to stand on at this spot of the current room? (Stops a shove at an edge.)</summary>
    bool CanStandAt(Vector3 pos)
    {
        Room room = rooms[cur];
        if (pos.z < room.MinZ || pos.z >= room.MaxZ) return true;       // doorways and corridors have real floor
        int x, r;
        room.WorldToTile(pos, out x, out r);
        return room.In(x, r) && IsSolidNow(cur, x, r);
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
                Hurt("Ouch! Watch the old game.", ref firstSlime);
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
                if (!firstFlicker) { firstFlicker = true; hud.Popup("They blink! Listen for the beat.", 2.2f); }
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
                if (!firstBreak) { firstBreak = true; hud.Popup("It holds... but it snaps behind you!", 2.4f); }
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
            if (!strandWarned) { strandWarned = true; hud.Popup("Dead end. Step off to try again.", 2.2f); }
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

    void Hurt(string firstTimeText, ref bool firstFlag, float stop = 0.06f)
    {
        hearts--;
        hits++;
        invuln = 1.7f;
        sound.Play("hurt", 0.85f);
        player.AddTrauma(0.5f);
        SketchCamera.Flash(0.42f, Pal.Red);
        pixel.Shake(10);
        hud.HurtFlash();
        StartCoroutine(HitStop(stop));
        if (firstTimeText != null && !firstFlag) { firstFlag = true; hud.Popup(firstTimeText, 2.2f); }
        if (hearts <= 0)
        {
            sound.Play("jingle_gameover", 0.8f);
            hud.Popup("GAME OVER...   just kidding.", 2.2f);
            hearts = MaxHearts;
            Respawn(cur, true);
        }
    }

    void Fall()
    {
        falls++;
        sound.Play("fall", 0.7f);
        if (!firstFall) { firstFall = true; hud.Popup("Nothing there. Check the old game.", 2.2f); }
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
        if (room.def.boss && boss != null) boss.Reset();
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
        if (!firstWrap) { firstWrap = true; hud.Popup("SCREEN WRAP! The 1989 bug still works.", 2.2f); }
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
