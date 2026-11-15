using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Room data. Every room is one 13x11 text grid that describes BOTH games at once:
/// the old pixel game (the truth: what you can actually stand on) and the 3D remake (what you see).
///
///   #  wall, real                     W  wall, real, fancy "finished" look
///   F  FAKE wall, fancy look          f  FAKE wall, plain look      (3D shows a wall, the old game has floor)
///   G  gate, real                     .  floor
///   ~  pit                            B  bridge that only exists in the old game (invisible in 3D, solid)
///   c  coin on such a bridge          C  coin on floor               (coins exist only in the old game)
///   o  one-use plank: breaks after you step off it       k  one-use plank with a coin
///   a  flicker plank, group A         b  flicker plank, group B      (the groups take turns existing)
///   N  dev note signpost (floor)      S  start doorway (bottom)      X  exit doorway (top)
/// </summary>
public class SlimeDef
{
    public Vector2Int[] loop;
    public float speed = 1.5f;      // tiles per second
    public float phase;             // 0..1 start offset along the loop
    public SlimeDef(float speed, float phase, params int[] xy)
    {
        this.speed = speed; this.phase = phase;
        loop = new Vector2Int[xy.Length / 2];
        for (int i = 0; i < loop.Length; i++) loop[i] = new Vector2Int(xy[i * 2], xy[i * 2 + 1]);
    }
}

/// <summary>A set of laser lines that fire together, at <see cref="t"/> seconds into the room's loop.</summary>
public class LaserVolley
{
    public float t;
    public int[] rows, cols;
    public LaserVolley(float t, int[] rows, int[] cols) { this.t = t; this.rows = rows ?? new int[0]; this.cols = cols ?? new int[0]; }
    public bool Hits(int x, int r)
    {
        for (int i = 0; i < rows.Length; i++) if (rows[i] == r) return true;
        // column beams stop at the safe-zone line, so the entry area is a place to stand and read the pattern
        if (r < Maps.LaserLobbyRow)
            for (int i = 0; i < cols.Length; i++) if (cols[i] == x) return true;
        return false;
    }
}

public class RoomDef
{
    public string title;
    public string todo;         // short label
    public string note;         // the dev's sticky note for this room
    public string[] rows;
    public bool wrap;
    public bool deleteWave;
    public float finish = 0.7f; // how much of the 3D room looks finished (cosmetic)
    public SlimeDef[] slimes = new SlimeDef[0];
    public float flickerPeriod = 2.3f;      // seconds each flicker group stays solid
    public LaserVolley[] lasers;            // null when the room has no lasers
    public float laserPeriod = 12f;         // length of the laser loop in seconds
    public bool needBridge, needFake, needWrap;   // what the map checker expects the room to require

    // NEW in the remake: the old game has never heard of these, so only the 3D world shows them.
    public Vector2Int key = new Vector2Int(-1, -1);     // tile the key floats over; the exit stays locked until you hold it
    public SlimeDef[] guards = new SlimeDef[0];         // patrols (tiles per second, start offset, loop of waypoints)
    public bool boss;                                   // the Captain's arena: the exit opens when he is beaten (see BossFight)
    public bool plankMaze;                              // its one-use planks are meant as a maze: the map checker then insists on a winding route
}

public static class Maps
{
    public const float TS = 2.0f;           // metres per tile
    public const int W = 13, H = 11;
    public const int CorridorTiles = 3;
    public const float CorridorRise = 1.5f;
    public const float WallHeight = 3.2f;
    public const float LaserLead = 3.0f;    // the old game shows a laser this long before the remake fires it
    public const float LaserHot = 0.35f;    // how long a fired beam can hurt
    public const int LaserLobbyRow = 8;     // rows from here down are never hit by column beams
    public const float GuardHit = 0.5f;     // a guard catches you within this many tiles (one metre)
    public const float GuardClear = 0.74f;  // what the auto-walker keeps away from a guard (it is not always dead centre)
    public const float KeyReach = 0.95f;    // metres: how close you have to get to pick the key up
    public const float GuardAlert = 1.9f;   // tiles: a guard notices you this close (it chirps, turns its head, lowers its spear)
    public const float GuardCalm = 2.7f;    // tiles: and forgets you again this far away
    // a guard's strike, in seconds: cock the spear back, thrust, then wobble back to rest
    public const float GuardWindup = 0.17f, GuardStrike = 0.09f, GuardRecover = 0.52f;
    public const float GuardImpactAt = 0.235f;      // when the spear lands and the heart is lost
    public const float GuardReach = 1.05f;          // tiles: the spear lands only if you are still this close when it arrives
    // the chase: a guard whose ring you stepped into comes after you, then gives up and walks back
    public const float ChaseLeash = 3.2f;           // tiles it will go from where it left its patrol
    public const float ChaseSeconds = 4.5f;         // or this long since its last strike, whichever comes first
    public const float ChaseLose = 4.2f;            // tiles: get this far away and it loses you

