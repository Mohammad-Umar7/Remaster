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
    readonly Dictionary<int, List<Renderer>> bridgeVis = new Dictionary<int, List<Renderer>>();
    readonly Dictionary<int, List<Renderer>> fakeVis = new Dictionary<int, List<Renderer>>();
    MaterialPropertyBlock mpb;

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
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
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
        mr.receiveShadows = false;
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
                    if (fancy) m = TexLib.Mat(Pal.Stone, 0, 0f, TexLib.bricksFancy, fake);
                    else if (fin || fake) m = TexLib.Mat(Hash(ri, x * 3, r * 5) > 0.5f ? Pal.Stone : Pal.StoneDark, 0, 0f, TexLib.bricks, fake);
                    else m = TexLib.Mat(Color.white, 1, 0.5f);
                    Box(vis, (fake ? "k" : "w") + id, c + new Vector3(0, WH * 0.5f, 0), new Vector3(TS, WH, TS), m);
                    if (fancy) AddBanners(room, vis, x, r, c, fake, id);
                    else if (fin && !fake) MaybeTorch(room, deco, x, r, c);
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
                bool walk = x < Maps.W && room.TruthWalkable(x, r);
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

        RegisterVis(vis, ri);

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
            float dist = 62f + (float)rng.NextDouble() * 70f;
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
