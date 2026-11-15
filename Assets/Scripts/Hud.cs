using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whole screen is the unfinished 3D game. The 1989 original floats over it in its own little
/// window (it sways when you turn and bobs when you walk), and the dev's TODO checklist and
/// sticky-note instructions are pinned inside the 3D view. Everything is hand-drawn (TexLib / Doodle).
/// </summary>
public class Hud : MonoBehaviour
{
    GameManager gm;
    Canvas canvas;
    RectTransform root;
    public RenderTexture fullRT;
    RenderTexture pixelRT;
    public bool fullscreen = true;      // true = cinematic (title, intro, ending): no floating window, no play HUD

    RawImage fullView, pixelView;
    GameObject playUi, titleUi, pauseUi, resultsUi, pixelFloat;
    RectTransform floatRect, noteRect, checkRow, keyRect;
    RawImage keyIcon, keyCheck;
    Text keyText, keySub;
    Image keyBack;
    float keyPulse;
    Text timerText, roomText, hintText, popupText, toastText, captionText, noteHead, noteBody;
    Image popupBack, captionBack;
    RawImage crosshair, hurtBurst;
    Image lidTop, lidBottom, fade;
    RawImage[] todoBoxes, todoChecks; Text[] todoNums;
    Text pressText;
    // hearts in the 3D view: guards live in the 3D world, so your health shows here too
    RectTransform[] heartRects;
    RawImage[] heartFills, heartLines;
    readonly float[] heartPunch = new float[GameManager.MaxHearts];
    int shownHearts = -1;
    // start menu: two sticky notes, START and QUIT
    public int menuIndex;
    RectTransform[] menuNotes;
    RectTransform menuArrow;
    Image[] menuBacks;
    Text[] menuLabels, menuSubs;
    readonly float[] menuSel = { 1f, 0f }, menuTilt = { -3f, 2.6f };
    readonly Color[] menuCols = { Pal.Sticky, Pal.Hex("f7a8c4") };
    float menuPunch;
    float popupTimer, toastTimer, hintTimer, hurtTimer, coinPulse, captionAlpha, captionTarget;
    float fadeTarget, fadeSpeed = 3f;
    float floatIn = 1f, floatScale = 1f, floatScaleTarget = 1f, lastYaw, yawVel, swayX, swayTilt, stepKick, checkPulse;
    bool floatBig = true;
    int lastW, lastH, pulseIndex = -1;

    const float PixelW = 624f, PixelH = 576f, TitleH = 42f, Pad = 12f;