    static int[] L(params int[] v) { return v; }
    static Vector2Int K(int x, int r) { return new Vector2Int(x, r); }

    public static readonly RoomDef[] Defs =
    {
        new RoomDef
        {
            title = "THE MISSING BRIDGE",
            todo = "model the bridge",
            note = "TODO: model the bridge.\nIt holds, but every plank\nSNAPS behind you!",
            finish = 0.92f, needBridge = true,
            // The bridge only exists in the old game, and it is made of old planks: each one snaps a moment
            // after you step off it, so there is no going back the way you came (the second column is the
            // way back, once). If you use up both, the room puts itself back.
            // the key hangs in plain sight across the pit; one slow guard circles it
            key = K(9, 2),
            guards = new[] { new SlimeDef(0.7f, 0.55f, 8,1, 10,1, 10,3, 8,3) },
            rows = new[]
            {
                "######X######",
                "#....C.C....#",
                "#...........#",
                "#...........#",
                "#~~~~~oo~~~~#",
                "#~~~~~ok~~~~#",
                "#~~~~~oo~~~~#",
                "#....N......#",
                "#...........#",
                "#...........#",
                "######S######",
            }
        },
        new RoomDef
        {
            title = "PLACEHOLDER WALL",
            todo = "give walls collision",
            note = "TODO: some walls are\nonly decoration.\nNo collision yet.",
            finish = 0.85f, needFake = true,
            // the key is a few steps down the first hidden passage; a slow guard paces in front of the exit
            key = K(2, 6),
            guards = new[] { new SlimeDef(0.9f, 0.30f, 1,1, 11,1) },
            rows = new[]
            {
                "######X######",
                "#...........#",
                "#.....C.....#",
                "##F######W###",
                "#..........C#",
                "###W######F##",
                "#C..........#",
                "#####FFF#####",
                "#....N......#",
                "#...........#",
                "######S######",
            }
        },
        new RoomDef
        {
            title = "UNMODELLED ENEMIES",
            todo = "model the slimes",
            note = "TODO: model the slimes.\nThey only exist in the\nold game. They still bite.",
            finish = 0.6f, needBridge = true,
            // the key floats over a bridge that only the old game shows, right on the slimes' lap;
            // a guard paces the ledge in front of the exit
            key = K(7, 3),
            guards = new[] { new SlimeDef(1.0f, 0.15f, 4,1, 11,1) },
            slimes = new[]
            {
                new SlimeDef(1.45f, 0.55f, 6,6, 10,6, 10,7, 6,7),
                new SlimeDef(1.6f, 0.30f, 10,3, 3,3, 3,4, 10,4),
            },
            rows = new[]
            {
                "######X######",
                "#.....C.....#",
                "#~~B~~~~~~~~#",
                "#~~BBBBBBBB~#",
                "#~~BBBBBBBc~#",
                "#~~~~~~~~~B~#",
                "#~~~~~BBBBB~#",
                "#~~~~~BBBBc~#",
                "#....N......#",
                "#...........#",
                "######S######",
            }
        },
        new RoomDef
        {
            title = "SCREEN WRAP",
            todo = "fix screen wrap (won't)",
            note = "KNOWN BUG since 1989:\nexit LEFT, appear RIGHT.\nWON'T FIX.",
            wrap = true, needWrap = true,
            finish = 0.5f,
            // the key is at the far end of the hall behind the gate, and a guard laps that hall;
            // another one circles the way to the screen edge
            key = K(2, 1),
            guards = new[]
            {
                new SlimeDef(1.1f, 0.00f, 1,5, 4,5, 4,7, 1,7),
                new SlimeDef(1.2f, 0.25f, 1,1, 11,1, 11,2, 1,2),
            },
            rows = new[]
            {
                "######X######",
                "#.....C.....#",
                "#...........#",
                "######G####.#",
                "#C......#...#",
                "#.......#...#",
                "........#....",
                "#....N..#.C.#",
                "#.......#####",
                "#.......#####",
                "######S######",
            }
        },
        new RoomDef
        {
            title = "SPRITE FLICKER",
            todo = "fix sprite flicker",
            note = "BUG: sprite flicker.\nSome planks only exist\nhalf the time. Listen!",
            finish = 0.45f, needBridge = true,
            flickerPeriod = 2.3f,
            // the key sits on the side island, a detour over blinking planks; one guard walks past the
            // first plank (so you cannot just stand and wait for the beat), one paces the exit ledge
            key = K(10, 5),
            guards = new[]
            {
                new SlimeDef(1.3f, 0.45f, 7,8, 11,8, 11,9, 7,9),
                new SlimeDef(1.3f, 0.10f, 1,1, 11,1),
            },
            rows = new[]
            {
                "######X######",
                "#.........C.#",
                "#Cbb.bb.~~~~#",
                "#~~~a~~~~~~~#",
                "#~~~a~~~~~~~#",
                "#~~~.bb.aaC~#",
                "#~~~~~~a~~~~#",
                "#~~~~~~a~~~~#",
                "#....N......#",
                "#...........#",
                "######S######",
            }
        },
        new RoomDef
        {
            // The last room is a fight, not a puzzle. The Captain of the guards is NEW in the remake,
            // so the old game shows an empty hall. No key here: the exit opens when he is beaten.
            // The remake never modelled a sword, so you fight with the old game's one, and every coin
            // from the old game takes a little off his health (see BossFight).
            title = "THE CAPTAIN",
            todo = "beat the Captain",
            note = "BOSS (new in the remake).\nLEFT CLICK sword, SPACE dodge.\nDodge first, then hit him.",
            finish = 0.95f,
            boss = true,
            rows = new[]
            {
                "######X######",
                "#...........#",
                "#...........#",
                "#...........#",
                "#...........#",
                "#...........#",
                "#...........#",
                "#...........#",
                "#...........#",
                "#N..........#",
                "######S######",
            }
        },
    };
}

