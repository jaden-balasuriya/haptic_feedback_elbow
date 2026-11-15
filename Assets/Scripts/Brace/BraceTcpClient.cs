using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Unity-facing brace TCP client for the Arduino UNO Q Wi-Fi hotspot.
/// Sends ASCII lines: "9.810\n" (Newtons) to host:port (default 10.42.0.1:7777).
/// On Android/Quest, binds the process to the Wi-Fi network so TCP works even
/// when the hotspot has no upstream internet.
/// </summary>
public class BraceTcpClient : MonoBehaviour
{
    public static BraceTcpClient Instance { get; private set; }

    [Header("Server (UNO Q hotspot)")]
    [Tooltip("UNO Q hotspot gateway IP (NetworkManager default is often 10.42.0.1).")]
    public string host = "10.42.0.1";

    [Tooltip("TCP port of python/main.py on the UNO Q. Avoid 9000 — Quest SELinux blocks it.")]
    public int port = 7777;

    [Tooltip("Connect automatically when the scene starts.")]
    public bool autoConnectOnStart = true;

    [Tooltip("Seconds between reconnect attempts when disconnected.")]
    [Min(0.5f)]
    public float reconnectIntervalSeconds = 2f;

    [Header("Send rate")]
    [Tooltip("Max force lines per second.")]
    [Range(1f, 30f)]
    public float sendHz = 10f;

    [Tooltip("Also send immediately when force changes by at least this much (N).")]
    public float sendOnChangeEpsilonN = 0.05f;

    [Header("Runtime (read-only)")]
    [SerializeField] bool connected;
    [SerializeField] string lastError;
    [SerializeField] float lastSentForceN = float.NaN;
    [SerializeField] float lastElbowAngleDeg = float.NaN;
    [SerializeField] float lastPitchDeg = float.NaN;

    readonly object _gate = new object();
    TcpClient _client;
    NetworkStream _stream;
    Thread _connectThread;
    Thread _receiveThread;
    volatile bool _receiveRunning;
    float _sendInterval;
    float _sendTimer;
    float _reconnectTimer;
    float _pendingForceN;
    bool _hasPending;
    bool _connectInProgress;
    float _latestElbowAngleDeg = float.NaN;
    bool _hasElbowAngle;
    float _latestPitchDeg = float.NaN;
    bool _hasPitch;

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                try
                {
                    return _client != null && _client.Connected && _stream != null;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    /// <summary>Latest elbow angle (degrees) from the UNO Q pot, or NaN if none yet.</summary>
    public float LastElbowAngleDeg
    {
        get
        {
            lock (_gate)
            {
                return _hasElbowAngle ? _latestElbowAngleDeg : float.NaN;
            }
        }
    }

    public bool HasElbowAngle
    {
        get
        {
            lock (_gate)
            {
                return _hasElbowAngle;
            }
        }
    }

    /// <summary>Latest MPU6050 pitch (degrees, tilt vs ground) from the UNO Q, or NaN if none yet.</summary>
    public float LastPitchDeg
    {
        get
        {
            lock (_gate)
            {
                return _hasPitch ? _latestPitchDeg : float.NaN;
            }
        }
    }

    public bool HasPitch
    {
        get
        {
            lock (_gate)
            {
                return _hasPitch;
            }
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BraceTCP] Multiple BraceTcpClient instances; keeping the first.");
            return;
        }

        Instance = this;
        _sendInterval = 1f / Mathf.Max(1f, sendHz);
    }

    void Start()
    {
        BindAndroidProcessToWifi();
        if (autoConnectOnStart)
            Connect();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        Disconnect();
    }

    void Update()
    {
        connected = IsConnected;
        lastElbowAngleDeg = LastElbowAngleDeg;
        lastPitchDeg = LastPitchDeg;
        if (!connected)
            lastError = string.IsNullOrEmpty(lastError) ? "not connected" : lastError;

        if (!connected && autoConnectOnStart && !_connectInProgress)
        {
            _reconnectTimer += Time.deltaTime;
            if (_reconnectTimer >= reconnectIntervalSeconds)
            {
                _reconnectTimer = 0f;
                Connect();
            }
        }

        if (!_hasPending || !connected)
            return;

        _sendTimer += Time.deltaTime;
        bool dueByTime = _sendTimer >= _sendInterval;
        bool dueByChange = float.IsNaN(lastSentForceN)
            || Mathf.Abs(_pendingForceN - lastSentForceN) >= sendOnChangeEpsilonN;

        if (!dueByTime && !dueByChange)
            return;

        _sendTimer = 0f;
        FlushPending();
    }

