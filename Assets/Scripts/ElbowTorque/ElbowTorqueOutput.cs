using TMPro;
using UnityEngine;

/// <summary>
/// Single place that owns the current elbow torque command.
/// Later: replace LogAndDisplay() / PublishTorque() with a network send to the brace.
/// </summary>
public class ElbowTorqueOutput : MonoBehaviour
{
    public static ElbowTorqueOutput Instance { get; private set; }

    [Header("Torque model (MVP)")]
    [Tooltip("Nm of elbow torque commanded per kilogram of held mass.")]
    public float torquePerKg = 0.5f;

    [Header("Display")]
    [Tooltip("Floating world-space TextMeshPro label (e.g. 'Elbow Torque: 4.2 Nm').")]
    public TMP_Text torqueLabel;

    [Tooltip("How often to Debug.Log torque (seconds). 0 = every frame.")]
    [Min(0f)]
    public float logIntervalSeconds = 0.25f;

    [Header("Runtime (read-only)")]
    [SerializeField] float heldMassKg;
    [SerializeField] float currentTorqueNm;

    float _logTimer;

    /// <summary>Current commanded elbow torque in Newton-metres.</summary>
    public float CurrentTorqueNm => currentTorqueNm;

    /// <summary>Mass of the box currently held by the right hand, or 0.</summary>
    public float HeldMassKg => heldMassKg;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ElbowTorque] Multiple ElbowTorqueOutput instances; keeping the first.");
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        // torque = mass * torquePerKg  (add sin(theta) later if needed)
        currentTorqueNm = heldMassKg * torquePerKg;

        PublishTorque(currentTorqueNm);
    }

    /// <summary>
    /// Call when the right hand starts holding a box (or mass changes).
    /// </summary>
    public void SetHeldMass(float massKg)
    {
        heldMassKg = Mathf.Max(0f, massKg);
    }

    /// <summary>
    /// Call when the right hand releases / holds nothing.
    /// </summary>
    public void ClearHeld()
    {
        heldMassKg = 0f;
    }

    /// <summary>
    /// HOOK FOR HARDWARE: swap the body of this method for a network send to the brace.
    /// Called every frame with the latest torque (Nm).
    /// </summary>
    void PublishTorque(float torqueNm)
    {
        UpdateLabel(torqueNm);
        MaybeLog(torqueNm);

        // TODO: replace Debug.Log / UI with brace network send, e.g.:
        // BraceClient.SendElbowTorque(torqueNm);
    }

    void UpdateLabel(float torqueNm)
    {
        if (torqueLabel == null)
            return;

        torqueLabel.text = $"Elbow Torque: {torqueNm:F1} Nm";
    }

    void MaybeLog(float torqueNm)
    {
        if (logIntervalSeconds <= 0f)
        {
            Debug.Log($"[ElbowTorque] {torqueNm:F3} Nm (mass={heldMassKg:F1} kg)");
            return;
        }

        _logTimer += Time.deltaTime;
        if (_logTimer < logIntervalSeconds)
            return;

        _logTimer = 0f;
        Debug.Log($"[ElbowTorque] {torqueNm:F3} Nm (mass={heldMassKg:F1} kg)");
    }
}
