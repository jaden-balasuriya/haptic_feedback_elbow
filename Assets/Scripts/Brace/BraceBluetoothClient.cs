using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Unity-facing brace Bluetooth client for the Arduino UNO Q onboard radio.
/// Add to the same GameObject as <see cref="ElbowTorqueOutput"/> (or assign the reference).
/// Sends ASCII lines: "5.000\n" (Nm). Runs only on a Quest / Android device build.
/// Pair the UNO Q (alias Arduino-UNO-Q) in Quest Settings, then this opens Classic SPP.
/// </summary>
public class BraceBluetoothClient : MonoBehaviour
{
    public static BraceBluetoothClient Instance { get; private set; }

    [Header("Device")]
    [Tooltip("Bluetooth name (setup_bt.sh sets Arduino-UNO-Q). Quest Settings often will not list it.")]
    public string deviceName = "Arduino-UNO-Q";

    [Tooltip("UNO Q MAC from `bluetoothctl show` (Address). Preferred on Quest — skips Settings discovery.")]
    public string macAddress = "94:97:4F:AC:32:F7";

    [Tooltip("Connect automatically when the scene starts (after permission).")]
    public bool autoConnectOnStart = true;

    [Header("Send rate")]
    [Tooltip("Max torque lines per second.")]
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

    /// <summary>Begin SPP connect to the paired UNO Q. Safe to call more than once.</summary>
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
        const string connect = "android.permission.BLUETOOTH_CONNECT";
        const string scan = "android.permission.BLUETOOTH_SCAN";

        bool hasConnect = Permission.HasUserAuthorizedPermission(connect);
        bool hasScan = Permission.HasUserAuthorizedPermission(scan);
        if (hasConnect && hasScan)
            return true;

        if (!_permissionAsked)
        {
            _permissionAsked = true;
            if (!hasConnect)
                Permission.RequestUserPermission(connect);
            if (!hasScan)
                Permission.RequestUserPermission(scan);
        }

        return Permission.HasUserAuthorizedPermission(connect);
    }
#endif
}
