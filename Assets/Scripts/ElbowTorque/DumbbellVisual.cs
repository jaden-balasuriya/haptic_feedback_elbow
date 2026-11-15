using UnityEngine;

/// <summary>
/// Hides the host cube mesh and tints Handle / Plate children.
/// Scene startup keeps only the purple 10 kg dumbbell.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxMass))]
public class DumbbellVisual : MonoBehaviour
{
    [Tooltip("Tint applied to handle + plates. If clear, color is chosen from BoxMass.")]
    public Color overrideColor = default;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnableAllInScene()
    {
        // Keep only the purple 10 kg dumbbell; remove 1 kg / 3 kg (and any other masses).
        var boxes = FindObjectsByType<BoxMass>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var box in boxes)
        {
            if (box == null)
                continue;
            bool isDumbbell = box.GetComponent<DumbbellVisual>() != null
                || box.name.IndexOf("Dumbbell", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isDumbbell)
                continue;
            // Purple = 10 kg.
            if (box.massKg >= 9f)
                continue;
            Object.Destroy(box.gameObject);
        }

        // SampleScene ships with DumbbellVisual disabled — turn on and tint.
        var visuals = FindObjectsByType<DumbbellVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var v in visuals)
        {
            if (v == null)
                continue;
            if (!v.enabled)
                v.enabled = true;
            v.ApplyFromMass();
        }
    }

    void Awake()
    {
        ApplyFromMass();
    }

    public void ApplyFromMass()
    {
        var hostRenderer = GetComponent<MeshRenderer>();
        if (hostRenderer != null)
            hostRenderer.enabled = false;

        Color color = overrideColor.a > 0.01f ? overrideColor : ColorForMass();
        ApplyColor(color);
    }

    Color ColorForMass()
    {
        var box = GetComponent<BoxMass>();
        float kg = box != null ? box.massKg : 1f;

        // Distinct, saturated colors per dumbbell.
        if (kg <= 1.5f)
            return new Color(0.15f, 0.75f, 0.35f); // green — 1 kg
        if (kg <= 5f)
            return new Color(0.95f, 0.65f, 0.1f); // amber — 3 kg
        return new Color(0.55f, 0.2f, 0.85f); // purple — 10 kg
    }

    void ApplyColor(Color color)
    {
        Material mat = MakeLit(color);
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.gameObject == gameObject)
                continue;
            r.sharedMaterial = mat;
        }
    }

    static Material MakeLit(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
        return mat;
    }
}