/// <summary>A parsed room placed in the world.</summary>
public class Room
{
    public RoomDef def;
    public int index;
    public char[,] c = new char[Maps.W, Maps.H];
    public Vector3 origin;          // south-west corner at floor height
    public Vector2Int start, exit, noteTile;
    public List<Vector2Int> coins = new List<Vector2Int>();

    public Room(RoomDef def, int index)
    {
        this.def = def; this.index = index;
        noteTile = new Vector2Int(-1, -1);
        for (int r = 0; r < Maps.H; r++)
        {
            string line = def.rows[r];
            for (int x = 0; x < Maps.W; x++)
            {
                char ch = x < line.Length ? line[x] : '#';
                c[x, r] = ch;
                if (ch == 'S') start = new Vector2Int(x, r);
                if (ch == 'X') exit = new Vector2Int(x, r);
                if (ch == 'N') noteTile = new Vector2Int(x, r);
                if (IsCoinCh(ch)) coins.Add(new Vector2Int(x, r));
            }
        }
    }

    public float MinX => origin.x;
    public float MaxX => origin.x + Maps.W * Maps.TS;
    public float MinZ => origin.z;
    public float MaxZ => origin.z + Maps.H * Maps.TS;
    public float FloorY => origin.y;

    public bool In(int x, int r) { return x >= 0 && x < Maps.W && r >= 0 && r < Maps.H; }
    public char At(int x, int r) { return In(x, r) ? c[x, r] : '#'; }

    public static bool IsRealWallCh(char ch) { return ch == '#' || ch == 'W' || ch == 'G'; }
    public static bool IsFakeCh(char ch) { return ch == 'F' || ch == 'f'; }
    public static bool IsOneUseCh(char ch) { return ch == 'o' || ch == 'k'; }
    public static bool IsFlickerCh(char ch) { return ch == 'a' || ch == 'b'; }
    /// <summary>Anything that exists only in the old game: plain, one-use and flicker planks.</summary>
    public static bool IsBridgeCh(char ch) { return ch == 'B' || ch == 'c' || IsOneUseCh(ch) || IsFlickerCh(ch); }
    public static bool IsPitCh(char ch) { return ch == '~'; }
    public static bool IsFancyCh(char ch) { return ch == 'W' || ch == 'F'; }
    public static bool IsCoinCh(char ch) { return ch == 'C' || ch == 'c' || ch == 'k'; }

