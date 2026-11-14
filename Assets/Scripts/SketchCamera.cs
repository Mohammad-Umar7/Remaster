using UnityEngine;

/// <summary>Turns a camera's picture into a pencil-and-ink drawing (see SketchPost.shader).</summary>
[RequireComponent(typeof(Camera))]
public class SketchCamera : MonoBehaviour
{
    public static float flash;              // 0..1 page-white (or coloured) wash, fades by itself
    public static Color flashColor = Color.white;
    public static float desat;              // sleepy wash for the intro
    public static float boilFps = 8f;
    public float thickness = 1.15f, wobble = 2.4f, depthSens = 0.022f, normalSens = 0.55f, paperAmt = 1f;

    Material mat;
    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.DepthNormals;
        var sh = Shader.Find("Hidden/Remaster/SketchPost");
        if (sh != null) mat = new Material(sh);
    }

    public static void Flash(float amount, Color color)
    {
        if (amount > flash) { flash = amount; flashColor = color; }
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (mat == null) { Graphics.Blit(src, dst); return; }
        float k = Mathf.Max(1f, src.height / 900f);
        mat.SetFloat("_Thick", thickness * k);
        mat.SetFloat("_Wobble", wobble * k);
        mat.SetFloat("_BoilFps", boilFps);
        mat.SetFloat("_DepthSens", depthSens);
        mat.SetFloat("_NormalSens", normalSens);
        mat.SetFloat("_PaperAmt", paperAmt);
        mat.SetFloat("_Flash", Mathf.Clamp01(flash));
        mat.SetColor("_FlashColor", flashColor);
        mat.SetFloat("_Desat", Mathf.Clamp01(desat));
        Graphics.Blit(src, dst, mat);
    }
}
