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

public class RoomDef
{
    public string title;
    public string todo;         // checklist line on the TODO pad
    public string note;         // the dev's sticky note for this room
    public string[] rows;
    public bool wrap;
    public bool deleteWave;
    public float finish = 0.7f; // how much of the 3D room looks finished (cosmetic)
    public SlimeDef[] slimes = new SlimeDef[0];
}

public static class Maps
{
    public const float TS = 2.0f;           // metres per tile
    public const int W = 13, H = 11;
    public const int CorridorTiles = 3;
    public const float CorridorRise = 1.5f;
    public const float WallHeight = 3.2f;

    public static readonly RoomDef[] Defs =
    {
        new RoomDef
        {
            title = "THE MISSING BRIDGE",
            todo = "model the bridge",
            note = "TODO: model the bridge.\nCollision already works!\n(check the old game  -->)",
            finish = 0.92f,
            rows = new[]
            {
                "######X######",
                "#....C.C....#",
                "#...........#",
                "#...........#",
                "#~~~~~BB~~~~#",
                "#~~~~~Bc~~~~#",
                "#~~~~~BB~~~~#",
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
            finish = 0.85f,
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
            finish = 0.6f,
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
            wrap = true,
            finish = 0.5f,
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
            title = "SHIP IT",
            todo = "ship it",
            note = "FIXME: unfinished floor\ngets deleted. RUN!\nExit = BUILD & RUN.",
            wrap = true,
            deleteWave = true,
            finish = 0.25f,
            rows = new[]
            {
                "#########X###",
                "########...##",
                ".C....#......",
                "###f#####W###",
                "#..........C#",
                "#~~~~~~~BB~~#",
                "#~~~BBBBBc~~#",
                "#~~~BB~~~~~~#",
                "#......N....#",
                "#...........#",
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
                if (ch == 'C' || ch == 'c') coins.Add(new Vector2Int(x, r));
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
    public static bool IsBridgeCh(char ch) { return ch == 'B' || ch == 'c'; }
    public static bool IsPitCh(char ch) { return ch == '~'; }
    public static bool IsFancyCh(char ch) { return ch == 'W' || ch == 'F'; }

    /// <summary>What the OLD GAME says you can walk on. This is the real collision.</summary>
    public bool TruthWalkable(int x, int r)
    {
        if (!In(x, r)) return false;
        char ch = c[x, r];
        return !(IsRealWallCh(ch) || IsPitCh(ch));
    }
    public bool IsBridge(int x, int r) { return In(x, r) && IsBridgeCh(c[x, r]); }
    public bool IsFake(int x, int r) { return In(x, r) && IsFakeCh(c[x, r]); }
    public bool IsPit(int x, int r) { return In(x, r) && IsPitCh(c[x, r]); }
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

    // ---------- path finding (used by the validator and the test autopilot) ----------
    public struct Step { public Vector2Int tile; public bool wrapped; }

    public List<Step> FindPath(Vector2Int from, Vector2Int to, bool allowBridge = true, bool allowFake = true, bool allowWrap = true)
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
            sb.AppendLine("room " + (i + 1) + " '" + d.title + "': path " + full.Count + " steps, needsBridge=" + needsBridge + " needsFake=" + needsFake + " needsWrap=" + needsWrap + " coins=" + coinsReach);
        }
        // expectations per room: which trick each one must require
        bool[] wantBridge = { true, false, true, false, true };
        bool[] wantFake = { false, true, false, false, true };
        bool[] wantWrap = { false, false, false, true, true };
        for (int i = 0; i < Maps.Defs.Length && i < 5; i++)
        {
            var room = new Room(Maps.Defs[i], i);
            var from = room.SpawnTile;
            if (room.FindPath(from, room.exit) == null) continue;
            bool nb = room.FindPath(from, room.exit, false, true, true) == null;
            bool nf = room.FindPath(from, room.exit, true, false, true) == null;
            bool nw = room.FindPath(from, room.exit, true, true, false) == null;
            if (nb != wantBridge[i] || nf != wantFake[i] || nw != wantWrap[i])
            {
                sb.AppendLine("FAIL room " + (i + 1) + ": trick requirements differ from the design (bridge " + nb + "/" + wantBridge[i] + ", fake " + nf + "/" + wantFake[i] + ", wrap " + nw + "/" + wantWrap[i] + ")");
                ok = false;
            }
        }
        sb.Insert(0, ok ? "MAPS OK\n" : "MAPS HAVE PROBLEMS\n");
        return sb.ToString();
    }
}