    public void Connect()
    {
        if (_connectInProgress || IsConnected)
            return;

        string targetHost = (host ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(targetHost))
        {
            lastError = "host is empty";
            Debug.LogError("[BraceTCP] host is empty");
            return;
        }

        if (!IPAddress.TryParse(targetHost, out IPAddress ip))
        {
            lastError = $"host must be an IP (got '{targetHost}')";
            Debug.LogError($"[BraceTCP] {lastError}");
            return;
        }

        _connectInProgress = true;
        lastError = null;
        Debug.Log($"[BraceTCP] Connecting to {targetHost}:{port} ...");

        _connectThread = new Thread(() =>
        {
            try
            {
                BindAndroidProcessToWifi();

                var client = new TcpClient(AddressFamily.InterNetwork)
                {
                    NoDelay = true,
                    SendTimeout = 3000,
                    // 0 = blocking read; receive thread owns lifetime of the socket.
                    ReceiveTimeout = 0
                };

                IAsyncResult ar = client.BeginConnect(ip, port, null, null);
                bool completed = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                if (!completed)
                {
                    try { client.Close(); } catch { /* ignore */ }
                    throw new TimeoutException($"connect timed out to {targetHost}:{port}");
                }

                client.EndConnect(ar);
                if (!client.Connected)
                {
                    try { client.Close(); } catch { /* ignore */ }
                    throw new InvalidOperationException($"socket not connected to {targetHost}:{port}");
                }

                var stream = client.GetStream();
                lock (_gate)
                {
                    try { _stream?.Close(); } catch { /* ignore */ }
                    try { _client?.Close(); } catch { /* ignore */ }
                    _client = client;
                    _stream = stream;
                }

                StartReceiveLoop(stream);
                Debug.Log($"[BraceTCP] Connected to {targetHost}:{port}");
            }
            catch (Exception e)
            {
                lastError = e.Message;
                Debug.LogWarning($"[BraceTCP] Connect failed: {e.Message}");
                Disconnect();
            }
            finally
            {
                _connectInProgress = false;
            }
        })
        {
            IsBackground = true,
            Name = "BraceTcpClient"
        };
        _connectThread.Start();
    }

    public void Disconnect()
    {
        _receiveRunning = false;
        lock (_gate)
        {
            try { _stream?.Close(); } catch { /* ignore */ }
            try { _client?.Close(); } catch { /* ignore */ }
            _stream = null;
            _client = null;
        }
        connected = false;
    }

    void StartReceiveLoop(NetworkStream stream)
    {
        _receiveRunning = false;
        try { _receiveThread?.Join(100); } catch { /* ignore */ }

        _receiveRunning = true;
        _receiveThread = new Thread(() => ReceiveLoop(stream))
        {
            IsBackground = true,
            Name = "BraceTcpReceive"
        };
        _receiveThread.Start();
    }

    void ReceiveLoop(NetworkStream stream)
    {
        var buf = new byte[256];
        var text = new StringBuilder();

        while (_receiveRunning)
        {
            try
            {
                if (stream == null || !stream.CanRead)
                    break;

                int n = stream.Read(buf, 0, buf.Length);
                if (n <= 0)
                    break;

                text.Append(Encoding.ASCII.GetString(buf, 0, n));
                while (true)
                {
                    string s = text.ToString();
                    int nl = s.IndexOf('\n');
                    int cr = s.IndexOf('\r');
                    if (nl < 0 && cr < 0)
                        break;

                    int idx;
                    if (nl < 0)
                        idx = cr;
                    else if (cr < 0)
                        idx = nl;
                    else
                        idx = Math.Min(nl, cr);

                    string line = s.Substring(0, idx).Trim();
                    text.Remove(0, idx + 1);
                    HandleIncomingLine(line);
                }
            }
            catch (Exception e)
            {
                if (_receiveRunning)
                {
                    lastError = e.Message;
                    Debug.LogWarning($"[BraceTCP] Receive failed: {e.Message}");
                }
                break;
            }
        }

        connected = false;
    }

