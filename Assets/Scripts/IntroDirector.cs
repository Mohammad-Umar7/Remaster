using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The dev's desk at 3:47 AM, seen through his own eyes: title screen, the short
/// "fell asleep and got pulled into the monitor" opening, and the morning-after ending.
/// </summary>
public class IntroDirector : MonoBehaviour
{
    GameManager gm;
    Transform set;
    Camera cam;
    Vector3 O = new Vector3(0, 400, -300);
    Vector3 eye, screenCenter;
    float baseFov = 54f;
    Coroutine running, captionRoutine;
    System.Action pendingDone;
    bool titleIdle;
    float keyClock, sway;
    Renderer wallRd;
    Text clockLabel, wipLabel;
    RawImage wipPanel;
    GameObject zeds;

    public void Build(GameManager gm)
    {
        this.gm = gm;
        set = new GameObject("DeskSet").transform;
        set.SetParent(transform, false);

        // the room: a dark wall behind the desk with a night window and a poster
        const float WZ = 0.62f;     // wall distance behind the desk centre
        var wall = MakeQuad("wall", O + new Vector3(0, 1.4f, WZ), new Vector3(9f, 5f, 1f), TexLib.UniqueMat(Pal.Night, 0, 0f, TexLib.bricks));
        wallRd = wall.GetComponent<Renderer>();
        MakeQuad("window", O + new Vector3(1.00f, 1.42f, WZ - 0.02f), new Vector3(0.70f, 0.80f, 1f), TexLib.Mat(Pal.Hex("20203a"), 2, 0f));
        MakeQuad("moon", O + new Vector3(1.12f, 1.60f, WZ - 0.03f), new Vector3(0.20f, 0.20f, 1f), TexLib.Mat(Pal.Hex("fff3c4"), 2, 1f));
        MakeBox("win_bar_v", O + new Vector3(1.00f, 1.42f, WZ - 0.04f), new Vector3(0.04f, 0.84f, 0.04f), TexLib.Mat(Pal.WoodDark, 0, 0f));
        MakeBox("win_bar_h", O + new Vector3(1.00f, 1.42f, WZ - 0.04f), new Vector3(0.74f, 0.04f, 0.04f), TexLib.Mat(Pal.WoodDark, 0, 0f));
        MakeBox("win_frame_t", O + new Vector3(1.00f, 1.84f, WZ - 0.04f), new Vector3(0.82f, 0.06f, 0.06f), TexLib.Mat(Pal.WoodDark, 0, 0f));
        MakeBox("win_frame_b", O + new Vector3(1.00f, 1.00f, WZ - 0.04f), new Vector3(0.82f, 0.06f, 0.06f), TexLib.Mat(Pal.WoodDark, 0, 0f));
        MakeBox("win_frame_l", O + new Vector3(0.62f, 1.42f, WZ - 0.04f), new Vector3(0.06f, 0.90f, 0.06f), TexLib.Mat(Pal.WoodDark, 0, 0f));
        MakeBox("win_frame_r", O + new Vector3(1.38f, 1.42f, WZ - 0.04f), new Vector3(0.06f, 0.90f, 0.06f), TexLib.Mat(Pal.WoodDark, 0, 0f));

        MakeQuad("poster", O + new Vector3(-0.98f, 1.42f, WZ - 0.02f), new Vector3(0.56f, 0.72f, 1f), TexLib.Mat(Pal.Purple, 2, 0f)).transform.localRotation = Quaternion.Euler(0, 0, 2.5f);
        World3D.WorldLabel(set, "TINY\nKEEP", O + new Vector3(-0.98f, 1.50f, WZ - 0.035f), Quaternion.Euler(0, 0, 2.5f), 0.54f, 0.42f, 64, Pal.Yellow, TextAnchor.MiddleCenter, TexLib.title);
        World3D.WorldLabel(set, "1989", O + new Vector3(-0.97f, 1.18f, WZ - 0.035f), Quaternion.Euler(0, 0, 2.5f), 0.54f, 0.16f, 40, Pal.White, TextAnchor.MiddleCenter, TexLib.title);

        MakeQuad("sticky1", O + new Vector3(-0.46f, 1.58f, WZ - 0.02f), new Vector3(0.24f, 0.24f, 1f), TexLib.Mat(Pal.Sticky, 2, 0f)).transform.localRotation = Quaternion.Euler(0, 0, -6f);
        World3D.WorldLabel(set, "ship it\nby FRIDAY", O + new Vector3(-0.46f, 1.58f, WZ - 0.03f), Quaternion.Euler(0, 0, -6f), 0.23f, 0.22f, 25, Pal.Ink, TextAnchor.MiddleCenter);
        MakeQuad("sticky2", O + new Vector3(0.40f, 1.62f, WZ - 0.02f), new Vector3(0.24f, 0.24f, 1f), TexLib.Mat(Pal.Hex("ffb3c8"), 2, 0f)).transform.localRotation = Quaternion.Euler(0, 0, 5f);
        World3D.WorldLabel(set, "TODO:\nsleep", O + new Vector3(0.40f, 1.62f, WZ - 0.03f), Quaternion.Euler(0, 0, 5f), 0.23f, 0.22f, 27, Pal.Ink, TextAnchor.MiddleCenter);

        // the desk and what is on it (all modelled in Blender). The models face +Z, so the whole
        // set is turned 180 degrees: the dev sits on the -Z side looking toward +Z.
        Props.Spawn("desk", set, O, 180f);
        Props.Spawn("monitor", set, O + new Vector3(0, 0.74f, 0.14f), 180f);
        Props.Spawn("keyboard", set, O + new Vector3(0, 0.74f, -0.17f), 180f);
        Props.Spawn("mouse", set, O + new Vector3(0.33f, 0.74f, -0.17f), 186f);
        Props.Spawn("mug", set, O + new Vector3(-0.36f, 0.74f, -0.12f), 150f);
        Props.Spawn("lamp", set, O + new Vector3(-0.60f, 0.74f, 0.20f), 140f);
        var clock = Props.Spawn("clock", set, O + new Vector3(0.47f, 0.74f, 0.17f), 198f);
        Props.Spawn("plant", set, O + new Vector3(0.68f, 0.74f, 0.24f), 180f);
        Props.Spawn("books", set, O + new Vector3(0.61f, 0.74f, -0.11f), 165f);

        // the monitor shows his two windows: the unfinished 3D build and the old game, live
        float sw = 0.62f, shh = 0.37f;
        screenCenter = O + new Vector3(0, 0.74f + 0.39f, 0.14f - 0.022f - 0.004f);
        BuildScreenCanvas(screenCenter, sw, shh);

        Quaternion clockRot = Quaternion.Euler(0, 18f, 0);
        clockLabel = World3D.WorldLabel(set, "3:47 AM", clock.transform.position + clockRot * new Vector3(0, 0.051f, -0.041f), clockRot, 0.125f, 0.055f, 15, Pal.Red, TextAnchor.MiddleCenter).GetComponentInChildren<Text>();

        // camera = the dev's eyes
        var cgo = new GameObject("DeskCamera");
        cgo.transform.SetParent(transform, false);
        cam = cgo.AddComponent<Camera>();
        cam.nearClipPlane = 0.03f; cam.farClipPlane = 40f; cam.fieldOfView = baseFov;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(Pal.Night.r, Pal.Night.g, Pal.Night.b, 1f);
        cam.allowHDR = false; cam.allowMSAA = false;
        var sk = cgo.AddComponent<SketchCamera>();
        sk.thickness = 1.3f;
        eye = O + new Vector3(0, 1.22f, -0.80f);
        cam.transform.position = eye;
        cam.transform.rotation = Quaternion.LookRotation(screenCenter - eye, Vector3.up);
        cam.enabled = false;
        set.gameObject.SetActive(false);
    }

