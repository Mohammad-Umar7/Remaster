using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the unfinished 3D remake from the room grids: finished parts in marker colour,
/// grey-box parts on graph paper, and nothing at all where the dev never modelled anything.
/// Collision comes from the OLD GAME's truth, not from what is drawn.
/// </summary>
public class World3D
{
    public Transform root;
    public Room[] rooms;
    public float portalTriggerZ;
    public Vector3 portalPos;

    const float TS = Maps.TS;
    const float WH = Maps.WallHeight;

    // lookups for things that change while playing
    readonly Dictionary<int, List<GameObject>> floorVis = new Dictionary<int, List<GameObject>>();   // floor + skirt visuals per tile
    readonly Dictionary<int, List<Collider>> rowCols = new Dictionary<int, List<Collider>>();         // floor colliders per row
    readonly Dictionary<int, Collider> tileCols = new Dictionary<int, Collider>();                      // planks that come and go
    public const float LaserY = 1.0f;
    readonly Dictionary<int, List<Renderer>> bridgeVis = new Dictionary<int, List<Renderer>>();
    readonly Dictionary<int, List<Renderer>> fakeVis = new Dictionary<int, List<Renderer>>();
    MaterialPropertyBlock mpb;

    // things that are NEW in the remake (the old game never had them): one set per room.
    // Rooms that wrap are drawn three times side by side, so each thing has up to three copies.
    class RoomDyn
    {
        public readonly List<Transform> gates = new List<Transform>();
        public readonly List<GameObject> padlocks = new List<GameObject>();
        public readonly List<Transform> keys = new List<Transform>();
        public readonly List<Transform> rings = new List<Transform>();
        public readonly List<Transform> discs = new List<Transform>(), bursts = new List<Transform>();
        public readonly List<Vector3> gateBase = new List<Vector3>(), keyBase = new List<Vector3>();
        public readonly List<List<Transform>> guards = new List<List<Transform>>();     // [guard][copy]
        public readonly List<List<Transform>> guardRings = new List<List<Transform>>();
        public readonly List<float> guardYaw = new List<float>();
        public readonly List<float> copyOffset = new List<float>();
        public Collider gateCol;
        public Vector3 gatePos;
        public float open, keyGone, rattle;
    }
    RoomDyn[] dyn;
    public const float GuardScale = 1.12f;
    const float KeyScale = 1.45f, KeyHeight = 1.2f;

    static int Key(int room, int x, int r) { return room * 10000 + r * 100 + x; }
    static int RowKey(int room, int r) { return room * 100 + r; }