    /// <summary>What the OLD GAME says you can walk on (ignoring planks that are currently gone).</summary>
    public bool TruthWalkable(int x, int r)
    {
        if (!In(x, r)) return false;
        char ch = c[x, r];
        return !(IsRealWallCh(ch) || IsPitCh(ch));
    }
    public bool IsBridge(int x, int r) { return In(x, r) && IsBridgeCh(c[x, r]); }
    public bool IsOneUse(int x, int r) { return In(x, r) && IsOneUseCh(c[x, r]); }
    public bool IsFlicker(int x, int r) { return In(x, r) && IsFlickerCh(c[x, r]); }
    /// <summary>Planks whose solidity changes while you play (they get their own collider).</summary>
    public bool IsDynamic(int x, int r) { return In(x, r) && (IsOneUseCh(c[x, r]) || IsFlickerCh(c[x, r])); }
    public bool IsFake(int x, int r) { return In(x, r) && IsFakeCh(c[x, r]); }
    public bool IsPit(int x, int r) { return In(x, r) && IsPitCh(c[x, r]); }
    public bool HasCoin(int x, int r) { return In(x, r) && IsCoinCh(c[x, r]); }
    public bool IsRealWall(int x, int r) { return !In(x, r) || IsRealWallCh(c[x, r]); }
    /// <summary>What the 3D REMAKE draws as a wall (real or fake).</summary>
    public bool Looks3DWall(int x, int r) { return In(x, r) && (IsRealWallCh(c[x, r]) || IsFakeCh(c[x, r])); }
    /// <summary>What the 3D remake draws as floor.</summary>
    public bool Looks3DFloor(int x, int r)
    {
        if (!In(x, r)) return false;
        char ch = c[x, r];
        return ch == '.' || ch == 'C' || ch == 'N' || ch == 'S' || ch == 'X' || IsFakeCh(ch);
    }

    /// <summary>Is flicker group <paramref name="ch"/> solid at this room-clock time?</summary>
    public bool FlickerSolidAt(char ch, float clock)
    {
        int phase = Mathf.FloorToInt(Mathf.Max(0f, clock) / def.flickerPeriod) % 2;
        return (ch == 'a') == (phase == 0);
    }

    public Vector3 TileCenter(int x, int r)
    {
        return new Vector3(origin.x + (x + 0.5f) * Maps.TS, origin.y, origin.z + (Maps.H - 1 - r + 0.5f) * Maps.TS);
    }
    public Vector3 TileCenter(Vector2Int t) { return TileCenter(t.x, t.y); }

    public void WorldToTile(Vector3 p, out int x, out int r)
    {
        x = Mathf.FloorToInt((p.x - origin.x) / Maps.TS);
        int fromBottom = Mathf.FloorToInt((p.z - origin.z) / Maps.TS);
        r = Maps.H - 1 - fromBottom;
    }

    /// <summary>Continuous position in tile units: x from the left edge, y from the TOP edge (pixel-game orientation).</summary>
    public Vector2 WorldToTileF(Vector3 p)
    {
        return new Vector2((p.x - origin.x) / Maps.TS, (MaxZ - p.z) / Maps.TS);
    }

    public Vector3 TileFToWorld(Vector2 t)
    {
        return new Vector3(origin.x + t.x * Maps.TS, origin.y, MaxZ - t.y * Maps.TS);
    }

    public Vector2Int SpawnTile => new Vector2Int(start.x, start.y - 1);
    public bool HasKey => def.key.x >= 0;
    /// <summary>Real floor that the remake draws and a guard may walk on (no notes, doors, planks or fake walls).</summary>
    public bool GuardFloor(int x, int r) { return In(x, r) && (c[x, r] == '.' || c[x, r] == 'C'); }

    // ---------- path finding (used by the map checker) ----------
    public struct Step { public Vector2Int tile; public bool wrapped; }

