using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen is the dev's desktop: the unfinished 3D build in a big window on the left,
/// the 1989 original in a small window on the right, and his TODO list under it.
/// Everything is hand-drawn (see TexLib / Doodle).
/// </summary>
public class Hud : MonoBehaviour
{
    GameManager gm;
    Canvas canvas;
    RectTransform root;
    public RenderTexture fullRT, viewRT;
    public bool fullscreen = true;

    // layout pieces
    RawImage fullView, windowView, pixelView;
    GameObject leftWindow, rightColumn, playUi, titleUi, pauseUi, resultsUi;
    RectTransform windowViewRect, rightRect, todoRect;
    Text timerText, roomText, hintText, popupText, toastText, captionText, coinText3d;
    Image popupBack, captionBack;
    RawImage crosshair, hurtBurst, roomUnderline;
    Image lidTop, lidBottom, fade;
    Text[] todoLines; RawImage[] todoBoxes, todoChecks;
    Text todoNote, todoTitle;
    Text pressText;
    float popupTimer, toastTimer, hintTimer, hurtTimer, coinPulse, roomTitleTimer, rightPop = 1f, captionAlpha, captionTarget;
    float fadeTarget, fadeSpeed = 3f;
    int lastW, lastH;

    const float RightW = 648f, Margin = 24f, PixelW = 624f, PixelH = 576f, PixelWinH = 636f;

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

