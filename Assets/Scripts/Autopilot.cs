using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Test driver: walks the whole game by itself using the same path finding the map checker uses,
/// so we can prove every room is finishable in the real build. Start with -autopilot.
/// </summary>
public class Autopilot : MonoBehaviour
{
    public static string status = "idle";
    public static readonly List<string> log = new List<string>();
    public static bool sprint = false;
    public static bool takeShots = true;

    class Wp { public Vector3 pos; public bool wrapExit; public Vector2Int tile; public bool inRoom; }
    GameManager gm;
    List<Wp> wps;
    int wi, plannedRoom = -1;
    float progressClock, shotClock, runClock, lastZ, waitClock;
    int shotIndex, lastFalls, lastHits;
    bool done;

    void Start()
    {
        gm = GetComponent<GameManager>();
        GameInput.overrideActive = true;
        status = "running";
        log.Clear();
        Application.runInBackground = true;
    }

    void OnDestroy() { GameInput.overrideActive = false; GameInput.overrideYaw = null; }

    void Note(string s) { log.Add(s); Debug.Log("[AUTOPILOT] " + s); }

    void Shot(string tag)
    {
        if (!takeShots || string.IsNullOrEmpty(Boot.shotDir)) return;
        try
        {
            System.IO.Directory.CreateDirectory(Boot.shotDir);
            string path = System.IO.Path.Combine(Boot.shotDir, (shotIndex++).ToString("000") + "_" + tag + ".png");
            ScreenCapture.CaptureScreenshot(path);
        }
        catch (System.Exception e) { Debug.LogWarning("shot failed: " + e.Message); }
    }

    void Plan()
    {
        plannedRoom = gm.cur;
        Room room = gm.rooms[plannedRoom];
        Vector3 p = gm.player.transform.position;
        int x, r;
        room.WorldToTile(p, out x, out r);
        Vector2Int from = (room.In(x, r) && room.TruthWalkable(x, r) && p.z >= room.MinZ && p.z < room.MaxZ) ? new Vector2Int(x, r) : room.SpawnTile;
        var path = room.FindPath(from, room.exit);
        wps = new List<Wp>();
        wi = 0;
        if (path == null) { Note("room " + (plannedRoom + 1) + ": NO PATH from " + from); return; }
        Vector2Int prev = from;
        foreach (var st in path)
        {
            if (st.wrapped)
            {
                // walk off the edge; the game teleports us to the other side
                bool goingWest = prev.x == 0;
                Vector3 edge = room.TileCenter(prev.x, prev.y) + new Vector3(goingWest ? -Maps.TS * 0.9f : Maps.TS * 0.9f, 0, 0);
                wps.Add(new Wp { pos = edge, wrapExit = true, tile = prev, inRoom = true });
            }
            wps.Add(new Wp { pos = room.TileCenter(st.tile.x, st.tile.y), tile = st.tile, inRoom = true });
            prev = st.tile;
        }
        // then out through the corridor
        if (plannedRoom < gm.rooms.Length - 1)
        {
            Room next = gm.rooms[plannedRoom + 1];
            wps.Add(new Wp { pos = next.TileCenter(next.start.x, next.start.y) });
            wps.Add(new Wp { pos = next.TileCenter(next.SpawnTile.x, next.SpawnTile.y) });
        }
        else wps.Add(new Wp { pos = gm.world.portalPos + new Vector3(0, -1.5f, 0.5f) });
        progressClock = 0f;
        Note("room " + (plannedRoom + 1) + ": planned " + wps.Count + " waypoints from " + from);
    }

    void Update()
    {
        if (done || gm == null) return;
        runClock += Time.unscaledDeltaTime;
        shotClock += Time.unscaledDeltaTime;

        if (gm.state == GameManager.State.Title) { GameInput.overrideAnyKey = true; return; }
        if (gm.state == GameManager.State.Results || gm.state == GameManager.State.Ending)
        {
            if (gm.state == GameManager.State.Ending) { GameInput.overrideMove = Vector2.zero; if (shotClock > 1.5f) { shotClock = 0f; Shot("ending"); } return; }
            Note("DONE time=" + gm.time.ToString("0.0") + " coins=" + gm.coins + "/" + gm.totalCoins + " falls=" + gm.falls + " hits=" + gm.hits);
            status = "done";
            done = true;
            GameInput.overrideMove = Vector2.zero;
            Shot("results");
            if (!Application.isEditor) Invoke("Quit", 2.5f);
            return;
        }
        if (gm.state != GameManager.State.Playing) return;

        if (gm.falls != lastFalls) { Note("fell in room " + (gm.cur + 1) + " (falls=" + gm.falls + ")"); lastFalls = gm.falls; plannedRoom = -1; Shot("fall"); }
        if (gm.hits != lastHits) { Note("slime hit in room " + (gm.cur + 1) + " (hits=" + gm.hits + ")"); lastHits = gm.hits; }
        if (gm.cur != plannedRoom || wps == null) { Plan(); Shot("room" + (gm.cur + 1) + "_start"); }
        if (wps == null || wi >= wps.Count) { GameInput.overrideMove = Vector2.zero; return; }

        if (shotClock > 3.0f) { shotClock = 0f; Shot("room" + (gm.cur + 1)); }

        Vector3 p = gm.player.transform.position;
        Wp w = wps[wi];
        Vector3 d = w.pos - p; d.y = 0f;
        float dist = d.magnitude;

        if (w.wrapExit)
        {
            if (dist > Maps.TS * 6f) { wi++; return; }        // we were teleported: carry on from the other side
        }
        else if (dist < 0.32f) { wi++; progressClock = 0f; return; }

        // do not walk into a slime: wait until the next tile is clear
        bool hold = false;
        Room room = gm.rooms[gm.cur];
        var rs = gm.rs[gm.cur];
        if (rs.slimes.Count > 0 && w.inRoom)
        {
            Vector2 target = new Vector2(w.tile.x + 0.5f, w.tile.y + 0.5f);
            Vector2 me = room.WorldToTileF(p);
            foreach (var s in rs.slimes)
            {
                float toTarget = Vector2.Distance(s.pos, target), toMe = Vector2.Distance(s.pos, me);
                if (toTarget < 1.7f && toMe > 0.95f) hold = true;
            }
        }
        if (hold) { waitClock += Time.deltaTime; if (waitClock > 6f) hold = false; } else waitClock = 0f;

        float yawTarget = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        GameInput.overrideYaw = yawTarget;
        float err = Mathf.Abs(Mathf.DeltaAngle(gm.player.yaw, yawTarget));
        GameInput.overrideMove = (hold || err > 22f) ? Vector2.zero : new Vector2(0, 1);
        GameInput.overrideSprint = sprint || room.def.deleteWave;

        progressClock += Time.deltaTime;
        if (progressClock > 14f) { Note("stuck near waypoint " + wi + " in room " + (gm.cur + 1) + " at " + p + " -> replanning"); plannedRoom = -1; progressClock = 0f; }
        if (runClock > 420f) { Note("TIMEOUT"); status = "timeout"; done = true; if (!Application.isEditor) Invoke("Quit", 1f); }
    }

    void Quit() { Application.Quit(); }
}
