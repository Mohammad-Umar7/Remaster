using UnityEngine;

/// <summary>One frame of a guard: where every joint is. Angles in degrees, lengths in metres.</summary>
public struct GuardPose
{
    public bool visible;
    public Vector3 feet;            // on the floor, world space (patrol position plus the lunge)
    public float yaw;               // which way it faces
    public float bob, squash;       // body height offset, body height scale (1 = at rest)
    public float lean, roll, twist; // body: forward lean, sideways rock, shoulder twist
    public float headYaw, headPitch;
    public float spear, spearAim, spearOut;     // spear arm: swing forward (+) or back (-), turn inward, tilt out
    public float shield, shieldOut; // shield arm: the same
    public float legL, legR;        // legs: forward (+) or back (-)
    public float bang;              // the "!" above its head: 0 hidden, 1 full size
}

/// <summary>What a guard's animation remembers between frames (one per guard, shared by its copies).</summary>
public class GuardAnim
{
    public float yaw = float.NaN, headYaw;
}

/// <summary>
/// The keep guard, put together from six Blender-made parts so it can move like a toy soldier:
/// it marches on its patrol, turns its head to you when you come close, cocks its spear back,
/// lunges and strikes. There are no animation clips: every pose is worked out each frame from
/// the guard's state, so the animation can never drift away from the rules.
/// </summary>
public class GuardRig
{
    Transform root, body, head, armSpear, armShield, legL, legR, bang, bangFill, bangLine;
    const float S = World3D.GuardScale;
    // joints, from the Blender model (see GUARD_ASSEMBLY in tools/gen_props_blender.py): x mirrored, y up
    static readonly Vector3 Hip = new Vector3(0f, 0.30f, 0f), Neck = new Vector3(0f, 0.80f, 0f);
    static readonly Vector3 ShoulderSpear = new Vector3(-0.47f, 0.62f, 0f), ShoulderShield = new Vector3(0.47f, 0.62f, 0f);
    const float LegX = 0.19f;

    public static GuardRig Build(Transform parent)
    {
        var r = new GuardRig();
        r.root = new GameObject("guard").transform;
        r.root.SetParent(parent, false);
        r.root.localScale = Vector3.one * S;
        r.body = Part("guard_body", r.root, Hip);
        r.head = Part("guard_head", r.body, Neck);
        r.armSpear = Part("guard_arm_spear", r.body, ShoulderSpear);
        r.armShield = Part("guard_arm_shield", r.body, ShoulderShield);
        r.legL = Part("guard_leg", r.root, new Vector3(-LegX, Hip.y, 0f));
        r.legR = Part("guard_leg", r.root, new Vector3(LegX, Hip.y, 0f));

        // the "!" that pops over its head when it notices you: a comic burst that always faces you
        r.bang = new GameObject("bang").transform;
        r.bang.SetParent(parent, false);
        r.bangFill = Flat(r.bang, TexLib.burstFill, Pal.Yellow, 1.25f, 1.25f, 0f);
        r.bangLine = Flat(r.bang, TexLib.burst, Pal.Ink, 1.25f, 1.25f, 0.004f);
        Flat(r.bang, TexLib.bang, Pal.Red, 0.36f, 0.72f, 0.008f);
        r.bang.gameObject.SetActive(false);
        return r;
    }

    static Transform Part(string name, Transform parent, Vector3 local)
    {
        var t = Props.Spawn(name, parent, Vector3.zero, 0f, 1f).transform;
        t.localPosition = local; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
        return t;
    }

    static Transform Flat(Transform parent, Texture tex, Color color, float width, float height, float lift)
    {
        var go = new GameObject("flat");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, lift, 0f);
        go.transform.localScale = new Vector3(width, 1f, height);
        go.AddComponent<MeshFilter>().sharedMesh = TexLib.QuadUp;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = TexLib.DecalMat(tex, color);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go.transform;
    }

    /// <summary>Put every joint where the pose says. <paramref name="shift"/> moves a whole copy (wrapped rooms).</summary>
    public void Apply(GuardPose p, Vector3 shift, Vector3 eye)
    {
        if (root.gameObject.activeSelf != p.visible) root.gameObject.SetActive(p.visible);
        bool showBang = p.visible && p.bang > 0.01f;
        if (bang.gameObject.activeSelf != showBang) bang.gameObject.SetActive(showBang);
        if (!p.visible) return;

        root.position = p.feet + shift;
        root.rotation = Quaternion.Euler(0f, p.yaw, 0f);
        body.localPosition = Hip + new Vector3(0f, p.bob, 0f);
        body.localRotation = Quaternion.Euler(p.lean, p.twist, p.roll);
        // squash and stretch keep the volume: shorter means wider
        float wide = 1f + (1f - p.squash) * 0.55f;
        body.localScale = new Vector3(wide, p.squash, wide);
        head.localRotation = Quaternion.Euler(p.headPitch, p.headYaw, 0f);
        armSpear.localRotation = Quaternion.Euler(p.spear, p.spearAim, p.spearOut);
        armShield.localRotation = Quaternion.Euler(p.shield, p.shieldOut, 0f);
        legL.localRotation = Quaternion.Euler(p.legL, 0f, 0f);
        legR.localRotation = Quaternion.Euler(p.legR, 0f, 0f);

        if (showBang)
        {
            Vector3 at = p.feet + shift + Vector3.up * (2.35f * S);
            Vector3 toEye = eye - at;
            if (toEye.sqrMagnitude < 0.01f) toEye = Vector3.back;
            toEye.Normalize();
            // face you, with the top of the "!" kept toward the sky
            Vector3 top = Vector3.up - toEye * Vector3.Dot(Vector3.up, toEye);
            if (top.sqrMagnitude < 0.0001f) top = Vector3.forward;
            bang.position = at;
            bang.rotation = Quaternion.LookRotation(top.normalized, toEye) * Quaternion.AngleAxis(-8f, Vector3.up);
            bang.localScale = Vector3.one * p.bang;
        }
    }

}
