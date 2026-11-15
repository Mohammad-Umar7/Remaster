using UnityEngine;

/// <summary>One frame of the hero: where every joint is. Angles in degrees.</summary>
public struct HeroPose
{
    public bool visible;
    public Vector3 feet;
    public float yaw;
    public float bob, squash, lean, roll, twist;    // body: height offset, height scale, forward lean, sideways rock, shoulder twist
    public float tumble;                            // a whole forward roll about his middle (the dodge)
    public float headYaw, headPitch;
    public float armL, armLOut;                     // left arm: forward is negative; out to the side
    public float armR, armRYaw, armRRoll;           // sword arm: -90 is straight ahead, -180 overhead; turned right (+) or left (-); twisted
    public float sword;                             // how the blade sits in the fist: 60 = upright in front, 180 = in line with the arm
    public float swordShow;                         // 0 no sword, 1 full size
    public float legL, legR;
}

/// <summary>
/// You, in the last room: the 1989 game's little hero, rebuilt in 3D out of blocks (teal tunic, purple
/// hair, like the sprite), carrying the old game's sword. Like the guard he has no animation clips:
/// every pose is worked out each frame from what you are doing.
/// </summary>
public class HeroRig
{
    public const float S = 1.25f;
    Transform root, core, body, head, armL, armR, legL, legR, sword;
    static readonly Vector3 Middle = new Vector3(0f, 0.5f, 0f), Hip = new Vector3(0f, -0.16f, 0f);

    static Transform Joint(Transform parent, string name, Vector3 local)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = local;
        return t;
    }

    static void Block(Transform parent, Vector3 local, Vector3 size, Color color)
    {
        var go = new GameObject("block");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = TexLib.Cube;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = TexLib.Mat(color, 0, 0f, null, false, false, 0.9f);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;
    }

    public static HeroRig Build(Transform parent)
    {
        var r = new HeroRig();
        Color skin = Pal.Hex("f6dcb4"), tunic = Pal.Teal, hair = Pal.Purple, trousers = Pal.Night, boot = Pal.Ink;
        r.root = Joint(parent, "hero", Vector3.zero);
        r.root.localScale = Vector3.one * S;
        r.core = Joint(r.root, "core", Middle);                 // the dodge rolls him about this point
        r.body = Joint(r.core, "body", Hip);
        Block(r.body, new Vector3(0f, 0.25f, 0f), new Vector3(0.52f, 0.5f, 0.34f), tunic);
        Block(r.body, new Vector3(0f, 0.04f, 0f), new Vector3(0.56f, 0.09f, 0.38f), Pal.Yellow);        // belt
        Block(r.body, new Vector3(0f, 0.27f, -0.2f), new Vector3(0.46f, 0.46f, 0.05f), Pal.Red);        // a short cape
        r.head = Joint(r.body, "head", new Vector3(0f, 0.5f, 0f));
        Block(r.head, new Vector3(0f, 0.21f, 0f), new Vector3(0.44f, 0.42f, 0.42f), skin);
        Block(r.head, new Vector3(0f, 0.41f, -0.02f), new Vector3(0.5f, 0.14f, 0.5f), hair);
        Block(r.head, new Vector3(0f, 0.25f, -0.2f), new Vector3(0.5f, 0.36f, 0.12f), hair);
        Block(r.head, new Vector3(-0.1f, 0.22f, 0.215f), new Vector3(0.07f, 0.1f, 0.02f), Pal.Ink);     // eyes
        Block(r.head, new Vector3(0.1f, 0.22f, 0.215f), new Vector3(0.07f, 0.1f, 0.02f), Pal.Ink);
        r.armL = Joint(r.body, "armL", new Vector3(-0.34f, 0.44f, 0f));
        r.armR = Joint(r.body, "armR", new Vector3(0.34f, 0.44f, 0f));
        foreach (var arm in new[] { r.armL, r.armR })
        {
            Block(arm, new Vector3(0f, -0.19f, 0f), new Vector3(0.15f, 0.4f, 0.17f), tunic);
            Block(arm, new Vector3(0f, -0.43f, 0f), new Vector3(0.14f, 0.1f, 0.16f), skin);
        }
        // the old game's sword, in his right fist: grip at the joint, blade along +Y
        r.sword = Joint(r.armR, "sword", new Vector3(0f, -0.43f, 0.02f));
        Block(r.sword, new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.26f, 0.07f), Pal.WoodDark);
        Block(r.sword, new Vector3(0f, -0.16f, 0f), new Vector3(0.12f, 0.1f, 0.12f), Pal.Yellow);       // pommel
        Block(r.sword, new Vector3(0f, 0.15f, 0f), new Vector3(0.46f, 0.085f, 0.11f), Pal.Yellow);      // crossguard
        Block(r.sword, new Vector3(0f, 0.74f, 0f), new Vector3(0.16f, 1.1f, 0.045f), Pal.White);        // blade
        Block(r.sword, new Vector3(0f, 0.7f, 0f), new Vector3(0.05f, 0.98f, 0.06f), Pal.Teal);          // the pixel-blue line down its middle
        Block(r.sword, new Vector3(0f, 1.34f, 0f), new Vector3(0.09f, 0.12f, 0.045f), Pal.White);       // tip
        r.legL = Joint(r.core, "legL", Hip + new Vector3(-0.14f, 0f, 0f));
        r.legR = Joint(r.core, "legR", Hip + new Vector3(0.14f, 0f, 0f));
        foreach (var leg in new[] { r.legL, r.legR })
        {
            Block(leg, new Vector3(0f, -0.14f, 0f), new Vector3(0.2f, 0.3f, 0.22f), trousers);
            Block(leg, new Vector3(0f, -0.3f, 0.03f), new Vector3(0.21f, 0.09f, 0.29f), boot);
        }
        r.root.gameObject.SetActive(false);
        return r;
    }

    /// <summary>The tip of the blade, in the world (sparks and the slash come from here).</summary>
    public Vector3 Tip { get { return sword.TransformPoint(new Vector3(0f, 1.3f, 0f)); } }
    public Vector3 Chest { get { return body.TransformPoint(new Vector3(0f, 0.3f, 0f)); } }

    public void Apply(HeroPose p)
    {
        if (root.gameObject.activeSelf != p.visible) root.gameObject.SetActive(p.visible);
        if (!p.visible) return;
        root.position = p.feet;
        root.rotation = Quaternion.Euler(0f, p.yaw, 0f);
        core.localPosition = Middle + new Vector3(0f, p.bob, 0f);
        core.localRotation = Quaternion.Euler(p.tumble, 0f, 0f);
        body.localRotation = Quaternion.Euler(p.lean, p.twist, p.roll);
        float wide = 1f + (1f - p.squash) * 0.5f;
        body.localScale = new Vector3(wide, p.squash, wide);
        head.localRotation = Quaternion.Euler(p.headPitch, p.headYaw, 0f);
        armL.localRotation = Quaternion.Euler(p.armL, 0f, -p.armLOut);
        armR.localRotation = Quaternion.Euler(p.armR, p.armRYaw, p.armRRoll);
        bool hasSword = p.swordShow > 0.01f;
        if (sword.gameObject.activeSelf != hasSword) sword.gameObject.SetActive(hasSword);
        if (hasSword)
        {
            sword.localRotation = Quaternion.Euler(p.sword, 0f, 0f);
            sword.localScale = Vector3.one * p.swordShow;
        }
        legL.localRotation = Quaternion.Euler(p.legL, 0f, 0f);
        legR.localRotation = Quaternion.Euler(p.legR, 0f, 0f);
    }
}
