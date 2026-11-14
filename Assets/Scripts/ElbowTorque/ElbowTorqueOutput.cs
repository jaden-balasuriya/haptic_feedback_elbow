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
    [Tooltip("If enabled, create a head-locked TextMesh HUD in front of the XR camera.")]
    public bool autoCreateHeadLockedHud = true;

    [Tooltip("Meters in front of the camera for the auto HUD.")]
    public float hudDistanceMeters = 0.75f;

    [Tooltip("Local offset from camera (meters). Negative X = left, positive Y = up.")]
    public Vector3 hudLocalOffset = new Vector3(-0.18f, 0.1f, 0f);

    [Tooltip("If text looks mirrored, toggle this.")]
    public bool flipHudText = true;

    [Tooltip("How often to Debug.Log torque (seconds). 0 = every frame.")]
    [Min(0f)]
    public float logIntervalSeconds = 0.25f;

    [Header("Runtime (read-only)")]
    [SerializeField] float heldMassKg;
    [SerializeField] float currentTorqueNm;

    float _logTimer;
    Transform _hudRoot;
    Transform _labelTransform;
    TextMesh _hudText;
    Camera _hudCamera;
    bool _hudParentedToCamera;

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

        if (autoCreateHeadLockedHud)
            CreateHeadLockedHud();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (_hudRoot != null)
            Destroy(_hudRoot.gameObject);
    }

    void LateUpdate()
    {
        AttachHudToCameraIfNeeded();
        ApplyFacing();

        if (_hudRoot != null && !_hudParentedToCamera && _hudCamera != null)
        {
            var cam = _hudCamera.transform;
            _hudRoot.position = cam.position
                + cam.forward * hudDistanceMeters
                + cam.up * hudLocalOffset.y
                + cam.right * hudLocalOffset.x;
            // Root faces same way as camera; label flip makes glyphs readable to the wearer.
            _hudRoot.rotation = Quaternion.LookRotation(cam.forward, cam.up);
        }

        currentTorqueNm = heldMassKg * torquePerKg;
        PublishTorque(currentTorqueNm);
    }

    public void SetHeldMass(float massKg)
    {
        heldMassKg = Mathf.Max(0f, massKg);
    }

    public void ClearHeld()
    {
        heldMassKg = 0f;
    }

    /// <summary>
    /// HOOK FOR HARDWARE: swap the body of this method for a network send to the brace.
    /// </summary>
    void PublishTorque(float torqueNm)
    {
        UpdateLabel(torqueNm);
        MaybeLog(torqueNm);

        // TODO: BraceClient.SendElbowTorque(torqueNm);
    }

    void UpdateLabel(float torqueNm)
    {
        if (_hudText == null)
            return;

        _hudText.text = $"Torque: {torqueNm:F1} Nm\nMass: {heldMassKg:F1} kg";
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
        try
        {
            var root = new GameObject("ElbowTorqueHUD");
            _hudRoot = root.transform;

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Background";
            bg.transform.SetParent(_hudRoot, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            bg.transform.localScale = new Vector3(0.42f, 0.16f, 1f);
            Object.Destroy(bg.GetComponent<Collider>());
            bg.GetComponent<MeshRenderer>().material.color = Color.black;

            var labelGo = new GameObject("TorqueLabel");
            _labelTransform = labelGo.transform;
            _labelTransform.SetParent(_hudRoot, false);
            _labelTransform.localPosition = Vector3.zero;

            _hudText = labelGo.AddComponent<TextMesh>();
            _hudText.text = "Torque: 0.0 Nm\nMass: 0.0 kg";
            _hudText.fontSize = 42;
            _hudText.characterSize = 0.01f;
            _hudText.anchor = TextAnchor.MiddleCenter;
            _hudText.alignment = TextAlignment.Center;
            _hudText.color = Color.white;
            _hudText.fontStyle = FontStyle.Bold;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
                _hudText.font = font;

            ApplyFacing();
            AttachHudToCameraIfNeeded();
            Debug.Log("[ElbowTorque] Auto-created head-locked TextMesh torque HUD.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ElbowTorque] Failed to create HUD: {e}");
            _hudRoot = null;
            _hudText = null;
            _labelTransform = null;
        }
    }

    void ApplyFacing()
    {
        if (_labelTransform == null)
            return;

        // TextMesh faces +Z. With the HUD root aligned to the camera forward, a 180° yaw
        // turns the glyphs toward the wearer. flipHudText toggles if a build still looks mirrored.
        float yaw = flipHudText ? 180f : 0f;
        _labelTransform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        _labelTransform.localScale = Vector3.one;

        var bg = _hudRoot != null ? _hudRoot.Find("Background") : null;
        if (bg != null)
            bg.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    void AttachHudToCameraIfNeeded()
    {
        if (_hudRoot == null || _hudParentedToCamera)
            return;

        if (_hudCamera == null)
            _hudCamera = ResolveXrCamera();

        if (_hudCamera == null)
            return;

        _hudRoot.SetParent(_hudCamera.transform, false);
        _hudRoot.localPosition = new Vector3(hudLocalOffset.x, hudLocalOffset.y, hudDistanceMeters);
        _hudRoot.localRotation = Quaternion.identity;
        _hudRoot.localScale = Vector3.one;
        _hudParentedToCamera = true;
        Debug.Log($"[ElbowTorque] HUD attached to camera '{_hudCamera.name}'.");
    }

    static Camera ResolveXrCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        return cameras.Length > 0 ? cameras[0] : null;
    }
}
