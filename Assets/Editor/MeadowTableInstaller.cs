#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Replaces the old brown cube Table in SampleScene with the Furniture Pack
/// table from HapticMeadow.unitypackage, and saves a Resources prefab for builds.
/// </summary>
[InitializeOnLoad]
public static class MeadowTableInstaller
{
    const string TableFbxPath =
        "Assets/Furniture Pack - March 2019-20261115T081521Z-1-001/Furniture Pack - March 2019/FBX/Table.fbx";
    const string ResourcesPrefabPath = "Assets/Resources/MeadowTable.prefab";
    const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    const string SessionFlag = "HapticMeadow.TableReplaced.v4";

    // Mesh AABB after FBX axis fix (matches MeadowSource collider size).
    static readonly Vector3 MeshLocalSize = new Vector3(2.7802258f, 0.8244294f, 1.4183017f);
    static readonly Vector3 MeshLocalCenter = new Vector3(0f, 0.4122147f, 0f);

    // Old cube Table: scale (1.2, 1.15, 0.5) centered at (0, 0.575, 0.55).
    static readonly Vector3 OldTableWorldSize = new Vector3(1.2f, 1.15f, 0.5f);
    static readonly Vector3 OldTableCenter = new Vector3(0f, 0.575f, 0.55f);

    static Vector3 TableLocalScale => new Vector3(
        OldTableWorldSize.x / MeshLocalSize.x,
        OldTableWorldSize.y / MeshLocalSize.y,
        OldTableWorldSize.z / MeshLocalSize.z);

    static MeadowTableInstaller()
    {
        EditorApplication.delayCall += TryAutoInstall;
    }

    static void TryAutoInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (SessionState.GetBool(SessionFlag, false))
            return;
        if (!File.Exists(TableFbxPath))
            return;

        Debug.Log("[MeadowTable] Auto-replacing cube table with furniture pack table…");
        if (InstallInternal())
            SessionState.SetBool(SessionFlag, true);
    }

    [MenuItem("HapticFeedback/Replace Table With Meadow Furniture")]
    public static void InstallFromMenu()
    {
        SessionState.SetBool(SessionFlag, false);
        if (InstallInternal())
            SessionState.SetBool(SessionFlag, true);
    }

    /// <summary>Batchmode: Unity -batchmode -executeMethod MeadowTableInstaller.Install -quit</summary>
    public static void Install()
    {
        bool ok = InstallInternal();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool InstallInternal()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(TableFbxPath);
        if (fbx == null)
        {
            Debug.LogError($"[MeadowTable] Missing FBX at {TableFbxPath}");
            return false;
        }

        if (!CreateResourcesPrefab(fbx))
            return false;

        if (!PatchSampleScene())
            return false;

        AssetDatabase.SaveAssets();
        Debug.Log("[MeadowTable] SampleScene table replaced with meadow furniture table.");
        return true;
    }

    static bool CreateResourcesPrefab(GameObject fbx)
    {
        Directory.CreateDirectory("Assets/Resources");

        var root = BuildTableHierarchy(fbx);
        PrefabUtility.SaveAsPrefabAsset(root, ResourcesPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.ImportAsset(ResourcesPrefabPath);
        Debug.Log($"[MeadowTable] Saved {ResourcesPrefabPath}");
        return AssetDatabase.LoadAssetAtPath<GameObject>(ResourcesPrefabPath) != null;
    }

    static GameObject BuildTableHierarchy(GameObject fbx)
    {
        var root = new GameObject("Table");
        root.transform.rotation = Quaternion.identity;
        root.transform.localScale = TableLocalScale;
        // Position so scaled mesh center matches old cube center.
        root.transform.position = OldTableCenter - Vector3.Scale(MeshLocalCenter, TableLocalScale);

        var col = root.AddComponent<BoxCollider>();
        col.size = MeshLocalSize;
        col.center = MeshLocalCenter;

        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.mass = 100f;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        visual.name = "TableMesh";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        // Same FBX orientation override used in MeadowSource Workbench/Table.
        visual.transform.localRotation = new Quaternion(-0.50000006f, 0.49999997f, 0.50000006f, 0.49999997f);
        visual.transform.localScale = Vector3.one;

        return root;
    }

    static bool PatchSampleScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResourcesPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[MeadowTable] Prefab missing at {ResourcesPrefabPath}");
            return false;
        }

        var scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;
            if (root.name != "Table")
                continue;

            // Only replace the old cube / previous runtime placeholder.
            Object.DestroyImmediate(root);
            Debug.Log("[MeadowTable] Removed old Table from SampleScene.");
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "Table";
        instance.transform.localScale = TableLocalScale;
        instance.transform.SetPositionAndRotation(
            OldTableCenter - Vector3.Scale(MeshLocalCenter, TableLocalScale),
            Quaternion.identity);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return true;
    }
}
#endif
