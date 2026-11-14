using System.Collections.Generic;
using UnityEngine;

public class PSprite
{
    public int w, h;
    public Color32[] px;    // row 0 is the top row
}

/// <summary>Loads the pixel sprites from Resources/pixelart.txt (plain text grids, 8 colours).</summary>
public static class PixelArt
{
    static readonly Dictionary<string, PSprite> sprites = new Dictionary<string, PSprite>();
    static readonly Dictionary<char, Color32> pal = new Dictionary<char, Color32>();
    static bool loaded;
    public static int loadedCount;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        pal['K'] = Pal.K; pal['D'] = Pal.D; pal['P'] = Pal.P; pal['T'] = Pal.T;
        pal['G'] = Pal.G; pal['Y'] = Pal.Y; pal['R'] = Pal.R; pal['W'] = Pal.W;
        pal['.'] = Pal.Clear;
        var ta = Resources.Load<TextAsset>("pixelart");
        if (ta != null) Parse(ta.text);
        loadedCount = sprites.Count;
        Derive();
    }

    static bool Same(Color32 a, Color32 b) { return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a; }

    static PSprite Clone(PSprite s)
    {
        var c = new PSprite { w = s.w, h = s.h, px = new Color32[s.px.Length] };
        System.Array.Copy(s.px, c.px, s.px.Length);
        return c;
    }

    static PSprite Recolored(PSprite s, Color32 fromA, Color32 toA, Color32 fromB, Color32 toB)
    {
        var c = Clone(s);
        for (int i = 0; i < c.px.Length; i++)
        {
            if (Same(c.px[i], fromA)) c.px[i] = toA;
            else if (Same(c.px[i], fromB)) c.px[i] = toB;
        }
        return c;
    }

    /// <summary>
    /// The newer plank types are drawn from the hand-made bridge sprites, so they stay in the same style:
    /// one-use planks are cracked, flicker planks are tinted per group and have a dotted "ghost" when gone.
    /// </summary>
    static void Derive()
    {
        if (!sprites.ContainsKey("bridge_v") || !sprites.ContainsKey("bridge_h") || !sprites.ContainsKey("pit")) return;
        int[] crack = { 4,1, 5,2, 5,3, 6,4, 7,5, 7,6, 8,7, 9,8, 9,9, 10,10, 11,11, 11,12, 12,13, 6,7, 5,8, 4,9, 10,6, 11,5 };
        foreach (string o in new[] { "v", "h" })
        {
            var b = sprites["bridge_" + o];
            var plank = Clone(b);
            for (int i = 0; i < crack.Length; i += 2)
            {
                int x = crack[i], y = crack[i + 1];
                if (x < plank.w && y < plank.h) plank.px[y * plank.w + x] = Pal.K;
            }
            sprites["plank_" + o] = plank;
            sprites["flick_a_" + o] = Recolored(b, Pal.Y, Pal.T, Pal.R, Pal.W);
            sprites["flick_b_" + o] = Recolored(b, Pal.Y, Pal.R, Pal.R, Pal.Y);
        }
        foreach (string g in new[] { "a", "b" })
        {
            Color32 col = g == "a" ? Pal.T : Pal.R;
            var ghost = Clone(sprites["pit"]);
            for (int y = 1; y < ghost.h - 1; y++)
                for (int x = 1; x < ghost.w - 1; x++)
                {
                    bool edge = x == 1 || y == 1 || x == ghost.w - 2 || y == ghost.h - 2;
                    if (edge && (x + y) % 2 == 0) ghost.px[y * ghost.w + x] = col;
                }
            ghost.px[7 * ghost.w + 7] = col; ghost.px[8 * ghost.w + 8] = col;
            sprites["ghost_" + g] = ghost;
        }
    }

    static void Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i].TrimEnd();
            if (line.StartsWith("@palette"))
            {
                i++;
                while (i < lines.Length && !lines[i].StartsWith("@"))
                {
                    var parts = lines[i].Trim().Split(' ');
                    if (parts.Length >= 2 && parts[0].Length == 1 && parts[1].Length == 6)
                    {
                        try { pal[parts[0][0]] = Pal.Hex(parts[1]); } catch { }
                    }
                    i++;
                }
                continue;
            }
            if (line.StartsWith("@sprite"))
            {
                var parts = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                int w, h;
                if (parts.Length >= 4 && int.TryParse(parts[2], out w) && int.TryParse(parts[3], out h))
                {
                    var s = new PSprite { w = w, h = h, px = new Color32[w * h] };
                    for (int r = 0; r < h; r++)
                    {
                        i++;
                        string row = i < lines.Length ? lines[i] : "";
                        for (int x = 0; x < w; x++)
                        {
                            char ch = x < row.Length ? row[x] : '.';
                            Color32 col;
                            if (!pal.TryGetValue(ch, out col)) col = Pal.Clear;
                            s.px[r * w + x] = col;
                        }
                    }
                    sprites[parts[1]] = s;
                }
            }
            i++;
        }
    }

    public static bool Has(string name) { return sprites.ContainsKey(name); }

    public static PSprite Get(string name)
    {
        PSprite s;
        if (sprites.TryGetValue(name, out s)) return s;
        s = Fallback(name);
        sprites[name] = s;
        return s;
    }

    /// <summary>Simple stand-in so the game still runs if a sprite is missing.</summary>
    static PSprite Fallback(string name)
    {
        int w = 16, h = 16;
        if (name.StartsWith("glyph_")) { w = 5; h = 7; }
        else if (name.StartsWith("heart")) { w = 9; h = 8; }
        else if (name == "coin_icon") { w = 8; h = 8; }
        else if (name == "logo_tinykeep") { w = 96; h = 24; }
        var s = new PSprite { w = w, h = h, px = new Color32[w * h] };
        Color32 fill = Pal.D, edge = Pal.K;
        bool opaque = true, ball = false;
        if (name.StartsWith("floor")) { fill = Pal.D; edge = Pal.D; }
        else if (name.StartsWith("wall")) { fill = Pal.P; edge = Pal.K; }
        else if (name.StartsWith("pit")) { fill = Pal.K; edge = Pal.K; }
        else if (name.StartsWith("bridge")) { fill = Pal.Y; edge = Pal.K; }
        else if (name.StartsWith("gate")) { fill = Pal.W; edge = Pal.K; }
        else if (name.StartsWith("door_exit")) { fill = Pal.T; edge = Pal.W; }
        else if (name.StartsWith("door")) { fill = Pal.K; edge = Pal.P; }
        else if (name.StartsWith("crumble")) { fill = Pal.D; edge = Pal.K; }
        else if (name.StartsWith("hero")) { fill = Pal.T; edge = Pal.K; opaque = false; ball = true; }
        else if (name.StartsWith("slime")) { fill = Pal.G; edge = Pal.K; opaque = false; ball = true; }
        else if (name.StartsWith("coin")) { fill = Pal.Y; edge = Pal.K; opaque = false; ball = true; }
        else if (name.StartsWith("heart_full")) { fill = Pal.R; edge = Pal.K; opaque = false; ball = true; }
        else if (name.StartsWith("heart")) { fill = Pal.K; edge = Pal.D; opaque = false; ball = true; }
        else if (name.StartsWith("note")) { fill = Pal.W; edge = Pal.K; opaque = false; ball = true; }
        else if (name.StartsWith("glyph")) { fill = Pal.W; edge = Pal.W; opaque = false; }
        else { fill = Pal.W; edge = Pal.K; opaque = false; ball = true; }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool border = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                Color32 c = border ? edge : fill;
                if (ball)
                {
                    float dx = (x + 0.5f - w * 0.5f) / (w * 0.5f), dy = (y + 0.5f - h * 0.5f) / (h * 0.5f);
                    float d = dx * dx + dy * dy;
                    if (d > 0.85f) c = Pal.Clear; else if (d > 0.55f) c = edge; else c = fill;
                }
                else if (!opaque && name.StartsWith("glyph"))
                {
                    c = border ? fill : Pal.Clear;
                }
                s.px[y * w + x] = c;
            }
        return s;
    }
}