    static float Hash(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    public void Build(Room[] rooms)
    {
        this.rooms = rooms;
        mpb = new MaterialPropertyBlock();
        root = new GameObject("World").transform;

        // place rooms: each one north of the last, a little higher, doors lined up
        Vector3 o = Vector3.zero;
        for (int i = 0; i < rooms.Length; i++)
        {
            rooms[i].origin = o;
            if (i < rooms.Length - 1)
            {
                o.x += (rooms[i].exit.x - rooms[i + 1].start.x) * TS;
                o.z += Maps.H * TS + Maps.CorridorTiles * TS;
                o.y += Maps.CorridorRise;
            }
        }

        dyn = new RoomDyn[rooms.Length];
        for (int i = 0; i < rooms.Length; i++) BuildRoom(rooms[i]);
        for (int i = 0; i < rooms.Length; i++) BuildCorridor(rooms[i], i == rooms.Length - 1);
        BuildSkyline();
    }

    // ------------------------------------------------------------------ rooms
    bool Finished(Room room, int x, int r)
    {
        // more finished near the entrance, less toward the far end; a little patchy
        float along = 1f - r / (float)(Maps.H - 1);           // 0 at the start, 1 at the exit
        float threshold = room.def.finish * 1.35f - along * 0.55f;
        return Hash(room.index * 7 + 3, x / 2, r / 2) * 0.55f + Hash(room.index, x, r) * 0.25f < threshold;
    }

    readonly Dictionary<string, Material> wallMats = new Dictionary<string, Material>();
    /// <summary>Wall material: like the shared sketch material but darker toward its base.</summary>
    Material WallMat(Color color, Texture tex, bool dissolve, bool grey = false)
    {
        string key = ColorUtility.ToHtmlStringRGB(color) + (tex ? tex.GetInstanceID() : 0) + dissolve + grey;
        Material m;
        if (wallMats.TryGetValue(key, out m) && m != null) return m;
        m = new Material(TexLib.SketchShader);
        m.color = color;
        m.SetFloat("_Mode", grey ? 1 : 0);
        m.SetFloat("_Flag", grey ? 0.5f : 0f);
        m.SetFloat("_Dissolve", dissolve ? 1 : 0);
        m.SetFloat("_Ghost", 0);
        m.SetFloat("_Reveal", 1f);
        m.SetFloat("_HatchScale", 0.35f);
        m.SetFloat("_AO", 1f);
        if (tex != null) m.mainTexture = tex;
        wallMats[key] = m;
        return m;
    }

    GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool collider = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.localScale = size;
        if (mat != null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = TexLib.Cube;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
        if (collider) go.AddComponent<BoxCollider>();
        return go;
    }

    GameObject Quad(Transform parent, string name, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos; go.transform.rotation = rot; go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        return go;
    }

    void BuildRoom(Room room)
    {
        var parent = new GameObject("Room" + (room.index + 1)).transform;
        parent.SetParent(root, false);
        var vis = new GameObject("Vis").transform; vis.SetParent(parent, false);
        var col = new GameObject("Col").transform; col.SetParent(parent, false);
        var deco = new GameObject("Deco").transform; deco.SetParent(parent, false);
        float y = room.FloorY;
        int ri = room.index;

        Material matCliff = TexLib.Mat(Pal.Stone, 0, 0f, TexLib.bricks);
        Material matCliffGrey = TexLib.Mat(Color.white, 1, 0.5f);

        for (int r = 0; r < Maps.H; r++)
        {
            for (int x = 0; x < Maps.W; x++)
            {
                char ch = room.c[x, r];
                Vector3 c = room.TileCenter(x, r);
                bool fin = Finished(room, x, r);
                string id = "_" + x + "_" + r;

                // ---- what the remake DRAWS ----
                if (room.Looks3DFloor(x, r))
                {
                    Material m = fin
                        ? TexLib.Mat(((x + r) % 2 == 0) ? Pal.Floor : Pal.FloorAlt, 0, 0f, TexLib.tileOutline, false, false, 0.5f)
                        : TexLib.Mat(Color.white, 1, 0.5f);
                    Box(vis, "f" + id, c + new Vector3(0, -0.2f, 0), new Vector3(TS, 0.4f, TS), m);
                }
                if (room.Looks3DWall(x, r) && ch != 'G')
                {
                    bool fake = Room.IsFakeCh(ch);
                    bool fancy = Room.IsFancyCh(ch);
                    Material m;
                    if (fancy) m = WallMat(Pal.Stone, TexLib.bricksFancy, fake);
                    else if (fin || fake) m = WallMat(Hash(ri, x * 3, r * 5) > 0.5f ? Pal.Stone : Pal.StoneDark, TexLib.bricks, fake);
                    else m = WallMat(Color.white, null, false, true);
                    bool sideEdge = (x == 0 || x == Maps.W - 1) && r > 0 && r < Maps.H - 1 && !fake && !fancy;
                    if (sideEdge)
                    {
                        // the outer side walls are open colonnades: a low parapet, columns, and a beam on top,
                        // so you can see out across the blank page to everything the dev never finished
                        Box(vis, "w" + id, c + new Vector3(0, 0.55f, 0), new Vector3(TS, 1.1f, TS), m);
                        Material beam = fin ? WallMat(Pal.StoneDark, TexLib.bricks, false) : WallMat(Color.white, null, false, true);
                        Box(vis, "w" + id + "_beam", c + new Vector3(0, WH - 0.2f, 0), new Vector3(0.8f, 0.4f, TS), beam);
                        if (r % 2 == 1)
                        {
                            var col2 = Props.Spawn("pillar", deco, c + new Vector3(0, 1.1f, 0), Hash(ri, x, r) * 90f, 1f, false, !fin);
                            col2.transform.localScale = new Vector3(1f, 0.6f, 1f);
                        }
                    }
                    else
                    {
                        Box(vis, (fake ? "k" : "w") + id, c + new Vector3(0, WH * 0.5f, 0), new Vector3(TS, WH, TS), m);
                        // battlements along the top give the walls a real silhouette against the sky
                        if ((x + r) % 2 == 0)
                            Box(vis, (fake ? "k" : "w") + id + "_m", c + new Vector3(0, WH + 0.3f, 0), new Vector3(TS * 0.55f, 0.6f, TS * 0.55f), m);
                        if (fancy) AddBanners(room, vis, x, r, c, fake, id);
                        else if (fin && !fake) MaybeTorch(room, deco, x, r, c);
                    }
                }
                if (ch == 'G')
                {
                    Props.Spawn("gate", vis, c, 0f, 1f);
                    // little stone lintel so the gate sits in a frame
                    Box(vis, "w" + id, c + new Vector3(0, WH - 0.1f, 0), new Vector3(TS, 0.2f, TS * 0.5f), TexLib.Mat(Pal.StoneDark, 0, 0f, TexLib.bricks));
                }
                if (room.IsBridge(x, r))
                {
                    // never modelled: a hidden slab that gets "sketched in" the first time you stand on it
                    var slab = Box(vis, "b" + id, c + new Vector3(0, -0.07f, 0), new Vector3(TS * 0.97f, 0.14f, TS * 0.97f), TexLib.Mat(Color.white, 1, 0.5f));
                    slab.GetComponent<Renderer>().enabled = false;
                }

                // ---- cliff skirts: finished edges fall away into the blank page ----
                if (room.Looks3DFloor(x, r) || (room.Looks3DWall(x, r) && ch != 'G'))
                {
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 1 ? 1 : d == 3 ? -1 : 0), nr = r + (d == 0 ? -1 : d == 2 ? 1 : 0);
                        bool open;
                        if (!room.In(nx, nr))
                        {
                            // outside the room: open unless a doorway continues there, or the room wraps sideways
                            bool door = (ch == 'X' && nr < 0) || (ch == 'S' && nr >= Maps.H);
                            bool wrapSide = room.def.wrap && (nx < 0 || nx >= Maps.W);
                            open = !door && !wrapSide;
                        }
                        else open = room.IsPit(nx, nr) || room.IsBridge(nx, nr);
                        if (!open) continue;
                        Vector3 dir = new Vector3(d == 1 ? 1 : d == 3 ? -1 : 0, 0, d == 0 ? 1 : d == 2 ? -1 : 0);
                        float top = room.Looks3DFloor(x, r) ? -0.4f : 0f;
                        Quad(vis, "s" + id + "_" + d, TexLib.FadeQuad, c + dir * (TS * 0.5f) + new Vector3(0, top, 0),
                            Quaternion.LookRotation(-dir), new Vector3(TS, 2.8f, 1f), fin ? matCliff : matCliffGrey);
                    }
                }

                // ---- set dressing ----
                if (ch == 'N') BuildNote(room, deco, c);
                if (ch == 'X' || ch == 'S')
                {
                    Props.Spawn("door_arch", deco, c + new Vector3(0, 0, ch == 'X' ? -TS * 0.5f + 0.3f : TS * 0.5f - 0.3f), 0f, 1f);
                    if (ch == 'S' && ri == 0)
                    {
                        // the way you came in is shut: you are stuck in here
                        Box(vis, "w_shut", c + new Vector3(0, WH * 0.5f, -TS * 0.25f), new Vector3(TS, WH, TS * 0.5f), TexLib.Mat(Pal.WoodDark, 0, 0f, TexLib.planks));
                        Box(col, "c_shut", c + new Vector3(0, WH * 0.5f, -TS * 0.25f), new Vector3(TS, WH, TS * 0.5f), null, true);
                    }
                }
            }
        }

