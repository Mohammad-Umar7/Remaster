using System.Collections.Generic;
using UnityEngine;

/// <summary>Small pencil effects in the 3D world: footprints on things that are not there, puffs, bridge tiles being sketched in.</summary>
public class Fx : MonoBehaviour
{
    class Mark { public GameObject go; public Renderer rd; public Material mat; public float life, maxLife, grow; public Color color; public bool billboard; public Vector3 baseScale; }
    readonly List<Mark> marks = new List<Mark>();
    readonly List<Mark> spare = new List<Mark>();
    class Reveal { public Renderer rd; public float t; }
    readonly List<Reveal> reveals = new List<Reveal>();
    public World3D world;
    Transform cam;
    bool leftFoot;

    public void SetCamera(Transform c) { cam = c; }

    Mark Get(Texture tex)
    {
        Mark m;
        if (spare.Count > 0) { m = spare[spare.Count - 1]; spare.RemoveAt(spare.Count - 1); m.go.SetActive(true); }
        else
        {
            m = new Mark();
            m.go = new GameObject("mark");
            m.go.transform.SetParent(transform, false);
            m.go.AddComponent<MeshFilter>().sharedMesh = TexLib.QuadUp;
            m.rd = m.go.AddComponent<MeshRenderer>();
            m.mat = TexLib.DecalMat(tex, Color.white);
            m.rd.sharedMaterial = m.mat;
            m.rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        m.mat.mainTexture = tex;
        marks.Add(m);
        return m;
    }

    /// <summary>A pencil shoe print on the "ground" (which may be thin air).</summary>
    public void Footprint(Vector3 feet, float yaw, Color color, float life = 3.2f)
    {
        var m = Get(TexLib.footprint);
        leftFoot = !leftFoot;
        Quaternion rot = Quaternion.Euler(0, yaw, 0);
        m.go.transform.position = feet + rot * new Vector3(leftFoot ? -0.16f : 0.16f, 0.025f, 0.05f);
        m.go.transform.rotation = rot * Quaternion.Euler(0, leftFoot ? -8f : 8f, 0);
        m.baseScale = new Vector3(0.26f, 1f, 0.5f);
        m.go.transform.localScale = m.baseScale;
        m.life = m.maxLife = life; m.grow = 0f; m.color = color; m.billboard = false;
    }

    /// <summary>A little scribbled cloud that pops and fades.</summary>
    public void Puff(Vector3 pos, Color color, float size = 0.6f, float life = 0.55f)
    {
        var m = Get(TexLib.puff);
        m.go.transform.position = pos;
        m.baseScale = new Vector3(size, 1f, size);
        m.go.transform.localScale = m.baseScale;
        m.life = m.maxLife = life; m.grow = 1.2f; m.color = color; m.billboard = true;
    }

    public void Star(Vector3 pos, Color color, float size = 0.5f, float life = 0.6f)
    {
        var m = Get(TexLib.star);
        m.go.transform.position = pos;
        m.baseScale = new Vector3(size, 1f, size);
        m.go.transform.localScale = m.baseScale;
        m.life = m.maxLife = life; m.grow = 1.6f; m.color = color; m.billboard = true;
    }

    public void RevealBridge(List<Renderer> rds)
    {
        if (rds == null) return;
        foreach (var rd in rds) if (rd != null) reveals.Add(new Reveal { rd = rd, t = 0f });
    }

    public void ClearReveals() { reveals.Clear(); }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var m = marks[i];
            m.life -= dt;
            if (m.life <= 0f) { m.go.SetActive(false); marks.RemoveAt(i); spare.Add(m); continue; }
            float k = m.life / m.maxLife;
            Color c = m.color; c.a *= Mathf.Clamp01(k * 1.6f);
            m.mat.color = c;
            if (m.grow > 0f) m.go.transform.localScale = m.baseScale * (1f + (1f - k) * m.grow);
            if (m.billboard && cam != null)
            {
                // face the camera: the quad lies flat, so stand it up toward the viewer
                Vector3 toCam = cam.position - m.go.transform.position;
                if (toCam.sqrMagnitude > 0.001f) m.go.transform.rotation = Quaternion.FromToRotation(Vector3.up, toCam.normalized);
            }
        }
        for (int i = reveals.Count - 1; i >= 0; i--)
        {
            var r = reveals[i];
            r.t += dt / 0.45f;
            if (world != null) world.SetReveal(r.rd, Mathf.Clamp01(r.t));
            if (r.t >= 1f) reveals.RemoveAt(i);
        }
    }
}