    public List<Step> FindPath(Vector2Int from, Vector2Int to, bool allowBridge = true, bool allowFake = true, bool allowWrap = true, bool blockExit = false)
    {
        var prev = new Dictionary<Vector2Int, Step>();
        var q = new Queue<Vector2Int>();
        q.Enqueue(from);
        prev[from] = new Step { tile = from, wrapped = false };
        int[] dx = { 0, 1, 0, -1 }, dr = { -1, 0, 1, 0 };
        bool found = false;
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == to) { found = true; break; }
            for (int i = 0; i < 4; i++)
            {
                int nx = cur.x + dx[i], nr = cur.y + dr[i];
                bool wrapped = false;
                if (def.wrap && allowWrap)
                {
                    if (nx < 0) { nx = Maps.W - 1; wrapped = true; }
                    else if (nx >= Maps.W) { nx = 0; wrapped = true; }
                }
                if (!In(nx, nr)) continue;
                if (!TruthWalkable(nx, nr)) continue;
                if (!allowBridge && IsBridge(nx, nr)) continue;
                if (!allowFake && IsFake(nx, nr)) continue;
                if (blockExit && nx == exit.x && nr == exit.y) continue;
                var n = new Vector2Int(nx, nr);
                if (prev.ContainsKey(n)) continue;
                prev[n] = new Step { tile = cur, wrapped = wrapped };
                q.Enqueue(n);
            }
        }
        if (!found) return null;
        var path = new List<Step>();
        var t = to;
        while (t != from)
        {
            var p = prev[t];
            path.Add(new Step { tile = t, wrapped = p.wrapped });
            t = p.tile;
        }
        path.Reverse();
        return path;
    }
}