        // ---- what the OLD GAME says is solid: merged runs of colliders per row ----
        for (int r = 0; r < Maps.H; r++)
        {
            int runStart = -1;
            for (int x = 0; x <= Maps.W; x++)
            {
                bool walk = x < Maps.W && room.TruthWalkable(x, r) && !room.IsDynamic(x, r);
                if (walk && runStart < 0) runStart = x;
                if (!walk && runStart >= 0)
                {
                    int n = x - runStart;
                    Vector3 a = room.TileCenter(runStart, r), b = room.TileCenter(x - 1, r);
                    var go = Box(col, "cf_" + runStart + "_" + r, (a + b) * 0.5f + new Vector3(0, -0.5f, 0), new Vector3(n * TS, 1.0f, TS), null, true);
                    List<Collider> list;
                    if (!rowCols.TryGetValue(RowKey(ri, r), out list)) { list = new List<Collider>(); rowCols[RowKey(ri, r)] = list; }
                    list.Add(go.GetComponent<Collider>());
                    runStart = -1;
                }
            }
            runStart = -1;
            for (int x = 0; x <= Maps.W; x++)
            {
                bool wall = x < Maps.W && Room.IsRealWallCh(room.c[x, r]);
                if (wall && runStart < 0) runStart = x;
                if (!wall && runStart >= 0)
                {
                    int n = x - runStart;
                    Vector3 a = room.TileCenter(runStart, r), b = room.TileCenter(x - 1, r);
                    Box(col, "cw_" + runStart + "_" + r, (a + b) * 0.5f + new Vector3(0, WH * 0.5f + 1f, 0), new Vector3(n * TS, WH + 2f, TS), null, true);
                    runStart = -1;
                }
            }
        }

        // planks that come and go (one-use, flicker) each get their own collider so they can switch off
        for (int r = 0; r < Maps.H; r++)
            for (int x = 0; x < Maps.W; x++)
            {
                if (!room.IsDynamic(x, r)) continue;
                var go = Box(col, "ct_" + x + "_" + r, room.TileCenter(x, r) + new Vector3(0, -0.5f, 0), new Vector3(TS, 1.0f, TS), null, true);
                tileCols[Key(ri, x, r)] = go.GetComponent<Collider>();
            }

        // laser emitters: little nodes on the walls at both ends of every line that can fire
        if (room.def.lasers != null)
        {
            var rowsUsed = new HashSet<int>(); var colsUsed = new HashSet<int>();
            foreach (var v in room.def.lasers) { foreach (int r in v.rows) rowsUsed.Add(r); foreach (int x in v.cols) colsUsed.Add(x); }
            Material body = TexLib.Mat(Pal.Ink, 0, 0f), lens = TexLib.Mat(Pal.Red, 2, 0f);
            foreach (int r in rowsUsed)
            {
                float z = room.TileCenter(0, r).z;
                for (int s = 0; s < 2; s++)
                {
                    float x = s == 0 ? room.MinX + TS + 0.12f : room.MaxX - TS - 0.12f;
                    Box(deco, "emitter", new Vector3(x, y + LaserY, z), new Vector3(0.26f, 0.36f, 0.36f), body);
                    Box(deco, "lens", new Vector3(x + (s == 0 ? 0.12f : -0.12f), y + LaserY, z), new Vector3(0.08f, 0.2f, 0.2f), lens);
                }
            }
            // column beams run from the far wall down to the safe-zone line; their near ends are plates in the floor
            float zLine = room.TileCenter(0, Maps.LaserLobbyRow - 1).z - TS * 0.5f;
            foreach (int x in colsUsed)
            {
                float cx = room.TileCenter(x, 0).x;
                float zFar = room.MaxZ - TS - 0.12f;
                Box(deco, "emitter", new Vector3(cx, y + LaserY, zFar), new Vector3(0.36f, 0.36f, 0.26f), body);
                Box(deco, "lens", new Vector3(cx, y + LaserY, zFar - 0.12f), new Vector3(0.2f, 0.2f, 0.08f), lens);
                Box(deco, "plate", new Vector3(cx, y + 0.03f, zLine), new Vector3(0.6f, 0.06f, 0.34f), body);
                Box(deco, "lens", new Vector3(cx, y + 0.07f, zLine), new Vector3(0.34f, 0.05f, 0.16f), lens);
            }
            if (colsUsed.Count > 0)
            {
                Quad(deco, "safeline", TexLib.QuadUp, new Vector3((room.MinX + room.MaxX) * 0.5f, y + 0.025f, zLine), Quaternion.identity,
                    new Vector3((Maps.W - 2) * TS, 1f, 0.14f), TexLib.DecalMat(TexLib.white, Pal.A(Pal.Teal, 0.85f)));
                WorldLabel(deco, "safe zone", new Vector3((room.MinX + room.MaxX) * 0.5f + 5.2f, y + 0.035f, zLine - 0.55f), Quaternion.Euler(90f, 0, 0), 3.2f, 0.7f, 110, Pal.Hex("1f8f84"), TextAnchor.MiddleCenter, TexLib.title);
            }
        }

