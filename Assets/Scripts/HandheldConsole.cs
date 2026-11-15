using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The 1989 original runs on a chunky handheld that you carry in your left hand, low in the 3D view.
/// It is real geometry parented to the player's camera, so the remake draws it with the same ink
/// lines as everything else. It lags behind when you turn, dips with your steps and jumps when
/// things happen. Tab raises it (big) or lowers it (small).
/// </summary>
public class HandheldConsole : MonoBehaviour
{
    // ---- the device, in units of its own width (origin in the middle of the body; the front face looks back at the camera) ----
    const float BodyH = 1.42f, BodyD = 0.20f, Front = -BodyD * 0.5f;
    const float ScreenW = 0.76f, ScreenH = ScreenW * PixelScreen.H / PixelScreen.W, ScreenY = 0.27f;
    const float Surround = 0.064f;                      // cream border around the screen
    const float PadX = -0.22f, PadY = -0.50f;           // the D-pad; the two buttons sit opposite
    const float LogoX = 0.06f, StripY = -0.215f;        // the strip under the screen: logo and power light

    // ---- where it sits in the view ----
    const float Depth = 0.19f;                          // metres from the eye: closer than a wall can ever get, so nothing cuts into it
    const float BigWidth = 0.68f, SmallWidth = 0.46f;   // body width, as a fraction of the view height (raised / lowered)
    const float BigLift = 0.10f, SmallLift = 0.09f;     // gap under the screen, same unit
    const float HandRoom = 0.34f;                       // from the left edge of the view to the screen, in body widths: room for the hand
    const float MaxReach = 0.25f;                       // ... but never further out than this (metres), even on a very wide screen
    // How it is held: tipped back a little, turned a little toward your eyes, leaning a little to the right.
    // Keep these small: this close to the eye, a few degrees already bend the screen out of shape.
    static readonly Quaternion Hold = Quaternion.Euler(3f, -4f, -2.5f);
    static readonly Vector3 ScreenCorner = new Vector3(-ScreenW * 0.5f, ScreenY - ScreenH * 0.5f, Front);     // bottom left of the screen

    static readonly Color BodyColor = Pal.Purple;
    static readonly Color SkinColor = Pal.Hex("f4efe1");    // the pixel hero's skin
    static readonly Color SleeveColor = Pal.Teal;           // ... and shirt: the hand is yours

    GameManager gm;
    Camera cam;
    Transform inHand;           // device + hand; all the sway happens here, in body units
    RenderTexture screenRT;
    bool raised = true;
    float rise = 1f, size = 1f;
    float lastYaw, lastPitch, yawVel, pitchVel, swayX, swayY, swayRoll, bump, coinPulse, hurt, bobPhase, bobAmt;

    public static HandheldConsole Create(GameManager gm)
    {
        var go = new GameObject("Handheld");
        go.transform.SetParent(gm.player.cam.transform, false);
        var h = go.AddComponent<HandheldConsole>();
        h.gm = gm; h.cam = gm.player.cam;
        h.Build();
        go.SetActive(false);
        return h;
    }

    // ------------------------------------------------------------------ public controls
    /// <summary>Shown while playing; put away for the title, the intro and the ending. It comes up from below the view.</summary>
    public void SetShown(bool on)
    {
        if (on && !gameObject.activeSelf)
        {
            rise = 0f;
            if (gm.player != null) { lastYaw = gm.player.yaw; lastPitch = gm.player.pitch; }
            yawVel = pitchVel = swayX = swayY = swayRoll = 0f;
        }
        gameObject.SetActive(on);
    }

    /// <summary>Tab: hold it up (big) or let it hang lower (small).</summary>
    public void Toggle() { raised = !raised; bump = Mathf.Max(bump, 0.03f); }

    /// <summary>A jolt downward, in body widths (a step, a coin, a hit).</summary>
    public void Bump(float amount) { bump = Mathf.Max(bump, amount); }
    public void CoinPulse() { coinPulse = 1f; Bump(0.016f); }
    public void Hurt() { hurt = 1f; Bump(0.03f); }