/// <summary>Checks that every room can be finished and that each one really needs its trick.</summary>
public static class MapValidator
{
    public static string RunAll()
    {
        var sb = new System.Text.StringBuilder();
        bool ok = true;
        for (int i = 0; i < Maps.Defs.Length; i++)
        {
            var d = Maps.Defs[i];
            if (d.rows.Length != Maps.H) { sb.AppendLine("FAIL room " + (i + 1) + ": needs " + Maps.H + " rows"); ok = false; continue; }
            bool lenOk = true;
            foreach (var row in d.rows) if (row.Length != Maps.W) { lenOk = false; sb.AppendLine("FAIL room " + (i + 1) + ": row '" + row + "' has length " + row.Length); }
            if (!lenOk) { ok = false; continue; }
            var room = new Room(d, i);
            var from = room.SpawnTile;
            var full = room.FindPath(from, room.exit);
            if (full == null) { sb.AppendLine("FAIL room " + (i + 1) + ": no path to the exit"); ok = false; continue; }
            bool needsBridge = room.FindPath(from, room.exit, false, true, true) == null;
            bool needsFake = room.FindPath(from, room.exit, true, false, true) == null;
            bool needsWrap = room.FindPath(from, room.exit, true, true, false) == null;
            int coinsReach = 0;
            foreach (var cn in room.coins) if (room.FindPath(from, cn) != null) coinsReach++;
            if (d.boss)
            {
                // the Captain's arena: an open hall with no coins, key or patrols (the fight is the room)
                if (room.coins.Count != 0 || room.HasKey || d.guards.Length != 0) { sb.AppendLine("FAIL room " + (i + 1) + ": a boss room has no coins, key or guards"); ok = false; }
                if (i != Maps.Defs.Length - 1) { sb.AppendLine("FAIL room " + (i + 1) + ": the boss room has to be the last one"); ok = false; }
                sb.AppendLine("room " + (i + 1) + " '" + d.title + "': boss arena, path " + full.Count + " steps");
                continue;
            }
            if (room.coins.Count != 3 || coinsReach != 3) { sb.AppendLine("FAIL room " + (i + 1) + ": coins " + coinsReach + "/" + room.coins.Count + " reachable (want 3)"); ok = false; }
            if (d.wrap)
            {
                for (int r = 0; r < Maps.H; r++)
                {
                    bool l = room.TruthWalkable(0, r), rr = room.TruthWalkable(Maps.W - 1, r);
                    if (l != rr) { sb.AppendLine("FAIL room " + (i + 1) + ": wrap row " + r + " is open on one side only"); ok = false; }
                }
            }
            else
            {
                for (int r = 0; r < Maps.H; r++)
                    if (room.TruthWalkable(0, r) || room.TruthWalkable(Maps.W - 1, r)) { sb.AppendLine("FAIL room " + (i + 1) + ": open side edge at row " + r + " without wrap"); ok = false; }
            }
            foreach (var s in d.slimes)
                foreach (var p in s.loop)
                    if (!room.TruthWalkable(p.x, p.y)) { sb.AppendLine("FAIL room " + (i + 1) + ": slime waypoint " + p + " is not walkable"); ok = false; }
            if (d.lasers != null)
                foreach (var v in d.lasers)
                {
                    foreach (int r in v.rows) if (r < 1 || r > Maps.H - 2) { sb.AppendLine("FAIL room " + (i + 1) + ": laser row " + r + " is outside the room"); ok = false; }
                    foreach (int x in v.cols) if (x < 1 || x > Maps.W - 2) { sb.AppendLine("FAIL room " + (i + 1) + ": laser column " + x + " is outside the room"); ok = false; }
                    if (v.t < 0f || v.t >= d.laserPeriod) { sb.AppendLine("FAIL room " + (i + 1) + ": laser time " + v.t + " is outside the loop"); ok = false; }
                }
            // keys and guards (new in the remake)
            if (room.HasKey)
            {
                var kt = d.key;
                if (!room.TruthWalkable(kt.x, kt.y) || room.IsDynamic(kt.x, kt.y) || kt == room.exit || kt == room.start)
                { sb.AppendLine("FAIL room " + (i + 1) + ": the key at " + kt + " is not on a tile you can stand on"); ok = false; }
                else if (room.FindPath(from, kt, true, true, true, true) == null)
                { sb.AppendLine("FAIL room " + (i + 1) + ": the key at " + kt + " cannot be reached"); ok = false; }
            }
            else { sb.AppendLine("FAIL room " + (i + 1) + ": no key"); ok = false; }
            if (d.guards.Length == 0) { sb.AppendLine("FAIL room " + (i + 1) + ": no guard"); ok = false; }
            foreach (var g in d.guards)
            {
                int n = g.loop.Length;
                for (int k = 0; k < n; k++)
                {
                    Vector2Int a = g.loop[k], b = g.loop[(k + 1) % n];
                    if (a.x != b.x && a.y != b.y) { sb.AppendLine("FAIL room " + (i + 1) + ": guard leg " + a + " to " + b + " is not straight"); ok = false; continue; }
                    int steps = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
                    for (int q = 0; q <= steps; q++)
                    {
                        int gx = a.x + (steps == 0 ? 0 : (b.x - a.x) * q / steps), gr = a.y + (steps == 0 ? 0 : (b.y - a.y) * q / steps);
                        if (!room.GuardFloor(gx, gr)) { sb.AppendLine("FAIL room " + (i + 1) + ": guard walks over (" + gx + "," + gr + "), which is not plain floor"); ok = false; }
                        if (gx == from.x && gr == from.y) { sb.AppendLine("FAIL room " + (i + 1) + ": a guard walks over the spawn tile"); ok = false; }
                    }
                }
            }
            // a room that calls itself a plank maze must be one: its quickest route has to turn several times
            // on the planks, so there is never a straight ladder from the door to the exit.
            // (Room 1 uses the same snapping planks for a short straight bridge: that is not a maze.)
            bool anyOneUse = false;
            for (int r = 0; r < Maps.H; r++) for (int x = 0; x < Maps.W; x++) if (room.IsOneUse(x, r)) anyOneUse = true;
            if (anyOneUse && d.plankMaze)
            {
                int turns = 0;
                var pts = new List<Vector2Int> { from };
                foreach (var st in full) pts.Add(st.tile);
                for (int k = 1; k < pts.Count - 1; k++)
                {
                    if (!room.IsOneUse(pts[k].x, pts[k].y)) continue;
                    Vector2Int a = pts[k] - pts[k - 1], b = pts[k + 1] - pts[k];
                    if (a != b) turns++;
                }
                if (turns < 5) { sb.AppendLine("FAIL room " + (i + 1) + ": the plank maze is too direct (only " + turns + " turns on the quickest route)"); ok = false; }
                else sb.AppendLine("room " + (i + 1) + ": plank maze quickest route has " + turns + " turns");
            }
            if (needsBridge != d.needBridge || needsFake != d.needFake || needsWrap != d.needWrap)
            {
                sb.AppendLine("FAIL room " + (i + 1) + ": trick requirements differ from the design (bridge " + needsBridge + "/" + d.needBridge + ", fake " + needsFake + "/" + d.needFake + ", wrap " + needsWrap + "/" + d.needWrap + ")");
                ok = false;
            }
            sb.AppendLine("room " + (i + 1) + " '" + d.title + "': path " + full.Count + " steps, needsBridge=" + needsBridge + " needsFake=" + needsFake + " needsWrap=" + needsWrap + " coins=" + coinsReach);
        }
        sb.Insert(0, ok ? "MAPS OK\n" : "MAPS HAVE PROBLEMS\n");
        return sb.ToString();
    }
}