        // checkpoint: drawn on the floor where you (re)start this room
        {
            Vector3 sp = room.TileCenter(room.SpawnTile);
            Quad(deco, "checkpoint", TexLib.QuadUp, sp + new Vector3(0, 0.03f, 0), Quaternion.identity, new Vector3(1.7f, 1f, 1.7f), TexLib.DecalMat(TexLib.ring, Pal.A(Pal.Teal, 0.9f)));
            WorldLabel(deco, "checkpoint " + (ri + 1), sp + new Vector3(0, 0.04f, -0.05f), Quaternion.Euler(90f, 0, 0), 1.5f, 0.5f, 46, Pal.Hex("1f8f84"), TextAnchor.MiddleCenter, TexLib.title);
        }

        RegisterVis(vis, ri);
        BuildDyn(room, parent, col);

        // rooms that wrap are drawn three times side by side, so the "bug" bends space seamlessly
        if (room.def.wrap)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                var copy = Object.Instantiate(vis.gameObject, parent);
                copy.name = "VisCopy" + (s < 0 ? "W" : "E");
                copy.transform.position += new Vector3(s * Maps.W * TS, 0, 0);
                foreach (var cl in copy.GetComponentsInChildren<Collider>()) Object.Destroy(cl);
                RegisterVis(copy.transform, ri);
                var dcopy = Object.Instantiate(deco.gameObject, parent);
                dcopy.name = "DecoCopy" + (s < 0 ? "W" : "E");
                dcopy.transform.position += new Vector3(s * Maps.W * TS, 0, 0);
            }
        }
    }

    /// <summary>
    /// The locked exit, the key and the guards. They are new in the remake, so nothing here is ever
    /// drawn by the pixel game: you find the key and dodge the guards with your own eyes.
    /// </summary>
    void BuildDyn(Room room, Transform parent, Transform col)
    {
        var d = new RoomDyn();
        dyn[room.index] = d;
        var holder = new GameObject("New").transform;
        holder.SetParent(parent, false);
        int copies = room.def.wrap ? 3 : 1;
        for (int k = 0; k < copies; k++) d.copyOffset.Add(room.def.wrap ? (k - 1) * Maps.W * TS : 0f);

        d.gatePos = room.TileCenter(room.exit) + new Vector3(0, 0, -TS * 0.5f + 0.78f);
        if (room.HasKey)
        {
            Vector3 kp = room.TileCenter(room.def.key);
            foreach (float ox in d.copyOffset)
            {
                Vector3 off = new Vector3(ox, 0, 0);
                // a portcullis across the exit, with a padlock at eye height
                var gate = Props.Spawn("gate", holder, d.gatePos + off, 0f, 1f);
                var padlock = Props.Spawn("padlock", gate.transform, d.gatePos + off + new Vector3(0, 1.3f, -0.16f), 180f, 1.35f);
                d.gates.Add(gate.transform); d.gateBase.Add(d.gatePos + off); d.padlocks.Add(padlock);
                // the key turns slowly above a pencilled ring, so it catches the eye from across the room
                var key = Props.Spawn("key", holder, kp + off + new Vector3(0, KeyHeight, 0), 0f, KeyScale);
                d.keys.Add(key.transform); d.keyBase.Add(kp + off + new Vector3(0, KeyHeight, 0));
                var ring = Quad(holder, "keyring", TexLib.QuadUp, kp + off + new Vector3(0, 0.035f, 0), Quaternion.identity, new Vector3(1.5f, 1f, 1.5f),
                    TexLib.DecalMat(TexLib.ring, Pal.A(Pal.Hex("d19a00"), 0.9f)));
                d.rings.Add(ring.transform);
                // a yellow key on pale paper is easy to miss, so it floats in front of a purple badge
                // with a comic burst round it; both always face you
                var disc = Quad(holder, "keydisc", TexLib.QuadUp, d.keyBase[d.keyBase.Count - 1], Quaternion.identity, new Vector3(1.35f, 1f, 1.35f),
                    TexLib.DecalMat(TexLib.keyDisc, Pal.A(Pal.Purple, 0.92f)));
                var burst = Quad(holder, "keyburst", TexLib.QuadUp, d.keyBase[d.keyBase.Count - 1], Quaternion.identity, new Vector3(2.1f, 1f, 2.1f),
                    TexLib.DecalMat(TexLib.burst, Pal.A(Pal.Hex("e0a400"), 0.95f)));
                disc.GetComponent<Renderer>().receiveShadows = false; burst.GetComponent<Renderer>().receiveShadows = false;
                d.discs.Add(disc.transform); d.bursts.Add(burst.transform);
            }
            // the collider reaches forward to the arch, so you stop a step back from the bars and can read the padlock
            d.gateCol = Box(col, "c_gate", d.gatePos + new Vector3(0, WH * 0.5f, -0.3f), new Vector3(TS, WH, 0.9f), null, true).GetComponent<Collider>();
        }
        for (int g = 0; g < room.def.guards.Length; g++)
        {
            var list = new List<Transform>();
            var rings = new List<Transform>();
            Vector2Int at = room.def.guards[g].loop[0];
            // the red pencil ring under a guard is exactly how close it can catch you
            float ringSize = Maps.GuardHit * TS / 0.42f;
            foreach (float ox in d.copyOffset)
            {
                Vector3 gp = room.TileCenter(at) + new Vector3(ox, 0, 0);
                list.Add(Props.Spawn("guard", holder, gp, 0f, GuardScale).transform);
                var ring = Quad(holder, "guardring", TexLib.QuadUp, gp + new Vector3(0, 0.03f, 0), Quaternion.identity, new Vector3(ringSize, 1f, ringSize),
                    TexLib.DecalMat(TexLib.ring, Pal.A(Pal.Red, 0.6f)));
                rings.Add(ring.transform);
            }
            d.guards.Add(list);
            d.guardRings.Add(rings);
            d.guardYaw.Add(float.NaN);
        }
    }

    /// <summary>Where the locked exit's gate stands (also when the room has no key).</summary>
    public Vector3 GatePos(int room) { return dyn[room].gatePos; }

    /// <summary>The gate shakes when you walk into it without the key.</summary>
    public void RattleGate(int room) { dyn[room].rattle = 0.45f; }

    /// <summary>Put every gate straight into its right state (new run, room jump): no opening animation.</summary>
    public void SnapDoors(GameManager gm)
    {
        for (int i = 0; i < rooms.Length; i++)
        {
            bool open = gm.ExitOpen(i);
            dyn[i].open = open ? 1f : 0f;
            dyn[i].keyGone = open ? 1f : 0f;
            dyn[i].rattle = 0f;
        }
        Animate(gm, 0f);
    }

    /// <summary>Every frame: keys turn and bob, gates sink once unlocked, guards hop along their patrols.</summary>
    public void Animate(GameManager gm, float dt)
    {
        if (dyn == null) return;
        float t = Time.time;
        Vector3 eye = (gm.player != null && gm.player.cam != null) ? gm.player.cam.transform.position : Vector3.zero;
        for (int i = 0; i < rooms.Length; i++)
        {
            var d = dyn[i];
            if (d == null) continue;
            Room room = rooms[i];
            RoomState s = gm.rs[i];

            if (room.HasKey)
            {
                bool open = s.keyTaken;
                d.open = Mathf.MoveTowards(d.open, open ? 1f : 0f, dt / 0.8f);
                d.keyGone = open ? Mathf.Min(1f, d.keyGone + dt / 0.4f) : 0f;
                if (d.rattle > 0f) d.rattle = Mathf.Max(0f, d.rattle - dt);
                float e = d.open * d.open * (3f - 2f * d.open);
                float shake = d.rattle > 0f ? Mathf.Sin(t * 70f) * 0.035f * (d.rattle / 0.45f) : 0f;
                for (int k = 0; k < d.gates.Count; k++)
                {
                    bool show = d.open < 0.999f;
                    if (d.gates[k].gameObject.activeSelf != show) d.gates[k].gameObject.SetActive(show);
                    // unlocked bars sink into the floor
                    d.gates[k].position = d.gateBase[k] + new Vector3(shake, -3.15f * e, 0);
                    if (d.padlocks[k].activeSelf == open) d.padlocks[k].SetActive(!open);
                    bool keyShow = d.keyGone < 0.999f;
                    if (d.keys[k].gameObject.activeSelf != keyShow) d.keys[k].gameObject.SetActive(keyShow);
                    if (d.rings[k].gameObject.activeSelf == open) d.rings[k].gameObject.SetActive(!open);
                    if (d.discs[k].gameObject.activeSelf != keyShow) { d.discs[k].gameObject.SetActive(keyShow); d.bursts[k].gameObject.SetActive(keyShow); }
                    if (keyShow)
                    {
                        // picked up: it shoots upward, spins fast and shrinks away
                        float g = d.keyGone;
                        d.keys[k].position = d.keyBase[k] + new Vector3(0, Mathf.Sin(t * 2.3f + i) * 0.13f + g * 1.3f, 0);
                        d.keys[k].rotation = Quaternion.Euler(0, t * 130f + g * 720f, Mathf.Sin(t * 1.7f + i) * 8f);
                        d.keys[k].localScale = Vector3.one * (KeyScale * (1f - g));
                        // the badge sits just behind the key as seen from where you stand
                        Vector3 kp = d.keys[k].position;
                        Vector3 toEye = eye - kp;
                        if (toEye.sqrMagnitude < 0.01f) toEye = Vector3.back;
                        toEye.Normalize();
                        Quaternion face = Quaternion.FromToRotation(Vector3.up, toEye);
                        float fade = 1f - g;
                        d.discs[k].position = kp - toEye * 0.42f;
                        d.discs[k].rotation = face * Quaternion.AngleAxis(Mathf.Sin(t * 0.9f + i) * 12f, Vector3.up);
                        d.discs[k].localScale = new Vector3(1.35f, 1f, 1.35f) * fade;
                        d.bursts[k].position = kp - toEye * 0.5f;
                        d.bursts[k].rotation = face * Quaternion.AngleAxis(Mathf.Floor(t * 6f) * 17f, Vector3.up);
                        float bs = 2.1f * (1f + 0.07f * Mathf.Sin(t * 5f)) * fade;
                        d.bursts[k].localScale = new Vector3(bs, 1f, bs);
                    }
                    if (!open)
                    {
                        float pulse = 1.5f * (1f + 0.09f * Mathf.Sin(t * 3.1f));
                        d.rings[k].localScale = new Vector3(pulse, 1f, pulse);
                        d.rings[k].rotation = Quaternion.Euler(0, t * 25f, 0);
                    }
                }
                if (d.gateCol != null) d.gateCol.enabled = d.open < 0.55f;
            }

            for (int g = 0; g < s.guards.Count && g < d.guards.Count; g++)
            {
                Slime gs = s.guards[g];
                Vector3 wp = room.TileFToWorld(gs.pos);
                // tile space has y pointing down the screen, which is -Z in the world
                float yawTarget = Mathf.Atan2(gs.dir.x, -gs.dir.y) * Mathf.Rad2Deg;
                float yaw = float.IsNaN(d.guardYaw[g]) ? yawTarget : Mathf.MoveTowardsAngle(d.guardYaw[g], yawTarget, 520f * dt);
                d.guardYaw[g] = yaw;
                float hop = Mathf.Sin(Mathf.Clamp01(gs.hopClock / gs.hopPeriod) * Mathf.PI);
                float side = (gs.steps % 2 == 0) ? 1f : -1f;
                float bump = gs.hitFlash > 0f ? gs.hitFlash / 0.4f : 0f;
                var list = d.guards[g];
                var rings = d.guardRings[g];
                for (int k = 0; k < list.Count; k++)
                {
                    bool show = !gs.gone;
                    if (list[k].gameObject.activeSelf != show) { list[k].gameObject.SetActive(show); rings[k].gameObject.SetActive(show); }
                    if (!show) continue;
                    rings[k].position = wp + new Vector3(d.copyOffset[k], 0.03f, 0);
                    rings[k].rotation = Quaternion.Euler(0, Mathf.Floor(t * 6f) * 23f + g * 40f, 0);
                    // a toy-soldier waddle: hop, rock from foot to foot, lean into the walk
                    list[k].position = wp + new Vector3(d.copyOffset[k], hop * 0.15f, 0);
                    list[k].rotation = Quaternion.Euler(4f + hop * 3f, yaw, side * hop * 6f);
                    float sy = GuardScale * (1f + hop * 0.05f + bump * 0.18f), sxz = GuardScale * (1f - hop * 0.03f + bump * 0.10f);
                    list[k].localScale = new Vector3(sxz, sy, sxz);
                }
            }
        }
    }

    void RegisterVis(Transform vis, int ri)
    {
        foreach (Transform t in vis)
        {
            string n = t.name;
            if (n.Length < 4 || n[1] != '_') continue;
            var parts = n.Split('_');
            int x, r;
            if (parts.Length < 3 || !int.TryParse(parts[1], out x) || !int.TryParse(parts[2], out r)) continue;
            int key = Key(ri, x, r);
            char kind = n[0];
            if (kind == 'f' || kind == 's')
            {
                List<GameObject> l; if (!floorVis.TryGetValue(key, out l)) { l = new List<GameObject>(); floorVis[key] = l; }
                l.Add(t.gameObject);
            }
            else if (kind == 'b')
            {
                List<Renderer> l; if (!bridgeVis.TryGetValue(key, out l)) { l = new List<Renderer>(); bridgeVis[key] = l; }
                l.Add(t.GetComponent<Renderer>());
            }
            else if (kind == 'k')
            {
                List<Renderer> l; if (!fakeVis.TryGetValue(key, out l)) { l = new List<Renderer>(); fakeVis[key] = l; }
                l.AddRange(t.GetComponentsInChildren<Renderer>());
            }
        }
    }

    void AddBanners(Room room, Transform vis, int x, int r, Vector3 c, bool fake, string id)
    {
        // hang a banner on each face that looks onto walkable ground
        for (int d = 0; d < 4; d += 2)
        {
            int nr = r + (d == 0 ? -1 : 1);
            if (!room.In(x, nr) || room.Looks3DWall(x, nr)) continue;
            Vector3 dir = new Vector3(0, 0, d == 0 ? 1 : -1);
            var b = Props.Spawn("banner", vis, c + dir * (TS * 0.5f + 0.04f) + new Vector3(0, WH - 0.35f, 0), d == 0 ? 0f : 180f, 1f, fake);
            b.name = (fake ? "k" : "w") + id + "_banner" + d;
        }
    }

    void MaybeTorch(Room room, Transform deco, int x, int r, Vector3 c)
    {
        if (Hash(room.index * 13 + 1, x, r) > 0.20f) return;
        for (int d = 0; d < 4; d++)
        {
            int nx = x + (d == 1 ? 1 : d == 3 ? -1 : 0), nr = r + (d == 0 ? -1 : d == 2 ? 1 : 0);
            if (!room.In(nx, nr) || !room.Looks3DFloor(nx, nr)) continue;
            Vector3 dir = new Vector3(d == 1 ? 1 : d == 3 ? -1 : 0, 0, d == 0 ? 1 : d == 2 ? -1 : 0);
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Props.Spawn("torch", deco, c + dir * (TS * 0.5f + 0.02f) + new Vector3(0, 1.75f, 0), yaw, 1.3f);
            return;
        }
    }

    void BuildNote(Room room, Transform deco, Vector3 c)
    {
        // a signpost just beside the walking line, turned to face someone coming from the door
        // lean it toward the walking line and push it to the far edge of its tile so it is in view from the door
        float side = (room.noteTile.x < room.start.x) ? 0.2f : -0.2f;
        Vector3 pos = c + new Vector3(side, 0, 0.75f);
        const float sc = 1.6f;
        Props.Spawn("signpost", deco, pos, 0f, sc);
        // the board's paper face is at (-0.015, 1.02, -0.101) on the model, 0.80 x 0.50, tilted 3 degrees
        Vector3 face = pos + new Vector3(-0.015f, 1.02f, -0.101f) * sc;
        Quad(deco, "sticky", TexLib.QuadXY, face + new Vector3(0, 0, -0.012f), Quaternion.Euler(0, 0, 3f), new Vector3(0.76f * sc, 0.47f * sc, 1f), TexLib.Mat(Pal.Sticky, 2, 0f));
        WorldLabel(deco, room.def.note, face + new Vector3(0, 0, -0.024f), Quaternion.Euler(0, 0, 3f), 0.74f * sc, 0.45f * sc, 47, Pal.Ink, TextAnchor.MiddleCenter);
    }

    /// <summary>Handwritten text on a surface in the world. Faces -Z by default (readable looking along +Z).</summary>
    public static GameObject WorldLabel(Transform parent, string text, Vector3 pos, Quaternion rot, float width, float height, int fontSize, Color color, TextAnchor anchor, Font font = null)
    {
        var go = new GameObject("label");
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        const float k = 0.0025f;        // metres per canvas unit
        rt.sizeDelta = new Vector2(width / k, height / k);
        go.transform.position = pos; go.transform.rotation = rot; go.transform.localScale = Vector3.one * k;
        var tgo = new GameObject("t");
        tgo.transform.SetParent(go.transform, false);
        var t = tgo.AddComponent<UnityEngine.UI.Text>();
        t.font = font != null ? font : TexLib.hand;
        t.text = text; t.fontSize = fontSize; t.color = color; t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.lineSpacing = 0.86f; t.raycastTarget = false;
        t.material = new Material(TexLib.WorldTextShader);
        var trt = tgo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        return go;
    }

    // ------------------------------------------------------------------ corridors
    void BuildCorridor(Room room, bool final)
    {
        var parent = new GameObject("Corridor" + (room.index + 1)).transform;
        parent.SetParent(root, false);
        float L = Maps.CorridorTiles * TS;
        float rise = final ? 0f : Maps.CorridorRise;
        Vector3 start = room.TileCenter(room.exit.x, 0) + new Vector3(0, 0, TS * 0.5f);   // north edge of the exit tile
        bool fin = room.def.finish > 0.55f;
        Material floorMat = fin ? TexLib.Mat(Pal.FloorAlt, 0, 0f, TexLib.tileOutline, false, false, 0.5f) : TexLib.Mat(Color.white, 1, 0.5f);
        Material wallMat = fin ? TexLib.Mat(Pal.StoneDark, 0, 0f, TexLib.bricks) : TexLib.Mat(Color.white, 1, 0.5f);

        // ramp
        float ang = Mathf.Atan2(rise, L);
        float len = Mathf.Sqrt(L * L + rise * rise) + 0.12f;
        Vector3 n = new Vector3(0, Mathf.Cos(ang), -Mathf.Sin(ang));
        Vector3 topMid = start + new Vector3(0, rise * 0.5f, L * 0.5f);
        var ramp = Box(parent, "ramp", topMid - n * 0.3f, new Vector3(TS, 0.6f, len), floorMat, true);
        ramp.transform.rotation = Quaternion.Euler(-ang * Mathf.Rad2Deg, 0, 0);

        // walls either side (tall enough for both ends), real
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 c = start + new Vector3(s * TS, (WH + rise) * 0.5f, L * 0.5f);
            Box(parent, "cwall", c, new Vector3(TS, WH + rise, L), wallMat, true);
            // skirt below the corridor walls so they do not float
            Quad(parent, "cskirt", TexLib.FadeQuad, start + new Vector3(s * TS * 1.5f, 0, L * 0.5f), Quaternion.LookRotation(new Vector3(-s, 0, 0)), new Vector3(L, 2.8f, 1f), wallMat);
        }

        // beams overhead: walking under them gives the corridor depth
        for (int b = 1; b <= 2; b++)
        {
            float t = b / 3f;
            Box(parent, "cbeam", start + new Vector3(0, rise * t + 2.75f, L * t), new Vector3(TS * 1.15f, 0.3f, 0.36f), wallMat);
        }

        if (final)
        {
            // the way out of the dream: a giant monitor showing BUILD & RUN
            Vector3 end = start + new Vector3(0, 0, L);
            portalPos = end + new Vector3(0, 1.5f, 0);
            portalTriggerZ = end.z - 1.4f;
            Box(parent, "portal_wall", end + new Vector3(0, WH * 0.5f, 0.6f), new Vector3(TS * 3f, WH, 1.2f), TexLib.Mat(Pal.Ink, 0, 0f), true);
            Quad(parent, "portal_screen", TexLib.QuadXY, end + new Vector3(0, 1.45f, -0.02f), Quaternion.identity, new Vector3(1.8f, 2.5f, 1f), TexLib.Mat(Pal.Teal, 2, 0f));
            Quad(parent, "portal_glow", TexLib.QuadXY, end + new Vector3(0, 1.45f, -0.04f), Quaternion.identity, new Vector3(1.45f, 2.1f, 1f), TexLib.Mat(Pal.White, 2, 1f));
            WorldLabel(parent, "BUILD\n& RUN", end + new Vector3(0, 1.5f, -0.06f), Quaternion.identity, 1.4f, 1.6f, 150, Pal.Ink, TextAnchor.MiddleCenter, TexLib.title);
        }
    }

    // ------------------------------------------------------------------ far background
    void BuildSkyline()
    {
        // huge grey-box shapes the dev blocked out and never finished: they give the page depth and height
        var parent = new GameObject("Skyline").transform;
        parent.SetParent(root, false);
        var rng = new System.Random(4242);
        Vector3 mid = (rooms[0].origin + rooms[rooms.Length - 1].origin) * 0.5f + new Vector3(Maps.W * TS * 0.5f, 0, Maps.H * TS * 0.5f);
        Material grey = TexLib.Mat(Color.white, 1, 0.5f);
        for (int i = 0; i < 34; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = 46f + (float)rng.NextDouble() * 70f;
            float w = 8f + (float)rng.NextDouble() * 18f, d = 8f + (float)rng.NextDouble() * 18f, h = 16f + (float)rng.NextDouble() * 60f;
            Vector3 p = mid + new Vector3(Mathf.Cos(a) * dist * 1.25f, -22f + h * 0.5f + (float)rng.NextDouble() * 10f, Mathf.Sin(a) * dist * 1.6f);
            var b = Box(parent, "tower", p, new Vector3(w, h, d), grey);
            b.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 90f, 0);
            if (rng.NextDouble() < 0.45)
                Box(parent, "cap", p + new Vector3(0, h * 0.5f + 2f, 0), new Vector3(w * 0.6f, 4f, d * 0.6f), grey).transform.rotation = b.transform.rotation;
        }
        // loose floating blocks nearer by
        for (int i = 0; i < 26; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = 30f + (float)rng.NextDouble() * 45f;
            float s = 1.5f + (float)rng.NextDouble() * 4.5f;
            Vector3 p = mid + new Vector3(Mathf.Cos(a) * dist, 6f + (float)rng.NextDouble() * 26f, Mathf.Sin(a) * dist * 1.5f);
            var b = Box(parent, "floater", p, new Vector3(s, s, s), grey);
            b.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 40f, (float)rng.NextDouble() * 180f, (float)rng.NextDouble() * 40f);
        }
    }

    // ------------------------------------------------------------------ runtime changes
    /// <summary>The first time you stand on an un-modelled bridge tile it gets pencilled in.</summary>
    public List<Renderer> BridgeRenderers(int room, int x, int r)
    {
        List<Renderer> l; bridgeVis.TryGetValue(Key(room, x, r), out l); return l;
    }

    public void SetReveal(Renderer rd, float v)
    {
        if (rd == null) return;
        rd.enabled = v > 0.001f;
        rd.GetPropertyBlock(mpb);
        mpb.SetFloat("_Reveal", v);
        rd.SetPropertyBlock(mpb);
    }

    /// <summary>A fake wall you have walked through turns into a see-through ghost.</summary>
    public void MarkFakeFound(int room, int x, int r)
    {
        List<Renderer> l;
        if (!fakeVis.TryGetValue(Key(room, x, r), out l)) return;
        foreach (var rd in l)
        {
            if (rd == null) continue;
            var mats = rd.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                mats[i] = TexLib.Mat(m.color, (int)m.GetFloat("_Mode"), 0.5f, m.mainTexture == TexLib.white ? null : m.mainTexture, true, true, m.GetFloat("_HatchScale"));
            }
            rd.sharedMaterials = mats;
        }
    }

    /// <summary>Switch a one-use or flicker plank on or off. Collision only: nothing is drawn for it anyway.</summary>
    public void SetTileSolid(int room, int x, int r, bool solid)
    {
        Collider c;
        if (tileCols.TryGetValue(Key(room, x, r), out c) && c != null) c.enabled = solid;
    }

    /// <summary>The remake finally fires a volley: beams across whole rows and columns at chest height.</summary>
    public void FireLasers(Room room, LaserVolley v, Fx fx)
    {
        float y = room.FloorY + LaserY;
        float midX = (room.MinX + room.MaxX) * 0.5f, midZ = (room.MinZ + room.MaxZ) * 0.5f;
        float lenX = (Maps.W - 2) * TS;
        // column beams stop at the safe-zone line
        float zNear = room.TileCenter(0, Maps.LaserLobbyRow - 1).z - TS * 0.5f, zFar = room.MaxZ - TS;
        midZ = (zNear + zFar) * 0.5f;
        float lenZ = zFar - zNear;
        foreach (int r in v.rows) fx.Beam(new Vector3(midX, y, room.TileCenter(0, r).z), new Vector3(lenX, 0.22f, 0.22f), Maps.LaserHot + 0.12f);
        foreach (int x in v.cols) fx.Beam(new Vector3(room.TileCenter(x, 0).x, y, midZ), new Vector3(0.22f, 0.22f, lenZ), Maps.LaserHot + 0.12f);
    }

    public void SetRowDeleted(int room, int r, bool deleted)
    {
        List<Collider> cols;
        if (rowCols.TryGetValue(RowKey(room, r), out cols)) foreach (var c in cols) if (c != null) c.enabled = !deleted;
        for (int x = 0; x < Maps.W; x++)
        {
            List<GameObject> l;
            if (floorVis.TryGetValue(Key(room, x, r), out l)) foreach (var g in l) if (g != null) g.SetActive(!deleted);
            List<Renderer> br;
            if (deleted && bridgeVis.TryGetValue(Key(room, x, r), out br)) foreach (var rd in br) if (rd != null) rd.enabled = false;
        }
    }

    /// <summary>While a row is crumbling, show it as raw grey-box so you can see it going.</summary>
    public void SetRowCrumbling(int room, int r)
    {
        Material grey = TexLib.Mat(Pal.Magenta, 1, 0.5f);
        for (int x = 0; x < Maps.W; x++)
        {
            List<GameObject> l;
            if (!floorVis.TryGetValue(Key(room, x, r), out l)) continue;
            foreach (var g in l)
            {
                if (g == null || g.name[0] != 'f') continue;
                var rd = g.GetComponent<Renderer>();
                if (rd != null) { if (!original.ContainsKey(rd)) original[rd] = rd.sharedMaterial; rd.sharedMaterial = grey; }
            }
        }
    }
    readonly Dictionary<Renderer, Material> original = new Dictionary<Renderer, Material>();

    public void RestoreRoom(int room)
    {
        for (int r = 0; r < Maps.H; r++) SetRowDeleted(room, r, false);
        foreach (var kv in original) if (kv.Key != null) kv.Key.sharedMaterial = kv.Value;
        original.Clear();
    }
}
