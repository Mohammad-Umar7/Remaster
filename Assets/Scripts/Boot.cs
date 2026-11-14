using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Starts the game in whatever scene is loaded. Nothing has to be placed in the scene by hand.</summary>
public static class Boot
{
    public static bool skipIntro;
    public static int startRoom;
    public static bool autopilot;
    public static string shotDir;
    static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        hooked = false;
        skipIntro = false; startRoom = 0; autopilot = false; shotDir = null;
        var argList = new System.Collections.Generic.List<string>(System.Environment.GetCommandLineArgs());
#if UNITY_EDITOR
        // in the Editor, test flags come from an EditorPref so play mode can be driven from scripts
        string extra = UnityEditor.EditorPrefs.GetString("RemasterArgs", "");
        if (!string.IsNullOrEmpty(extra)) argList.AddRange(extra.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
#endif
        var args = argList.ToArray();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-skipintro") skipIntro = true;
            if (args[i] == "-autopilot") { autopilot = true; skipIntro = true; }
            if (args[i] == "-room" && i + 1 < args.Length) { int r; if (int.TryParse(args[i + 1], out r)) startRoom = Mathf.Clamp(r - 1, 0, Maps.Defs.Length - 1); skipIntro = true; }
            if (args[i] == "-shots" && i + 1 < args.Length) shotDir = args[i + 1];
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (!hooked) { SceneManager.sceneLoaded += (s, m) => Spawn(); hooked = true; }
        Spawn();
    }

    static void Spawn()
    {
        if (Object.FindAnyObjectByType<GameManager>() != null) return;
        // the scene may contain a default camera and light: we do not use them
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.Destroy(c.gameObject);
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) Object.Destroy(l.gameObject);
        var go = new GameObject("Game");
        go.AddComponent<GameManager>();
        if (autopilot) go.AddComponent<Autopilot>();
    }
}
