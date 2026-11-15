using System.Collections.Generic;
using UnityEngine;

/// <summary>Builds every hand-drawn texture once at start-up and hands out shared materials and meshes.</summary>
public static class TexLib
{
    public static bool built;
    public static Texture2D paper, noise, hatch, grid, tileOutline, bricks, bricksFancy, planks;
    public static Texture2D footprint, puff, burst, scribble, check, box, dot, ring, arrow, underline, tape, squiggle, star, zed, keyIcon, keyDisc, heartFill, heartLine, bang, burstFill;
    public static Sprite frameSprite, panelSprite, whiteSprite;
    public static Texture2D frameTex, white;
    public static Font hand, title;

    public static void Build()
    {
        if (built) return;
        built = true;

        hand = Resources.Load<Font>("Fonts/PatrickHand");
        title = Resources.Load<Font>("Fonts/GochiHand");
        if (hand == null) hand = Font.CreateDynamicFontFromOSFont(new[] { "Ink Free", "Segoe Print", "Comic Sans MS", "Arial" }, 32);
        if (title == null) title = hand;

        white = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var wp = new Color32[16]; for (int i = 0; i < 16; i++) wp[i] = new Color32(255, 255, 255, 255);
        white.SetPixels32(wp); white.Apply();
        whiteSprite = Sprite.Create(white, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);

        paper = MakePaper(512, 11);
        noise = MakeNoise(256, 23);
        hatch = MakeHatch(512, 5);
        grid = MakeGrid(256, 7);
        tileOutline = MakeTileOutline(256, 31);
        bricks = MakeBricks(256, 41, false);
        bricksFancy = MakeBricks(256, 43, true);
        planks = MakePlanks(256, 47);

        footprint = MakeFootprint();
        puff = MakePuff();
        burst = MakeBurst();
        scribble = MakeScribble();
        check = MakeCheck();
        box = MakeBoxIcon();
        dot = MakeDot();
        ring = MakeRing();
        arrow = MakeArrow();
        underline = MakeUnderline();
        tape = MakeTape();
        squiggle = MakeSquiggle();
        star = MakeStar();
        zed = MakeZed();
        keyIcon = MakeKeyIcon();
        keyDisc = MakeKeyDisc();
        heartFill = MakeHeart(true);
        heartLine = MakeHeart(false);
        bang = MakeBang();
        burstFill = MakeBurstFill();

        frameTex = MakeFrame(96, 53);
        frameSprite = Sprite.Create(frameTex, new Rect(0, 0, 96, 96), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(30, 30, 30, 30));
        var panelTex = MakePanel(96, 59);
        panelSprite = Sprite.Create(panelTex, new Rect(0, 0, 96, 96), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(30, 30, 30, 30));

        Shader.SetGlobalTexture("_RM_Hatch", hatch);
        Shader.SetGlobalTexture("_RM_Grid", grid);
        Shader.SetGlobalTexture("_RM_Noise", noise);
        Shader.SetGlobalTexture("_RM_PaperTex", paper);
        Shader.SetGlobalColor("_RM_Paper", Pal.Paper);
        Shader.SetGlobalColor("_RM_Ink", Pal.Ink);
        Shader.SetGlobalColor("_RM_Pencil", Pal.Pencil);
        Shader.SetGlobalColor("_RM_GridFill", Pal.GridFill);
        Shader.SetGlobalColor("_RM_GridLine", Pal.GridLine);
        Shader.SetGlobalVector("_RM_LightDir", new Vector4(0.40f, 0.80f, -0.45f, 0));
        Shader.SetGlobalFloat("_RM_FogStart", 34f);
        Shader.SetGlobalFloat("_RM_FogEnd", 120f);
    }

