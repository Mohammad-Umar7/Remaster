using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>One place for all input (new Input System). The test autopilot can override it.</summary>
public static class GameInput
{
    public static bool overrideActive;
    public static Vector2 overrideMove;
    public static bool overrideSprint;
    public static float? overrideYaw;      // absolute yaw the autopilot wants
    public static bool overrideAnyKey;

    public static float mouseSensitivity = 0.09f;
    public static float stickLookSpeed = 150f;

    static Keyboard Kb => Keyboard.current;
    static Mouse Ms => Mouse.current;
    static Gamepad Gp => Gamepad.current;

    public static Vector2 Move
    {
        get
        {
            if (overrideActive) return overrideMove;
            Vector2 v = Vector2.zero;
            var k = Kb;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed) v.y += 1;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) v.y -= 1;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) v.x += 1;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) v.x -= 1;
            }
            var g = Gp;
            if (g != null)
            {
                Vector2 s = g.leftStick.ReadValue();
                if (s.sqrMagnitude > 0.04f) v += s;
            }
            return Vector2.ClampMagnitude(v, 1f);
        }
    }

    /// <summary>Look delta in degrees for this frame (x = yaw, y = pitch up).</summary>
    public static Vector2 Look
    {
        get
        {
            if (overrideActive) return Vector2.zero;
            Vector2 v = Vector2.zero;
            var m = Ms;
            if (m != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = m.delta.ReadValue();
                v += d * mouseSensitivity;
            }
            var g = Gp;
            if (g != null)
            {
                Vector2 s = g.rightStick.ReadValue();
                if (s.sqrMagnitude > 0.03f) v += s * stickLookSpeed * Time.unscaledDeltaTime;
            }
            return v;
        }
    }

    public static bool Sprint
    {
        get
        {
            if (overrideActive) return overrideSprint;
            var k = Kb; var g = Gp;
            return (k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed))
                || (g != null && (g.leftStickButton.isPressed || g.rightTrigger.ReadValue() > 0.4f));
        }
    }

    public static bool PausePressed
    {
        get
        {
            var k = Kb; var g = Gp;
            return (k != null && (k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame))
                || (g != null && g.startButton.wasPressedThisFrame);
        }
    }

    public static bool RestartPressed
    {
        get { var k = Kb; var g = Gp; return (k != null && k.rKey.wasPressedThisFrame) || (g != null && g.selectButton.wasPressedThisFrame); }
    }

    public static bool QuitPressed
    {
        get { var k = Kb; return k != null && k.qKey.wasPressedThisFrame; }
    }

    public static bool ConfirmPressed
    {
        get
        {
            var k = Kb; var g = Gp;
            return (k != null && (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame))
                || (g != null && g.buttonSouth.wasPressedThisFrame);
        }
    }

    public static bool AnyKeyPressed
    {
        get
        {
            if (overrideAnyKey) { overrideAnyKey = false; return true; }
            var k = Kb; var m = Ms; var g = Gp;
            if (k != null && k.anyKey.wasPressedThisFrame) return true;
            if (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame)) return true;
            if (g != null && (g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame)) return true;
            return false;
        }
    }

    public static bool ClickPressed
    {
        get { var m = Ms; return m != null && m.leftButton.wasPressedThisFrame; }
    }

    /// <summary>Returns 1..5 when a room-jump number key was pressed this frame, else 0.</summary>
    public static int NumberPressed
    {
        get
        {
            var k = Kb;
            if (k == null) return 0;
            if (k.digit1Key.wasPressedThisFrame) return 1;
            if (k.digit2Key.wasPressedThisFrame) return 2;
            if (k.digit3Key.wasPressedThisFrame) return 3;
            if (k.digit4Key.wasPressedThisFrame) return 4;
            if (k.digit5Key.wasPressedThisFrame) return 5;
            return 0;
        }
    }

    public static bool TabPressed { get { var k = Kb; var g = Gp; return (k != null && k.tabKey.wasPressedThisFrame) || (g != null && g.buttonNorth.wasPressedThisFrame); } }
    public static bool F1Pressed { get { var k = Kb; return k != null && k.f1Key.wasPressedThisFrame; } }
    public static bool F11Pressed { get { var k = Kb; return k != null && k.f11Key.wasPressedThisFrame; } }
}
