using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Test driver: plays the whole game by itself, to prove every room can be finished in the real build.
/// It plans in space AND time: every hazard in the game follows a fixed pattern (slime loops, flicker
/// planks, laser volleys, the deleting floor), so it searches for a route of "step" and "wait" moves
/// that is safe at the moment each tile is stood on. Guards are in that search too.
/// Each room is two errands: fetch the key (the exit is locked without it), then walk out.
/// Start the game with -autopilot.
/// </summary>
public class Autopilot : MonoBehaviour
{
    public static string status = "idle";
    public static readonly List<string> log = new List<string>();
    public static bool takeShots = true;

    GameManager gm;
    const float Step = 0.25f;           // planning time step, seconds
    const int MoveSteps = 2;            // a tile-to-tile move is budgeted two steps (0.5 s; walking takes about 0.45 s)
    const int Horizon = 200;            // look up to 50 s ahead

    // current action
    Vector3 target; bool hasTarget, wrapTarget, leaving;
    float waitUntil, shotClock, runClock, stuckClock, noPlanLog;
    List<Vector3> exitPath; int exitIndex;
    int plannedRoom = -1, shotIndex, lastFalls, lastHits, lastRoomLogged = -1;
    bool done, lastNeedKey;

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

    // ------------------------------------------------------------------ space-time search
    struct Act { public bool move; public int x, r; public bool wrap; public int wrapDir; }
    int[] prev, moves;

