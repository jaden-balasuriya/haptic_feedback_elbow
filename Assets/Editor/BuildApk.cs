using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildApk
{
    const string ApkPath = "Builds/HapticFeedbackArm.apk";

    [MenuItem("Build/Android APK")]
    public static void BuildAndroidApk()
    {
        Directory.CreateDirectory("Builds");

        var opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            options = BuildOptions.StrictMode
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[BuildApk] Failed: {report.summary.result}");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[BuildApk] OK → {Path.GetFullPath(ApkPath)} ({report.summary.totalSize} bytes)");
        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }
}
