using System.Collections.Generic;
using UnityEngine;

/// <summary>Spawns the Blender-made props and paints them with the sketch shader, by material slot name.</summary>
public static class Props
{
    // extra yaw per prop so "front" means the same thing for all of them (set after checking the imports)
    static readonly Dictionary<string, float> yawFix = new Dictionary<string, float>();
    static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
    public static readonly List<string> missing = new List<string>();

    public static void SetYawFix(string name, float deg) { yawFix[name] = deg; }

    public static GameObject Spawn(string name, Transform parent, Vector3 worldPos, float yawDeg, float scale = 1f, bool dissolve = false, bool grey = false)
    {
        GameObject prefab;
        if (!prefabs.TryGetValue(name, out prefab))
        {
            prefab = Resources.Load<GameObject>("Models/" + name);
            prefabs[name] = prefab;
            if (prefab == null && !missing.Contains(name)) missing.Add(name);
        }
        var wrap = new GameObject("prop_" + name);
        wrap.transform.SetParent(parent, false);
        float fix; yawFix.TryGetValue(name, out fix);
        wrap.transform.position = worldPos;
        wrap.transform.rotation = Quaternion.Euler(0, yawDeg + fix, 0);
        wrap.transform.localScale = Vector3.one * scale;

        if (prefab != null)
        {
            var inst = Object.Instantiate(prefab, wrap.transform);
            inst.name = name;
            foreach (var col in inst.GetComponentsInChildren<Collider>()) Object.Destroy(col);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    string slot = src[i] != null ? src[i].name : "stone";
                    dst[i] = MatForSlot(slot, dissolve, grey);
                }
                r.sharedMaterials = dst;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }
        else
        {
            // stand-in block so the level still reads if a model is missing
            var cube = new GameObject(name + "_standin");
            cube.transform.SetParent(wrap.transform, false);
            Vector3 size = FallbackSize(name);
            cube.transform.localPosition = new Vector3(0, size.y * 0.5f, 0);
            cube.transform.localScale = size;
            cube.AddComponent<MeshFilter>().sharedMesh = TexLib.Cube;
            cube.AddComponent<MeshRenderer>().sharedMaterial = TexLib.Mat(Color.white, 1, 0.5f, null, dissolve);
        }
        return wrap;
    }

    public static Material MatForSlot(string slot, bool dissolve = false, bool grey = false)
    {
        slot = slot.ToLowerInvariant().Replace(" (instance)", "");
        if (grey) return TexLib.Mat(Color.white, 1, 0.5f, null, dissolve);
        if (slot.Contains("flame")) return TexLib.Mat(Pal.Hex("ffb347"), 2, 0f, null, dissolve);
        if (slot.Contains("screen")) return TexLib.Mat(Pal.Hex("20203a"), 2, 0f, null, dissolve);
        return TexLib.Mat(Pal.SlotColor(slot), 0, 0f, null, dissolve, false, 0.9f);
    }

    static Vector3 FallbackSize(string name)
    {
        switch (name)
        {
            case "torch": return new Vector3(0.2f, 0.6f, 0.2f);
            case "pillar": return new Vector3(0.7f, 3f, 0.7f);
            case "door_arch": return new Vector3(0.15f, 3f, 0.15f);
            case "gate": return new Vector3(1.9f, 3f, 0.12f);
            case "banner": return new Vector3(0.9f, 0.05f, 0.05f);
            case "signpost": return new Vector3(0.9f, 1.4f, 0.1f);
            case "desk": return new Vector3(1.6f, 0.74f, 0.75f);
            case "monitor": return new Vector3(0.62f, 0.45f, 0.06f);
            case "keyboard": return new Vector3(0.44f, 0.03f, 0.14f);
            case "mug": return new Vector3(0.09f, 0.1f, 0.09f);
            case "lamp": return new Vector3(0.15f, 0.5f, 0.15f);
            case "clock": return new Vector3(0.16f, 0.07f, 0.09f);
            default: return new Vector3(0.4f, 0.4f, 0.4f);
        }
    }

    public static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }
}
