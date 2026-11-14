using TMPro;
using UnityEngine;

/// <summary>
/// Single place that owns the current elbow torque command.
/// Works without brace hardware: grab in VR → torque value for Console + on-screen HUD.
/// Later: replace PublishTorque() body with a network send to the brace.
/// </summary>
public class ElbowTorqueOutput : MonoBehaviour
{
    public static ElbowTorqueOutput Instance { get; private set; }

    [Header("Torque model (MVP)")]
    [Tooltip("Nm of elbow torque commanded per kilogram of held mass.")]
    public float torquePerKg = 0.5f;

    [Header("Display")]
    [Tooltip("Optional. If empty and Auto Create Head Locked Hud is on, a HUD is created at runtime.")]
    public TMP_Text torqueLabel;

    [Tooltip("If no Torque Label is assigned, create a head-locked TextMeshPro HUD in front of the XR camera.")]
    public bool autoCreateHeadLockedHud = true;

    [Tooltip("Meters in front of the camera for the auto HUD.")]
    public float hudDistanceMeters = 1.2f;

    [Tooltip("Local offset from camera look direction (meters).")]
    public Vector3 hudLocalOffset = new Vector3(0f, -0.15f, 0f);

    [Tooltip("How often to Debug.Log torque (seconds). 0 = every frame.")]
    [Min(0f)]
    public float logIntervalSeconds = 0.25f;

    [Header("Runtime (read-only)")]
    [SerializeField] float heldMassKg;
    [SerializeField] float currentTorqueNm;

    float _logTimer;
    Transform _hudRoot;
    Camera _hudCamera;

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

        if (torqueLabel == null && autoCreateHeadLockedHud)
            CreateHeadLockedHud();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void LateUpdate()
    {
        // Keep auto HUD locked to the headset view.
        if (_hudRoot != null)
        {
            if (_hudCamera == null)
                _hudCamera = ResolveXrCamera();

            if (_hudCamera != null)
            {
                var cam = _hudCamera.transform;
                _hudRoot.position = cam.position
                    + cam.forward * hudDistanceMeters
                    + cam.up * hudLocalOffset.y
                    + cam.right * hudLocalOffset.x;
                _hudRoot.rotation = Quaternion.LookRotation(cam.forward, cam.up);
            }
        }

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

    void CreateHeadLockedHud()
    {
        _hudCamera = ResolveXrCamera();

        var root = new GameObject("ElbowTorqueHUD");
        _hudRoot = root.transform;

        var tmpGo = new GameObject("TorqueLabel");
        tmpGo.transform.SetParent(_hudRoot, false);
        tmpGo.transform.localPosition = Vector3.zero;
        // Face the wearer (TMP forward is +Z; billboard toward camera means flip 180).
        tmpGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var tmp = tmpGo.AddComponent<TextMeshPro>();
        tmp.text = "Elbow Torque: 0.0 Nm";
        tmp.fontSize = 2.5f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.enableWordWrapping = false;

        // Reasonable default bounds so glyphs aren't clipped.
        tmp.rectTransform.sizeDelta = new Vector2(2.5f, 0.5f);

        torqueLabel = tmp;
        Debug.Log("[ElbowTorque] Auto-created head-locked HUD (no Torque Label was assigned).");
    }

    static Camera ResolveXrCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        return cameras.Length > 0 ? cameras[0] : null;
    }
}