    GameObject MakeQuad(string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(set, false);
        go.transform.position = pos; go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = TexLib.QuadXY;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    GameObject MakeBox(string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(set, false);
        go.transform.position = pos; go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = TexLib.Cube;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go;
    }

    void BuildScreenCanvas(Vector3 center, float w, float h)
    {
        var go = new GameObject("ScreenCanvas");
        go.transform.SetParent(set, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        const float k = 0.001f;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w / k, h / k);
        go.transform.position = center; go.transform.localScale = Vector3.one * k;
        float W = w / k, H = h / k;

        // left: the 3D build, clearly unfinished (grey-box grid)
        var l = new GameObject("wip", typeof(RectTransform)); l.transform.SetParent(go.transform, false);
        var lr = l.GetComponent<RectTransform>();
        lr.anchorMin = lr.anchorMax = new Vector2(0, 0.5f); lr.pivot = new Vector2(0, 0.5f);
        float ph0 = H - 60; float pw0 = ph0 * (208f / 192f);
        lr.anchoredPosition = new Vector2(14, 0); lr.sizeDelta = new Vector2(W - pw0 - 42, H - 28);
        wipPanel = l.AddComponent<RawImage>(); wipPanel.texture = TexLib.grid; wipPanel.color = Pal.GridFill; wipPanel.uvRect = new Rect(0, 0, 3f, 2.4f);
        var lt = new GameObject("t", typeof(RectTransform)); lt.transform.SetParent(l.transform, false);
        var ltr = lt.GetComponent<RectTransform>(); ltr.anchorMin = Vector2.zero; ltr.anchorMax = Vector2.one; ltr.offsetMin = new Vector2(10, 10); ltr.offsetMax = new Vector2(-10, -10);
        wipLabel = lt.AddComponent<Text>(); wipLabel.font = TexLib.title; wipLabel.fontSize = 32; wipLabel.alignment = TextAnchor.MiddleCenter; wipLabel.color = Pal.Ink;
        wipLabel.text = "TinyKeep3D\n<color=#ef5d5d>UNFINISHED</color>\n<size=20>TODO: everything</size>";
        wipLabel.raycastTarget = false; wipLabel.supportRichText = true;

        // right: the 1989 original, running
        var r = new GameObject("old", typeof(RectTransform)); r.transform.SetParent(go.transform, false);
        var rr = r.GetComponent<RectTransform>();
        rr.anchorMin = rr.anchorMax = new Vector2(1, 0.5f); rr.pivot = new Vector2(1, 0.5f);
        float ph = H - 60; float pw = ph * (208f / 192f);
        rr.anchoredPosition = new Vector2(-14, 0); rr.sizeDelta = new Vector2(pw, ph);
        var ri = r.AddComponent<RawImage>(); ri.texture = gm.pixel.scr.tex; ri.raycastTarget = false;
    }

    public void SetTarget(RenderTexture rt) { if (cam != null) cam.targetTexture = rt; }

    void Show(bool morning)
    {
        set.gameObject.SetActive(true);
        cam.enabled = true;
        if (gm.player != null && gm.player.cam != null) gm.player.cam.enabled = false;
        cam.targetTexture = gm.hud.CurrentTarget;
        cam.fieldOfView = baseFov;
        cam.transform.position = eye;
        cam.transform.rotation = Quaternion.LookRotation(screenCenter - eye, Vector3.up);
        Color wall = morning ? Pal.Hex("f2c58c") : (Color)Pal.Night;
        if (wallRd != null) wallRd.sharedMaterial.color = wall;
        cam.backgroundColor = new Color(wall.r, wall.g, wall.b, 1f);
        if (clockLabel != null) { clockLabel.text = morning ? "7:00 AM" : "3:47 AM"; }
        if (wipLabel != null)
        {
            wipLabel.text = morning ? "TinyKeep3D\n<color=#1f8f84>FINISHED?!</color>\n<size=20>build succeeded</size>" : "TinyKeep3D\n<color=#ef5d5d>UNFINISHED</color>\n<size=20>TODO: everything</size>";
            wipPanel.texture = morning ? (Texture)TexLib.tileOutline : TexLib.grid;
            wipPanel.color = morning ? (Color)Pal.Floor : (Color)Pal.GridFill;
        }
    }

    public void Hide()
    {
        titleIdle = false;
        if (running != null) { StopCoroutine(running); running = null; }
        set.gameObject.SetActive(false);
        cam.enabled = false;
        if (gm.player != null && gm.player.cam != null) gm.player.cam.enabled = true;
        gm.hud.SetEyelids(0f);
        gm.hud.ShowTitle(false);
        gm.sound.StopTone();
        SketchCamera.desat = 0f;
    }

    public void Abort()
    {
        if (captionRoutine != null) { StopCoroutine(captionRoutine); captionRoutine = null; }
        gm.hud.SetCaption(null);
        gm.hud.SetFade(0f, Pal.Paper, true);
        Hide();
    }

    // ------------------------------------------------------------------ title
    public void ShowTitle()
    {
        Show(false);
        titleIdle = true;
        gm.hud.ShowTitle(true);
        gm.hud.SetEyelids(0f);
        gm.sound.LoopTone("room_tone", 0.6f);
        gm.hud.SetFade(1f, Pal.K, true);
        gm.hud.SetFadeSpeed(1.2f);
        gm.hud.SetFade(0f, Pal.K);
    }

    void Update()
    {
        if (!cam.enabled) return;
        sway += Time.deltaTime;
        if (titleIdle)
        {
            // tired little head sway, and he is still typing
            Vector3 p = eye + new Vector3(Mathf.Sin(sway * 0.5f) * 0.012f, Mathf.Sin(sway * 0.83f) * 0.008f, 0);
            cam.transform.position = p;
            cam.transform.rotation = Quaternion.LookRotation(screenCenter + new Vector3(Mathf.Sin(sway * 0.37f) * 0.02f, 0, 0) - p, Vector3.up);
            keyClock -= Time.deltaTime;
            if (keyClock <= 0f)
            {
                keyClock = Random.Range(0.09f, 0.42f);
                if (Random.value < 0.8f) gm.sound.Play("key_" + Random.Range(1, 4), 0.22f, 0.06f);
            }
        }
    }

    // ------------------------------------------------------------------ opening (under 10 seconds, any key skips)
    public void PlayIntro(System.Action done)
    {
        titleIdle = false;
        pendingDone = done;
        gm.hud.ShowTitle(false);
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(IntroRoutine());
    }

    bool skip;
    IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            if (t > 0.25f && GameInput.AnyKeyPressed) { skip = true; yield break; }
            yield return null;
        }
    }

    IEnumerator IntroRoutine()
    {
        skip = false;
        var hud = gm.hud;
        hud.SetFadeSpeed(3f);
        hud.SetCaption("3:47 AM.   I'm remaking TINY KEEP (1989) in 3D.");
        float t = 0f;
        // typing
        while (t < 2.3f && !skip)
        {
            t += Time.deltaTime;
            keyClock -= Time.deltaTime;
            if (keyClock <= 0f) { keyClock = Random.Range(0.06f, 0.2f); gm.sound.Play("key_" + Random.Range(1, 4), 0.3f, 0.06f); }
            if (t > 0.3f && GameInput.AnyKeyPressed) skip = true;
            yield return null;
        }
        if (!skip)
        {
            hud.SetCaption("It's nowhere near finished...   and I'm so sleepy.");
            // two heavy blinks, then the head drops
            yield return Lids(0f, 0.75f, 0.32f); if (!skip) yield return Lids(0.75f, 0.08f, 0.22f);
            if (!skip) yield return Wait(0.35f);
            if (!skip) yield return Lids(0.08f, 0.92f, 0.42f); if (!skip) yield return Lids(0.92f, 0.2f, 0.26f);
            if (!skip) yield return Wait(0.3f);
            if (!skip) yield return Nod();
        }
        if (!skip)
        {
            // ...and the screen pulls him in
            hud.SetCaption(null);
            gm.sound.Play("yawn_whoosh", 0.9f);
            hud.SetEyelids(0f);
            Vector3 from = cam.transform.position; Quaternion fromRot = cam.transform.rotation;
            Vector3 to = screenCenter + new Vector3(0.12f, 0.0f, -0.10f);
            float d = 1.25f; t = 0f;
            while (t < d && !skip)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / d);
                float e = k * k * k;
                cam.transform.position = Vector3.Lerp(from, to, e);
                cam.transform.rotation = Quaternion.Slerp(fromRot, Quaternion.LookRotation(screenCenter - from, Vector3.up), e) * Quaternion.Euler(0, 0, Mathf.Sin(k * 9f) * 9f * k);
                cam.fieldOfView = Mathf.Lerp(baseFov, 104f, e);
                SketchCamera.Flash(Mathf.Clamp01((k - 0.55f) / 0.4f), Pal.Teal);
                if (k > 0.72f) hud.SetFade(1f, Pal.Paper);
                if (t > 0.2f && GameInput.AnyKeyPressed) skip = true;
                yield return null;
            }
        }
        // land inside the unfinished game
        hud.SetEyelids(0f);
        hud.SetFade(1f, Pal.Paper, true);
        running = null;
        var done = pendingDone; pendingDone = null;
        if (done != null) done();
        hud.SetFadeSpeed(1.6f);
        hud.SetFade(0f, Pal.Paper);
        if (captionRoutine != null) StopCoroutine(captionRoutine);
        captionRoutine = StartCoroutine(LandingCaption());
    }

    IEnumerator LandingCaption()
    {
        // no caption when you land in the remake any more: the levels play without subtitles
        yield return new WaitForSeconds(0.5f);
        gm.hud.SetCaption(null);
        captionRoutine = null;
    }

    IEnumerator Lids(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
            gm.hud.SetEyelids(Mathf.Lerp(from, to, k));
            SketchCamera.desat = Mathf.Lerp(from, to, k) * 0.6f;
            if (t > 0.1f && GameInput.AnyKeyPressed) { skip = true; yield break; }
            yield return null;
        }
        gm.hud.SetEyelids(to);
    }

    IEnumerator Nod()
    {
        // eyes close as the head sinks toward the keyboard
        Vector3 from = cam.transform.position; Quaternion fromRot = cam.transform.rotation;
        Vector3 to = from + new Vector3(0.02f, -0.07f, 0.05f);
        float d = 0.75f, t = 0f;
        while (t < d)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / d));
            cam.transform.position = Vector3.Lerp(from, to, k);
            cam.transform.rotation = fromRot * Quaternion.Euler(k * 9f, 0, k * -3f);
            gm.hud.SetEyelids(Mathf.Lerp(0.2f, 1f, k));
            if (t > 0.1f && GameInput.AnyKeyPressed) { skip = true; yield break; }
            yield return null;
        }
        yield return Wait(0.3f);
    }

    // ------------------------------------------------------------------ ending
    public void PlayEnding(System.Action done)
    {
        pendingDone = done;
        if (captionRoutine != null) { StopCoroutine(captionRoutine); captionRoutine = null; }
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(EndingRoutine());
    }

    IEnumerator EndingRoutine()
    {
        skip = false;
        var hud = gm.hud;
        hud.SetCaption(null);
        gm.sound.Play("yawn_whoosh", 0.7f);
        hud.SetFadeSpeed(3.5f);
        hud.SetFade(1f, Pal.Paper);
        yield return new WaitForSeconds(0.45f);
        hud.SetLayout(true);
        Show(true);
        gm.sound.SetMusicLevel(0.55f);
        // he wakes up face-down on the keyboard
        Vector3 down = eye + new Vector3(0.02f, -0.09f, 0.06f);
        Quaternion lookDown = Quaternion.LookRotation(screenCenter - eye, Vector3.up) * Quaternion.Euler(24f, 0, -4f);
        Quaternion lookUp = Quaternion.LookRotation(screenCenter - eye, Vector3.up);
        cam.transform.position = down; cam.transform.rotation = lookDown;
        hud.SetEyelids(1f);
        hud.SetFade(0f, Pal.Paper, true);
        gm.sound.Play("alarm", 0.7f);
        yield return Wait(0.7f);
        float t = 0f, d = 1.5f;
        while (t < d && !skip)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / d));
            hud.SetEyelids(1f - k);
            cam.transform.position = Vector3.Lerp(down, eye, k);
            cam.transform.rotation = Quaternion.Slerp(lookDown, lookUp, k);
            if (t > 0.3f && GameInput.AnyKeyPressed) skip = true;
            yield return null;
        }
        hud.SetEyelids(0f);
        cam.transform.position = eye; cam.transform.rotation = lookUp;
        if (!skip) { hud.SetCaption("...7:00 AM.   I fell asleep at my desk."); yield return Wait(2.4f); }
        if (!skip) { hud.SetCaption("Wait.   The bridge is modelled.   The walls have collision.   Who finished it?"); yield return Wait(3.4f); }
        hud.SetCaption(null);
        gm.sound.Play("jingle_win", 0.9f);
        titleIdle = true;       // gentle sway behind the results card
        yield return new WaitForSeconds(0.45f);
        running = null;
        var done = pendingDone; pendingDone = null;
        if (done != null) done();
    }
}
