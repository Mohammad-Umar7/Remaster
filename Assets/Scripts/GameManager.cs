using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Slime
{
    public SlimeDef def;
    public float d, total, hopClock, hitFlash;
    public bool hopped;
    public Vector2 pos;     // tile units, centre of the slime
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

    public void Reset() { d = def.phase * total; hopClock = 0f; hitFlash = 0f; Place(); }

    public void Tick(float dt)
    {
        d = (d + def.speed * dt) % total;
        Place();
        hopped = false;
        hopClock += dt;
        if (hopClock >= 0.44f) { hopClock -= 0.44f; hopped = true; }
        if (hitFlash > 0f) hitFlash -= dt;
    }

    void Place()
    {
        float left = d;
        int n = def.loop.Length;
        for (int i = 0; i < n; i++)
        {
            if (left <= seg[i] || i == n - 1)
            {
                Vector2 a = def.loop[i], b = def.loop[(i + 1) % n];
                float t = seg[i] > 0f ? Mathf.Clamp01(left / seg[i]) : 0f;
                Vector2 p = Vector2.Lerp(a, b, t);
                pos = new Vector2(p.x + 0.5f, p.y + 0.5f);
                return;
            }
            left -= seg[i];
        }
    }
}

public class RoomState
{
    public bool[,] coinTaken = new bool[Maps.W, Maps.H];
    public bool[,] revealed = new bool[Maps.W, Maps.H];
    public bool[,] fakeFound = new bool[Maps.W, Maps.H];
    public float[] rowCrumble = new float[Maps.H];     // -1 intact, 0..1 crumbling, 2 deleted
    public List<Slime> slimes = new List<Slime>();
    public bool cleared, waveActive;
    public float waveClock;
    public int waveRow;
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

    public const float WaveInterval = 1.8f, CrumbleTime = 0.75f;
    int lastTileX = -99, lastTileR = -99;
    bool firstBridge, firstFake, firstWrap, firstCoin, firstSlime, firstFall, chipFoot;
    int hitStopId;
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
            totalCoins += rooms[i].coins.Count;
        }

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
        for (int i = 0; i < rooms.Length; i++) rs[i].cleared = i < roomIndex;
        hearts = MaxHearts;
        player.controlEnabled = true;
        Respawn(roomIndex, false);
        pixel.bootTimer = 1.0f;
        sound.Play("jingle_boot", 0.8f);
        sound.StartMusic();
        sound.SetLayers(clearedCount);
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
        for (int k = 0; k < rooms.Length; k++) rs[k].cleared = k < i;
        sound.SetLayers(clearedCount);
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
        int num = GameInput.NumberPressed;
        if (num > 0) JumpToRoom(num - 1);

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
                if (GameInput.RestartPressed) RestartRoom();
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
        if (!inRoom) { lastTileX = -99; return; }

        if (room.def.wrap)
        {
            if (p.x < room.MinX) { DoWrap(room, 1); p = player.transform.position; }
            else if (p.x >= room.MaxX) { DoWrap(room, -1); p = player.transform.position; }
        }

        room.WorldToTile(p, out tileX, out tileR);
        if (tileX != lastTileX || tileR != lastTileR)
        {
            lastTileX = tileX; lastTileR = tileR;
            OnEnterTile(room, s, tileX, tileR);
        }

        // coins: they only exist in the old game, so you find them by walking where it says
        if (room.In(tileX, tileR))
        {
            char ch = room.c[tileX, tileR];
            if ((ch == 'C' || ch == 'c') && !s.coinTaken[tileX, tileR] && s.rowCrumble[tileR] < 2f)
            {
                Vector3 c = room.TileCenter(tileX, tileR);
                if (new Vector2(p.x - c.x, p.z - c.z).magnitude < Maps.TS * 0.45f) Collect(room, s, tileX, tileR);
            }
        }

        TickSlimes(room, s, p, dt);
        TickWave(room, s, dt);
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
                clearedCount++;
                sound.Play("jingle_room", 0.8f);
                sound.SetLayers(clearedCount);
                if (clearedCount >= 1 && clearedCount <= 4)
                {
                    StartCoroutine(After(0.9f, () => sound.Play("jingle_layer", 0.6f)));
                    hud.Toast("checkpoint!     soundtrack remastered:  + " + LayerNames[clearedCount - 1]);
                }
                hud.RefreshTodo();
            }
            hearts = MaxHearts;
            ResetRoomDynamic(i);
            hud.ShowRoomTitle(i);
        }
        else hud.ShowRoomTitle(i);
    }

    void OnEnterTile(Room room, RoomState s, int x, int r)
    {
        if (!room.In(x, r)) return;
        if (room.IsBridge(x, r) && !s.revealed[x, r])
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
            if (invuln <= 0f && Vector2.Distance(pf, sl.pos) < 0.62f) Hurt(sl);
        }
    }

    void Hurt(Slime sl)
    {
        hearts--;
        hits++;
        invuln = 1.7f;
        sl.hitFlash = 0.35f;
        sound.Play("hurt", 0.85f);
        player.AddTrauma(0.5f);
        SketchCamera.Flash(0.42f, Pal.Red);
        pixel.Shake(10);
        hud.HurtFlash();
        StartCoroutine(HitStop(0.06f));
        if (!firstSlime) { firstSlime = true; hud.Popup("Ouch! Something un-modelled bit you. Watch the old game.", 3.4f); }
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
        foreach (var sl in s.slimes) sl.Reset();
        if (rooms[i].def.deleteWave)
        {
            s.waveActive = false; s.waveClock = 0f; s.waveRow = Maps.H - 1;
            for (int r = 0; r < Maps.H; r++) s.rowCrumble[r] = -1f;
            world.RestoreRoom(i);
            fx.ClearReveals();
            for (int r = 0; r < Maps.H; r++)
                for (int x = 0; x < Maps.W; x++)
                    if (s.revealed[x, r])
                    {
                        s.revealed[x, r] = false;
                        var l = world.BridgeRenderers(i, x, r);
                        if (l != null) foreach (var rd in l) world.SetReveal(rd, 0f);
                    }
            sound.SetHurry(false);
        }
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
