using UnityEngine;

/// <summary>
/// Owns held-mass → elbow joint torque (N·m) for the brace.
/// Uses hand weight, upper-arm pitch (MPU), and elbow angle (pot).
/// Sent to the UNO Q as the TCP <c>forceN</c> field (numeric value = torque N·m).
/// See Docs/ELBOW_TORQUE_CALCULATION.md.
/// </summary>
public class ElbowTorqueOutput : MonoBehaviour
{
    public static ElbowTorqueOutput Instance { get; private set; }

    [Header("Torque model")]
    [Tooltip("Gravity used for hand weight W = m * g (N).")]
    public float gravityMetersPerSecSq = 9.81f;

    [Tooltip("Elbow → hand length (m). Default ≈ average adult forearm + grip.")]
    [Min(0.05f)]
    public float forearmLengthMeters = 0.27f;

    [Tooltip("Pot reading (deg) when the elbow is fully extended (straight). Flexion = this − angleDeg.")]
    public float elbowExtendedDeg = 180f;

    [Tooltip("Subtracted from MPU pitch so 0 = upper arm straight down. pitch_used = pitchDeg − offset.")]
    public float pitchDownOffsetDeg = 0f;

    [Header("Motor output (L298N)")]
    [Tooltip("Elbow torque (N·m) at which the motor reaches Max Motor Output. Above this, output stays maxed.")]
    [Min(0.01f)]
    public float torqueAtMaxMotorNm = 5f;

    [Tooltip("Max PWM duty sent to the UNO Q / L298N (0–255). Linear from 0 at τ=0 up to this at Torque At Max.")]
    [Range(0f, 255f)]
    public float maxMotorOutput = 255f;

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

    [Tooltip("How often to Debug.Log torque (seconds). 0 = every frame.")]
    [Min(0f)]
    public float logIntervalSeconds = 0.25f;

    [Header("Runtime (read-only)")]
    [SerializeField] float heldMassKg;
    [SerializeField] float currentTorqueNm;
    [SerializeField] float motorOutput;
    [SerializeField] float elbowAngleDeg = float.NaN;
    [SerializeField] float pitchDeg = float.NaN;
    [SerializeField] float forearmFromDownDeg = float.NaN;

    float _logTimer;
    Transform _hudRoot;
    Transform _labelTransform;
    TextMesh _hudText;
    Camera _hudCamera;
    bool _hudParentedToCamera;

    /// <summary>Elbow joint torque (N·m) from the held mass.</summary>
    public float CurrentTorqueNm => currentTorqueNm;

    /// <summary>Motor PWM command (0–maxMotorOutput) sent over TCP as forceN.</summary>
    public float MotorOutput => motorOutput;

    /// <summary>Legacy alias for motor PWM command (what is sent on the wire).</summary>
    public float CurrentForceN => motorOutput;

    /// <summary>Mass of the box currently held by the right hand, or 0.</summary>
    public float HeldMassKg => heldMassKg;

    /// <summary>Elbow angle (degrees) from the UNO Q potentiometer, or NaN if not yet received.</summary>
    public float ElbowAngleDeg => elbowAngleDeg;

    /// <summary>MPU6050 pitch (degrees) from the UNO Q, or NaN if not yet received.</summary>
    public float PitchDeg => pitchDeg;

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

        if (braceTcp == null)
            braceTcp = GetComponent<BraceTcpClient>() ?? BraceTcpClient.Instance;

        elbowAngleDeg = braceTcp != null && braceTcp.HasElbowAngle
            ? braceTcp.LastElbowAngleDeg
            : float.NaN;
        pitchDeg = braceTcp != null && braceTcp.HasPitch
            ? braceTcp.LastPitchDeg
            : float.NaN;

        currentTorqueNm = ComputeElbowTorqueNm(
            heldMassKg,
            pitchDeg,
            elbowAngleDeg,
            out forearmFromDownDeg);
        motorOutput = TorqueToMotorOutput(currentTorqueNm);

