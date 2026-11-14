using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Editor helpers: one-time project setup, import rules and the Windows build.</summary>
public static class ProjectTools
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Remaster/Setup Project")]
    public static void Setup()
    {
        if (!File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        PlayerSettings.productName = "REMASTER";
        PlayerSettings.runInBackground = true;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.visibleInBackground = true;
        PlayerSettings.SplashScreen.showUnityLogo = true;
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        AssetDatabase.SaveAssets();
        Debug.Log("[REMASTER] project setup done");
    }

    [MenuItem("Remaster/Open Main Scene")]
    public static void OpenMain()
    {
        if (!File.Exists(ScenePath)) Setup();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    [MenuItem("Remaster/Build Windows")]
    public static void BuildWindows()
    {
        Setup();
        string outDir = "Builds/Windows";
        Directory.CreateDirectory(outDir);
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outDir + "/Remaster.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log("[REMASTER] build result: " + s.result + " size " + (s.totalSize / (1024 * 1024)) + " MB, errors " + s.totalErrors + ", time " + s.totalTime);
        if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
    }
}

/// <summary>Import rules: audio stays uncompressed so the eight music layers loop and stay in sync sample-for-sample.</summary>
public class RemasterImportRules : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (!assetPath.Contains("Resources/Audio")) return;
        var imp = (AudioImporter)assetImporter;
        var s = imp.defaultSampleSettings;
        s.loadType = AudioClipLoadType.DecompressOnLoad;
        s.compressionFormat = AudioCompressionFormat.PCM;
        s.preloadAudioData = true;
        imp.defaultSampleSettings = s;
        imp.forceToMono = false;
        imp.loadInBackground = false;
    }

    void OnPreprocessModel()
    {
        if (!assetPath.Contains("Resources/Models")) return;
        var imp = (ModelImporter)assetImporter;
        imp.importCameras = false;
        imp.importLights = false;
        imp.importAnimation = false;
        imp.animationType = ModelImporterAnimationType.None;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        imp.isReadable = false;
    }
}