        // desk paper behind everything
        var bg = Rect("Paper", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var bgRaw = Raw(bg, TexLib.paper, Pal.PaperDark);
        bgRaw.uvRect = new Rect(0, 0, 3.75f, 2.1f);

        // ---------- full-screen view (title, intro, ending) ----------
        var fv = Rect("FullView", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fullView = Raw(fv, null, Color.white);

        // ---------- left window: the unfinished 3D build ----------
        var lw = Rect("LeftWindow", root, Vector2.zero, Vector2.one, new Vector2(Margin, Margin), new Vector2(-(RightW + Margin * 2f), -Margin));
        leftWindow = lw.gameObject;
        var lwBack = Rect("Back", lw, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));
        Img(lwBack, TexLib.whiteSprite, Pal.Paper);
        windowViewRect = Rect("View", lw, Vector2.zero, Vector2.one, new Vector2(14, 14), new Vector2(-14, -46));
        windowView = Raw(windowViewRect, null, Color.white);
        var lwFrame = Rect("Frame", lw, Vector2.zero, Vector2.one, new Vector2(-4, -4), new Vector2(4, 4));
        Img(lwFrame, TexLib.frameSprite, Pal.Ink, true);
        var lwTitle = Rect("Title", lw, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -44), new Vector2(-22, -8));
        Txt(lwTitle, "TinyKeep3D   <color=#8f83c9>—  my remake  —</color>   <color=#ef5d5d>UNFINISHED</color>", 27, Pal.Ink, TextAnchor.MiddleLeft);
        var lwRight = Rect("TitleR", lw, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -44), new Vector2(-22, -8));
        Txt(lwRight, "<color=#8f83c9>what I see</color>", 24, Pal.Ink, TextAnchor.MiddleRight);

        // ---------- right column: the 1989 original + TODO pad ----------
        rightRect = Box("RightColumn", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Margin, -Margin), new Vector2(RightW, 1080 - Margin * 2f));
        rightColumn = rightRect.gameObject;
        var pw = Box("PixelWindow", rightRect, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(RightW, PixelWinH));
        var pwBack = Rect("Back", pw, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));
        Img(pwBack, TexLib.whiteSprite, Pal.K);
        var pv = Box("Pixels", pw, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -46), new Vector2(PixelW, PixelH));
        pixelView = Raw(pv, gm.pixel.scr.tex, Color.white);
        var pwTitleBack = Rect("TitleBack", pw, new Vector2(0, 1), new Vector2(1, 1), new Vector2(6, -46), new Vector2(-6, -6));
        Img(pwTitleBack, TexLib.whiteSprite, Pal.Paper);
        var pwFrame = Rect("Frame", pw, Vector2.zero, Vector2.one, new Vector2(-4, -4), new Vector2(4, 4));
        Img(pwFrame, TexLib.frameSprite, Pal.Ink, true);
        var pwTitle = Rect("Title", pw, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -44), new Vector2(-22, -8));
        Txt(pwTitle, "tinykeep.exe   <color=#8f83c9>—  the 1989 original</color>", 27, Pal.Ink, TextAnchor.MiddleLeft);
        var pwTitleR = Rect("TitleR", pw, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -44), new Vector2(-22, -8));
        Txt(pwTitleR, "<color=#2fb5a8>the truth</color>", 24, Pal.Ink, TextAnchor.MiddleRight);

        // TODO pad
        todoRect = Box("Todo", rightRect, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -(PixelWinH + 18f)), new Vector2(RightW - 8f, 1080 - Margin * 2f - PixelWinH - 18f));
        Img(todoRect, TexLib.panelSprite, Pal.Sticky, true);
        todoRect.localRotation = Quaternion.Euler(0, 0, -0.8f);
        var tape = Box("Tape", todoRect, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(170, 44));
        Raw(tape, TexLib.tape, Pal.A(Pal.White, 0.75f)).transform.localRotation = Quaternion.Euler(0, 0, 2.5f);
        var tt = Box("TodoTitle", todoRect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -22), new Vector2(360, 48));
        todoTitle = Txt(tt, "TODO.txt", 40, Pal.Ink, TextAnchor.MiddleLeft, TexLib.title);
        var ul = Box("Underline", todoRect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -64), new Vector2(190, 18));
        Raw(ul, TexLib.underline, Pal.Ink);
        int n = Maps.Defs.Length;
        todoLines = new Text[n]; todoBoxes = new RawImage[n]; todoChecks = new RawImage[n];
        for (int i = 0; i < n; i++)
        {
            float y = -84f - i * 33f;
            var bx = Box("Box" + i, todoRect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, y), new Vector2(30, 30));
            todoBoxes[i] = Raw(bx, TexLib.box, Pal.Ink);
            var ck = Box("Check" + i, todoRect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, y + 5), new Vector2(40, 40));
            todoChecks[i] = Raw(ck, TexLib.check, Pal.Red);
            var ln = Box("Line" + i, todoRect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(76, y + 3), new Vector2(520, 36));
            todoLines[i] = Txt(ln, (i + 1) + ". " + Maps.Defs[i].todo, 29, Pal.Ink, TextAnchor.MiddleLeft);
        }
        var nt = Rect("Note", todoRect, new Vector2(0, 0), new Vector2(1, 0), new Vector2(34, 16), new Vector2(-30, 122));
        todoNote = Txt(nt, "", 27, Pal.Hex("5a4a2a"), TextAnchor.UpperLeft);
        todoNote.lineSpacing = 0.9f;
        todoRect.gameObject.AddComponent<Boil>().degrees = 0.25f;

        // ---------- things drawn over the 3D view while playing ----------
        var pu = Rect("PlayUi", windowViewRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        playUi = pu.gameObject;
        var ch = Box("Crosshair", pu, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
        crosshair = Raw(ch, TexLib.dot, Pal.A(Pal.Ink, 0.65f));
        var rt = Box("RoomTitle", pu, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -14), new Vector2(900, 54));
        roomText = Txt(rt, "", 40, Pal.Ink, TextAnchor.MiddleLeft, TexLib.title);
        Halo(roomText, Pal.A(Pal.Paper, 0.9f));
        var ru = Box("RoomUnderline", pu, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -62), new Vector2(330, 20));
        roomUnderline = Raw(ru, TexLib.underline, Pal.Ink);
        var tm = Box("Timer", pu, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-26, -14), new Vector2(300, 54));
        timerText = Txt(tm, "0:00.0", 38, Pal.Ink, TextAnchor.MiddleRight);
        Halo(timerText, Pal.A(Pal.Paper, 0.9f));
        var hn = Box("Hint", pu, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(1100, 44));
        hintText = Txt(hn, "WASD  move      mouse  look      Shift  run      Esc  pause", 30, Pal.Ink, TextAnchor.MiddleCenter);
        Halo(hintText, Pal.A(Pal.Paper, 0.9f));
        var pb = Box("PopupBack", pu, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(980, 86));
        popupBack = Img(pb, TexLib.panelSprite, Pal.Paper, true);
        var pt = Rect("PopupText", pb, Vector2.zero, Vector2.one, new Vector2(24, 8), new Vector2(-24, -8));
        popupText = Txt(pt, "", 38, Pal.Ink, TextAnchor.MiddleCenter);
        pb.gameObject.AddComponent<Boil>().degrees = 0.4f;
        var to = Box("Toast", pu, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -78), new Vector2(900, 46));
        toastText = Txt(to, "", 32, Pal.Hex("1f8f84"), TextAnchor.MiddleCenter, TexLib.title);
        Halo(toastText, Pal.A(Pal.Paper, 0.95f));
        var hb = Rect("Hurt", pu, Vector2.zero, Vector2.one, new Vector2(-60, -60), new Vector2(60, 60));
        hurtBurst = Raw(hb, TexLib.burst, Pal.A(Pal.Red, 0f));

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

    Text titleBig, titleSub;
    void BuildTitle()
    {
        var t = Rect("TitleUi", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        titleUi = t.gameObject;
        var card = Box("Card", t, new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -60), new Vector2(880, 330));
        Img(card, TexLib.panelSprite, Pal.Paper, true);
        card.localRotation = Quaternion.Euler(0, 0, 1.2f);
        var big = Rect("Big", card, Vector2.zero, Vector2.one, new Vector2(40, 96), new Vector2(-30, -14));
        titleBig = Txt(big, "REMASTER", 176, Pal.Ink, TextAnchor.MiddleLeft, TexLib.title);
        var ul = Box("Underline", card, new Vector2(0, 0), new Vector2(0, 0), new Vector2(44, 92), new Vector2(770, 34));
        Raw(ul, TexLib.underline, Pal.Purple);
        var sub = Rect("Sub", card, new Vector2(0, 0), new Vector2(1, 0), new Vector2(46, 22), new Vector2(-30, 92));
        titleSub = Txt(sub, "a game about an <color=#ef5d5d>unfinished</color> game", 46, Pal.Ink, TextAnchor.MiddleLeft);
        card.gameObject.AddComponent<Boil>().degrees = 0.35f;

        var press = Box("Press", t, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(900, 80));
        var pressBack = Img(press, TexLib.panelSprite, Pal.Sticky, true);
        var pressT = Rect("T", press, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        pressText = Txt(pressT, "press any key to keep working...", 44, Pal.Ink, TextAnchor.MiddleCenter);
        press.gameObject.AddComponent<Boil>().degrees = 0.5f;

        var jam = Box("Jam", t, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 30), new Vector2(1200, 40));
        var jt = Txt(jam, "SHIFT Game Jam 2026   ·   theme: Between Two Worlds", 28, Pal.Paper, TextAnchor.MiddleLeft);
        Halo(jt, Pal.A(Pal.Ink, 0.85f), 2f);
    }

    void BuildPause()
    {
        var p = Rect("PauseUi", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        pauseUi = p.gameObject;
        Img(p, TexLib.whiteSprite, Pal.A(Pal.Ink, 0.45f));
        var card = Box("Card", p, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 470));
        Img(card, TexLib.panelSprite, Pal.Paper, true);
        var h = Box("H", card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(700, 110));
        Txt(h, "coffee break", 92, Pal.Ink, TextAnchor.MiddleCenter, TexLib.title);
        var b = Box("B", card, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 46), new Vector2(660, 260));
        var bt = Txt(b, "<b>Esc</b>   back to work\n<b>R</b>   restart this room\n<b>1 - 5</b>   jump to a room\n<b>Q</b>   quit to the title", 40, Pal.Ink, TextAnchor.MiddleCenter);
        bt.lineSpacing = 1.05f;
        card.gameObject.AddComponent<Boil>().degrees = 0.3f;
    }

    Text resTitle, resBody, resRank, resFoot;
    RawImage resRing;
    void BuildResults()
    {
        var p = Rect("ResultsUi", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        resultsUi = p.gameObject;
        var card = Box("Card", p, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1120, 720));
        Img(card, TexLib.panelSprite, Pal.Paper, true);
        card.localRotation = Quaternion.Euler(0, 0, -0.8f);
        var h = Box("H", card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(1040, 130));
        resTitle = Txt(h, "BUILD SUCCEEDED", 108, Pal.Ink, TextAnchor.MiddleCenter, TexLib.title);
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

    // ------------------------------------------------------------------ layout + render targets
    public void SetLayout(bool full)
    {
        fullscreen = full;
        fullView.gameObject.SetActive(full);
        leftWindow.SetActive(!full);
        rightColumn.SetActive(!full);
        if (!full) rightPop = 0f;
        // captions sit over the 3D view, not over the note pad
        var cbr = captionBack.rectTransform;
        cbr.anchoredPosition = new Vector2(full ? 0f : -(RightW + Margin) * 0.5f, full ? 96f : 150f);
        cbr.sizeDelta = new Vector2(full ? 1300f : 1100f, 120f);
        EnsureTargets(true);
    }

    void EnsureTargets(bool force)
    {
        int sw = Mathf.Max(64, Screen.width), sh = Mathf.Max(64, Screen.height);
        if (!force && sw == lastW && sh == lastH) return;
        lastW = sw; lastH = sh;
        float k = sh / 1080f;
        Canvas.ForceUpdateCanvases();
        int vw = Mathf.Max(64, Mathf.RoundToInt(windowViewRect.rect.width * k));
        int vh = Mathf.Max(64, Mathf.RoundToInt(windowViewRect.rect.height * k));
        if (vw < 100) { vw = Mathf.RoundToInt(1172 * k); vh = Mathf.RoundToInt(972 * k); }
        if (fullRT == null || fullRT.width != sw || fullRT.height != sh)
        {
            if (fullRT != null) fullRT.Release();
            fullRT = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32) { name = "FullRT", antiAliasing = 1, filterMode = FilterMode.Bilinear };
        }
        if (viewRT == null || viewRT.width != vw || viewRT.height != vh)
        {
            if (viewRT != null) viewRT.Release();
            viewRT = new RenderTexture(vw, vh, 24, RenderTextureFormat.ARGB32) { name = "ViewRT", antiAliasing = 1, filterMode = FilterMode.Bilinear };
        }
        fullView.texture = fullRT;
        windowView.texture = viewRT;
        var target = fullscreen ? fullRT : viewRT;
        if (gm.player != null && gm.player.cam != null) gm.player.cam.targetTexture = target;
        if (gm.intro != null) gm.intro.SetTarget(target);
    }

    public RenderTexture CurrentTarget => fullscreen ? fullRT : viewRT;

    // ------------------------------------------------------------------ public controls
    public void ShowPlayUi(bool on) { playUi.SetActive(on); }
    public void ShowTitle(bool on) { titleUi.SetActive(on); }
    public void ShowPause(bool on) { pauseUi.SetActive(on); }

    public void RefreshTodo()
    {
        for (int i = 0; i < todoLines.Length; i++)
        {
            bool done = gm.rs[i].cleared;
            bool now = i == gm.cur && !done;
            todoChecks[i].enabled = done;
            todoLines[i].color = done ? Pal.Hex("8a7a52") : (now ? Pal.Ink : Pal.Hex("5a4a2a"));
            todoLines[i].fontStyle = now ? FontStyle.Bold : FontStyle.Normal;
            todoLines[i].text = (i + 1) + ". " + Maps.Defs[i].todo + (now ? "   <color=#ef5d5d><--</color>" : "");
        }
        todoNote.text = Maps.Defs[Mathf.Clamp(gm.cur, 0, Maps.Defs.Length - 1)].note;
    }

    public void ShowRoomTitle(int i)
    {
        roomText.text = (i + 1) + "/" + Maps.Defs.Length + "   " + Maps.Defs[i].title;
        roomTitleTimer = 0f;
        RefreshTodo();
    }

    public void ShowControlsHint() { hintTimer = 9f; }

    public void Popup(string text, float seconds)
    {
        popupText.text = text;
        popupTimer = seconds;
        popupBack.gameObject.SetActive(true);
        popupBack.rectTransform.localScale = new Vector3(1.25f, 0.75f, 1f);
    }

    public void Toast(string text) { toastText.text = text; toastTimer = 3.5f; }
    public void CoinPulse() { coinPulse = 1f; }
    public void HurtFlash() { hurtTimer = 0.5f; }

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

    public void ShowResults(float time, int coins, int total, int falls, int hits)
    {
        resultsUi.SetActive(true);
        string rank = "B";
        if (coins >= total && time < 150f) rank = "S";
        else if (coins >= total - 3 && time < 240f) rank = "A";
        else if (time < 420f) rank = "B";
        else rank = "C";
        resRank.text = rank;
        Color rc = rank == "S" ? Pal.Hex("d19a00") : (rank == "A" ? (Color)Pal.Red : (Color)Pal.Purple);
        resRank.color = rc; resRing.color = rc;
        resBody.text =
            "time            <b>" + FormatTime(time) + "</b>\n" +
            "coins           <b>" + coins + " / " + total + "</b>\n" +
            "fell off        <b>" + falls + "x</b>\n" +
            "slime bites   <b>" + hits + "x</b>\n" +
            "<size=32><color=#6a6280>soundtrack: fully remastered</color></size>";
        resFoot.text = "<b>Enter</b>  play again        <b>1 - 5</b>  jump to a room\n" +
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

        if (gm.state == GameManager.State.Playing)
        {
            timerText.text = FormatTime(gm.time);
            roomTitleTimer += dt;
        }

        // window pop-in when the layout appears
        if (rightPop < 1f)
        {
            rightPop = Mathf.Min(1f, rightPop + dt / 0.45f);
            float e = EaseOutBack(rightPop);
            rightRect.anchoredPosition = new Vector2(-Margin + (1f - e) * (RightW + 80f), -Margin);
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

        Color fc = fade.color;
        fc.a = Mathf.MoveTowards(fc.a, fadeTarget, dt * fadeSpeed);
        fade.color = fc;
        fade.enabled = fc.a > 0.002f;
    }

    public void SetFadeSpeed(float s) { fadeSpeed = s; }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