        PublishMotorCommand(currentTorqueNm, motorOutput);
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
    /// τ = m g L |sin(θ_forearm)| with θ_forearm = θ_upper + flexion.
    /// Returns 0 if mass is 0 or pitch/elbow sensors are missing.
    /// </summary>
    public float ComputeElbowTorqueNm(
        float massKg,
        float pitchRawDeg,
        float elbowRawDeg,
        out float forearmFromDownDegrees)
    {
        forearmFromDownDegrees = float.NaN;

        if (massKg <= 0f)
            return 0f;

        if (float.IsNaN(pitchRawDeg) || float.IsNaN(elbowRawDeg))
            return 0f;

        // θ_u: upper arm from downward vertical (0 = straight down).
        float thetaUpperDeg = pitchRawDeg - pitchDownOffsetDeg;

        // θ_flex: 0 = fully extended, 90 = right angle (pot = interior angle).
        float flexionDeg = elbowExtendedDeg - elbowRawDeg;

        // Forearm absolute angle from downward vertical (sagittal).
        float thetaForearmDeg = thetaUpperDeg + flexionDeg;
        forearmFromDownDegrees = thetaForearmDeg;

        float weightN = massKg * Mathf.Max(0f, gravityMetersPerSecSq);
        float lever = Mathf.Max(0.05f, forearmLengthMeters);
        float sinTerm = Mathf.Abs(Mathf.Sin(thetaForearmDeg * Mathf.Deg2Rad));

        // Moment arm for vertical gravity is L |sin(θ_f)| → τ in N·m.
        return weightN * lever * sinTerm;
    }

    /// <summary>
    /// Linear map: 0 at τ=0 → maxMotorOutput at torqueAtMaxMotorNm; clamp above that.
    /// </summary>
    public float TorqueToMotorOutput(float torqueNm)
    {
        float tauMax = Mathf.Max(0.01f, torqueAtMaxMotorNm);
        float maxOut = Mathf.Clamp(maxMotorOutput, 0f, 255f);
        float cmd = (torqueNm / tauMax) * maxOut;
        return Mathf.Clamp(cmd, 0f, maxOut);
    }

    void PublishMotorCommand(float torqueNm, float motorCmd)
    {
        UpdateLabel(torqueNm, motorCmd);
        MaybeLog(torqueNm, motorCmd);

        if (braceTcp == null)
            braceTcp = GetComponent<BraceTcpClient>() ?? BraceTcpClient.Instance;

        // Wire protocol forceN= carries motor PWM 0–255 for the L298N.
        braceTcp?.SendForce(motorCmd);
    }

    void UpdateLabel(float torqueNm, float motorCmd)
    {
        if (_hudText == null)
            return;

        if (braceTcp == null)
            braceTcp = GetComponent<BraceTcpClient>() ?? BraceTcpClient.Instance;

        string netStatus = braceTcp != null && braceTcp.IsConnected
            ? "WiFi: Connected"
            : "WiFi: Not connected";

        string angleLine = float.IsNaN(elbowAngleDeg)
            ? "Arm angle: --.- deg"
            : $"Arm angle: {elbowAngleDeg:F1} deg";

        string pitchLine = float.IsNaN(pitchDeg)
            ? "Pitch: --.- deg"
            : $"Pitch: {pitchDeg:F1} deg";

        float maxOut = Mathf.Max(1f, maxMotorOutput);
        float motorPct = 100f * motorCmd / maxOut;

        _hudText.text =
            $"Torque: {torqueNm:F2} N·m\nMotor: {motorPct:F0}%\nMass: {heldMassKg:F1} kg\n{netStatus}\n{angleLine}\n{pitchLine}";
    }

    void MaybeLog(float torqueNm, float motorCmd)
    {
        if (logIntervalSeconds <= 0f)
        {
            Debug.Log(
                $"[ElbowForce] τ={torqueNm:F3} N·m motor={motorCmd:F0} (mass={heldMassKg:F1} kg)");
            return;
        }

        _logTimer += Time.deltaTime;
        if (_logTimer < logIntervalSeconds)
            return;

        _logTimer = 0f;
        Debug.Log(
            $"[ElbowForce] τ={torqueNm:F3} N·m motor={motorCmd:F0} (mass={heldMassKg:F1} kg)");
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
            _hudText.text =
                "Torque: 0.00 N·m\nMotor: 0%\nMass: 0.0 kg\nWiFi: Not connected\nArm angle: --.- deg\nPitch: --.- deg";
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
