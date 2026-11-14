using UnityEngine;

/// <summary>Shared colours. The pixel game uses exactly eight; the 3D world uses marker-pen versions of the same family.</summary>
public static class Pal
{
    public static Color32 Hex(string h)
    {
        if (h.StartsWith("#")) h = h.Substring(1);
        byte r = System.Convert.ToByte(h.Substring(0, 2), 16);
        byte g = System.Convert.ToByte(h.Substring(2, 2), 16);
        byte b = System.Convert.ToByte(h.Substring(4, 2), 16);
        return new Color32(r, g, b, 255);
    }

    // --- pixel game (8 colours) ---
    public static readonly Color32 K = Hex("14121f");
    public static readonly Color32 D = Hex("3b2f5c");
    public static readonly Color32 P = Hex("7a4fd6");
    public static readonly Color32 T = Hex("2fb5a8");
    public static readonly Color32 G = Hex("7bd65a");
    public static readonly Color32 Y = Hex("ffd24a");
    public static readonly Color32 R = Hex("ef5d5d");
    public static readonly Color32 W = Hex("f4efe1");
    public static readonly Color32 Clear = new Color32(0, 0, 0, 0);

    // --- 3D sketch world ---
    public static readonly Color Paper = Hex("f3ead7");
    public static readonly Color PaperDark = Hex("e4d7bd");
    public static readonly Color Ink = Hex("2b2540");
    public static readonly Color Pencil = Hex("5b7fd1");
    public static readonly Color GridFill = Hex("e6edf6");
    public static readonly Color GridLine = Hex("9db4dc");
    public static readonly Color Stone = Hex("b9aee0");
    public static readonly Color StoneDark = Hex("8f83c9");
    public static readonly Color Floor = Hex("ead9b4");
    public static readonly Color FloorAlt = Hex("e2cfa6");
    public static readonly Color Wood = Hex("c98f5a");
    public static readonly Color WoodDark = Hex("8a5a3c");
    public static readonly Color Metal = Hex("8d93a8");
    public static readonly Color Teal = Hex("2fb5a8");
    public static readonly Color Yellow = Hex("ffd24a");
    public static readonly Color Red = Hex("ef5d5d");
    public static readonly Color Purple = Hex("7a4fd6");
    public static readonly Color Green = Hex("7bd65a");
    public static readonly Color Sticky = Hex("ffe27a");
    public static readonly Color Magenta = Hex("ff5bd0");
    public static readonly Color White = Hex("fbf7ee");
    public static readonly Color Night = Hex("3b2f5c");

    public static Color SlotColor(string slot)
    {
        slot = slot.ToLowerInvariant();
        if (slot.Contains("stone_dark")) return StoneDark;
        if (slot.Contains("stone")) return Stone;
        if (slot.Contains("wood_dark")) return WoodDark;
        if (slot.Contains("wood")) return Wood;
        if (slot.Contains("metal")) return Metal;
        if (slot.Contains("teal")) return Teal;
        if (slot.Contains("yellow")) return Yellow;
        if (slot.Contains("red")) return Red;
        if (slot.Contains("purple")) return Purple;
        if (slot.Contains("paper")) return Paper;
        if (slot.Contains("screen")) return Hex("20203a");
        if (slot.Contains("flame")) return Hex("ffb347");
        if (slot.Contains("cloth")) return Purple;
        if (slot.Contains("green")) return Green;
        if (slot.Contains("ink")) return Ink;
        if (slot.Contains("white")) return White;
        return Stone;
    }

    public static Color A(Color c, float a) { c.a = a; return c; }
}
