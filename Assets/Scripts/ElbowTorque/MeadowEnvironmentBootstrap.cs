using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads the HapticMeadow environment at runtime:
/// removes the old flat floor, keeps gameplay objects, brings in grass ground + nature.
/// </summary>
public class MeadowEnvironmentBootstrap : MonoBehaviour
{
    const string MeadowSceneName = "MeadowSource";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<MeadowEnvironmentBootstrap>() != null)
            return;
        if (GameObject.Find("MeadowEnvironment") != null || GameObject.Find("Meadow") != null)
        {
            // Prefab already placed by Editor installer — still remove old grey floor if present.
            DestroyOldFloors();
            return;
        }

        var go = new GameObject("MeadowEnvironmentBootstrap");
        go.AddComponent<MeadowEnvironmentBootstrap>();
    }

    void Start()
    {
        StartCoroutine(LoadMeadow());
    }

    IEnumerator LoadMeadow()
    {
        DestroyOldFloors();

        // Already have meadow from a previous load.
        if (GameObject.Find("MeadowEnvironment") != null || GameObject.Find("Meadow") != null)
            yield break;

        // Ensure the meadow scene is in Build Settings (Editor installer / package import).
        if (!Application.CanStreamedLevelBeLoaded(MeadowSceneName))
        {
            Debug.LogWarning(
                $"[Meadow] Scene '{MeadowSceneName}' is not in Build Settings. " +
                "Use menu HapticFeedback/Install Meadow Environment, or add Assets/Meadow/MeadowSource.unity to Build Settings.");
            // Fallback: at least keep the grass-tinted floor if GrassFloor exists.
            yield break;
        }

        var op = SceneManager.LoadSceneAsync(MeadowSceneName, LoadSceneMode.Additive);
        if (op == null)
        {
            Debug.LogError("[Meadow] Failed to start additive load of MeadowSource.");
            yield break;
        }

        while (!op.isDone)
            yield return null;

        var meadowScene = SceneManager.GetSceneByName(MeadowSceneName);
        if (!meadowScene.IsValid() || !meadowScene.isLoaded)
        {
            Debug.LogError("[Meadow] MeadowSource scene did not load.");
            yield break;
        }

        var mainScene = SceneManager.GetActiveScene();
        GameObject meadowRoot = null;

        foreach (var root in meadowScene.GetRootGameObjects())
        {
            if (root == null)
                continue;

            if (root.name == "Meadow")
            {
                meadowRoot = root;
                continue;
            }

            // Drop duplicate camera / light / volume from the packaged scene.
            Destroy(root);
        }

        if (meadowRoot == null)
        {
            Debug.LogError("[Meadow] No 'Meadow' root in MeadowSource.");
            yield return SceneManager.UnloadSceneAsync(meadowScene);
            yield break;
        }

        // Strip packaged placeholders — SampleScene already has real gameplay props.
        StripChild(meadowRoot.transform, "Props");
        StripChild(meadowRoot.transform, "Workbench");
        StripChild(meadowRoot.transform, "PlayerSpawn");

        meadowRoot.name = "MeadowEnvironment";
        SceneManager.MoveGameObjectToScene(meadowRoot, mainScene);

        // Second pass: old floor may have been recreated; ensure it's gone.
        DestroyOldFloors();

        yield return SceneManager.UnloadSceneAsync(meadowScene);
        Debug.Log("[Meadow] Meadow environment active (grass ground + nature).");
        Destroy(gameObject);
    }

    static void DestroyOldFloors()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.parent != null)
                continue;
            string n = t.name;
            if (n == "Plane" || n == "GrassFloor" || n == "OldFloor")
            {
                Debug.Log($"[Meadow] Removing old floor '{n}'.");
                Destroy(t.gameObject);
            }
        }
    }

    static void StripChild(Transform parent, string childName)
    {
        var child = parent.Find(childName);
        if (child != null)
            Destroy(child.gameObject);
    }
}
