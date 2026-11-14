using UnityEngine;

/// <summary>
/// Optional Inspector wiring helper. Hook XRGrabInteractable (or Meta grab)
/// Select Entered / Select Exited UnityEvents to OnGrabbed / OnReleased.
/// Prefer RightHandGrabTorqueBridge on the right interactor when using XRIT.
/// </summary>
[RequireComponent(typeof(BoxMass))]
public class BoxGrabTorqueEvents : MonoBehaviour
{
    BoxMass _boxMass;

    void Awake()
    {
        _boxMass = GetComponent<BoxMass>();
    }

    /// <summary>Wire to Select Entered (only if this box is grabbed by the right hand).</summary>
    public void OnGrabbed()
    {
        var output = ElbowTorqueOutput.Instance;
        if (output == null)
        {
            Debug.LogWarning("[ElbowTorque] No ElbowTorqueOutput in scene.");
            return;
        }

        output.SetHeldMass(_boxMass.massKg);
        Debug.Log($"[ElbowTorque] Grabbed '{name}' mass={_boxMass.massKg:F1} kg");
    }

    /// <summary>Wire to Select Exited.</summary>
    public void OnReleased()
    {
        ElbowTorqueOutput.Instance?.ClearHeld();
        Debug.Log("[ElbowTorque] Released — torque = 0");
    }
}
