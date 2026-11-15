#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Imports the HapticMeadow environment into SampleScene:
/// removes the old Plane floor, keeps gameplay objects, adds the meadow + grass ground.
/// </summary>
[InitializeOnLoad]
public static class MeadowEnvironmentInstaller
{
    const string MeadowSourceScene = "Assets/Meadow/MeadowSource.unity";
    const string MeadowPrefabPath = "Assets/Meadow/MeadowEnvironment.prefab";
    const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    const string SessionFlag = "HapticMeadow.Installed.v1";

    static MeadowEnvironmentInstaller()
    {
        // Auto-run once when the Editor reloads after the package assets land.
        EditorApplication.delayCall += TryAutoInstall;
    }

    static void TryAutoInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (SessionState.GetBool(SessionFlag, false))
            return;
        if (!File.Exists(MeadowSourceScene))
            return;
        if (File.Exists(MeadowPrefabPath) && SampleSceneAlreadyHasMeadow())
        {
            SessionState.SetBool(SessionFlag, true);
            return;
        }

        Debug.Log("[Meadow] Auto-installing meadow environment…");
        if (InstallInternal())
            SessionState.SetBool(SessionFlag, true);
    }

    [MenuItem("HapticFeedback/Install Meadow Environment")]
    public static void InstallFromMenu()
    {
        SessionState.SetBool(SessionFlag, false);
        if (InstallInternal())
            SessionState.SetBool(SessionFlag, true);
    }

    /// <summary>Batchmode entry: Unity -batchmode -executeMethod MeadowEnvironmentInstaller.Install -quit</summary>
    public static void Install()
    {
        bool ok = InstallInternal();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool InstallInternal()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        if (!File.Exists(MeadowSourceScene))
        {
            Debug.LogError($"[Meadow] Missing source scene at {MeadowSourceScene}");
            return false;
        }

        if (!CreateMeadowPrefab())
            return false;

        if (!PatchSampleScene())
            return false;

        Debug.Log("[Meadow] Environment installed into SampleScene.");
        AssetDatabase.SaveAssets();
        return true;
    }

    static bool SampleSceneAlreadyHasMeadow()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != SampleScenePath)
            return AssetDatabase.LoadAssetAtPath<GameObject>(MeadowPrefabPath) != null
                && File.ReadAllText(SampleScenePath).Contains("MeadowEnvironment");
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root != null && (root.name == "MeadowEnvironment" || root.name == "Meadow"))
                return true;
        }
        return false;
    }

    static bool CreateMeadowPrefab()
    {
        var scene = EditorSceneManager.OpenScene(MeadowSourceScene, OpenSceneMode.Single);
        var meadow = FindRootByName(scene, "Meadow");
        if (meadow == null)
        {
            Debug.LogError("[Meadow] Could not find Meadow root in MeadowSource.unity");
            return false;
        }

        // Drop placeholder props / spawn — SampleScene owns the furniture table + dumbbells.
        // Workbench/Table is replaced in-scene by MeadowTableInstaller (Furniture Pack Table.fbx).
        StripChild(meadow, "Props");
        StripChild(meadow, "Workbench");
        StripChild(meadow, "PlayerSpawn");

        // Ensure grass ground plane exists and uses Meadow_Ground.
        var ground = meadow.transform.Find("Ground/Ground_Plane");
        if (ground != null)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Meadow/Meadow_Ground.mat");
            var renderer = ground.GetComponent<MeshRenderer>();
            if (renderer != null && mat != null)
                renderer.sharedMaterial = mat;

            // Large walkable grass floor.
            if (ground.localScale.x < 10f)
                ground.localScale = new Vector3(30f, 1f, 30f);
        }
        else
        {
            Debug.LogWarning("[Meadow] Ground_Plane not found under Meadow/Ground — prefab may still work.");
        }

        Directory.CreateDirectory("Assets/Meadow");
        PrefabUtility.SaveAsPrefabAsset(meadow, MeadowPrefabPath);
        Debug.Log($"[Meadow] Saved prefab {MeadowPrefabPath}");

        // Don't leave MeadowSource as the active scene.
        return true;
    }

    static bool PatchSampleScene()
    {
        var scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);

        // Remove old grey Plane floor (and any leftover named Ground_Plane at root).
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;
            string n = root.name;
            if (n == "Plane" || n == "GrassFloor" || n == "Ground_Plane" || n == "OldFloor")
            {
                Object.DestroyImmediate(root);
                Debug.Log($"[Meadow] Removed old floor object '{n}'.");
            }

            // Avoid duplicate meadow if installer is re-run.
            if (n == "Meadow" || n == "MeadowEnvironment")
                Object.DestroyImmediate(root);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MeadowPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[Meadow] Prefab missing at {MeadowPrefabPath}");
            return false;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "MeadowEnvironment";
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.SetAsFirstSibling();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Meadow] SampleScene updated with meadow environment.");
        return true;
    }

    static GameObject FindRootByName(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root != null && root.name == name)
                return root;
        }

        // Also search deeper in case hierarchy differs.
        foreach (var root in scene.GetRootGameObjects())
        {
            var t = root.GetComponentsInChildren<Transform>(true);
            foreach (var c in t)
            {
                if (c.name == name && c.parent != null && c.parent.parent == null)
                    return c.gameObject;
                if (c.name == name)
                    return c.gameObject;
            }
        }

        return null;
    }

    static void StripChild(GameObject parent, string childName)
    {
        if (parent == null)
            return;
        var child = parent.transform.Find(childName);
        if (child != null)
        {
            Object.DestroyImmediate(child.gameObject);
            Debug.Log($"[Meadow] Stripped '{childName}' from meadow prefab.");
        }
    }
}
#endif
