using UnityEngine;

/// <summary>
/// Owns the current held-mass → force command (Newtons).
/// Grab in VR → force on HUD / Console; Quest builds also send over Wi-Fi via <see cref="BraceTcpClient"/>.
/// </summary>
public class ElbowTorqueOutput : MonoBehaviour
{
    public static ElbowTorqueOutput Instance { get; private set; }

    [Header("Force model (MVP)")]
    [Tooltip("Gravity used to convert held mass (kg) to weight force (N). F = m * g.")]
    public float gravityMetersPerSecSq = 9.81f;

    [Header("Brace network (UNO Q hotspot)")]
    [Tooltip("Assign BraceTcpClient on this GameObject (or leave empty to use GetComponent / Instance).")]
    public BraceTcpClient braceTcp;

    [Header("Display")]
    [Tooltip("If enabled, create a head-locked TextMesh HUD in front of the XR camera.")]
    public bool autoCreateHeadLockedHud = true;

    [Tooltip("Meters in front of the camera for the auto HUD.")]
    public float hudDistanceMeters = 0.75f;

    [Tooltip("Local offset from camera (meters). Negative X = left, positive Y = up.")]
    public Vector3 hudLocalOffset = new Vector3(-0.18f, 0.1f, 0f);

    [Tooltip("If text looks mirrored, toggle this.")]
    public bool flipHudText = false;

    [Tooltip("How often to Debug.Log force (seconds). 0 = every frame.")]
    [Min(0f)]
    public float logIntervalSeconds = 0.25f;

    [Header("Runtime (read-only)")]
    [SerializeField] float heldMassKg;
    [SerializeField] float currentForceN;

    float _logTimer;
    Transform _hudRoot;
    Transform _labelTransform;
    TextMesh _hudText;
    Camera _hudCamera;
    bool _hudParentedToCamera;

    /// <summary>Current commanded force in Newtons (weight of held mass).</summary>
    public float CurrentForceN => currentForceN;

    /// <summary>Mass of the box currently held by the right hand, or 0.</summary>
    public float HeldMassKg => heldMassKg;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ElbowForce] Multiple ElbowTorqueOutput instances; keeping the first.");
            return;
        }

        Instance = this;

        if (braceTcp == null)
            braceTcp = GetComponent<BraceTcpClient>() ?? BraceTcpClient.Instance;

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
            _hudRoot.rotation = Quaternion.LookRotation(cam.forward, cam.up);
        }

        currentForceN = heldMassKg * Mathf.Max(0f, gravityMetersPerSecSq);
        PublishForce(currentForceN);
    }

    public void SetHeldMass(float massKg)
    {
        heldMassKg = Mathf.Max(0f, massKg);
    }

    public void ClearHeld()
    {
        heldMassKg = 0f;
    }

    void PublishForce(float forceN)
    {
        UpdateLabel(forceN);
        MaybeLog(forceN);

        if (braceTcp == null)
            braceTcp = GetComponent<BraceTcpClient>() ?? BraceTcpClient.Instance;

        braceTcp?.SendForce(forceN);
    }

    void UpdateLabel(float forceN)
    {
        if (_hudText == null)
            return;

        if (braceTcp == null)
            braceTcp = GetComponent<BraceTcpClient>() ?? BraceTcpClient.Instance;

        string netStatus = braceTcp != null && braceTcp.IsConnected
            ? "WiFi: Connected"
            : "WiFi: Not connected";

        _hudText.text = $"Force: {forceN:F1} N\nMass: {heldMassKg:F1} kg\n{netStatus}";
    }

    void MaybeLog(float forceN)
    {
        if (logIntervalSeconds <= 0f)
        {
            Debug.Log($"[ElbowForce] {forceN:F3} N (mass={heldMassKg:F1} kg)");
            return;
        }

        _logTimer += Time.deltaTime;
        if (_logTimer < logIntervalSeconds)
            return;

        _logTimer = 0f;
        Debug.Log($"[ElbowForce] {forceN:F3} N (mass={heldMassKg:F1} kg)");
    }

    void CreateHeadLockedHud()
    {
        try
        {
            var root = new GameObject("ElbowForceHUD");
            _hudRoot = root.transform;

            var labelGo = new GameObject("ForceLabel");
            _labelTransform = labelGo.transform;
            _labelTransform.SetParent(_hudRoot, false);
            _labelTransform.localPosition = Vector3.zero;

            _hudText = labelGo.AddComponent<TextMesh>();
            _hudText.text = "Force: 0.0 N\nMass: 0.0 kg\nWiFi: Not connected";
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
            Debug.Log("[ElbowForce] Auto-created head-locked TextMesh force HUD.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ElbowForce] Failed to create HUD: {e}");
            _hudRoot = null;
            _hudText = null;
            _labelTransform = null;
        }
    }

    void ApplyFacing()
    {
        if (_labelTransform == null)
            return;

        float yaw = flipHudText ? 180f : 0f;
        _labelTransform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        _labelTransform.localScale = Vector3.one;
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
        Debug.Log($"[ElbowForce] HUD attached to camera '{_hudCamera.name}'.");
    }

    static Camera ResolveXrCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        return cameras.Length > 0 ? cameras[0] : null;
    }
}
