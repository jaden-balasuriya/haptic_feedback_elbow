using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Unity-facing HC-06 / brace Bluetooth client.
/// Add to the same GameObject as <see cref="ElbowTorqueOutput"/> (or assign the reference).
/// Sends ASCII lines: "5.000\n" (Nm). Runs only on a Quest / Android device build.
/// </summary>
public class BraceBluetoothClient : MonoBehaviour
{
    public static BraceBluetoothClient Instance { get; private set; }

    [Header("Device")]
    [Tooltip("Bonded Bluetooth name as shown in Quest Settings (often HC-06).")]
    public string deviceName = "HC-06";

    [Tooltip("Optional MAC override, e.g. AA:BB:CC:DD:EE:FF. If set, name is ignored.")]
    public string macAddress = "";

    [Tooltip("Connect automatically when the scene starts (after permission).")]
    public bool autoConnectOnStart = true;

    [Header("Send rate")]
    [Tooltip("Max torque lines per second. Keep well under HC-06 / UART capacity.")]
    [Range(1f, 30f)]
    public float sendHz = 10f;

    [Tooltip("Also send immediately when torque changes by at least this much (Nm).")]
    public float sendOnChangeEpsilonNm = 0.05f;

    [Header("Runtime (read-only)")]
    [SerializeField] bool connected;
    [SerializeField] string lastError;
    [SerializeField] float lastSentTorqueNm = float.NaN;

    BraceBluetoothSpp _spp;
    float _sendInterval;
    float _sendTimer;
    float _pendingTorqueNm;
    bool _hasPending;
    bool _permissionAsked;
    bool _waitingForPermission;

    public bool IsConnected => connected;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BraceBT] Multiple BraceBluetoothClient instances; keeping the first.");
            return;
        }

        Instance = this;
        _spp = new BraceBluetoothSpp();
        _sendInterval = 1f / Mathf.Max(1f, sendHz);
    }

    void Start()
    {
        if (autoConnectOnStart)
            Connect();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        _spp?.Dispose();
        _spp = null;
        connected = false;
    }

    void Update()
    {
        connected = _spp != null && _spp.IsConnected;
        lastError = _spp?.LastError;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (_waitingForPermission && EnsureBluetoothConnectPermission())
        {
            _waitingForPermission = false;
            Connect();
        }
#endif

        if (!_hasPending || _spp == null || !connected)
            return;

        _sendTimer += Time.deltaTime;
        bool dueByTime = _sendTimer >= _sendInterval;
        bool dueByChange = float.IsNaN(lastSentTorqueNm)
            || Mathf.Abs(_pendingTorqueNm - lastSentTorqueNm) >= sendOnChangeEpsilonNm;

        if (!dueByTime && !dueByChange)
            return;

        _sendTimer = 0f;
        FlushPending();
    }

    /// <summary>Begin SPP connect to the paired HC-06. Safe to call more than once.</summary>
    public void Connect()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureBluetoothConnectPermission())
        {
            _waitingForPermission = true;
            Debug.LogWarning("[BraceBT] Waiting for BLUETOOTH_CONNECT permission");
            return;
        }

        _waitingForPermission = false;
#endif
        if (_spp == null)
            _spp = new BraceBluetoothSpp();

        if (_spp.IsConnected)
            return;

        Debug.Log($"[BraceBT] Connecting to '{deviceName}' mac='{macAddress}' ...");
        _spp.ConnectAsync(deviceName, macAddress, (ok, err) =>
        {
            if (!ok)
                Debug.LogError($"[BraceBT] Connect error: {err}");
        });
    }

    public void Disconnect()
    {
        _spp?.Disconnect();
        connected = false;
    }

    /// <summary>
    /// Queue a torque command (Nm). Flushed at <see cref="sendHz"/> or on meaningful change.
    /// </summary>
    public void SendTorque(float torqueNm)
    {
        _pendingTorqueNm = Mathf.Max(0f, torqueNm);
        _hasPending = true;
    }

    void FlushPending()
    {
        if (_spp == null || !_spp.IsConnected)
            return;

        string line = _pendingTorqueNm.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        if (_spp.WriteLine(line))
        {
            lastSentTorqueNm = _pendingTorqueNm;
            _hasPending = false;
        }
        else
        {
            connected = false;
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    bool EnsureBluetoothConnectPermission()
    {
        const string perm = "android.permission.BLUETOOTH_CONNECT";
        if (Permission.HasUserAuthorizedPermission(perm))
            return true;

        if (!_permissionAsked)
        {
            _permissionAsked = true;
            Permission.RequestUserPermission(perm);
        }

        return Permission.HasUserAuthorizedPermission(perm);
    }
#endif
}
