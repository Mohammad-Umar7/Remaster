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

