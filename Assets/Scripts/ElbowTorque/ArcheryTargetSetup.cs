using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a small archery gallery in front of the player for bow practice.
/// Targets flash and tip when hit; reset restores them.
/// </summary>
public class ArcheryTargetSetup : MonoBehaviour
{
    [Header("Gallery layout (world)")]
    [Tooltip("Center of the target line — out in front of the play area.")]
    public Vector3 galleryCenter = new Vector3(0f, 0f, 5.0f);

    [Tooltip("Horizontal spacing between targets (m).")]
    public float targetSpacing = 1.1f;

    [Tooltip("How many standing targets to spawn.")]
    public int targetCount = 3;

    [Header("Target size")]
    public float faceRadius = 0.32f;
    public float standHeight = 1.15f;

    [Header("Colors")]
    public Color standColor = new Color(0.2f, 0.2f, 0.22f);
    public Color ringWhite = new Color(0.97f, 0.97f, 0.95f);
    public Color ringRed = new Color(0.92f, 0.1f, 0.1f);
    public Color hitFlashColor = new Color(1f, 0.95f, 0.4f);

    readonly List<ArcheryTarget> _targets = new List<ArcheryTarget>();
    Material _standMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<ArcheryTargetSetup>() != null)
            return;
        // Remove leftover Hanoi / brown bow table from earlier builds.
        foreach (var hanoi in FindObjectsByType<TowerOfHanoiSetup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (hanoi != null)
                Destroy(hanoi.gameObject);
        }

        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null)
                continue;
            if (t.name == "BowTable" || t.name == "HanoiTable")
                Destroy(t.gameObject);
        }

        var go = new GameObject("ArcheryTargets");
        go.AddComponent<ArcheryTargetSetup>();
    }

    void Start()
    {
        BuildGallery();
    }

    void BuildGallery()
    {
        _standMat = MakeLit(standColor, metallic: 0.05f, smoothness: 0.25f);
        _targets.Clear();

        float startX = galleryCenter.x - (targetCount - 1) * 0.5f * targetSpacing;
        for (int i = 0; i < targetCount; i++)
        {
            float x = startX + i * targetSpacing;
            // Slight height variation so shots aren't all the same elevation.
            float faceY = standHeight + (i == 1 ? 0.15f : (i == 0 ? -0.05f : 0.05f));
            Vector3 basePos = new Vector3(x, 0f, galleryCenter.z);
            var target = SpawnTarget($"Target_{i}", basePos, faceY);
            _targets.Add(target);
        }

        Debug.Log($"[Targets] Spawned {_targets.Count} archery targets.");
    }

    ArcheryTarget SpawnTarget(string name, Vector3 basePos, float faceCenterY)
    {
        var root = new GameObject(name).transform;
        root.SetParent(transform, false);
        root.position = basePos;
        // Face the player (gallery is along +Z).
        root.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

        // Ground stake / stand post.
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "Post";
        post.transform.SetParent(root, false);
        post.transform.localPosition = new Vector3(0f, faceCenterY * 0.5f, 0f);
        post.transform.localScale = new Vector3(0.06f, faceCenterY * 0.5f, 0.06f);
        ApplyMat(post, _standMat);
        var postRb = post.AddComponent<Rigidbody>();
        postRb.isKinematic = true;
        postRb.useGravity = false;

        // Crossbar behind the face.
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Crossbar";
        bar.transform.SetParent(root, false);
        bar.transform.localPosition = new Vector3(0f, faceCenterY, 0.02f);
        bar.transform.localScale = new Vector3(faceRadius * 2.1f, 0.04f, 0.04f);
        ApplyMat(bar, _standMat);
        Object.Destroy(bar.GetComponent<Collider>());

        // Classic red/white bullseye (outer → inner).
        Color[] rings = { ringRed, ringWhite, ringRed, ringWhite, ringRed };
        float[] radii = { 1f, 0.78f, 0.56f, 0.34f, 0.16f };
        Transform faceRoot = new GameObject("Face").transform;
        faceRoot.SetParent(root, false);
        faceRoot.localPosition = new Vector3(0f, faceCenterY, -0.02f);

        for (int r = 0; r < rings.Length; r++)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = r == rings.Length - 1 ? "Bullseye" : $"Ring_{r}";
            ring.transform.SetParent(faceRoot, false);
            // Cylinder default is Y-up — rotate to face along local Z (toward player).
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float rad = faceRadius * radii[r];
            ring.transform.localScale = new Vector3(rad * 2f, 0.012f, rad * 2f);
            ring.transform.localPosition = new Vector3(0f, 0f, -0.001f * r);
            ApplyMat(ring, MakeLit(rings[r], metallic: 0f, smoothness: 0.4f));
            // Only outermost ring keeps a collider for hits.
            if (r == 0)
            {
                var col = ring.GetComponent<Collider>();
                if (col != null)
                    col.isTrigger = false;
                var ringRb = ring.AddComponent<Rigidbody>();
                ringRb.isKinematic = true;
                ringRb.useGravity = false;
            }
            else
            {
                Object.Destroy(ring.GetComponent<Collider>());
            }
        }

        var target = root.gameObject.AddComponent<ArcheryTarget>();
        target.Init(this, faceRoot, hitFlashColor);
        return target;
    }

    /// <summary>Restore all targets to upright / unhit state.</summary>
    public void ResetTargets()
    {
        foreach (var t in _targets)
        {
            if (t != null)
                t.ResetTarget();
        }
        Debug.Log("[Targets] Reset.");
    }

    static Material MakeLit(Color color, float metallic = 0f, float smoothness = 0.4f)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness"))
            mat.SetFloat("_Glossiness", smoothness);
        return mat;
    }

    static void ApplyMat(GameObject go, Material mat)
    {
        var r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null)
            r.sharedMaterial = mat;
    }
}

