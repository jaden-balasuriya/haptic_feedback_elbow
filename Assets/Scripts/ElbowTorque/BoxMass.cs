using UnityEngine;

/// <summary>
/// Marks a grabbable box with a logical mass (kg) used for elbow torque.
/// Attach to each cube; keep in sync with Rigidbody.mass for physics feel.
/// </summary>
[DisallowMultipleComponent]
public class BoxMass : MonoBehaviour
{
    [Tooltip("Mass in kilograms used for force = mass * 9.81 (Newtons).")]
    [Min(0f)]
    public float massKg = 1f;

    [Tooltip("If a Rigidbody is present, copy massKg into Rigidbody.mass on Awake.")]
    public bool syncRigidbodyMass = true;

    void Awake()
    {
        if (!syncRigidbodyMass)
            return;

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
            rb.mass = massKg;
    }
}