    // ---------- tileable value noise ----------
    static float[,] Lattice(int cells, int seed)
    {
        var r = new System.Random(seed);
        var l = new float[cells, cells];
        for (int y = 0; y < cells; y++) for (int x = 0; x < cells; x++) l[x, y] = (float)r.NextDouble();
        return l;
    }
    static float Sample(float[,] l, int cells, float u, float v)
    {
        float fx = u * cells, fy = v * cells;
        int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
        float tx = fx - x0, ty = fy - y0;
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        int xa = ((x0 % cells) + cells) % cells, xb = (xa + 1) % cells;
        int ya = ((y0 % cells) + cells) % cells, yb = (ya + 1) % cells;
        return Mathf.Lerp(Mathf.Lerp(l[xa, ya], l[xb, ya], tx), Mathf.Lerp(l[xa, yb], l[xb, yb], tx), ty);
    }

    static Texture2D MakeNoise(int size, int seed)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var px = new Color32[size * size];
        float[][,] la = new float[6][,];
        int[] cells = { 6, 14, 6, 14, 5, 12 };
        for (int i = 0; i < 6; i++) la[i] = Lattice(cells[i], seed + i * 17);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float r = Sample(la[0], cells[0], u, v) * 0.68f + Sample(la[1], cells[1], u, v) * 0.32f;
                float g = Sample(la[2], cells[2], u, v) * 0.68f + Sample(la[3], cells[3], u, v) * 0.32f;
                float b = Sample(la[4], cells[4], u, v) * 0.6f + Sample(la[5], cells[5], u, v) * 0.4f;
                px[y * size + x] = new Color(r, g, b, 1);
            }
        tex.SetPixels32(px); tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Bilinear; tex.Apply(true);
        return tex;
    }

    static Texture2D MakePaper(int size, int seed)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var px = new Color32[size * size];
        var r = new System.Random(seed);
        var la = Lattice(9, seed + 1); var lb = Lattice(31, seed + 2); var lc = Lattice(64, seed + 3);
        // a few faint fibres
        var fib = new Doodle(size, size, seed + 5, true);
        for (int i = 0; i < 110; i++)
        {
            Vector2 p = new Vector2(fib.Rand(0, size), fib.Rand(0, size));
            float ang = fib.Rand(0, 6.28f); float len = fib.Rand(6, 26);
            fib.Line(p.x, p.y, p.x + Mathf.Cos(ang) * len, p.y + Mathf.Sin(ang) * len, 1.1f, 1.5f, 0.3f, fib.Rand(0.15f, 0.4f));
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float low = Sample(la, 9, u, v), mid = Sample(lb, 31, u, v), hi = Sample(lc, 64, u, v);
                float grain = (float)r.NextDouble();
                float val = 0.955f + (low - 0.5f) * 0.05f + (mid - 0.5f) * 0.035f + (grain - 0.5f) * 0.035f;
                val -= fib.a[y * size + x] * 0.06f;
                float tooth = Mathf.Clamp01(0.5f + (hi - 0.5f) * 1.3f + (grain - 0.5f) * 0.9f);
                // warm paper: slightly less blue
                px[y * size + x] = new Color(Mathf.Clamp01(val + 0.012f), Mathf.Clamp01(val + 0.004f), Mathf.Clamp01(val - 0.022f), tooth);
            }
        tex.SetPixels32(px); tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Bilinear; tex.Apply(true);
        return tex;
    }

    static Texture2D MakeHatch(int size, int seed)
    {
        var a = new Doodle(size, size, seed, true);
        var b = new Doodle(size, size, seed + 1, true);
        for (int i = 0; i < 300; i++)
        {
            Vector2 p = new Vector2(a.Rand(0, size), a.Rand(0, size));
            float ang = (38f + a.Rand(-7f, 7f)) * Mathf.Deg2Rad; float len = a.Rand(50, 150);
            a.Line(p.x, p.y, p.x + Mathf.Cos(ang) * len, p.y + Mathf.Sin(ang) * len, a.Rand(1.6f, 2.8f), 1.6f, 0.5f, a.Rand(0.55f, 1f));
        }
        for (int i = 0; i < 260; i++)
        {
            Vector2 p = new Vector2(b.Rand(0, size), b.Rand(0, size));
            float ang = (-52f + b.Rand(-7f, 7f)) * Mathf.Deg2Rad; float len = b.Rand(50, 140);
            b.Line(p.x, p.y, p.x + Mathf.Cos(ang) * len, p.y + Mathf.Sin(ang) * len, b.Rand(1.6f, 2.6f), 1.6f, 0.5f, b.Rand(0.55f, 1f));
        }
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var px = new Color32[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(Mathf.Clamp01(a.a[i]), Mathf.Clamp01(b.a[i]), 0, 1);
        tex.SetPixels32(px); tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Trilinear; tex.anisoLevel = 4; tex.Apply(true);
        return tex;
    }

    static Texture2D MakeGrid(int size, int seed)
    {
        var d = new Doodle(size, size, seed, true);
        float s = size;
        // major lines every half texture (= 1 m), minor lines every quarter
        d.Line(0, 0.5f, s, 0.5f, 3.2f, 1.2f, 0.3f, 1f); d.Line(0.5f, 0, 0.5f, s, 3.2f, 1.2f, 0.3f, 1f);
        d.Line(0, s * 0.5f, s, s * 0.5f, 3.0f, 1.2f, 0.3f, 1f); d.Line(s * 0.5f, 0, s * 0.5f, s, 3.0f, 1.2f, 0.3f, 1f);
        d.Line(0, s * 0.25f, s, s * 0.25f, 1.4f, 1.0f, 0.3f, 0.45f); d.Line(s * 0.25f, 0, s * 0.25f, s, 1.4f, 1.0f, 0.3f, 0.45f);
        d.Line(0, s * 0.75f, s, s * 0.75f, 1.4f, 1.0f, 0.3f, 0.45f); d.Line(s * 0.75f, 0, s * 0.75f, s, 1.4f, 1.0f, 0.3f, 0.45f);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var px = new Color32[size * size];
        for (int i = 0; i < px.Length; i++) { float v = 1f - Mathf.Clamp01(d.a[i]); px[i] = new Color(v, v, v, 1); }
        tex.SetPixels32(px); tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Trilinear; tex.anisoLevel = 4; tex.Apply(true);
        return tex;
    }

    static Texture2D MakeTileOutline(int size, int seed)
    {
        var d = new Doodle(size, size, seed);
        float m = 7f, s = size;
        d.Box(m, m, s - m, s - m, 3.4f, 2.0f, 5f, 0.85f);
        d.Box(m + 5, m + 4, s - m - 4, s - m - 5, 1.4f, 2.4f, 8f, 0.35f);
        // a couple of scratches so tiles do not look stamped
        for (int i = 0; i < 5; i++)
        {
            Vector2 p = new Vector2(d.Rand(40, s - 40), d.Rand(40, s - 40));
            float ang = d.Rand(0, 6.28f), len = d.Rand(10, 34);
            d.Line(p.x, p.y, p.x + Mathf.Cos(ang) * len, p.y + Mathf.Sin(ang) * len, 1.3f, 1.5f, 0.4f, 0.28f);
        }
        return d.ToOpaqueTexture(Color.white, new Color(0.42f, 0.38f, 0.5f), FilterMode.Trilinear, TextureWrapMode.Clamp);
    }

    static Texture2D MakeBricks(int size, int seed, bool fancy)
    {
        var d = new Doodle(size, size, seed, true);
        float s = size;
        int courses = 4;
        for (int c = 0; c < courses; c++)
        {
            float y = c * s / courses + 2;
            d.Line(0, y, s, y, 2.6f, 1.8f, 0.4f, 0.8f);
            int n = 2;
            for (int k = 0; k < n; k++)
            {
                float x = (k + ((c % 2 == 0) ? 0.25f : 0.75f)) * s / n + d.Rand(-6, 6);
                d.Line(x, y, x + d.Rand(-2, 2), y + s / courses, 2.2f, 1.5f, 0.4f, 0.7f);
            }
        }
        if (fancy)
        {
            // a carved trim band of little diamonds
            float y0 = s * 0.44f, y1 = s * 0.56f;
            d.Line(0, y0, s, y0, 3f, 1.2f, 0.3f, 0.95f); d.Line(0, y1, s, y1, 3f, 1.2f, 0.3f, 0.95f);
            for (int k = 0; k < 6; k++)
            {
                float cx = (k + 0.5f) * s / 6f, cy = (y0 + y1) * 0.5f, r = (y1 - y0) * 0.36f;
                d.Stroke(new[] { new Vector2(cx - r, cy), new Vector2(cx, cy - r), new Vector2(cx + r, cy), new Vector2(cx, cy + r), new Vector2(cx - r, cy) }, 1.8f, 0.6f, 0.3f, 0.9f);
            }
        }
        return d.ToOpaqueTexture(Color.white, new Color(0.40f, 0.36f, 0.52f), FilterMode.Trilinear, TextureWrapMode.Repeat);
    }

    static Texture2D MakePlanks(int size, int seed)
    {
        var d = new Doodle(size, size, seed, true);
        float s = size;
        for (int c = 0; c < 4; c++)
        {
            float x = c * s / 4 + 2;
            d.Line(x, 0, x, s, 2.6f, 1.6f, 0.4f, 0.85f);
            for (int k = 0; k < 3; k++)
            {
                float gx = x + d.Rand(10, s / 4 - 10);
                d.Line(gx, d.Rand(0, s), gx + d.Rand(-3, 3), d.Rand(0, s), 1.2f, 2.5f, 0.5f, 0.3f);
            }
        }
        return d.ToOpaqueTexture(Color.white, new Color(0.45f, 0.33f, 0.25f), FilterMode.Trilinear, TextureWrapMode.Repeat);
    }

    // ---------- marks ----------
    static Texture2D MakeFootprint()
    {
        var d = new Doodle(64, 128, 61);
        // sole and heel, drawn as two loose scribbled ovals
        var sole = new List<Vector2>(); var heel = new List<Vector2>();
        for (int i = 0; i <= 20; i++)
        {
            float a = i / 20f * Mathf.PI * 2f;
            sole.Add(new Vector2(32 + Mathf.Cos(a) * 15, 48 + Mathf.Sin(a) * 30));
            heel.Add(new Vector2(32 + Mathf.Cos(a) * 11, 104 + Mathf.Sin(a) * 13));
        }
        d.Stroke(sole, 2.6f, 1.5f, 0.4f, 0.9f); d.Stroke(heel, 2.6f, 1.5f, 0.4f, 0.9f);
        d.Scribble(20, 22, 44, 74, 6f, 1.6f, 20f, 0.55f);
        d.Scribble(23, 94, 41, 114, 6f, 1.6f, 20f, 0.55f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakePuff()
    {
        var d = new Doodle(128, 128, 67);
        d.Circle(new Vector2(52, 70), 26, 3f, 2f); d.Circle(new Vector2(80, 60), 22, 3f, 2f); d.Circle(new Vector2(70, 84), 18, 2.6f, 2f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeBurst()
    {
        var d = new Doodle(256, 256, 71);
        var pts = new List<Vector2>();
        int n = 14;
        for (int i = 0; i <= n * 2; i++)
        {
            float a = i / (float)(n * 2) * Mathf.PI * 2f;
            float r = (i % 2 == 0) ? 118f : 72f + d.Rand(-8, 8);
            pts.Add(new Vector2(128 + Mathf.Cos(a) * r, 128 + Mathf.Sin(a) * r));
        }
        d.Stroke(pts, 5f, 2f, 0.4f, 1f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeScribble()
    {
        var d = new Doodle(512, 512, 73);
        d.Scribble(-20, -20, 532, 532, 13f, 15f, 32f, 1f);
        d.Scribble(-20, -20, 532, 532, 17f, 13f, -41f, 1f);
        d.Scribble(-20, -20, 532, 532, 23f, 16f, 80f, 1f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeCheck()
    {
        var d = new Doodle(64, 64, 79);
        d.Stroke(new[] { new Vector2(10, 34), new Vector2(25, 50), new Vector2(56, 10) }, 6f, 1.5f, 0.5f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeBoxIcon()
    {
        var d = new Doodle(64, 64, 83);
        d.Box(12, 12, 52, 52, 4.2f, 1.6f, 4f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeDot()
    {
        var d = new Doodle(32, 32, 89);
        d.Disc(new Vector2(16, 16), 5.2f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeRing()
    {
        var d = new Doodle(256, 256, 97);
        d.Circle(new Vector2(128, 128), 108, 9f, 2.5f, 1.12f);
        d.Circle(new Vector2(128, 128), 92, 3.5f, 2.5f, 1.05f, 0.8f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeArrow()
    {
        var d = new Doodle(128, 128, 101);
        d.Line(14, 64, 110, 64, 8f, 2f);
        d.Stroke(new[] { new Vector2(78, 34), new Vector2(112, 64), new Vector2(78, 96) }, 8f, 2f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeUnderline()
    {
        var d = new Doodle(256, 32, 103);
        d.Line(6, 14, 250, 16, 5f, 2.5f, 0.5f);
        d.Line(16, 22, 232, 21, 2.4f, 2.5f, 0.5f, 0.7f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeTape()
    {
        var d = new Doodle(128, 48, 107);
        for (int y = 6; y < 42; y++) d.Seg(new Vector2(6 + d.Rand(0, 3), y), new Vector2(122 - d.Rand(0, 3), y), 1.6f, 0.9f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeSquiggle()
    {
        var d = new Doodle(256, 64, 109);
        var pts = new List<Vector2>();
        for (int i = 0; i <= 40; i++) pts.Add(new Vector2(8 + i * 6f, 32 + Mathf.Sin(i * 0.9f) * 14f));
        d.Stroke(pts, 5f, 1.5f, 0.4f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeStar()
    {
        var d = new Doodle(96, 96, 113);
        var pts = new List<Vector2>();
        for (int i = 0; i <= 10; i++)
        {
            float a = -Mathf.PI / 2f + i / 10f * Mathf.PI * 2f;
            float r = (i % 2 == 0) ? 40f : 17f;
            pts.Add(new Vector2(48 + Mathf.Cos(a) * r, 48 + Mathf.Sin(a) * r));
        }
        d.Stroke(pts, 5f, 1.2f, 0.4f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeKeyIcon()
    {
        // a key lying on its side: round bow on the left, two teeth pointing down on the right
        var d = new Doodle(128, 64, 131);
        d.Circle(new Vector2(26, 30), 15f, 6.5f, 1.4f, 1.1f);
        d.Line(41, 30, 114, 30, 6.5f, 1.4f);
        d.Line(92, 31, 92, 50, 6f, 1.0f);
        d.Line(110, 31, 110, 46, 6f, 1.0f);
        return d.ToAlphaTexture(Color.white);
    }

    /// <summary>A heart: the coloured-in inside, or just its wobbly pencil outline (two textures, two tints).</summary>
    static Texture2D MakeHeart(bool fill)
    {
        var d = new Doodle(64, 64, 139);
        var pts = new List<Vector2>();
        const int n = 48;
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n * Mathf.PI * 2f;
            float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
            float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
            pts.Add(new Vector2(32f + x * 1.62f, 29f - y * 1.62f));
        }
        if (!fill) { d.Stroke(pts, 4.4f, 1.0f, 0.35f); return d.ToAlphaTexture(Color.white); }
        // colour it in: one soft run per pixel row, wherever the row is inside the outline
        for (int py = 0; py < 64; py++)
        {
            float yy = py + 0.5f;
            var xs = new List<float>();
            for (int i = 0; i < n; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                if ((a.y <= yy) == (b.y <= yy)) continue;
                xs.Add(a.x + (yy - a.y) / (b.y - a.y) * (b.x - a.x));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                if (xs[k + 1] - xs[k] > 0.5f) d.Seg(new Vector2(xs[k], yy), new Vector2(xs[k + 1], yy), 1.8f, 0.95f);
        }
        return d.ToAlphaTexture(Color.white);
    }

    /// <summary>An exclamation mark: what a guard "says" when it notices you.</summary>
    static Texture2D MakeBang()
    {
        var d = new Doodle(64, 128, 149);
        d.Stroke(new[] { new Vector2(33, 12), new Vector2(31, 50), new Vector2(32, 84) }, 17f, 1.2f, 0.45f);
        d.Disc(new Vector2(32, 110), 9.5f);
        return d.ToAlphaTexture(Color.white);
    }

    /// <summary>The inside of the comic burst (same spikes as the outline in MakeBurst), coloured in.</summary>
    static Texture2D MakeBurstFill()
    {
        var d = new Doodle(256, 256, 71);
        var pts = new List<Vector2>();
        int n = 14;
        for (int i = 0; i <= n * 2; i++)
        {
            float a = i / (float)(n * 2) * Mathf.PI * 2f;
            float r = (i % 2 == 0) ? 118f : 72f + d.Rand(-8, 8);
            pts.Add(new Vector2(128 + Mathf.Cos(a) * r, 128 + Mathf.Sin(a) * r));
        }
        for (int py = 0; py < 256; py++)
        {
            float yy = py + 0.5f;
            var xs = new List<float>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                if ((a.y <= yy) == (b.y <= yy)) continue;
                xs.Add(a.x + (yy - a.y) / (b.y - a.y) * (b.x - a.x));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                if (xs[k + 1] - xs[k] > 0.5f) d.Seg(new Vector2(xs[k], yy), new Vector2(xs[k + 1], yy), 1.8f, 1f);
        }
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeKeyDisc()
    {
        // a coloured-in blob with a pencil line round it: the badge the key floats in front of
        var d = new Doodle(128, 128, 137);
        d.Disc(new Vector2(64, 64), 50f, 0.9f);
        d.Circle(new Vector2(64, 64), 54f, 5f, 2.2f, 1.12f);
        return d.ToAlphaTexture(Color.white);
    }

    static Texture2D MakeZed()
    {
        var d = new Doodle(96, 96, 127);
        d.Stroke(new[] { new Vector2(20, 22), new Vector2(76, 22), new Vector2(22, 74), new Vector2(78, 74) }, 8f, 1.6f, 0.4f);
        return d.ToAlphaTexture(Color.white);
    }

    /// <summary>Window frame: an inked rectangle drawn twice, transparent inside. 9-sliced by the UI.</summary>
    static Texture2D MakeFrame(int size, int seed)
    {
        var d = new Doodle(size, size, seed);
        float s = size;
        d.Box(7, 7, s - 7, s - 7, 4.6f, 1.6f, 3f, 1f);
        d.Box(12, 11, s - 11, s - 12, 1.6f, 2.0f, 5f, 0.55f);
        return d.ToAlphaTexture(Color.white);
    }

    /// <summary>Solid card with an inked edge (for note pads and dialogs): alpha is solid inside the frame.</summary>
    static Texture2D MakePanel(int size, int seed)
    {
        var d = new Doodle(size, size, seed);
        float s = size;
        d.Box(7, 7, s - 7, s - 7, 4.6f, 1.6f, 3f, 1f);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float line = Mathf.Clamp01(d.a[(size - 1 - y) * size + x]);
                bool inside = x > 8 && y > 8 && x < size - 9 && y < size - 9;
                // red = how much ink line; UI tints it. Fill is white, the line is darker via colour below.
                Color c = Color.Lerp(Color.white, new Color(0.17f, 0.145f, 0.25f), line);
                c.a = Mathf.Max(inside ? 1f : 0f, line);
                px[y * size + x] = c;
            }
        tex.SetPixels32(px); tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp; tex.Apply(true);
        return tex;
    }

    // ---------- materials ----------
    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
    static Shader sketch, decal, worldText;

    public static Shader SketchShader { get { if (sketch == null) sketch = Shader.Find("Remaster/Sketch"); return sketch; } }
    public static Shader DecalShader { get { if (decal == null) decal = Shader.Find("Remaster/Decal"); return decal; } }
    public static Shader WorldTextShader { get { if (worldText == null) worldText = Shader.Find("Remaster/WorldText"); return worldText; } }

    /// <summary>A unique id for a texture, used in material cache keys ("0" for none).
    /// Newer Unity 6 releases turned GetInstanceID into a compile error, so they use GetEntityId instead.</summary>
    public static string TexKey(Texture tex)
    {
        if (!tex) return "0";
#if UNITY_6000_4_OR_NEWER
        return tex.GetEntityId().ToString();
#else
        return tex.GetInstanceID().ToString();
#endif
    }

    /// <summary>mode 0 = marker colour, 1 = grey-box grid, 2 = flat unlit. flag 0 = ink lines, 0.5 = blue pencil, 1 = none.</summary>
    public static Material Mat(Color color, int mode = 0, float flag = 0f, Texture tex = null, bool dissolve = false, bool ghost = false, float hatchScale = 0.35f)
    {
        string key = ColorUtility.ToHtmlStringRGB(color) + "|" + mode + "|" + flag + "|" + TexKey(tex) + "|" + dissolve + "|" + ghost + "|" + hatchScale;
        Material m;
        if (cache.TryGetValue(key, out m) && m != null) return m;
        m = new Material(SketchShader);
        m.color = color;
        m.SetFloat("_Mode", mode);
        m.SetFloat("_Flag", flag);
        m.SetFloat("_Dissolve", dissolve ? 1 : 0);
        m.SetFloat("_Ghost", ghost ? 1 : 0);
        m.SetFloat("_Reveal", 1f);
        m.SetFloat("_HatchScale", hatchScale);
        if (tex != null) m.mainTexture = tex;
        cache[key] = m;
        return m;
    }

    public static Material UniqueMat(Color color, int mode = 0, float flag = 0f, Texture tex = null)
    {
        var m = new Material(SketchShader);
        m.color = color; m.SetFloat("_Mode", mode); m.SetFloat("_Flag", flag); m.SetFloat("_Reveal", 1f); m.SetFloat("_HatchScale", 0.35f);
        if (tex != null) m.mainTexture = tex;
        return m;
    }

    public static Material DecalMat(Texture tex, Color color)
    {
        var m = new Material(DecalShader);
        m.mainTexture = tex; m.color = color;
        return m;
    }

    // ---------- meshes ----------
    static Mesh cube, quadUp, fadeQuad, quadXY;
    public static Mesh Cube
    {
        get
        {
            if (cube == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = go.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(go);
            }
            return cube;
        }
    }

    /// <summary>1x1 quad lying flat, facing up, centred.</summary>
    public static Mesh QuadUp
    {
        get
        {
            if (quadUp == null)
            {
                quadUp = new Mesh { name = "QuadUp" };
                quadUp.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
                quadUp.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                quadUp.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                quadUp.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quadUp.RecalculateBounds();
            }
            return quadUp;
        }
    }

    /// <summary>1x1 upright quad facing -Z (toward a viewer looking along +Z), centred.</summary>
    public static Mesh QuadXY
    {
        get
        {
            if (quadXY == null)
            {
                quadXY = new Mesh { name = "QuadXY" };
                quadXY.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
                quadXY.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                quadXY.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                quadXY.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quadXY.RecalculateBounds();
            }
            return quadXY;
        }
    }

    /// <summary>Upright quad, top edge at y=0 going down to y=-1, facing -Z, that fades to the page (vertex alpha) at the bottom.</summary>
    public static Mesh FadeQuad
    {
        get
        {
            if (fadeQuad == null)
            {
                fadeQuad = new Mesh { name = "FadeQuad" };
                fadeQuad.vertices = new[] { new Vector3(-0.5f, -1, 0), new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, -1, 0) };
                fadeQuad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                fadeQuad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                fadeQuad.colors = new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) };
                fadeQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                fadeQuad.RecalculateBounds();
            }
            return fadeQuad;
        }
    }
}
