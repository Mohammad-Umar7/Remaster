using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws TINY KEEP (1989), the old top-down game that is still running in a window.
/// It is not a separate game: it is the same room data, drawn the way the old game sees it,
/// which is why it shows bridges, coins and slimes the 3D remake never got.
/// </summary>
public class PixelGame
{
    public readonly PixelScreen scr = new PixelScreen();
    public float bootTimer;         // > 0 while the boot logo shows
    float clock;
    int shake;

    public class Fxp { public string prefix; public int frames; public float t, dur; public Vector2 tile; public int room; }
    public readonly List<Fxp> fx = new List<Fxp>();

    const int T = 16;
    const int PF_Y = PixelScreen.HUD;   // playfield top

    public void AddFx(string prefix, int frames, float dur, Vector2 tile, int room)
    {
        fx.Add(new Fxp { prefix = prefix, frames = frames, dur = dur, tile = tile, room = room });
    }

    public void Shake(int frames) { shake = Mathf.Max(shake, frames); }

    public void Render(GameManager gm, float dt)
    {
        clock += dt;
        scr.ClearClip();
        scr.Clear(Pal.K);

        if (gm.state == GameManager.State.Title || gm.state == GameManager.State.Intro || gm.state == GameManager.State.Boot)
        {
            DrawAttract();
            scr.Apply();
            return;
        }
        if (bootTimer > 0f)
        {
            bootTimer -= dt;
            DrawBoot();
            scr.Apply();
            return;
        }

        int sx = 0, sy = 0;
        if (shake > 0) { shake--; sx = Random.Range(-2, 3); sy = Random.Range(-1, 2); }

        scr.SetClip(0, PF_Y, PixelScreen.W, PixelScreen.H);
        Room room = gm.rooms[gm.cur];
        Vector3 pp = gm.player.transform.position;

        if (gm.corridorFrom >= 0 && gm.corridorFrom < gm.rooms.Length - 1)
        {
            // walking between rooms: the old game scrolls to the next screen, the hero rides the seam
            float t = Mathf.Clamp01(gm.corridorT);
            int off = Mathf.RoundToInt(t * 176f);
            Room a = gm.rooms[gm.corridorFrom], b = gm.rooms[gm.corridorFrom + 1];
            DrawRoom(gm, a, sx, PF_Y + off + sy, false);
            DrawRoom(gm, b, sx + Mathf.RoundToInt((b.MinX - a.MinX) / Maps.TS * T), PF_Y + off - 176 + sy, false);
            float hx = (pp.x - a.MinX) / Maps.TS * T;
            DrawHero(gm, Mathf.RoundToInt(hx) + sx, PF_Y + off + sy);
        }
        else if (gm.cur == gm.rooms.Length - 1 && pp.z >= room.MaxZ)
        {
            // the last corridor: the old game has nothing up there, just the way out
            DrawRoom(gm, room, sx, PF_Y + sy, true);
            Vector2 tf = room.WorldToTileF(pp);
            DrawHero(gm, Mathf.RoundToInt(tf.x * T) + sx, PF_Y + 4 + sy);
        }
        else
        {
            DrawRoom(gm, room, sx, PF_Y + sy, true);
            Vector2 tf = room.WorldToTileF(pp);
            DrawHero(gm, Mathf.RoundToInt(tf.x * T) + sx, PF_Y + Mathf.RoundToInt(tf.y * T) + sy);
        }

        // effects on top
        for (int i = fx.Count - 1; i >= 0; i--)
        {
            var f = fx[i];
            f.t += dt;
            if (f.t >= f.dur) { fx.RemoveAt(i); continue; }
            if (f.room != gm.cur) continue;
            int frame = Mathf.Min(f.frames - 1, (int)(f.t / f.dur * f.frames)) + 1;
            var sp = PixelArt.Get(f.prefix + "_" + frame);
            scr.Blit(sp, Mathf.RoundToInt(f.tile.x * T) - sp.w / 2 + sx, PF_Y + Mathf.RoundToInt(f.tile.y * T) - sp.h / 2 + sy);
        }

        scr.ClearClip();
        DrawHud(gm);
        scr.Apply();
    }

