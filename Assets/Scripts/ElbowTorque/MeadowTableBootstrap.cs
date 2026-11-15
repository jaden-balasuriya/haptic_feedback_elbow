using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// At play/build time, replaces the old brown cube Table with the Furniture Pack
/// table from HapticMeadow, scaled to the old cube's world size (1.2 × 1.15 × 0.5).
/// </summary>
public class MeadowTableBootstrap : MonoBehaviour
{
    const string MeadowSceneName = "MeadowSource";
    const string ResourcesPrefabName = "MeadowTable";

    // Old cube Table: position (0, 0.575, 0.55), scale (1.2, 1.15, 0.5) on a 1m cube.
    static readonly Vector3 OldTableWorldSize = new Vector3(1.2f, 1.15f, 0.5f);
    static readonly Vector3 OldTableCenter = new Vector3(0f, 0.575f, 0.55f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<MeadowTableBootstrap>() != null)
            return;

        var go = new GameObject("MeadowTableBootstrap");
        go.AddComponent<MeadowTableBootstrap>();
    }

    void Start()
    {
        StartCoroutine(ReplaceTable());
    }

    IEnumerator ReplaceTable()
    {
        yield return null;

        DestroyOldCubeTables();

        Transform table = FindFurnitureTable();
        if (table == null)
        {
            var prefab = Resources.Load<GameObject>(ResourcesPrefabName);
            if (prefab != null)
            {
                var instance = Instantiate(prefab);
                instance.name = "Table";
                table = instance.transform;
                Debug.Log("[MeadowTable] Spawned furniture table from Resources.");
            }
        }

        if (table == null)
            yield return StartCoroutine(TryStealFromMeadow());

        table = FindFurnitureTable();
        if (table == null)
        {
            Debug.LogWarning("[MeadowTable] No furniture table found to scale.");
            Destroy(gameObject);
            yield break;
        }

        // Wait until renderers have valid bounds, then fit exactly to old cube size.
        yield return null;
        FitToOldTableSize(table);
        EnsurePhysics(table.gameObject);

        var b = GetWorldBounds(table);
        Debug.Log(
            $"[MeadowTable] Fitted to old cube size. scale={table.localScale} bounds={b.size} " +
            $"(target {OldTableWorldSize})");

        Destroy(gameObject);
    }

    IEnumerator TryStealFromMeadow()
    {
        if (!Application.CanStreamedLevelBeLoaded(MeadowSceneName))
            yield break;

        bool sceneWasLoaded = SceneManager.GetSceneByName(MeadowSceneName).isLoaded;
        if (!sceneWasLoaded)
        {
            var op = SceneManager.LoadSceneAsync(MeadowSceneName, LoadSceneMode.Additive);
            if (op == null)
                yield break;
            while (!op.isDone)
                yield return null;
        }

        var meadowScene = SceneManager.GetSceneByName(MeadowSceneName);
        foreach (var root in meadowScene.GetRootGameObjects())
        {
            if (root == null)
                continue;

            Transform workbench = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Workbench")
                {
                    workbench = t;
                    break;
                }
            }

            if (workbench == null)
                continue;

            var stolen = workbench.Find("Table");
            if (stolen == null)
                continue;

            stolen.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(stolen.gameObject, SceneManager.GetActiveScene());
            stolen.name = "Table";
            Object.Destroy(workbench.gameObject);
            Debug.Log("[MeadowTable] Adopted furniture table from MeadowSource.");
            break;
        }

        if (!sceneWasLoaded && meadowScene.isLoaded
            && GameObject.Find("MeadowEnvironment") == null
            && GameObject.Find("Meadow") == null)
        {
            yield return SceneManager.UnloadSceneAsync(meadowScene);
        }
    }

    static Transform FindFurnitureTable()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null)
                continue;
            if (t.name != "Table" && t.name != "MeadowTable")
                continue;
            if (IsOldCubeTable(t.gameObject))
                continue;
            if (t.childCount > 0 || HasNonCubeMesh(t.gameObject))
                return t;
        }

        return null;
    }

    static void DestroyOldCubeTables()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null || t.parent != null)
                continue;
            if (t.name != "Table")
                continue;
            if (!IsOldCubeTable(t.gameObject))
                continue;
            Debug.Log("[MeadowTable] Removing old cube Table.");
            Object.Destroy(t.gameObject);
        }
    }

    static bool IsOldCubeTable(GameObject go)
    {
        if (go == null || go.transform.childCount > 0)
            return false;
        var mf = go.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Cube";
    }

    static bool HasNonCubeMesh(GameObject go)
    {
        var mf = go.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null && mf.sharedMesh.name != "Cube";
    }

    /// <summary>
    /// Scales and positions the furniture table so its rendered world AABB matches
    /// the old brown cube (1.2 × 1.15 × 0.5 centered at y=0.575).
    /// </summary>
    static void FitToOldTableSize(Transform table)
    {
        table.SetParent(null, true);
        table.name = "Table";
        table.rotation = Quaternion.identity;

        // Start from uniform scale so bounds measurement is stable.
        table.localScale = Vector3.one;
        table.position = OldTableCenter;

        Bounds local = GetLocalMeshBounds(table);
        if (local.size.x < 1e-4f || local.size.y < 1e-4f || local.size.z < 1e-4f)
        {
            // Fallback: previous collider-based scale.
            table.localScale = new Vector3(
                OldTableWorldSize.x / 2.7802258f,
                OldTableWorldSize.y / 0.8244294f,
                OldTableWorldSize.z / 1.4183017f);
            table.position = new Vector3(0f, 0f, 0.55f);
            Debug.LogWarning("[MeadowTable] Mesh bounds empty — used collider fallback scale.");
            return;
        }

        table.localScale = new Vector3(
            OldTableWorldSize.x / local.size.x,
            OldTableWorldSize.y / local.size.y,
            OldTableWorldSize.z / local.size.z);

        // Center the scaled mesh on the old cube center.
        Bounds world = GetWorldBounds(table);
        table.position += OldTableCenter - world.center;
    }

    static Bounds GetLocalMeshBounds(Transform table)
    {
        var filters = table.GetComponentsInChildren<MeshFilter>(true);
        bool any = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var mf in filters)
        {
            if (mf == null || mf.sharedMesh == null)
                continue;
            // Mesh bounds are in mesh-local space; transform into table-local via relative TRS at scale=1.
            Matrix4x4 toTable = table.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Bounds mb = mf.sharedMesh.bounds;
            Vector3[] corners =
            {
                mb.min,
                new Vector3(mb.min.x, mb.min.y, mb.max.z),
                new Vector3(mb.min.x, mb.max.y, mb.min.z),
                new Vector3(mb.min.x, mb.max.y, mb.max.z),
                new Vector3(mb.max.x, mb.min.y, mb.min.z),
                new Vector3(mb.max.x, mb.min.y, mb.max.z),
                new Vector3(mb.max.x, mb.max.y, mb.min.z),
                mb.max,
            };
            foreach (var c in corners)
            {
                Vector3 p = toTable.MultiplyPoint3x4(c);
                if (!any)
                {
                    b = new Bounds(p, Vector3.zero);
                    any = true;
                }
                else
                {
                    b.Encapsulate(p);
                }
            }
        }

        return b;
    }

    static Bounds GetWorldBounds(Transform table)
    {
        var renderers = table.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return new Bounds(table.position, Vector3.zero);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static void EnsurePhysics(GameObject table)
    {
        // Collider in local space sized so that after localScale it matches old cube world size.
        var col = table.GetComponent<BoxCollider>();
        if (col == null)
            col = table.AddComponent<BoxCollider>();

        Vector3 s = table.transform.localScale;
        if (Mathf.Abs(s.x) < 1e-6f || Mathf.Abs(s.y) < 1e-6f || Mathf.Abs(s.z) < 1e-6f)
            s = Vector3.one;

        col.size = new Vector3(
            OldTableWorldSize.x / Mathf.Abs(s.x),
            OldTableWorldSize.y / Mathf.Abs(s.y),
            OldTableWorldSize.z / Mathf.Abs(s.z));
        col.center = Vector3.zero;

        // Align collider center with mesh center in local space.
        Bounds local = GetLocalMeshBounds(table.transform);
        if (local.size.sqrMagnitude > 1e-6f)
            col.center = local.center;

        var rb = table.GetComponent<Rigidbody>();
        if (rb == null)
            rb = table.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.mass = 100f;
        rb.constraints = RigidbodyConstraints.FreezeAll;
    }
}