    // ------------------------------------------------------------------ build
    Material Flat(Color c) { return TexLib.Mat(c, 2, 0f); }

    Transform Part(string name, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(inHand, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot ?? Quaternion.identity;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go.transform;
    }

    /// <summary>A rounded limb from a to b (Unity's capsule is 2 tall and 1 wide).</summary>
    Transform Limb(string name, Mesh capsule, Material mat, Vector3 a, Vector3 b, float width, float thickness)
    {
        Vector3 d = b - a;
        return Part(name, capsule, mat, (a + b) * 0.5f, new Vector3(width, (d.magnitude + width) * 0.5f, thickness), Quaternion.FromToRotation(Vector3.up, d.normalized));
    }

    void Build()
    {
        inHand = new GameObject("InHand").transform;
        inHand.SetParent(transform, false);

        // the old game is upscaled 3x with hard pixels first, so it stays crisp on the tilted screen
        screenRT = new RenderTexture(PixelScreen.W * 3, PixelScreen.H * 3, 0, RenderTextureFormat.ARGB32)
        {
            name = "HandheldScreen", antiAliasing = 1, useMipMap = true, autoGenerateMips = true,
            filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
        };

        Mesh cube = ShadedCube, quad = TexLib.QuadXY;
        Mesh capsule = Primitive(PrimitiveType.Capsule), sphere = Primitive(PrimitiveType.Sphere), cylinder = Primitive(PrimitiveType.Cylinder);
        Quaternion faceOn = Quaternion.Euler(90f, 0f, 0f);       // a cylinder standing on the front face

        // ---------- the device ----------
        Part("Body", cube, Flat(BodyColor), Vector3.zero, new Vector3(1f, BodyH, BodyD));
        Part("Face", quad, TexLib.UniqueMat(Color.white, 2, 0f, MakeFace()), new Vector3(0f, 0f, Front - 0.003f), new Vector3(1f, BodyH, 1f));
        Part("Screen", quad, TexLib.UniqueMat(Color.white, 2, 0f, screenRT), new Vector3(0f, ScreenY, Front - 0.007f), new Vector3(ScreenW, ScreenH, 1f));
        Part("PadH", cube, Flat(Pal.Ink), new Vector3(PadX, PadY, Front - 0.020f), new Vector3(0.30f, 0.10f, 0.040f));
        Part("PadV", cube, Flat(Pal.Ink), new Vector3(PadX, PadY, Front - 0.021f), new Vector3(0.10f, 0.30f, 0.042f));
        Part("ButtonB", cylinder, Flat(Pal.Red), new Vector3(0.20f, -0.56f, Front - 0.020f), new Vector3(0.15f, 0.020f, 0.15f), faceOn);
        Part("ButtonA", cylinder, Flat(Pal.Red), new Vector3(0.37f, -0.45f, Front - 0.020f), new Vector3(0.15f, 0.020f, 0.15f), faceOn);

        // ---------- your left hand: the palm cups the left edge from behind, the thumb rests on the front ----------
        Material skin = Flat(SkinColor), sleeve = Flat(SleeveColor);
        Part("Palm", sphere, skin, new Vector3(-0.60f, -0.33f, 0.05f), new Vector3(0.36f, 0.66f, 0.34f));
        Part("ThumbBase", sphere, skin, new Vector3(-0.575f, -0.50f, -0.105f), new Vector3(0.25f, 0.30f, 0.20f));
        Limb("Thumb", capsule, skin, new Vector3(-0.545f, -0.42f, -0.160f), new Vector3(-0.455f, -0.085f, -0.165f), 0.130f, 0.115f);
        Limb("Wrist", capsule, skin, new Vector3(-0.62f, -0.55f, 0.06f), new Vector3(-0.68f, -0.80f, 0.10f), 0.34f, 0.30f);
        Limb("Sleeve", capsule, sleeve, new Vector3(-0.69f, -0.84f, 0.11f), new Vector3(-0.95f, -1.90f, 0.34f), 0.46f, 0.42f);
    }

    // ------------------------------------------------------------------ meshes
    static readonly Dictionary<PrimitiveType, Mesh> prims = new Dictionary<PrimitiveType, Mesh>();
    static Mesh Primitive(PrimitiveType type)
    {
        Mesh m;
        if (prims.TryGetValue(type, out m) && m != null) return m;
        var go = GameObject.CreatePrimitive(type);
        go.SetActive(false);
        m = go.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(go);
        prims[type] = m;
        return m;
    }

    static Mesh shadedCube;
    /// <summary>A unit cube whose faces carry their own shade (vertex colour): the marker was pressed harder on the sides.
    /// The sketch shader's own shading follows the world, which would crawl over something carried along with the camera.</summary>
    static Mesh ShadedCube
    {
        get
        {
            if (shadedCube != null) return shadedCube;
            Vector3[] normals = { Vector3.back, Vector3.forward, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
            float[] shade = { 1f, 0.55f, 0.72f, 0.64f, 0.88f, 0.56f };
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int f = 0; f < 6; f++)
            {
                Vector3 nn = normals[f];
                Vector3 up = Mathf.Abs(nn.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(nn, up);
                int b = v.Count;
                v.Add((nn - right - up) * 0.5f); v.Add((nn - right + up) * 0.5f); v.Add((nn + right + up) * 0.5f); v.Add((nn + right - up) * 0.5f);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                for (int k = 0; k < 4; k++) { n.Add(nn); c.Add(new Color(shade[f], shade[f], shade[f], 1f)); }
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
            shadedCube = new Mesh { name = "ShadedCube" };
            shadedCube.SetVertices(v); shadedCube.SetNormals(n); shadedCube.SetColors(c); shadedCube.SetUVs(0, uv); shadedCube.SetTriangles(t, 0);
            shadedCube.RecalculateBounds();
            return shadedCube;
        }
    }

    // ------------------------------------------------------------------ the printed front of the device
    /// <summary>Everything flat on the front: the cream screen surround, the dark screen well, the wells under the
    /// controls, the TINY KEEP logo and the power light, with the outlines inked by hand like the rest of the game.</summary>
    static Texture2D MakeFace()
    {
        const int W = 768;
        int H = Mathf.RoundToInt(W * BodyH);
        var px = new Color32[W * H];                        // row 0 is the top row
        Color32 body = BodyColor;
        for (int i = 0; i < px.Length; i++) px[i] = body;

        // body units -> texture pixels
        System.Func<float, float> X = x => (x + 0.5f) * W;
        System.Func<float, float> Y = y => (BodyH * 0.5f - y) * W;

        float top = ScreenY + ScreenH * 0.5f, bottom = ScreenY - ScreenH * 0.5f, half = ScreenW * 0.5f;
        float bx0 = X(-half - Surround), bx1 = X(half + Surround), by0 = Y(top + Surround), by1 = Y(bottom - Surround);
        float sx0 = X(-half - 0.012f), sx1 = X(half + 0.012f), sy0 = Y(top + 0.012f), sy1 = Y(bottom - 0.012f);
        FillRound(px, W, H, bx0, by0, bx1, by1, 26f, Pal.White);
        FillRound(px, W, H, sx0, sy0, sx1, sy1, 6f, Pal.K);

        Color32 well = Color.Lerp(BodyColor, Pal.Ink, 0.45f);
        Vector2 pad = new Vector2(X(PadX), Y(PadY)), bb = new Vector2(X(0.20f), Y(-0.56f)), ba = new Vector2(X(0.37f), Y(-0.45f));
        Vector2 led = new Vector2(X(0.40f), Y(StripY));
        FillRound(px, W, H, pad.x - 0.215f * W, pad.y - 0.215f * W, pad.x + 0.215f * W, pad.y + 0.215f * W, 0.215f * W, well);
        FillRound(px, W, H, bb.x - 0.105f * W, bb.y - 0.105f * W, bb.x + 0.105f * W, bb.y + 0.105f * W, 0.105f * W, well);
        FillRound(px, W, H, ba.x - 0.105f * W, ba.y - 0.105f * W, ba.x + 0.105f * W, ba.y + 0.105f * W, 0.105f * W, well);
        FillRound(px, W, H, led.x - 0.020f * W, led.y - 0.020f * W, led.x + 0.020f * W, led.y + 0.020f * W, 0.020f * W, Pal.Red);

        var logo = PixelArt.Get("logo_tinykeep");
        if (logo != null) Stamp(px, W, H, logo, Mathf.RoundToInt(X(LogoX) - logo.w * 1.5f), Mathf.RoundToInt(Y(StripY) - logo.h * 1.5f), 3);

        var d = new Doodle(W, H, 77);
        d.Box(bx0, by0, bx1, by1, 5f, 1.6f, 5f);
        d.Box(sx0, sy0, sx1, sy1, 4f, 1.2f, 3f);
        d.Circle(pad, 0.215f * W, 4.5f);
        d.Circle(bb, 0.105f * W, 4f);
        d.Circle(ba, 0.105f * W, 4f);
        d.Circle(led, 0.030f * W, 3f);
        for (int i = 0; i < 4; i++)                          // speaker slots
            d.Line(X(-0.10f + i * 0.055f), Y(-0.660f), X(-0.045f + i * 0.055f), Y(-0.595f), 5f, 1f);

        Color32 ink = Pal.Ink;
        for (int i = 0; i < px.Length; i++)
        {
            float a = Mathf.Clamp01(d.a[i]);
            if (a > 0f) px[i] = Color32.Lerp(px[i], ink, a);
        }

        var flipped = new Color32[px.Length];               // textures are stored bottom row first
        for (int y = 0; y < H; y++) System.Array.Copy(px, y * W, flipped, (H - 1 - y) * W, W);
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "HandheldFace", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
        tex.SetPixels32(flipped);
        tex.Apply(true);
        return tex;
    }

    /// <summary>A filled rectangle with rounded corners and a soft edge (a circle when the radius is half the size).</summary>
    static void FillRound(Color32[] px, int w, int h, float x0, float y0, float x1, float y1, float radius, Color32 c)
    {
        float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f, hx = (x1 - x0) * 0.5f - radius, hy = (y1 - y0) * 0.5f - radius;
        int ix0 = Mathf.Max(0, Mathf.FloorToInt(x0 - 1)), ix1 = Mathf.Min(w - 1, Mathf.CeilToInt(x1 + 1));
        int iy0 = Mathf.Max(0, Mathf.FloorToInt(y0 - 1)), iy1 = Mathf.Min(h - 1, Mathf.CeilToInt(y1 + 1));
        for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
            {
                float qx = Mathf.Max(Mathf.Abs(x + 0.5f - cx) - hx, 0f), qy = Mathf.Max(Mathf.Abs(y + 0.5f - cy) - hy, 0f);
                float cover = Mathf.Clamp01(radius - Mathf.Sqrt(qx * qx + qy * qy) + 0.5f);
                if (cover > 0f) px[y * w + x] = Color32.Lerp(px[y * w + x], c, cover);
            }
    }

    /// <summary>Print a pixel sprite, scaled up with hard pixels.</summary>
    static void Stamp(Color32[] px, int w, int h, PSprite s, int x0, int y0, int scale)
    {
        for (int sy = 0; sy < s.h * scale; sy++)
            for (int sx = 0; sx < s.w * scale; sx++)
            {
                Color32 c = s.px[(sy / scale) * s.w + sx / scale];
                int x = x0 + sx, y = y0 + sy;
                if (c.a == 0 || x < 0 || y < 0 || x >= w || y >= h) continue;
                px[y * w + x] = c;
            }
    }

    // ------------------------------------------------------------------ frame
    void LateUpdate()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        Graphics.Blit(gm.pixel.scr.tex, screenRT);

        rise = Mathf.Min(1f, rise + dt / 0.6f);
        float pop = EaseOutBack(rise);
        size = Mathf.Lerp(size, raised ? 1f : 0f, 1f - Mathf.Exp(-12f * dt));

        // Size and place it against the view, not the world: it keeps its place on screen whatever the
        // field of view (sprinting widens it) or the shape of the window.
        float viewH = 2f * Depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float viewW = viewH * cam.aspect;
        // on a narrow window it shrinks, so it never reaches the middle of the view
        float unit = viewH * Mathf.Min(Mathf.Lerp(SmallWidth, BigWidth, size), 0.38f * cam.aspect);
        float lift = Mathf.Lerp(SmallLift, BigLift, size);
        // the bottom left corner of the screen is what gets placed: a hand's width in from the left, `lift` up from the bottom
        Vector3 corner = new Vector3(Mathf.Max(-viewW * 0.5f, -MaxReach) + unit * HandRoom, -viewH * 0.5f + viewH * lift, Depth);
        transform.localPosition = corner - unit * (Hold * ScreenCorner);
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * unit;

        // it lags behind your head when you turn
        float yaw = gm.player.yaw, pitch = gm.player.pitch;
        float vy = dt > 0f ? Mathf.DeltaAngle(lastYaw, yaw) / dt : 0f, vp = dt > 0f ? (pitch - lastPitch) / dt : 0f;
        lastYaw = yaw; lastPitch = pitch;
        float follow = 1f - Mathf.Exp(-10f * dt), settle = 1f - Mathf.Exp(-6f * dt);
        yawVel = Mathf.Lerp(yawVel, vy, follow);
        pitchVel = Mathf.Lerp(pitchVel, vp, follow);
        swayX = Mathf.Lerp(swayX, Mathf.Clamp(-yawVel * 0.00040f, -0.085f, 0.085f), settle);
        swayY = Mathf.Lerp(swayY, Mathf.Clamp(pitchVel * 0.00040f, -0.06f, 0.06f), settle);
        swayRoll = Mathf.Lerp(swayRoll, Mathf.Clamp(-yawVel * 0.02f, -4f, 4f), settle);

        // it dips with every step
        float speed = gm.player.speedNow;
        bool walking = gm.player.controlEnabled && gm.player.grounded && speed > 0.6f;
        bobAmt = Mathf.Lerp(bobAmt, walking ? Mathf.Clamp01(speed / PlayerController.WalkSpeed) : 0f, 1f - Mathf.Exp(-8f * dt));
        if (walking) bobPhase += speed * Time.deltaTime / 2.0f * Mathf.PI;
        float t = Time.unscaledTime;
        float bobX = Mathf.Sin(bobPhase) * 0.020f * bobAmt + Mathf.Sin(t * 0.83f + 0.4f) * 0.006f;
        float bobY = -Mathf.Abs(Mathf.Sin(bobPhase)) * 0.030f * bobAmt + Mathf.Sin(t * 1.25f) * 0.008f;

        bump = Mathf.Lerp(bump, 0f, 1f - Mathf.Exp(-9f * dt));
        coinPulse = Mathf.Max(0f, coinPulse - dt * 4f);
        hurt = Mathf.Max(0f, hurt - dt * 2.4f);
        float shake = Mathf.Sin(t * 46f) * 5f * hurt * hurt;

        inHand.localPosition = new Vector3(swayX + bobX, swayY + bobY - bump - (1f - pop) * 1.25f, 0f);
        inHand.localRotation = Hold * Quaternion.Euler(Mathf.Clamp(pitchVel * 0.012f, -4f, 4f), swayX * 50f, swayRoll + shake + Mathf.Sin(t * 0.7f) * 0.5f - (1f - pop) * 16f);
        inHand.localScale = Vector3.one * (1f + coinPulse * 0.035f);
    }

    void OnDestroy()
    {
        if (screenRT != null) screenRT.Release();
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