    void DrawAttract()
    {
        // what the old game shows before you press start
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 13; x++)
                scr.Blit(PixelArt.Get(((x + y) % 2 == 0) ? "floor_a" : "floor_b"), x * T, y * T);
        for (int x = 0; x < 13; x++) { scr.Blit(PixelArt.Get("wall"), x * T, 0); scr.Blit(PixelArt.Get("wall_top"), x * T, 176); }
        scr.Fill(40, 52, 128, 62, Pal.K);
        scr.Fill(42, 54, 124, 58, Pal.D);
        var logo = PixelArt.Get("logo_tinykeep");
        scr.Blit(logo, (PixelScreen.W - logo.w) / 2, 60);
        if (((int)(clock * 2.2f)) % 2 == 0) scr.TextCentered("PRESS START", PixelScreen.W / 2, 94, Pal.W);
        scr.TextCentered("1989", PixelScreen.W / 2, 124, Pal.Y);
        int hx = 40 + (int)((clock * 22f) % 130f);
        scr.Blit(PixelArt.Get(((int)(clock * 6f) % 2 == 0) ? "hero_side_1" : "hero_side_2"), hx, 146);
        scr.Blit(PixelArt.Get(((int)(clock * 4f) % 2 == 0) ? "slime_1" : "slime_2"), hx - 30, 146);
    }

    void DrawBoot()
    {
        scr.Clear(Pal.K);
        var logo = PixelArt.Get("logo_tinykeep");
        scr.Blit(logo, (PixelScreen.W - logo.w) / 2, 70);
        scr.TextCentered("ORIGINAL 1989 BUILD", PixelScreen.W / 2, 106, Pal.T);
        scr.TextCentered("STILL RUNNING...", PixelScreen.W / 2, 120, Pal.W);
    }

    static bool Horizontal(Room room, int x, int r)
    {
        bool lr = (room.In(x - 1, r) && room.TruthWalkable(x - 1, r)) || (room.In(x + 1, r) && room.TruthWalkable(x + 1, r));
        bool ud = (room.In(x, r - 1) && room.TruthWalkable(x, r - 1)) || (room.In(x, r + 1) && room.TruthWalkable(x, r + 1));
        return lr && !ud;
    }

    static string PitSprite(Room room, RoomState rsx, int x, int r)
    {
        bool floorAbove = r > 0 && room.TruthWalkable(x, r - 1) && !room.IsBridge(x, r - 1) && rsx.rowCrumble[r - 1] < 2f;
        bool wallAbove = r > 0 && Room.IsRealWallCh(room.c[x, r - 1]);
        return (floorAbove || wallAbove) ? "pit_edge" : "pit";
    }

    /// <summary>
    /// The old game is three seconds ahead of the remake. When it fires a laser you see the flash here,
    /// then the line stays as moving red dashes that thicken and blink until the 3D world catches up.
    /// </summary>
    void DrawLasers(GameManager gm, Room room, int ox, int oy)
    {
        var vols = room.def.lasers;
        if (vols == null) return;
        var st = gm.rs[room.index];
        // column beams stop at the safe-zone line (the entry rows are a place to stand and watch)
        int x0 = ox + T, x1 = ox + T * (Maps.W - 1), y0 = oy + T, y1 = oy + T * Maps.LaserLobbyRow;
        bool anyCols = false;
        // emitters sit at both ends of every line
        for (int i = 0; i < vols.Length; i++)
        {
            foreach (int r in vols[i].rows) { Node(x0 - 6, oy + r * T + 5); Node(x1 + 1, oy + r * T + 5); }
            foreach (int x in vols[i].cols) { Node(ox + x * T + 5, y0 - 6); Node(ox + x * T + 5, y1 - 3); anyCols = true; }
        }
        if (anyCols)
            for (int px = x0; px < x1; px += 2) scr.Pixel(px, y1, Pal.T);
        int scroll = (int)(clock * 46f);
        for (int i = 0; i < vols.Length; i++)
        {
            float cd = gm.LaserCountdown(room, st, vols[i]);
            if (cd > Maps.LaserLead) continue;
            float since = Maps.LaserLead - cd;
            bool flash = since < 0.26f;                                     // the old game's own shot
            bool urgent = cd < 0.75f;
            if (!flash && urgent && ((int)(clock * 18f)) % 2 == 0) continue; // blink right before the remake fires
            int thick = flash ? 8 : (urgent ? 6 : (cd < 1.7f ? 4 : 2));
            Color32 col = flash ? Pal.W : (urgent ? Pal.Y : Pal.R);
            foreach (int r in vols[i].rows)
            {
                int y = oy + r * T + 8 - thick / 2;
                for (int px = x0; px < x1; px++)
                    if (flash || ((px + scroll) % 6) < 3) scr.Fill(px, y, 1, thick, col);
                if (flash) { scr.Fill(x0, y - 1, x1 - x0, 1, Pal.R); scr.Fill(x0, y + thick, x1 - x0, 1, Pal.R); }
            }
            foreach (int x in vols[i].cols)
            {
                int cx = ox + x * T + 8 - thick / 2;
                for (int py = y0; py < y1; py++)
                    if (flash || ((py + scroll) % 6) < 3) scr.Fill(cx, py, thick, 1, col);
                if (flash) { scr.Fill(cx - 1, y0, 1, y1 - y0, Pal.R); scr.Fill(cx + thick, y0, 1, y1 - y0, Pal.R); }
            }
        }
    }

    void Node(int x, int y)
    {
        scr.Fill(x, y, 6, 6, Pal.K);
        scr.Fill(x + 1, y + 1, 4, 4, Pal.R);
        scr.Pixel(x + 2, y + 2, Pal.W);
    }

    string TileSprite(GameManager gm, Room room, int x, int r)
    {
        char ch = room.c[x, r];
        var rsx = gm.rs[room.index];
        float cr = rsx.rowCrumble[r];
        bool floorLike = room.TruthWalkable(x, r);
        if (floorLike && cr >= 2f) return (r > 0 && room.TruthWalkable(x, r - 1) && rsx.rowCrumble[r - 1] < 2f && !room.IsBridge(x, r - 1)) ? "pit_edge" : "pit";
        if (floorLike && cr >= 0f && !room.IsBridge(x, r) && ch != 'X' && ch != 'S')
            return cr < 0.34f ? "crumble_1" : (cr < 0.67f ? "crumble_2" : "crumble_3");
        switch (ch)
        {
            case '#':
            case 'W':
                {
                    char below = r + 1 < Maps.H ? room.c[x, r + 1] : '#';
                    bool wallBelow = Room.IsRealWallCh(below) || below == 'S' || below == 'X';
                    return wallBelow ? "wall_top" : "wall";
                }
            case 'G': return "gate";
            case 'X': return "door_exit";
            case 'S': return "door_start";
            case '~':
                {
                    bool floorAbove = r > 0 && room.TruthWalkable(x, r - 1) && !room.IsBridge(x, r - 1) && rsx.rowCrumble[r - 1] < 2f;
                    bool wallAbove = r > 0 && Room.IsRealWallCh(room.c[x, r - 1]);
                    return (floorAbove || wallAbove) ? "pit_edge" : "pit";
                }
            case 'B':
            case 'c':
                return Horizontal(room, x, r) ? "bridge_h" : "bridge_v";
            case 'o':
            case 'k':
                {
                    // one-use plank: cracked, shaking when it is about to snap, a hole once it has
                    if (rsx.broken[x, r]) return PitSprite(room, rsx, x, r);
                    if (rsx.breakIn[x, r] > 0f) return "crumble_2";
                    return Horizontal(room, x, r) ? "plank_h" : "plank_v";
                }
            case 'a':
            case 'b':
                {
                    // flicker plank: solid in its group's turn, a dotted ghost otherwise, blinking just before it goes
                    float period = room.def.flickerPeriod;
                    bool solid = room.FlickerSolidAt(ch, rsx.clock);
                    float left = period - (rsx.clock % period);
                    bool blink = solid && left < 0.62f && ((int)(clock * 14f)) % 2 == 0;
                    if (solid && !blink) return "flick_" + ch + (Horizontal(room, x, r) ? "_h" : "_v");
                    return "ghost_" + ch;
                }
            default:
                return ((x * 7 + r * 13) % 5 < 3) ? "floor_a" : "floor_b";
        }
    }

    void DrawRoom(GameManager gm, Room room, int ox, int oy, bool entities)
    {
        var st = gm.rs[room.index];
        for (int r = 0; r < Maps.H; r++)
            for (int x = 0; x < Maps.W; x++)
                scr.Blit(PixelArt.Get(TileSprite(gm, room, x, r)), ox + x * T, oy + r * T);

        // notes, coins
        for (int r = 0; r < Maps.H; r++)
            for (int x = 0; x < Maps.W; x++)
            {
                char ch = room.c[x, r];
                if (st.rowCrumble[r] >= 2f) continue;
                if (ch == 'N') scr.Blit(PixelArt.Get("note"), ox + x * T, oy + r * T - 1);
                if (Room.IsCoinCh(ch) && !st.coinTaken[x, r] && !st.broken[x, r])
                {
                    int frame = ((int)(clock * 7f + x) % 4) + 1;
                    int bob = ((int)(clock * 3f + x * 0.5f) % 2 == 0) ? 0 : -1;
                    if (Room.IsBridgeCh(ch))
                    {
                        // a yellow coin on yellow planks disappears: give it a dark plate to sit on
                        int cx = ox + x * T, cy = oy + r * T;
                        scr.Fill(cx + 3, cy + 2, 10, 12, Pal.K);
                        scr.Fill(cx + 2, cy + 3, 12, 10, Pal.K);
                    }
                    scr.Blit(PixelArt.Get("coin_" + frame), ox + x * T, oy + r * T + bob);
                }
            }

        if (!entities) return;

        DrawLasers(gm, room, ox, oy);

        // screen-wrap rows get blinking arrows at the edges
        if (room.def.wrap && ((int)(clock * 3f)) % 2 == 0)
        {
            for (int r = 0; r < Maps.H; r++)
            {
                if (st.rowCrumble[r] >= 2f) continue;
                if (room.TruthWalkable(0, r)) scr.Blit(PixelArt.Get("arrow_left"), ox + 0, oy + r * T);
                if (room.TruthWalkable(Maps.W - 1, r)) scr.Blit(PixelArt.Get("arrow_right"), ox + (Maps.W - 1) * T, oy + r * T);
            }
        }

        // slimes
        foreach (var s in st.slimes)
        {
            string name = s.hitFlash > 0f ? "slime_hit" : (((int)(clock * 5f + s.d) % 2 == 0) ? "slime_1" : "slime_2");
            int hop = (s.hopClock < 0.16f) ? -2 : 0;
            scr.Blit(PixelArt.Get(name), ox + Mathf.RoundToInt(s.pos.x * T) - 8, oy + Mathf.RoundToInt(s.pos.y * T) - 10 + hop);
        }
    }

    void DrawHero(GameManager gm, int px, int py)
    {
        // blink while invulnerable
        if (gm.invuln > 0f && ((int)(clock * 14f)) % 2 == 0) return;
        float yaw = gm.player.yaw * Mathf.Deg2Rad;
        float fx = Mathf.Sin(yaw), fz = Mathf.Cos(yaw);
        int frame = ((int)(gm.player.distanceWalked / 0.7f) % 2) + 1;
        string name; bool flip = false;
        if (Mathf.Abs(fz) >= Mathf.Abs(fx)) name = fz > 0 ? "hero_up_" : "hero_down_";
        else { name = "hero_side_"; flip = fx < 0; }
        var sp = PixelArt.Get(name + frame);
        scr.Blit(sp, px - 8, py - 11, flip);
    }

    void DrawHud(GameManager gm)
    {
        scr.Fill(0, 0, PixelScreen.W, PixelScreen.HUD, Pal.K);
        for (int i = 0; i < GameManager.MaxHearts; i++)
            scr.Blit(PixelArt.Get(i < gm.hearts ? "heart_full" : "heart_empty"), 4 + i * 11, 4);
        scr.Blit(PixelArt.Get("coin_icon"), 46, 4);
        scr.Text("x" + gm.coins.ToString("00"), 56, 5, Pal.W);
        string rm = "ROOM " + (gm.cur + 1);
        scr.Text(rm, PixelScreen.W - PixelScreen.TextWidth(rm) - 4, 5, Pal.W);
        scr.TextCentered("TINY KEEP", 122, 5, Pal.Y);
    }
}
