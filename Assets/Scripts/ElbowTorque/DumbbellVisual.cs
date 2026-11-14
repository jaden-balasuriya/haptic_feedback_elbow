using UnityEngine;

/// <summary>
/// Optional helper. Dumbbell meshes are authored in the scene as child cylinders.
/// Left enabled only if you want runtime rebuild; default is off.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxMass))]
public class DumbbellVisual : MonoBehaviour
{
    void Awake()
    {
        // Scene already contains Handle / Plate_L / Plate_R children.
        // Keep host cube mesh hidden if present.
        var hostRenderer = GetComponent<MeshRenderer>();
        if (hostRenderer != null)
            hostRenderer.enabled = false;
    }
}