    void HandleIncomingLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        // Protocol from python/main.py: "angleDeg=90.0" / "pitchDeg=12.3"
        if (TryParsePrefixedFloat(line, "angleDeg=", out float angleDeg))
        {
            lock (_gate)
            {
                _latestElbowAngleDeg = angleDeg;
                _hasElbowAngle = true;
            }
            return;
        }

        if (TryParsePrefixedFloat(line, "pitchDeg=", out float pitchDeg))
        {
            lock (_gate)
            {
                _latestPitchDeg = pitchDeg;
                _hasPitch = true;
            }
        }
    }

    static bool TryParsePrefixedFloat(string line, string prefix, out float value)
    {
        value = 0f;
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        string num = line.Substring(prefix.Length).Trim();
        return float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public void SendForce(float forceN)
    {
        _pendingForceN = Mathf.Max(0f, forceN);
        _hasPending = true;
    }

    void FlushPending()
    {
        string line = _pendingForceN.ToString("F3", CultureInfo.InvariantCulture) + "\n";
        byte[] payload = Encoding.ASCII.GetBytes(line);

        lock (_gate)
        {
            if (_stream == null)
                return;

            try
            {
                _stream.Write(payload, 0, payload.Length);
                _stream.Flush();
                lastSentForceN = _pendingForceN;
                _hasPending = false;
                lastError = null;
            }
            catch (Exception e)
            {
                lastError = e.Message;
                Debug.LogWarning($"[BraceTCP] Write failed: {e.Message}");
                try { _stream?.Close(); } catch { /* ignore */ }
                try { _client?.Close(); } catch { /* ignore */ }
                _stream = null;
                _client = null;
                connected = false;
            }
        }
    }

    /// <summary>
    /// Quest/Android often refuses app sockets on hotspots with no upstream internet
    /// unless we explicitly bind to the Wi-Fi Network.
    /// </summary>
    static AndroidJavaObject GetAndroidContext()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            try
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity != null)
                    return activity;
            }
            catch
            {
                // Unity 6 GameActivity builds may not expose currentActivity.
            }
        }
        catch
        {
            // fall through
        }

        using var activityThread = new AndroidJavaClass("android.app.ActivityThread");
        return activityThread.CallStatic<AndroidJavaObject>("currentApplication");
#else
        return null;
#endif
    }

    static void BindAndroidProcessToWifi()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var context = GetAndroidContext();
            if (context == null)
            {
                Debug.LogWarning("[BraceTCP] No Android context for network bind");
                return;
            }

            using var cm = context.Call<AndroidJavaObject>("getSystemService", "connectivity");
            if (cm == null)
                return;

            // Prefer the active network if it is Wi-Fi.
            using var active = cm.Call<AndroidJavaObject>("getActiveNetwork");
            if (active != null)
            {
                using var caps = cm.Call<AndroidJavaObject>("getNetworkCapabilities", active);
                if (caps != null && caps.Call<bool>("hasTransport", 1 /* TRANSPORT_WIFI */))
                {
                    cm.Call<bool>("bindProcessToNetwork", active);
                    Debug.Log("[BraceTCP] Bound process to active Wi-Fi network");
                    return;
                }
            }

            // Fallback: scan all networks for Wi-Fi.
            AndroidJavaObject[] networks = cm.Call<AndroidJavaObject[]>("getAllNetworks");
            if (networks == null)
                return;

            foreach (var net in networks)
            {
                if (net == null)
                    continue;
                using (net)
                {
                    using var caps = cm.Call<AndroidJavaObject>("getNetworkCapabilities", net);
                    if (caps != null && caps.Call<bool>("hasTransport", 1))
                    {
                        cm.Call<bool>("bindProcessToNetwork", net);
                        Debug.Log("[BraceTCP] Bound process to Wi-Fi network from getAllNetworks");
                        return;
                    }
                }
            }

            Debug.LogWarning("[BraceTCP] No Wi-Fi Network found to bind");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BraceTCP] bindProcessToNetwork failed: {e.Message}");
        }
#endif
    }
}