    /// <summary>
    /// Breadth-first search over (tile, time step). Returns the first action of the quickest safe route to
    /// <paramref name="goalTile"/>. While the exit is locked it counts as a wall.
    /// </summary>
    bool PlanFirstAction(Room room, Vector2Int from, Vector2Int goalTile, bool exitLocked, out Act first)
    {
        first = new Act();
        int ri = room.index;
        int W = Maps.W, H = Maps.H;
        int cells = W * H;
        int total = cells * (Horizon + 1);
        if (prev == null || prev.Length != total) { prev = new int[total]; moves = new int[total]; }
        for (int i = 0; i < total; i++) { prev[i] = -2; moves[i] = int.MaxValue; }
        int startId = (from.y * W + from.x);
        prev[startId] = -1; moves[startId] = 0;
        int goal = -1;
        int exitCell = goalTile.y * W + goalTile.x;
        int[] dx = { 0, 1, 0, -1 }, dr = { -1, 0, 1, 0 };
        // Sweep forward in time, one layer per step. Every action moves forward in time, so when a layer
        // is reached all the ways into it are known: the first layer that contains the exit is the
        // quickest arrival, and among equally quick plans we keep the one with the fewest steps
        // (so the walker waits in place instead of pacing about).
        for (int k = 0; k <= Horizon && goal < 0; k++)
        {
            int layer = k * cells;
            if (prev[layer + exitCell] != -2) { goal = layer + exitCell; break; }
            if (k >= Horizon - MoveSteps) break;
            float t = k * Step;
            for (int cell = 0; cell < cells; cell++)
            {
                int id = layer + cell;
                if (prev[id] == -2) continue;
                int x = cell % W, r = cell / W;
                int m = moves[id];

                // wait here one step
                int wid = id + cells;
                if (m < moves[wid] && gm.SafeBetween(ri, x, r, t, t + Step + 0.1f)) { prev[wid] = id; moves[wid] = m; }

                // step to a neighbour
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], nr = r + dr[d];
                    if (room.def.wrap)
                    {
                        if (nx < 0) nx = W - 1; else if (nx >= W) nx = 0;
                    }
                    if (!room.In(nx, nr) || !room.TruthWalkable(nx, nr)) continue;
                    if (exitLocked && nx == room.exit.x && nr == room.exit.y) continue;
                    int nid = (k + MoveSteps) * cells + nr * W + nx;
                    if (m + 1 >= moves[nid]) continue;
                    float arrive = t + MoveSteps * Step;
                    // we are still on the old tile for the first part of the move, and on the new one from about halfway;
                    // the new tile must also stay safe long enough to decide the next move from it
                    if (!gm.SafeBetween(ri, x, r, t, t + MoveSteps * Step * 0.7f)) continue;
                    if (!gm.SafeBetween(ri, nx, nr, t + 0.1f, arrive + 0.45f)) continue;
                    prev[nid] = id; moves[nid] = m + 1;
                }
            }
        }
        if (goal < 0) return false;
        // walk back to the first action
        int cur = goal, child = -1;
        while (prev[cur] >= 0) { child = cur; cur = prev[cur]; }
        if (child < 0) return false;    // already on the exit tile
        int cc = child % cells;
        int cx = cc % W, cr = cc / W;
        if (cx == from.x && cr == from.y) { first.move = false; return true; }
        first.move = true; first.x = cx; first.r = cr;
        // stepping from column 0 to the last column means walking west off the edge (and the other way round)
        if (room.def.wrap && Mathf.Abs(cx - from.x) > 1) { first.wrap = true; first.wrapDir = cx > from.x ? 1 : -1; }
        return true;
    }

    void Update()
    {
        if (done || gm == null) return;
        runClock += Time.unscaledDeltaTime;
        shotClock += Time.unscaledDeltaTime;

        if (gm.state == GameManager.State.Title) { GameInput.overrideAnyKey = true; return; }
        if (gm.state == GameManager.State.Results || gm.state == GameManager.State.Ending)
        {
            GameInput.overrideMove = Vector2.zero;
            if (gm.state == GameManager.State.Ending) { if (shotClock > 1.5f) { shotClock = 0f; Shot("ending"); } return; }
            Note("DONE time=" + gm.time.ToString("0.0") + " coins=" + gm.coins + "/" + gm.totalCoins + " falls=" + gm.falls + " hits=" + gm.hits);
            status = "done";
            done = true;
            Shot("results");
            if (!Application.isEditor) Invoke("Quit", 2.5f);
            return;
        }
        if (gm.state != GameManager.State.Playing) return;
        if (runClock > 600f) { Note("TIMEOUT in room " + (gm.cur + 1)); status = "timeout"; done = true; GameInput.overrideMove = Vector2.zero; if (!Application.isEditor) Invoke("Quit", 1f); return; }

        if (gm.falls != lastFalls) { Note("fell in room " + (gm.cur + 1) + " (falls=" + gm.falls + ")"); lastFalls = gm.falls; hasTarget = false; leaving = false; Shot("fall"); }
        if (gm.hits != lastHits) { Note("hit in room " + (gm.cur + 1) + " (hits=" + gm.hits + ")"); lastHits = gm.hits; }
        if (gm.cur != plannedRoom) { plannedRoom = gm.cur; hasTarget = false; leaving = false; stuckClock = 0f; }
        if (gm.cur != lastRoomLogged) { lastRoomLogged = gm.cur; Note("room " + (gm.cur + 1) + " '" + gm.rooms[gm.cur].def.title + "' at t=" + gm.time.ToString("0.0")); Shot("room" + (gm.cur + 1) + "_start"); }
        if (shotClock > 3.0f) { shotClock = 0f; Shot("room" + (gm.cur + 1)); }

        Room room = gm.rooms[gm.cur];
        Vector3 p = gm.player.transform.position;
        bool inRoom = p.z >= room.MinZ && p.z < room.MaxZ;
        GameInput.overrideSprint = room.def.deleteWave;

        // out through the corridor to the next room (or the way out of the dream)
        if (leaving || !inRoom)
        {
            if (exitPath == null || !leaving) BuildExitPath(room);
            if (exitIndex >= exitPath.Count) { GameInput.overrideMove = Vector2.zero; return; }
            if (WalkTo(exitPath[exitIndex], p, 0.4f)) exitIndex++;
            return;
        }

        int tx, tr;
        room.WorldToTile(p, out tx, out tr);
        if (!room.In(tx, tr)) { GameInput.overrideMove = Vector2.zero; return; }
        // errand one: the key. It only exists in the 3D remake, and the exit stays shut without it.
        bool needKey = !gm.ExitOpen(gm.cur);
        Vector2Int goal = needKey ? room.def.key : room.exit;
        if (needKey != lastNeedKey)
        {
            lastNeedKey = needKey;
            if (!needKey) Note("key taken in room " + (gm.cur + 1) + " at t=" + gm.time.ToString("0.0"));
        }
        if (!needKey && tx == room.exit.x && tr == room.exit.y) { leaving = true; BuildExitPath(room); return; }

        if (hasTarget)
        {
            Vector3 d = target - p; d.y = 0f;
            if (wrapTarget) { if (d.magnitude > Maps.TS * 6f) { hasTarget = false; } else { WalkTo(target, p, 0.05f); return; } }
            else if (WalkTo(target, p, 0.28f)) hasTarget = false;
            else
            {
                stuckClock += Time.deltaTime;
                if (stuckClock > 6f) { Note("stuck walking to " + target + " in room " + (gm.cur + 1)); hasTarget = false; stuckClock = 0f; }
                return;
            }
        }
        stuckClock = 0f;

        if (Time.time < waitUntil)
        {
            // waiting: stay centred on the tile so a narrow plank does not slip out from under us
            Vector3 c = room.TileCenter(tx, tr);
            Vector3 off = c - p; off.y = 0f;
            if (off.magnitude > 0.25f) WalkTo(c, p, 0.2f); else GameInput.overrideMove = Vector2.zero;
            return;
        }

        if (needKey && tx == goal.x && tr == goal.y)
        {
            // standing on the key's tile: step to the middle of it and it is ours
            WalkTo(room.TileCenter(tx, tr), p, 0.15f);
            return;
        }

        Act act;
        if (!PlanFirstAction(room, new Vector2Int(tx, tr), goal, needKey, out act))
        {
            GameInput.overrideMove = Vector2.zero;
            waitUntil = Time.time + 0.2f;
            if (Time.time > noPlanLog) { noPlanLog = Time.time + 3f; Note("no safe route right now from " + tx + "," + tr + " in room " + (gm.cur + 1) + " (waiting)"); }
            return;
        }
        if (!act.move) { waitUntil = Time.time + Step; GameInput.overrideMove = Vector2.zero; return; }
        if (act.wrap)
        {
            // walk off the edge: the old game's bug carries us to the other side
            target = room.TileCenter(tx, tr) + new Vector3(-act.wrapDir * Maps.TS * 0.95f, 0, 0);
            wrapTarget = true;
        }
        else { target = room.TileCenter(act.x, act.r); wrapTarget = false; }
        hasTarget = true;
        WalkTo(target, p, 0.28f);
    }

    void BuildExitPath(Room room)
    {
        leaving = true;
        exitPath = new List<Vector3>();
        exitIndex = 0;
        exitPath.Add(room.TileCenter(room.exit.x, room.exit.y));
        if (room.index < gm.rooms.Length - 1)
        {
            Room next = gm.rooms[room.index + 1];
            exitPath.Add(next.TileCenter(next.start.x, next.start.y));
            exitPath.Add(next.TileCenter(next.SpawnTile.x, next.SpawnTile.y));
        }
        else exitPath.Add(gm.world.portalPos + new Vector3(0, -1.5f, 0.6f));
    }

    /// <summary>Turn toward a point and walk. Returns true once we are within <paramref name="reach"/> of it.</summary>
    bool WalkTo(Vector3 point, Vector3 p, float reach)
    {
        Vector3 d = point - p; d.y = 0f;
        if (d.magnitude < reach) { GameInput.overrideMove = Vector2.zero; return true; }
        float yawTarget = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        GameInput.overrideYaw = yawTarget;
        // Head straight for the point even while still turning to face it (a sidestep), so a corner costs
        // no time. The plan budgets half a second a tile; standing still to turn first used to eat a tenth
        // of that, which was enough to miss the tightest windows (the blinking planks, a guard's ring).
        float rel = Mathf.DeltaAngle(gm.player.yaw, yawTarget) * Mathf.Deg2Rad;
        GameInput.overrideMove = new Vector2(Mathf.Sin(rel), Mathf.Cos(rel));
        return false;
    }

    void Quit() { Application.Quit(); }
}
