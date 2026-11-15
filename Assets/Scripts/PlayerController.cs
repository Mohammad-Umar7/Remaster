using UnityEngine;

/// <summary>First-person walking. No jump on purpose: the old top-down game has no height.</summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public Camera cam;
    public Transform pivot;             // bob + shake live here, never on the body
    public bool controlEnabled;
    public float yaw, pitch;
    public float trauma;                // 0..1 screen shake energy
    public Vector3 velocity;
    public bool grounded;
    public float speedNow;
    public float distanceWalked;
    public System.Action<bool> onStep;  // bool = sprinting
    public Vector3 knock;               // extra speed from being hit; it dies away in a quarter of a second
    public System.Func<Vector3, bool> canStand;     // is there floor at this spot? (a hit never throws you into a pit)
    float kickPitch, kickRoll, kickFov;
    public bool lookLocked;             // the mouse does not turn you (the last fight keeps the view on the Captain)
    public float zoom;                  // degrees taken off the field of view (the camera leans in on whoever is talking)
    float zoomNow;

    public const float WalkSpeed = 4.6f, SprintSpeed = 7.4f, EyeHeight = 1.62f;
    const float Accel = 46f, Decel = 40f, Gravity = 26f;
    const float BaseFov = 72f, SprintFov = 80f;

    CharacterController cc;
    float stepAccum, bobPhase, bobAmt, roll, fov = BaseFov, landDip;
    bool wasGrounded;

    public static PlayerController Create()
    {
        var go = new GameObject("Player");
        go.layer = 2; // Ignore Raycast
        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.34f; cc.center = new Vector3(0, 0.9f, 0);
        cc.stepOffset = 0.3f; cc.slopeLimit = 50f; cc.skinWidth = 0.04f; cc.minMoveDistance = 0f;
        var pc = go.AddComponent<PlayerController>();
        var pv = new GameObject("CamPivot");
        pv.transform.SetParent(go.transform, false);
        pv.transform.localPosition = new Vector3(0, EyeHeight, 0);
        pc.pivot = pv.transform;
        var cgo = new GameObject("PlayerCamera");
        cgo.transform.SetParent(pv.transform, false);
        var cam = cgo.AddComponent<Camera>();
        cam.nearClipPlane = 0.06f; cam.farClipPlane = 190f; cam.fieldOfView = BaseFov;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(Pal.Paper.r, Pal.Paper.g, Pal.Paper.b, 1f);
        cam.allowHDR = false; cam.allowMSAA = false;
        cgo.AddComponent<SketchCamera>();
        cgo.AddComponent<AudioListener>();
        pc.cam = cam;
        return pc;
    }

    void Awake() { cc = GetComponent<CharacterController>(); }

    public void Teleport(Vector3 feet, float newYaw)
    {
        cc.enabled = false;
        transform.position = feet + Vector3.up * 0.05f;
        cc.enabled = true;
        yaw = newYaw; pitch = 4f; velocity = Vector3.zero; stepAccum = 0f;
        knock = Vector3.zero;
        ApplyRotation();
    }

    /// <summary>Move without changing where you look or how fast you go (used by the screen wrap).</summary>
    public void Shift(Vector3 delta)
    {
        cc.enabled = false;
        transform.position += delta;
        cc.enabled = true;
    }

    /// <summary>Knocked back by a hit: a shove along the floor and a little hop.</summary>
    public void Knock(Vector3 push, float hop)
    {
        knock = new Vector3(push.x, 0f, push.z);
        if (cc.isGrounded) velocity.y = hop;
    }

    /// <summary>The camera snaps back from a hit: head up, a tilt to one side, a punch of field of view.</summary>
    public void Kick(float pitchDegrees, float rollDegrees, float fovDegrees)
    {
        kickPitch = pitchDegrees; kickRoll = rollDegrees; kickFov = fovDegrees;
    }

    void ApplyRotation()
    {
        transform.rotation = Quaternion.Euler(0, yaw, 0);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector2 move = Vector2.zero; bool sprint = false;
        if (controlEnabled)
        {
            // in the last fight the view stays locked on the Captain, so the mouse does not turn you
            if (!lookLocked)
            {
                Vector2 look = GameInput.Look;
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -80f, 80f);
                if (GameInput.overrideActive && GameInput.overrideYaw.HasValue)
                {
                    yaw = Mathf.MoveTowardsAngle(yaw, GameInput.overrideYaw.Value, 540f * dt);
                    pitch = Mathf.MoveTowards(pitch, 10f, 120f * dt);
                }
            }
            move = GameInput.Move;
            sprint = GameInput.Sprint && move.y > 0.1f;
        }
        ApplyRotation();

        Vector3 wish = (transform.forward * move.y + transform.right * move.x);
        if (wish.sqrMagnitude > 1f) wish.Normalize();
        float target = sprint ? SprintSpeed : WalkSpeed;
        Vector3 horiz = new Vector3(velocity.x, 0, velocity.z);
        Vector3 want = wish * target;
        float rate = (wish.sqrMagnitude > 0.01f) ? Accel : Decel;
        horiz = Vector3.MoveTowards(horiz, want, rate * dt);

        grounded = cc.isGrounded;
        if (grounded && velocity.y < 0f) velocity.y = -3f; else velocity.y -= Gravity * dt;
        velocity.x = horiz.x; velocity.z = horiz.z;
        if (knock.sqrMagnitude > 0.01f)
        {
            // the shove stops where the floor ends, so being hit never costs you a fall as well
            if (canStand != null && !canStand(transform.position + knock.normalized * 0.6f)) knock = Vector3.zero;
        }
        cc.Move((velocity + knock) * dt);
        knock = Vector3.MoveTowards(knock, Vector3.zero, 28f * dt);
        grounded = cc.isGrounded;

        speedNow = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
        if (grounded && !wasGrounded && velocity.y < -6f) landDip = 0.12f;
        wasGrounded = grounded;

        // footsteps drive the head bob so sound and motion agree
        if (grounded && speedNow > 0.6f)
        {
            float stride = sprint ? 2.5f : 2.0f;
            stepAccum += speedNow * dt;
            distanceWalked += speedNow * dt;
            bobPhase += speedNow * dt / stride * Mathf.PI;
            if (stepAccum >= stride) { stepAccum -= stride; if (onStep != null) onStep(sprint); }
        }
        float bobTarget = (grounded && speedNow > 0.6f) ? Mathf.Clamp01(speedNow / WalkSpeed) : 0f;
        bobAmt = Mathf.Lerp(bobAmt, bobTarget, 1f - Mathf.Exp(-10f * dt));
        landDip = Mathf.Lerp(landDip, 0f, 1f - Mathf.Exp(-9f * dt));

        float strafe = controlEnabled ? move.x : 0f;
        roll = Mathf.Lerp(roll, -strafe * 1.6f, 1f - Mathf.Exp(-8f * dt));
        fov = Mathf.Lerp(fov, (sprint && speedNow > WalkSpeed + 0.5f) ? SprintFov : BaseFov, 1f - Mathf.Exp(-6f * dt));
        float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        kickPitch = Mathf.Lerp(kickPitch, 0f, 1f - Mathf.Exp(-7f * udt));
        kickRoll = Mathf.Lerp(kickRoll, 0f, 1f - Mathf.Exp(-6f * udt));
        kickFov = Mathf.Lerp(kickFov, 0f, 1f - Mathf.Exp(-9f * udt));
        zoomNow = Mathf.Lerp(zoomNow, zoom, 1f - Mathf.Exp(-5f * udt));
        cam.fieldOfView = fov + kickFov - zoomNow;

        // shake: trauma squared, on unscaled time, small rotation only
        trauma = Mathf.Max(0f, trauma - 1.4f * Time.unscaledDeltaTime);
        float sh = trauma * trauma;
        float t = Time.unscaledTime * 28f;
        Vector3 shakeRot = new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, Mathf.PerlinNoise(t, 7.7f) - 0.5f, Mathf.PerlinNoise(t, 13.1f) - 0.5f) * (sh * 5.5f);

        float bobY = Mathf.Abs(Mathf.Sin(bobPhase)) * 0.055f * bobAmt - 0.0275f * bobAmt;
        float bobX = Mathf.Sin(bobPhase) * 0.03f * bobAmt;
        pivot.localPosition = new Vector3(bobX, EyeHeight + bobY - landDip, 0);
        pivot.localRotation = Quaternion.Euler(pitch + shakeRot.x + kickPitch, shakeRot.y, roll + shakeRot.z + kickRoll);
    }

    public void AddTrauma(float amount) { trauma = Mathf.Clamp01(trauma + amount); }
}
