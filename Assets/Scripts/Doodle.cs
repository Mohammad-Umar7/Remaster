using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A tiny drawing kit that makes hand-drawn looking marks: wobbly, uneven pencil/ink strokes.
/// Every texture in the 3D world and the UI is drawn with this at start-up, so there are no image files.
/// </summary>
public class Doodle
{
    public readonly int w, h;
    public readonly float[] a;
    public bool wrap;
    System.Random rng;
    float seed;

    public Doodle(int w, int h, int seed = 1, bool wrap = false)
    {
        this.w = w; this.h = h; this.wrap = wrap;
        a = new float[w * h];
        rng = new System.Random(seed);
        this.seed = seed * 7.123f;
    }

    public float Rand() { return (float)rng.NextDouble(); }
    public float Rand(float lo, float hi) { return lo + (hi - lo) * (float)rng.NextDouble(); }

    void Put(int x, int y, float v)
    {
        if (wrap) { x = ((x % w) + w) % w; y = ((y % h) + h) % h; }
        else if (x < 0 || y < 0 || x >= w || y >= h) return;
        int i = y * w + x;
        if (v > a[i]) a[i] = v;
    }

    /// <summary>A straight soft-edged segment.</summary>
    public void Seg(Vector2 p0, Vector2 p1, float width, float strength = 1f)
    {
        float r = width * 0.5f + 1.5f;
        int x0 = Mathf.FloorToInt(Mathf.Min(p0.x, p1.x) - r), x1 = Mathf.CeilToInt(Mathf.Max(p0.x, p1.x) + r);
        int y0 = Mathf.FloorToInt(Mathf.Min(p0.y, p1.y) - r), y1 = Mathf.CeilToInt(Mathf.Max(p0.y, p1.y) + r);
        Vector2 d = p1 - p0;
        float len2 = Mathf.Max(d.sqrMagnitude, 0.0001f);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f - p0.x, py = y + 0.5f - p0.y;
                float t = Mathf.Clamp01((px * d.x + py * d.y) / len2);
                float qx = px - d.x * t, qy = py - d.y * t;
                float dist = Mathf.Sqrt(qx * qx + qy * qy);
                float v = Mathf.Clamp01(width * 0.5f - dist + 0.5f) * strength;
                if (v > 0f) Put(x, y, v);
            }
    }

    /// <summary>A hand-drawn stroke through the points: it wobbles sideways and the pressure changes along it.</summary>
    public void Stroke(IList<Vector2> pts, float width, float wobble = 1.5f, float pressure = 0.35f, float strength = 1f)
    {
        float sOff = Rand(0f, 100f);
        float travelled = 0f;
        Vector2 prev = Vector2.zero; bool has = false;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 A = pts[i], B = pts[i + 1];
            float len = Vector2.Distance(A, B);
            if (len < 0.01f) continue;
            Vector2 dir = (B - A) / len;
            Vector2 nrm = new Vector2(-dir.y, dir.x);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 4f));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                float dist = travelled + len * t;
                float off = (Mathf.PerlinNoise(dist * 0.035f + sOff, seed) - 0.5f) * 2f * wobble
                          + (Mathf.PerlinNoise(dist * 0.13f + sOff, seed + 3.3f) - 0.5f) * wobble * 0.5f;
                Vector2 p = Vector2.Lerp(A, B, t) + nrm * off;
                float wNow = width * (1f + pressure * (Mathf.PerlinNoise(dist * 0.05f + sOff, seed + 9.1f) - 0.5f) * 2f);
                if (has) Seg(prev, p, Mathf.Max(0.6f, wNow), strength);
                prev = p; has = true;
            }
            travelled += len;
        }
    }

    public void Line(float x0, float y0, float x1, float y1, float width, float wobble = 1.5f, float pressure = 0.35f, float strength = 1f)
    {
        Stroke(new[] { new Vector2(x0, y0), new Vector2(x1, y1) }, width, wobble, pressure, strength);
    }

    /// <summary>A rectangle drawn like a person would: four strokes that overshoot the corners a little.</summary>
    public void Box(float x0, float y0, float x1, float y1, float width, float wobble = 1.5f, float overshoot = 4f, float strength = 1f)
    {
        Line(x0 - Rand(0, overshoot), y0 + Rand(-1, 1), x1 + Rand(0, overshoot), y0 + Rand(-1, 1), width, wobble, 0.35f, strength);
        Line(x1 + Rand(-1, 1), y0 - Rand(0, overshoot), x1 + Rand(-1, 1), y1 + Rand(0, overshoot), width, wobble, 0.35f, strength);
        Line(x1 + Rand(0, overshoot), y1 + Rand(-1, 1), x0 - Rand(0, overshoot), y1 + Rand(-1, 1), width, wobble, 0.35f, strength);
        Line(x0 + Rand(-1, 1), y1 + Rand(0, overshoot), x0 + Rand(-1, 1), y0 - Rand(0, overshoot), width, wobble, 0.35f, strength);
    }

    public void Circle(Vector2 c, float radius, float width, float wobble = 1.5f, float turns = 1.08f, float strength = 1f)
    {
        var pts = new List<Vector2>();
        int n = Mathf.Max(12, Mathf.CeilToInt(radius * 0.9f));
        float a0 = Rand(0, 6.28f);
        for (int i = 0; i <= n * turns; i++)
        {
            float ang = a0 + i / (float)n * Mathf.PI * 2f;
            float rr = radius * (1f + 0.04f * Mathf.Sin(ang * 3f + a0));
            pts.Add(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr);
        }
        Stroke(pts, width, wobble, 0.3f, strength);
    }

    public void Disc(Vector2 c, float radius, float strength = 1f)
    {
        int x0 = Mathf.FloorToInt(c.x - radius - 1), x1 = Mathf.CeilToInt(c.x + radius + 1);
        int y0 = Mathf.FloorToInt(c.y - radius - 1), y1 = Mathf.CeilToInt(c.y + radius + 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                float v = Mathf.Clamp01(radius - d + 0.5f) * strength;
                if (v > 0) Put(x, y, v);
            }
    }

    /// <summary>Fill an area with back-and-forth scribble.</summary>
    public void Scribble(float x0, float y0, float x1, float y1, float spacing, float width, float angleDeg = 35f, float strength = 1f)
    {
        float ang = angleDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
        Vector2 nrm = new Vector2(-dir.y, dir.x);
        Vector2 c = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
        float half = Mathf.Max(x1 - x0, y1 - y0) * 0.75f;
        var pts = new List<Vector2>();
        bool flip = false;
        for (float o = -half; o <= half; o += spacing)
        {
            Vector2 p0 = c + nrm * (o + Rand(-spacing * 0.3f, spacing * 0.3f)) - dir * half * Rand(0.85f, 1.0f);
            Vector2 p1 = c + nrm * (o + Rand(-spacing * 0.3f, spacing * 0.3f)) + dir * half * Rand(0.85f, 1.0f);
            if (flip) { pts.Add(p1); pts.Add(p0); } else { pts.Add(p0); pts.Add(p1); }
            flip = !flip;
        }
        // clip to the box by clamping points
        for (int i = 0; i < pts.Count; i++)
            pts[i] = new Vector2(Mathf.Clamp(pts[i].x, x0, x1), Mathf.Clamp(pts[i].y, y0, y1));
        Stroke(pts, width, 1.2f, 0.4f, strength);
    }

    /// <summary>Alpha texture tinted with one colour (for marks on top of things).</summary>
    public Texture2D ToAlphaTexture(Color tint, FilterMode filter = FilterMode.Bilinear, TextureWrapMode wm = TextureWrapMode.Clamp)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        var px = new Color32[w * h];
        Color32 t = tint;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = a[(h - 1 - y) * w + x] * tint.a;
                px[y * w + x] = new Color32(t.r, t.g, t.b, (byte)(Mathf.Clamp01(v) * 255f));
            }
        tex.SetPixels32(px);
        tex.filterMode = filter; tex.wrapMode = wm;
        tex.Apply(true);
        return tex;
    }

    /// <summary>Opaque texture: paper colour where empty, ink colour where drawn (for multiply maps).</summary>
    public Texture2D ToOpaqueTexture(Color paper, Color ink, FilterMode filter = FilterMode.Bilinear, TextureWrapMode wm = TextureWrapMode.Repeat)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = Mathf.Clamp01(a[(h - 1 - y) * w + x]);
                px[y * w + x] = Color.Lerp(paper, ink, v);
            }
        tex.SetPixels32(px);
        tex.filterMode = filter; tex.wrapMode = wm;
        tex.Apply(true);
        return tex;
    }
}