/// <summary>One standing archery butt — reacts to Arrow collisions.</summary>
public class ArcheryTarget : MonoBehaviour
{
    ArcheryTargetSetup _setup;
    Transform _face;
    Color _flashColor;
    bool _hit;
    Quaternion _uprightRot;
    Vector3 _uprightPos;
    readonly List<Renderer> _faceRenderers = new List<Renderer>();
    readonly List<Color> _faceColors = new List<Color>();

    public void Init(ArcheryTargetSetup setup, Transform face, Color flashColor)
    {
        _setup = setup;
        _face = face;
        _flashColor = flashColor;
        _uprightRot = transform.rotation;
        _uprightPos = transform.position;

        if (_face != null)
        {
            foreach (var r in _face.GetComponentsInChildren<Renderer>(true))
            {
                _faceRenderers.Add(r);
                var mat = r.material;
                Color c = Color.white;
                if (mat.HasProperty("_BaseColor"))
                    c = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color"))
                    c = mat.GetColor("_Color");
                _faceColors.Add(c);
            }
        }

        // Listen on all colliders in the hierarchy.
        foreach (var col in GetComponentsInChildren<Collider>(true))
        {
            var relay = col.gameObject.GetComponent<ArcheryTargetHitRelay>();
            if (relay == null)
                relay = col.gameObject.AddComponent<ArcheryTargetHitRelay>();
            relay.Owner = this;
        }
    }

    public void NotifyHit(Collision collision)
    {
        if (_hit)
            return;
        if (collision == null || collision.rigidbody == null)
            return;

        // Fired arrows are named "Arrow" and are non-kinematic.
        Transform t = collision.rigidbody.transform;
        if (t == null || t.name != "Arrow")
            return;

        _hit = true;
        FlashAndTip(collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);
        Debug.Log($"[Targets] Hit {name}!");
    }

    void FlashAndTip(Vector3 hitPoint)
    {
        // Flash rings gold briefly.
        for (int i = 0; i < _faceRenderers.Count; i++)
        {
            var r = _faceRenderers[i];
            if (r == null)
                continue;
            var mat = r.material;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", _flashColor);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", _flashColor);
        }

        // Tip away from the shot (rotate around X a bit).
        Vector3 tipAxis = transform.right;
        transform.rotation = Quaternion.AngleAxis(18f, tipAxis) * _uprightRot;

        // Stick the arrow roughly where it hit (optional cosmetic).
        // Arrow keeps its own physics — no parenting required.

        CancelInvoke(nameof(RestoreFaceColors));
        Invoke(nameof(RestoreFaceColors), 0.45f);
    }

    void RestoreFaceColors()
    {
        for (int i = 0; i < _faceRenderers.Count; i++)
        {
            var r = _faceRenderers[i];
            if (r == null)
                continue;
            var mat = r.material;
            Color c = i < _faceColors.Count ? _faceColors[i] : Color.white;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", c);
        }
    }

    public void ResetTarget()
    {
        CancelInvoke(nameof(RestoreFaceColors));
        _hit = false;
        transform.SetPositionAndRotation(_uprightPos, _uprightRot);
        RestoreFaceColors();
    }
}

/// <summary>Forwards collision events from ring/post colliders to the target root.</summary>
public class ArcheryTargetHitRelay : MonoBehaviour
{
    public ArcheryTarget Owner;

    void OnCollisionEnter(Collision collision)
    {
        Owner?.NotifyHit(collision);
    }
}
