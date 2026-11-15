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

    // ------------------------------------------------------------------ the animation
    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    static float OutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
    static float OutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        t = Mathf.Clamp01(t) - 1f;
        return 1f + c3 * t * t * t + c1 * t * t;
    }

    /// <summary>Yaw (degrees) of a direction given in tile space, where y points down the screen (-Z).</summary>
    public static float YawOf(Vector2 tileDir) { return Mathf.Atan2(tileDir.x, -tileDir.y) * Mathf.Rad2Deg; }

    /// <summary>
    /// Work out this frame's pose from the guard's state: its patrol step, how alert it is,
    /// and how far into a spear strike it is.
    /// </summary>
    public static GuardPose Compute(Room room, Slime gs, GuardAnim an, float dt)
    {
        var p = new GuardPose();
        p.visible = !gs.gone;
        p.squash = 1f;
        if (gs.gone) return p;

        // --- marching: one step per hop, the front foot lands on the beat (when the step sound plays)
        float phase = Mathf.PI * (gs.steps + Mathf.Clamp01(gs.hopClock / gs.hopPeriod));
        float stride = Mathf.Cos(phase);                    // +1 left foot forward, -1 right foot forward
        float rise = 1f - Mathf.Abs(stride);                // 0 as a foot lands, 1 as the legs pass
        p.legL = stride * -34f; p.legR = stride * 34f;
        p.bob = rise * 0.05f;
        p.roll = stride * 3.5f;
        p.twist = stride * 7f;
        p.lean = 3f;
        p.spear = -5f - stride * 5f;
        p.shield = stride * 9f;
        p.headYaw = -p.twist * 0.7f;                        // the head stays on course while the shoulders swing
        p.headPitch = -rise * 2.5f;

        float patrolYaw = YawOf(gs.dir);
        an.yaw = float.IsNaN(an.yaw) ? patrolYaw : Mathf.MoveTowardsAngle(an.yaw, patrolYaw, 520f * dt);
        p.yaw = an.yaw;
        p.feet = room.TileFToWorld(gs.pos);

        // --- on guard: it has seen you. Spear lowered at you, shield up, eyes on you.
        float alert = Smooth(gs.alertT);
        float lookYaw = 0f;
        Vector2 toYou = gs.lookAt - gs.pos;
        if (toYou.sqrMagnitude > 0.0004f) lookYaw = Mathf.Clamp(Mathf.DeltaAngle(p.yaw, YawOf(toYou)), -85f, 85f);
        an.headYaw = Mathf.Lerp(an.headYaw, lookYaw * alert, 1f - Mathf.Exp(-14f * dt));
        p.headYaw = Mathf.Lerp(p.headYaw, an.headYaw, alert);
        p.spear += alert * 44f;
        p.shield += alert * -24f;
        p.shieldOut = alert * 16f;
        p.lean += alert * 5f;

        // --- the "!" pops, holds, and shrinks away
        if (gs.bangT >= 0f)
            p.bang = gs.bangT < 0.14f ? OutBack(gs.bangT / 0.14f) : (gs.bangT < 0.62f ? 1f : Mathf.Clamp01(1f - (gs.bangT - 0.62f) / 0.26f));

        // --- the strike: cock the spear back (squash), lunge and thrust (stretch), wobble back to rest
        float t = gs.attackT;
        if (t >= 0f)
        {
            float W = Maps.GuardWindup, K = Maps.GuardStrike, R = Maps.GuardRecover;
            float face, lunge, spear, aim, lean, squash, shieldOut;
            // it stops a spear's length from you: lunging in from further away, hopping back if you are on its toes,
            // so the whole thrust happens in front of your eyes and the spear tip comes at your face
            float reach = gs.attackReach;
            if (t < W)
            {
                float e = OutCubic(t / W);
                face = e; lunge = -0.16f * e; squash = 1f - 0.18f * e; lean = -15f * e; aim = 0f;
                spear = Mathf.Lerp(p.spear, -62f, e); shieldOut = Mathf.Lerp(p.shieldOut, -8f, e);
            }
            else if (t < W + K)
            {
                float e = OutCubic((t - W) / K);
                face = 1f; lunge = Mathf.Lerp(-0.16f, reach, e); squash = Mathf.Lerp(0.82f, 1.16f, e); lean = Mathf.Lerp(-15f, 11f, e);
                spear = Mathf.Lerp(-62f, 80f, e); aim = 21f * e; shieldOut = Mathf.Lerp(-8f, 34f, e);
            }
            else
            {
                float u = Mathf.Clamp01((t - W - K) / R);
                float wobble = Mathf.Exp(-5.5f * u) * Mathf.Cos(u * 16f);      // a damped spring: 1 at the hit, settling to 0
                float back = Smooth((u - 0.18f) * 1.5f);                       // it holds the thrust for a beat, then steps back
                face = 1f - Smooth((u - 0.55f) / 0.45f);
                lunge = reach * (1f - back);
                squash = 1f + 0.16f * wobble;
                lean = 11f * wobble;
                spear = Mathf.Lerp(80f, p.spear, back) + 8f * wobble;
                aim = 21f * (1f - back);
                shieldOut = Mathf.Lerp(34f, p.shieldOut, back);
            }
            float strikeYaw = YawOf(gs.attackDir);
            p.yaw = Mathf.LerpAngle(p.yaw, strikeYaw, face);
            p.feet += new Vector3(gs.attackDir.x, 0f, -gs.attackDir.y) * lunge;
            p.spear = spear; p.spearAim = aim; p.lean = lean; p.squash = squash; p.shieldOut = shieldOut;
            p.headYaw = Mathf.Lerp(p.headYaw, 0f, face);
            p.legL = Mathf.Lerp(p.legL, 26f, face * 0.8f); p.legR = Mathf.Lerp(p.legR, -30f, face * 0.8f);   // a fencer's stance
            p.bob *= 1f - face;
        }
        return p;
    }
}