    // ------------------------------------------------------------------ helpers
    RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = offMin; rt.offsetMax = offMax;
        return rt;
    }

    RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    Image Img(RectTransform rt, Sprite sprite, Color color, bool sliced = false)
    {
        var im = rt.gameObject.AddComponent<Image>();
        im.sprite = sprite; im.color = color; im.raycastTarget = false;
        if (sliced) { im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = 1f; }
        return im;
    }

    RawImage Raw(RectTransform rt, Texture tex, Color color)
    {
        var im = rt.gameObject.AddComponent<RawImage>();
        im.texture = tex; im.color = color; im.raycastTarget = false;
        return im;
    }

    Text Txt(RectTransform rt, string s, int size, Color color, TextAnchor anchor, Font font = null)
    {
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font != null ? font : TexLib.hand;
        t.text = s; t.fontSize = size; t.color = color; t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false; t.supportRichText = true;
        return t;
    }

    void Halo(Text t, Color color, float dist = 2.5f)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = color; o.effectDistance = new Vector2(dist, -dist);
        var o2 = t.gameObject.AddComponent<Outline>();
        o2.effectColor = color; o2.effectDistance = new Vector2(-dist, dist);
    }

    // ------------------------------------------------------------------ build
    public void Build(GameManager gm)
    {
        this.gm = gm;
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        root = GetComponent<RectTransform>();

        // the 3D game fills the screen
        var fv = Rect("View", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fullView = Raw(fv, null, Color.white);

        // ---------- things pinned inside the 3D view while playing ----------
        var pu = Rect("PlayUi", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        playUi = pu.gameObject;
        var ch = Box("Crosshair", pu, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
        crosshair = Raw(ch, TexLib.dot, Pal.A(Pal.Ink, 0.65f));

        // room title
        var rt = Box("RoomTitle", pu, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -24), new Vector2(1000, 60));
        roomText = Txt(rt, "", 46, Pal.Ink, TextAnchor.MiddleLeft, TexLib.title);
        Halo(roomText, Pal.A(Pal.Paper, 0.92f), 3f);
        var ru = Box("RoomUnderline", pu, new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -80), new Vector2(380, 22));
        Raw(ru, TexLib.underline, Pal.Ink);

        // checkpoints: one box per room, ticked as you clear them
        int roomCount = Maps.Defs.Length;
        checkRow = Box("Checkpoints", pu, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -108), new Vector2(104 + roomCount * 44 + 10, 48));
        var crBack = Rect("Back", checkRow, Vector2.zero, Vector2.one, new Vector2(-10, -4), new Vector2(4, 4));
        Img(crBack, TexLib.panelSprite, Pal.A(Pal.Paper, 0.92f), true);
        int n = Maps.Defs.Length;
        todoBoxes = new RawImage[n]; todoChecks = new RawImage[n]; todoNums = new Text[n];
        var lbl = Box("Label", checkRow, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(120, 40));
        Txt(lbl, "rooms", 28, Pal.Ink, TextAnchor.MiddleLeft);
        for (int i = 0; i < n; i++)
        {
            float x = 112f + i * 44f;
            var bx = Box("Box" + i, checkRow, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(34, 34));
            todoBoxes[i] = Raw(bx, TexLib.box, Pal.Ink);
            var nm = Rect("Num", bx, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            todoNums[i] = Txt(nm, (i + 1).ToString(), 22, Pal.Ink, TextAnchor.MiddleCenter);
            var ck = Box("Check" + i, checkRow, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x + 3, 4), new Vector2(46, 46));
            todoChecks[i] = Raw(ck, TexLib.check, Pal.Red);
        }

        // hearts, next to the room checklist: guards live in the 3D world, so your hearts show here too
        heartRects = new RectTransform[GameManager.MaxHearts];
        heartFills = new RawImage[GameManager.MaxHearts]; heartLines = new RawImage[GameManager.MaxHearts];
        float hx0 = 40f + checkRow.sizeDelta.x + 44f;
        for (int i = 0; i < GameManager.MaxHearts; i++)
        {
            var hr = Box("Heart" + i, pu, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(hx0 + i * 52f, -132f), new Vector2(50, 50));
            hr.localRotation = Quaternion.Euler(0, 0, (i - 1) * -6f);
            var hf = Rect("Fill", hr, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            heartFills[i] = Raw(hf, TexLib.heartFill, Pal.Red);
            var hl = Rect("Line", hr, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            heartLines[i] = Raw(hl, TexLib.heartLine, Pal.Ink);
            heartRects[i] = hr;
        }

        // the dev's sticky note for this room: the instructions
        noteRect = Box("Note", pu, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -176), new Vector2(430, 196));
        Img(noteRect, TexLib.panelSprite, Pal.Sticky, true);
        noteRect.localRotation = Quaternion.Euler(0, 0, -1.6f);
        var tape = Box("Tape", noteRect, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(150, 40));
        Raw(tape, TexLib.tape, Pal.A(Pal.White, 0.8f)).transform.localRotation = Quaternion.Euler(0, 0, 3f);
        var nh = Box("Head", noteRect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -18), new Vector2(390, 44));
        noteHead = Txt(nh, "", 33, Pal.Ink, TextAnchor.MiddleLeft, TexLib.title);
        var nu = Box("Underline", noteRect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -60), new Vector2(250, 16));
        Raw(nu, TexLib.underline, Pal.Hex("b98a1e"));
        var nb = Rect("Body", noteRect, new Vector2(0, 0), new Vector2(1, 1), new Vector2(26, 14), new Vector2(-18, -76));
        noteBody = Txt(nb, "", 30, Pal.Hex("4a3d1e"), TextAnchor.UpperLeft);
        noteBody.lineSpacing = 0.9f;
        noteRect.gameObject.AddComponent<Boil>().degrees = 0.3f;

        // the key tag: keys are new in the remake, so this lives in the 3D view and never in the old game
        // (the outer rect takes the pulse when something happens; the inner one "boils" like the other paper bits)
        keyRect = Box("KeyTag", pu, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(48 + 215, -386 - 42), new Vector2(430, 84));
        var kin = Rect("Inner", keyRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        keyBack = Img(kin, TexLib.panelSprite, Pal.A(Pal.Paper, 0.95f), true);
        var ki = Box("Icon", kin, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(62, 0), new Vector2(92, 46));
        keyIcon = Raw(ki, TexLib.keyIcon, Pal.Ink);
        ki.localRotation = Quaternion.Euler(0, 0, -8f);
        var kc = Box("Check", kin, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-44, 4), new Vector2(58, 58));
        keyCheck = Raw(kc, TexLib.check, Pal.Hex("1f8f84"));
        var kt = Box("Text", kin, new Vector2(0, 1), new Vector2(0, 1), new Vector2(122, -8), new Vector2(300, 40));
        keyText = Txt(kt, "", 31, Pal.Ink, TextAnchor.MiddleLeft, TexLib.title);
        var ks = Box("Sub", kin, new Vector2(0, 0), new Vector2(0, 0), new Vector2(122, 9), new Vector2(300, 32));
        keySub = Txt(ks, "", 24, Pal.Hex("6a6280"), TextAnchor.MiddleLeft);
        kin.gameObject.AddComponent<Boil>().degrees = 0.3f;

        var tm = Box("Timer", pu, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(300, 54));
        timerText = Txt(tm, "0:00.0", 42, Pal.Ink, TextAnchor.MiddleCenter);
        Halo(timerText, Pal.A(Pal.Paper, 0.92f), 3f);
        var hn = Box("Hint", pu, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(1500, 48));
        hintText = Txt(hn, "WASD  move      mouse  look      Shift  run      Tab  resize the old game      Esc  pause", 32, Pal.Ink, TextAnchor.MiddleCenter);
        Halo(hintText, Pal.A(Pal.Paper, 0.92f), 3f);
        var pb = Box("PopupBack", pu, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 170), new Vector2(1040, 92));
        popupBack = Img(pb, TexLib.panelSprite, Pal.Paper, true);
        var pt = Rect("PopupText", pb, Vector2.zero, Vector2.one, new Vector2(24, 8), new Vector2(-24, -8));
        popupText = Txt(pt, "", 40, Pal.Ink, TextAnchor.MiddleCenter);
        pb.gameObject.AddComponent<Boil>().degrees = 0.4f;
        var to = Box("Toast", pu, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(1000, 50));
        toastText = Txt(to, "", 36, Pal.Hex("1f8f84"), TextAnchor.MiddleCenter, TexLib.title);
        Halo(toastText, Pal.A(Pal.Paper, 0.95f), 3f);
        var hb = Rect("Hurt", pu, Vector2.zero, Vector2.one, new Vector2(-80, -80), new Vector2(80, 80));
        hurtBurst = Raw(hb, TexLib.burst, Pal.A(Pal.Red, 0f));

        // ---------- the 1989 original, floating in its own window ----------
        float fw = PixelW + Pad * 2f, fh = PixelH + TitleH + Pad * 2f;
        floatRect = Box("PixelFloat", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(fw, fh));
        pixelFloat = floatRect.gameObject;
        var sh = Rect("Shadow", floatRect, Vector2.zero, Vector2.one, new Vector2(16, -22), new Vector2(16, -22));
        Img(sh, TexLib.whiteSprite, Pal.A(Pal.Ink, 0.28f));
        var sh2 = Rect("Shadow2", floatRect, Vector2.zero, Vector2.one, new Vector2(8, -11), new Vector2(8, -11));
        Img(sh2, TexLib.whiteSprite, Pal.A(Pal.Ink, 0.18f));
        var fb = Rect("Back", floatRect, Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
        Img(fb, TexLib.whiteSprite, Pal.K);
        var tb = Rect("TitleBack", floatRect, new Vector2(0, 1), new Vector2(1, 1), new Vector2(5, -(TitleH + Pad)), new Vector2(-5, -5));
        Img(tb, TexLib.whiteSprite, Pal.Paper);
        var pv = Box("Pixels", floatRect, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, Pad), new Vector2(PixelW, PixelH));
        pixelView = Raw(pv, gm.pixel.scr.tex, Color.white);
        var ff = Rect("Frame", floatRect, Vector2.zero, Vector2.one, new Vector2(-5, -5), new Vector2(5, 5));
        Img(ff, TexLib.frameSprite, Pal.Ink, true);
        var ft = Rect("Title", floatRect, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -(TitleH + Pad) + 2), new Vector2(-22, -8));
        Txt(ft, "tinykeep.exe   <color=#8f83c9>—  the 1989 original</color>", 28, Pal.Ink, TextAnchor.MiddleLeft);
        var ftr = Rect("TitleR", floatRect, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -(TitleH + Pad) + 2), new Vector2(-22, -8));
        Txt(ftr, "<color=#1f8f84>the truth</color>", 26, Pal.Ink, TextAnchor.MiddleRight);

        // ---------- full-screen overlays ----------
        var cb = Box("CaptionBack", root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 96), new Vector2(1300, 120));
        captionBack = Img(cb, TexLib.panelSprite, Pal.Paper, true);
        var ct = Rect("CaptionText", cb, Vector2.zero, Vector2.one, new Vector2(30, 10), new Vector2(-30, -10));
        captionText = Txt(ct, "", 46, Pal.Ink, TextAnchor.MiddleCenter);
        cb.gameObject.AddComponent<Boil>().degrees = 0.3f;

        BuildTitle();
        BuildPause();
        BuildResults();

        var lt = Rect("LidTop", root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, 0));
        lidTop = Img(lt, TexLib.whiteSprite, Pal.K); lt.pivot = new Vector2(0.5f, 1);
        var lb = Rect("LidBottom", root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 0));
        lidBottom = Img(lb, TexLib.whiteSprite, Pal.K); lb.pivot = new Vector2(0.5f, 0);
        var fd = Rect("Fade", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fade = Img(fd, TexLib.whiteSprite, Pal.A(Pal.Paper, 0f));

        SetCaption(null);
        popupBack.gameObject.SetActive(false);
        toastText.text = "";
        ShowPlayUi(false);
        ShowTitle(false);
        pauseUi.SetActive(false);
        resultsUi.SetActive(false);
        SetLayout(true);
    }

    void BuildTitle()
    {
        var t = Rect("TitleUi", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        titleUi = t.gameObject;
        var card = Box("Card", t, new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -60), new Vector2(880, 330));
        Img(card, TexLib.panelSprite, Pal.Paper, true);
        card.localRotation = Quaternion.Euler(0, 0, 1.2f);
        var big = Rect("Big", card, Vector2.zero, Vector2.one, new Vector2(40, 96), new Vector2(-30, -14));
        Txt(big, "REMASTER", 176, Pal.Ink, TextAnchor.MiddleLeft, TexLib.title);
        var ul = Box("Underline", card, new Vector2(0, 0), new Vector2(0, 0), new Vector2(44, 92), new Vector2(770, 34));
        Raw(ul, TexLib.underline, Pal.Purple);
        var sub = Rect("Sub", card, new Vector2(0, 0), new Vector2(1, 0), new Vector2(46, 22), new Vector2(-30, 92));
        Txt(sub, "a game about an <color=#ef5d5d>unfinished</color> game", 46, Pal.Ink, TextAnchor.MiddleLeft);
        card.gameObject.AddComponent<Boil>().degrees = 0.35f;

        // The start menu: two sticky notes stuck on the dev's window, next to the "TODO: sleep" note on the wall.
        // START keeps you working on the remake; QUIT finally does that TODO.
        string[] names = { "START", "QUIT" }, subs = { "keep working on the remake", "go to sleep at last" };
        Vector2[] pos = { new Vector2(-352, 292), new Vector2(-338, 70) }, size = { new Vector2(450, 204), new Vector2(380, 168) };
        menuNotes = new RectTransform[2]; menuBacks = new Image[2]; menuLabels = new Text[2]; menuSubs = new Text[2];
        for (int i = 0; i < 2; i++)
        {
            // the outer rect is moved and scaled by the selection; the inner one "boils" like the other paper bits
            var note = Box("Menu" + names[i], t, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), pos[i], size[i]);
            var inner = Rect("Inner", note, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            menuBacks[i] = Img(inner, TexLib.panelSprite, menuCols[i], true);
            var tape = Box("Tape", inner, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(140, 38));
            Raw(tape, TexLib.tape, Pal.A(Pal.White, 0.85f)).transform.localRotation = Quaternion.Euler(0, 0, i == 0 ? 4f : -5f);
            var lb = Rect("Label", inner, new Vector2(0, 0.33f), new Vector2(1, 1), new Vector2(10, 0), new Vector2(-10, -16));
            menuLabels[i] = Txt(lb, names[i], i == 0 ? 108 : 88, Pal.Ink, TextAnchor.MiddleCenter, TexLib.title);
            var sb = Rect("Sub", inner, new Vector2(0, 0), new Vector2(1, 0.36f), new Vector2(10, 12), new Vector2(-10, 0));
            menuSubs[i] = Txt(sb, subs[i], i == 0 ? 30 : 27, Pal.Hex("4a3d1e"), TextAnchor.MiddleCenter);
            inner.gameObject.AddComponent<Boil>().degrees = 0.35f;
            note.localRotation = Quaternion.Euler(0, 0, menuTilt[i]);
            menuNotes[i] = note;
        }
        menuArrow = Box("MenuArrow", t, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), pos[0] + new Vector2(-size[0].x * 0.5f - 74f, 0), new Vector2(104, 104));
        Raw(menuArrow, TexLib.arrow, Pal.Red);
        var mh = Box("MenuHint", t, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(1400, 48));
        var mht = Txt(mh, "W / S  or  arrows   choose          Enter   select          or click a note", 32, Pal.Paper, TextAnchor.MiddleCenter);
        Halo(mht, Pal.A(Pal.Ink, 0.85f), 2f);

        var jam = Box("Jam", t, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 30), new Vector2(1200, 40));
        var jt = Txt(jam, "SHIFT Game Jam 2026   ·   theme: Between Two Worlds", 28, Pal.Paper, TextAnchor.MiddleLeft);
        Halo(jt, Pal.A(Pal.Ink, 0.85f), 2f);
    }

    void BuildPause()
    {
        var p = Rect("PauseUi", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        pauseUi = p.gameObject;
        Img(p, TexLib.whiteSprite, Pal.A(Pal.Ink, 0.45f));
        var card = Box("Card", p, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 520));
        Img(card, TexLib.panelSprite, Pal.Paper, true);
        var h = Box("H", card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(740, 110));
        Txt(h, "coffee break", 92, Pal.Ink, TextAnchor.MiddleCenter, TexLib.title);
        var b = Box("B", card, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(700, 320));
        var bt = Txt(b, "<b>Esc</b>   back to work\n<b>R</b>   restart this room\n<b>Tab</b>   resize the old game\n<b>1 - 8</b>   jump to a room\n<b>Q</b>   quit to the title", 40, Pal.Ink, TextAnchor.MiddleCenter);
        bt.lineSpacing = 1.05f;
        card.gameObject.AddComponent<Boil>().degrees = 0.3f;
    }

    Text resBody, resRank, resFoot;
    RawImage resRing;
    void BuildResults()
    {
        var p = Rect("ResultsUi", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        resultsUi = p.gameObject;
        var card = Box("Card", p, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1120, 720));
        Img(card, TexLib.panelSprite, Pal.Paper, true);
        card.localRotation = Quaternion.Euler(0, 0, -0.8f);
        var h = Box("H", card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(1040, 130));
        Txt(h, "BUILD SUCCEEDED", 108, Pal.Ink, TextAnchor.MiddleCenter, TexLib.title);
        var ul = Box("Ul", card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(760, 30));
        Raw(ul, TexLib.underline, Pal.Teal);
        var b = Box("B", card, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(80, -30), new Vector2(640, 330));
        resBody = Txt(b, "", 46, Pal.Ink, TextAnchor.UpperLeft);
        resBody.lineSpacing = 1.1f;
        var ring = Box("Ring", card, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-70, -20), new Vector2(300, 300));
        resRing = Raw(ring, TexLib.ring, Pal.Red);
        ring.localRotation = Quaternion.Euler(0, 0, -12f);
        var rk = Rect("Rank", ring, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        resRank = Txt(rk, "A", 190, Pal.Red, TextAnchor.MiddleCenter, TexLib.title);
        var f = Box("F", card, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(1040, 110));
        resFoot = Txt(f, "", 30, Pal.Hex("6a6280"), TextAnchor.MiddleCenter);
        card.gameObject.AddComponent<Boil>().degrees = 0.25f;
    }

    // ------------------------------------------------------------------ layout + render target
    /// <summary>full = cinematic (title / intro / ending). Otherwise the old game floats in and the play HUD shows.</summary>
    public void SetLayout(bool full)
    {
        bool was = fullscreen;
        fullscreen = full;
        pixelFloat.SetActive(!full);
        if (!full && was) floatIn = 0f;
        EnsureTargets(true);
    }

    void EnsureTargets(bool force)
    {
        int sw = Mathf.Max(64, Screen.width), sh = Mathf.Max(64, Screen.height);
        if (!force && sw == lastW && sh == lastH && fullRT != null) return;
        lastW = sw; lastH = sh;
        if (fullRT == null || fullRT.width != sw || fullRT.height != sh)
        {
            if (fullRT != null) fullRT.Release();
            fullRT = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32) { name = "ViewRT", antiAliasing = 1, filterMode = FilterMode.Bilinear };
        }
        fullView.texture = fullRT;
        if (gm.player != null && gm.player.cam != null) gm.player.cam.targetTexture = fullRT;
        if (gm.intro != null) gm.intro.SetTarget(fullRT);
    }

    public RenderTexture CurrentTarget { get { if (fullRT == null) EnsureTargets(true); return fullRT; } }

    // ------------------------------------------------------------------ public controls
    public void ShowPlayUi(bool on) { playUi.SetActive(on); }
    public void ShowTitle(bool on)
    {
        titleUi.SetActive(on);
        if (on) { menuIndex = 0; menuPunch = 0f; }
    }

    /// <summary>Highlight a start-menu note (0 = START, 1 = QUIT).</summary>
    public void SetMenu(int i) { menuIndex = Mathf.Clamp(i, 0, 1); }
    /// <summary>The chosen note jumps when you pick it.</summary>
    public void PunchMenu() { menuPunch = 1f; }
    /// <summary>Which start-menu note is under this screen point (-1 for none).</summary>
    public int MenuAt(Vector2 screenPoint)
    {
        if (menuNotes == null || !titleUi.activeSelf) return -1;
        for (int i = 0; i < menuNotes.Length; i++)
            if (RectTransformUtility.RectangleContainsScreenPoint(menuNotes[i], screenPoint, null)) return i;
        return -1;
    }
    public void ShowPause(bool on) { pauseUi.SetActive(on); }

    public void ToggleFloatSize()
    {
        floatBig = !floatBig;
        floatScaleTarget = floatBig ? 1f : 0.667f;
    }

    public void RefreshTodo()
    {
        int cur = Mathf.Clamp(gm.cur, 0, Maps.Defs.Length - 1);
        for (int i = 0; i < todoBoxes.Length; i++)
        {
            bool done = gm.rs[i].cleared;
            bool now = i == cur && !done;
            if (done && !todoChecks[i].enabled) { pulseIndex = i; checkPulse = 1f; }
            todoChecks[i].enabled = done;
            todoBoxes[i].color = now ? (Color)Pal.Red : (Color)Pal.Ink;
            todoNums[i].color = done ? Pal.A(Pal.Ink, 0.35f) : (now ? (Color)Pal.Red : (Color)Pal.Ink);
            todoNums[i].fontStyle = now ? FontStyle.Bold : FontStyle.Normal;
        }
        noteHead.text = "note to self  #" + (cur + 1);
        noteBody.text = Maps.Defs[cur].note;
        RefreshKey();
    }

    /// <summary>The key tag under the note: locked until you find this room's key in the 3D world.</summary>
    public void RefreshKey()
    {
        if (keyRect == null) return;
        int cur = Mathf.Clamp(gm.cur, 0, Maps.Defs.Length - 1);
        bool has = gm.rooms[cur].HasKey;
        keyRect.gameObject.SetActive(has);
        if (!has) return;
        bool got = gm.rs[cur].keyTaken;
        keyCheck.enabled = got;
        keyIcon.color = got ? (Color)Pal.Hex("d19a00") : (Color)Pal.Ink;
        keyText.text = got ? "exit open" : "exit locked";
        keyText.color = got ? (Color)Pal.Hex("1f8f84") : (Color)Pal.Red;
        keySub.text = got ? "you have the key" : "find the key  (3D only!)";
    }

    public void KeyPulse() { keyPulse = 1f; }

    public void ShowRoomTitle(int i)
    {
        roomText.text = (i + 1) + "/" + Maps.Defs.Length + "   " + Maps.Defs[i].title;
        RefreshTodo();
        noteRect.localScale = new Vector3(1.18f, 0.8f, 1f);
        keyPulse = 0.6f;
    }

    public void ShowControlsHint() { hintTimer = 10f; }

    public void Popup(string text, float seconds)
    {
        popupText.text = text;
        popupTimer = seconds;
        popupBack.gameObject.SetActive(true);
        popupBack.rectTransform.localScale = new Vector3(1.25f, 0.75f, 1f);
    }

    public void Toast(string text) { toastText.text = text; toastTimer = 3.5f; }
    public void CoinPulse() { coinPulse = 1f; stepKick = 10f; }
    public void HurtFlash() { hurtTimer = 0.5f; stepKick = 18f; }
    public void StepKick(float amount) { stepKick = Mathf.Max(stepKick, amount); }

    public void SetCaption(string text)
    {
        if (string.IsNullOrEmpty(text)) { captionTarget = 0f; return; }
        captionText.text = text;
        captionTarget = 1f;
        captionBack.gameObject.SetActive(true);
    }

    /// <summary>0 = eyes open, 1 = closed.</summary>
    public void SetEyelids(float amount)
    {
        float h = Mathf.Clamp01(amount) * 548f;
        lidTop.rectTransform.sizeDelta = new Vector2(0, h);
        lidBottom.rectTransform.sizeDelta = new Vector2(0, h);
        lidTop.enabled = lidBottom.enabled = amount > 0.001f;
    }

    public void SetFade(float alpha, Color color, bool instant = false)
    {
        fadeTarget = alpha;
        Color c = color; c.a = instant ? alpha : fade.color.a;
        fade.color = c;
    }

    public void SetFadeSpeed(float s) { fadeSpeed = s; }

    public void ShowResults(float time, int coins, int total, int falls, int hits)
    {
        resultsUi.SetActive(true);
        string rank;
        if (coins >= total && time < 330f) rank = "S";
        else if (coins >= total - 5 && time < 540f) rank = "A";
        else if (time < 840f) rank = "B";
        else rank = "C";
        resRank.text = rank;
        Color rc = rank == "S" ? Pal.Hex("d19a00") : (rank == "A" ? (Color)Pal.Red : (Color)Pal.Purple);
        resRank.color = rc; resRing.color = rc;
        resBody.text =
            "time            <b>" + FormatTime(time) + "</b>\n" +
            "coins           <b>" + coins + " / " + total + "</b>\n" +
            "fell off        <b>" + falls + "x</b>\n" +
            "got hit         <b>" + hits + "x</b>\n" +
            "<size=32><color=#6a6280>soundtrack: fully remastered</color></size>";
        resFoot.text = "<b>Enter</b>  play again\n" +
                       "SHIFT Game Jam 2026   ·   everything made during the jam   ·   see CREDITS";
    }

    public static string FormatTime(float t)
    {
        int m = (int)(t / 60f);
        float s = t - m * 60f;
        return m + ":" + s.ToString("00.0");
    }

    // ------------------------------------------------------------------ frame
    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);     // a long first frame must not eat the timers
        EnsureTargets(false);

        if (gm.state == GameManager.State.Playing) timerText.text = FormatTime(gm.time);

        // the old game is upscaled 3x with hard pixels first, so the tilted, floating window stays crisp
        if (pixelFloat.activeSelf)
        {
            if (pixelRT == null)
            {
                pixelRT = new RenderTexture(PixelScreen.W * 3, PixelScreen.H * 3, 0, RenderTextureFormat.ARGB32) { name = "PixelRT", filterMode = FilterMode.Bilinear, antiAliasing = 1 };
                pixelView.texture = pixelRT;
            }
            Graphics.Blit(gm.pixel.scr.tex, pixelRT);
        }

        UpdateFloat(dt);

        // the note pops back to rest after a new room
        noteRect.localScale = Vector3.Lerp(noteRect.localScale, Vector3.one, 1f - Mathf.Exp(-12f * dt));
        if (keyRect != null)
        {
            keyPulse = Mathf.Max(0f, keyPulse - dt * 2.4f);
            float kp = keyPulse * keyPulse;
            keyRect.localScale = new Vector3(1f + kp * 0.22f, 1f + kp * 0.22f, 1f);
            keyRect.localRotation = Quaternion.Euler(0, 0, 1.3f + Mathf.Sin(Time.unscaledTime * 38f) * 5f * kp);
        }
        UpdateHearts(dt);
        if (checkPulse > 0f && pulseIndex >= 0)
        {
            checkPulse = Mathf.Max(0f, checkPulse - dt * 2.2f);
            todoChecks[pulseIndex].rectTransform.localScale = Vector3.one * (1f + checkPulse * 1.2f);
        }

        if (popupTimer > 0f)
        {
            popupTimer -= dt;
            popupBack.rectTransform.localScale = Vector3.Lerp(popupBack.rectTransform.localScale, Vector3.one, 1f - Mathf.Exp(-16f * dt));
            float a = Mathf.Clamp01(popupTimer / 0.35f);
            popupBack.color = Pal.A(Pal.Paper, 0.94f * a);
            popupText.color = Pal.A(Pal.Ink, a);
            if (popupTimer <= 0f) popupBack.gameObject.SetActive(false);
        }
        if (toastTimer > 0f)
        {
            toastTimer -= dt;
            toastText.color = Pal.A(Pal.Hex("1f8f84"), Mathf.Clamp01(toastTimer / 0.5f));
            if (toastTimer <= 0f) toastText.text = "";
        }
        if (hintTimer > 0f) hintTimer -= dt;
        hintText.color = Pal.A(Pal.Ink, Mathf.Clamp01(hintTimer / 1f));

        if (hurtTimer > 0f) hurtTimer -= dt;
        hurtBurst.color = Pal.A(Pal.Red, Mathf.Clamp01(hurtTimer / 0.5f) * 0.55f);

        captionAlpha = Mathf.MoveTowards(captionAlpha, captionTarget, dt * 4f);
        captionBack.color = Pal.A(Pal.Paper, 0.95f * captionAlpha);
        captionText.color = Pal.A(Pal.Ink, captionAlpha);
        if (captionAlpha <= 0.001f && captionBack.gameObject.activeSelf) captionBack.gameObject.SetActive(false);

        if (titleUi.activeSelf && pressText != null)
            pressText.color = Pal.A(Pal.Ink, 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3.2f));
        if (titleUi.activeSelf && menuNotes != null) UpdateMenu(dt);

        Color fc = fade.color;
        fc.a = Mathf.MoveTowards(fc.a, fadeTarget, dt * fadeSpeed);
        fade.color = fc;
        fade.enabled = fc.a > 0.002f;
    }

    /// <summary>Three hearts in the 3D view. A lost heart jumps and empties; refilled hearts pop back in.</summary>
    void UpdateHearts(float dt)
    {
        if (heartRects == null || !playUi.activeSelf) return;
        int now = Mathf.Clamp(gm.hearts, 0, GameManager.MaxHearts);
        if (now != shownHearts)
        {
            if (shownHearts >= 0)
            {
                if (now < shownHearts) for (int i = now; i < shownHearts; i++) heartPunch[i] = 1f;
                else for (int i = shownHearts; i < now; i++) heartPunch[i] = 0.6f;
            }
            shownHearts = now;
        }
        for (int i = 0; i < heartRects.Length; i++)
        {
            bool full = i < now;
            heartPunch[i] = Mathf.Max(0f, heartPunch[i] - dt * 2.6f);
            float k = heartPunch[i] * heartPunch[i];
            heartFills[i].enabled = full;
            heartLines[i].color = full ? (Color)Pal.Ink : Pal.A(Pal.Ink, 0.4f);
            float s = (full ? 1f : 0.86f) + k * 0.7f;
            heartRects[i].localScale = new Vector3(s, s, 1f);
            heartRects[i].localRotation = Quaternion.Euler(0, 0, (i - 1) * -6f + Mathf.Sin(Time.unscaledTime * 40f) * 14f * k);
        }
    }

    /// <summary>The picked note grows, rocks a little and gets the red arrow; the other one sits back and dims.</summary>
    void UpdateMenu(float dt)
    {
        float tt = Time.unscaledTime;
        menuPunch = Mathf.Max(0f, menuPunch - dt * 3.2f);
        for (int i = 0; i < menuNotes.Length; i++)
        {
            bool sel = i == menuIndex;
            menuSel[i] = Mathf.Lerp(menuSel[i], sel ? 1f : 0f, 1f - Mathf.Exp(-14f * dt));
            float s = Mathf.Lerp(0.9f, 1.07f, menuSel[i]) + (sel ? menuPunch * menuPunch * 0.22f : 0f);
            menuNotes[i].localScale = new Vector3(s, s, 1f);
            menuNotes[i].localRotation = Quaternion.Euler(0, 0, menuTilt[i] + (sel ? Mathf.Sin(tt * 3.4f) * 1.5f : 0f));
            float dim = Mathf.Lerp(0.7f, 1f, menuSel[i]);
            Color c = menuCols[i];
            menuBacks[i].color = new Color(c.r * dim, c.g * dim, c.b * dim, 1f);
            menuLabels[i].color = Pal.A(Pal.Ink, Mathf.Lerp(0.55f, 1f, menuSel[i]));
            menuSubs[i].color = Pal.A(Pal.Hex("4a3d1e"), Mathf.Lerp(0.5f, 1f, menuSel[i]));
        }
        var target = menuNotes[menuIndex];
        Vector2 want = target.anchoredPosition + new Vector2(-target.sizeDelta.x * 0.5f - 78f + Mathf.Sin(tt * 6f) * 9f, 0);
        menuArrow.anchoredPosition = Vector2.Lerp(menuArrow.anchoredPosition, want, 1f - Mathf.Exp(-18f * dt));
    }

    /// <summary>The old game's window hovers: it drifts, lags behind when you turn, and bumps when things happen.</summary>
    void UpdateFloat(float dt)
    {
        if (!pixelFloat.activeSelf) return;
        float t = Time.unscaledTime;
        if (floatIn < 1f) floatIn = Mathf.Min(1f, floatIn + dt / 0.55f);
        float pop = EaseOutBack(floatIn);

        float yaw = gm.player != null ? gm.player.yaw : 0f;
        float dy = Mathf.DeltaAngle(lastYaw, yaw);
        lastYaw = yaw;
        float v = dt > 0f ? dy / dt : 0f;
        yawVel = Mathf.Lerp(yawVel, v, 1f - Mathf.Exp(-10f * dt));
        swayX = Mathf.Lerp(swayX, Mathf.Clamp(-yawVel * 0.22f, -46f, 46f), 1f - Mathf.Exp(-6f * dt));
        swayTilt = Mathf.Lerp(swayTilt, Mathf.Clamp(-yawVel * 0.02f, -4f, 4f), 1f - Mathf.Exp(-6f * dt));
        stepKick = Mathf.Lerp(stepKick, 0f, 1f - Mathf.Exp(-9f * dt));
        floatScale = Mathf.Lerp(floatScale, floatScaleTarget, 1f - Mathf.Exp(-12f * dt));

        float bobY = Mathf.Sin(t * 1.25f) * 7f + Mathf.Sin(t * 2.1f + 1f) * 2.5f;
        float bobX = Mathf.Sin(t * 0.83f + 0.4f) * 5f;
        floatRect.anchoredPosition = new Vector2(-40f + bobX + swayX, -40f + bobY - stepKick + (1f - pop) * 40f);
        floatRect.localRotation = Quaternion.Euler(0, 0, -1.4f + Mathf.Sin(t * 0.7f) * 0.6f + swayTilt + (1f - pop) * 14f);
        float s = floatScale * Mathf.Max(0.001f, pop) * (1f + coinPulse * 0.035f);
        floatRect.localScale = new Vector3(s, s, 1f);
        coinPulse = Mathf.Max(0f, coinPulse - dt * 4f);
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
