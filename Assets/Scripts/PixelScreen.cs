using UnityEngine;

/// <summary>A 208x192 software framebuffer for the old pixel game (16 px HUD bar + 13x11 tiles of 16 px).</summary>
public class PixelScreen
{
    public const int W = 208, H = 192, HUD = 16;
    public readonly Color32[] buf = new Color32[W * H];
    readonly Color32[] flipped = new Color32[W * H];
    public readonly Texture2D tex;
    int clipY0 = 0, clipY1 = H, clipX0 = 0, clipX1 = W;

    public PixelScreen()
    {
        tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
    }

    public void SetClip(int x0, int y0, int x1, int y1) { clipX0 = x0; clipY0 = y0; clipX1 = x1; clipY1 = y1; }
    public void ClearClip() { clipX0 = 0; clipY0 = 0; clipX1 = W; clipY1 = H; }

    public void Clear(Color32 c) { for (int i = 0; i < buf.Length; i++) buf[i] = c; }

    public void Fill(int x, int y, int w, int h, Color32 c)
    {
        int x0 = Mathf.Max(x, clipX0), y0 = Mathf.Max(y, clipY0), x1 = Mathf.Min(x + w, clipX1), y1 = Mathf.Min(y + h, clipY1);
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
                buf[yy * W + xx] = c;
    }

    public void Pixel(int x, int y, Color32 c)
    {
        if (x < clipX0 || y < clipY0 || x >= clipX1 || y >= clipY1) return;
        buf[y * W + x] = c;
    }

    /// <summary>Draw a sprite. mode 0 = normal, 1 = solid white silhouette, 2 = tinted with <paramref name="tint"/>.</summary>
    public void Blit(PSprite s, int x, int y, bool flipX = false, int mode = 0, Color32 tint = default(Color32))
    {
        if (s == null) return;
        for (int sy = 0; sy < s.h; sy++)
        {
            int dy = y + sy;
            if (dy < clipY0 || dy >= clipY1) continue;
            for (int sx = 0; sx < s.w; sx++)
            {
                int dx = x + sx;
                if (dx < clipX0 || dx >= clipX1) continue;
                Color32 c = s.px[sy * s.w + (flipX ? (s.w - 1 - sx) : sx)];
                if (c.a == 0) continue;
                if (mode == 1) c = Pal.W;
                else if (mode == 2) c = tint;
                buf[dy * W + dx] = c;
            }
        }
    }

    static string GlyphName(char ch)
    {
        if (ch >= 'A' && ch <= 'Z') return "glyph_" + char.ToLowerInvariant(ch);
        if (ch >= '0' && ch <= '9') return "glyph_" + ch;
        switch (ch)
        {
            case '.': return "glyph_dot";
            case ',': return "glyph_comma";
            case '!': return "glyph_excl";
            case '?': return "glyph_quest";
            case ':': return "glyph_colon";
            case '-': return "glyph_dash";
            case '+': return "glyph_plus";
            case '/': return "glyph_slash";
            case '\'': return "glyph_apos";
            case 'x': return "glyph_x_small";
            case '%': return "glyph_percent";
        }
        if (ch >= 'a' && ch <= 'z') return "glyph_" + ch;
        return null;
    }

    public static int TextWidth(string s) { return s.Length * 6 - 1; }

    public void Text(string s, int x, int y, Color32 color)
    {
        int cx = x;
        for (int i = 0; i < s.Length; i++)
        {
            string g = GlyphName(s[i]);
            if (g != null) Blit(PixelArt.Get(g), cx, y, false, 2, color);
            cx += 6;
        }
    }

    public void TextCentered(string s, int centerX, int y, Color32 color) { Text(s, centerX - TextWidth(s) / 2, y, color); }

    public void TextShadow(string s, int x, int y, Color32 color, Color32 shadow)
    {
        Text(s, x + 1, y + 1, shadow);
        Text(s, x, y, color);
    }

    public void Apply()
    {
        for (int y = 0; y < H; y++)
            System.Array.Copy(buf, y * W, flipped, (H - 1 - y) * W, W);
        tex.SetPixels32(flipped);
        tex.Apply(false);
    }
}
