using System;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Thin Android Bluetooth Classic SPP client for HC-05/HC-06.
/// Uses stock Android APIs via AndroidJavaObject (no Asset Store plugin).
/// Editor / non-Android builds are no-ops.
/// </summary>
public sealed class BraceBluetoothSpp : IDisposable
{
    public const string DefaultSppUuid = "00001101-0000-1000-8000-00805F9B34FB";

    readonly object _gate = new object();
    Thread _connectThread;

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject _socket;
    AndroidJavaObject _outputStream;
#endif

    public bool IsConnected
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            lock (_gate)
            {
                try
                {
                    return _socket != null && _socket.Call<bool>("isConnected");
                }
                catch
                {
                    return false;
                }
            }
#else
            return false;
#endif
        }
    }

    public string LastError { get; private set; }

    /// <summary>
    /// Connect on a background thread. Matches a bonded device by name (e.g. "HC-06"),
    /// or by MAC if <paramref name="macAddress"/> is non-empty (AA:BB:CC:DD:EE:FF).
    /// </summary>
    public void ConnectAsync(string deviceName, string macAddress, Action<bool, string> onComplete = null)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_connectThread != null && _connectThread.IsAlive)
        {
            onComplete?.Invoke(false, "connect already in progress");
            return;
        }

        _connectThread = new Thread(() =>
        {
            try
            {
                ConnectBlocking(deviceName, macAddress);
                onComplete?.Invoke(true, null);
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError($"[BraceBT] Connect failed: {e.Message}");
                Disconnect();
                onComplete?.Invoke(false, e.Message);
            }
        })
        {
            IsBackground = true,
            Name = "BraceBluetoothSpp"
        };
        _connectThread.Start();
#else
        LastError = "Bluetooth SPP only runs on an Android / Quest device build";
        Debug.LogWarning($"[BraceBT] {LastError}");
        onComplete?.Invoke(false, LastError);
#endif
    }

    public bool WriteLine(string line)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(line))
            return false;

        byte[] payload = Encoding.ASCII.GetBytes(line.EndsWith("\n", StringComparison.Ordinal) ? line : line + "\n");

        lock (_gate)
        {
            if (_outputStream == null)
                return false;

            try
            {
                _outputStream.Call("write", payload);
                _outputStream.Call("flush");
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogWarning($"[BraceBT] Write failed: {e.Message}");
                return false;
            }
        }
#else
        return false;
#endif
    }

    public void Disconnect()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        lock (_gate)
        {
            try { _outputStream?.Call("close"); } catch { /* ignore */ }
            try { _socket?.Call("close"); } catch { /* ignore */ }
            _outputStream?.Dispose();
            _socket?.Dispose();
            _outputStream = null;
            _socket = null;
        }
#endif
    }

    public void Dispose()
    {
        Disconnect();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    void ConnectBlocking(string deviceName, string macAddress)
    {
        using var adapterClass = new AndroidJavaClass("android.bluetooth.BluetoothAdapter");
        using var adapter = adapterClass.CallStatic<AndroidJavaObject>("getDefaultAdapter");

        if (adapter == null)
            throw new InvalidOperationException("no Bluetooth adapter");

        if (!adapter.Call<bool>("isEnabled"))
            throw new InvalidOperationException("Bluetooth is disabled");

        AndroidJavaObject device = ResolveBondedDevice(adapter, deviceName, macAddress);
        if (device == null)
            throw new InvalidOperationException("Bluetooth device not found");

        try
        {
            using var uuidClass = new AndroidJavaClass("java.util.UUID");
            using var uuid = uuidClass.CallStatic<AndroidJavaObject>("fromString", DefaultSppUuid);

            AndroidJavaObject socket = null;
            try
            {
                socket = device.Call<AndroidJavaObject>("createRfcommSocketToServiceRecord", uuid);
                adapter.Call("cancelDiscovery");
                socket.Call("connect");
            }
            catch (Exception first)
            {
                Debug.LogWarning($"[BraceBT] SPP UUID connect failed ({first.Message}); trying insecure RFCOMM");
                try { socket?.Call("close"); } catch { /* ignore */ }
                socket?.Dispose();

                socket = device.Call<AndroidJavaObject>("createInsecureRfcommSocketToServiceRecord", uuid);
                adapter.Call("cancelDiscovery");
                socket.Call("connect");
            }

            var output = socket.Call<AndroidJavaObject>("getOutputStream");

            lock (_gate)
            {
                try { _outputStream?.Call("close"); } catch { /* ignore */ }
                try { _socket?.Call("close"); } catch { /* ignore */ }
                _outputStream?.Dispose();
                _socket?.Dispose();
                _socket = socket;
                _outputStream = output;
            }

            string connectedName = device.Call<string>("getName") ?? "(unknown)";
            string connectedMac = device.Call<string>("getAddress") ?? "?";
            Debug.Log($"[BraceBT] Connected to {connectedName} ({connectedMac})");
        }
        finally
        {
            device.Dispose();
        }
    }

    static AndroidJavaObject ResolveBondedDevice(AndroidJavaObject adapter, string deviceName, string macAddress)
    {
        string mac = (macAddress ?? string.Empty).Trim();
        if (mac.Length > 0)
            return adapter.Call<AndroidJavaObject>("getRemoteDevice", mac.ToUpperInvariant());

        string want = string.IsNullOrWhiteSpace(deviceName) ? "HC-06" : deviceName.Trim();
        using var bonded = adapter.Call<AndroidJavaObject>("getBondedDevices");
        using var iterator = bonded.Call<AndroidJavaObject>("iterator");

        while (iterator.Call<bool>("hasNext"))
        {
            var candidate = iterator.Call<AndroidJavaObject>("next");
            string name = candidate.Call<string>("getName") ?? string.Empty;
            if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase))
                return candidate;

            candidate.Dispose();
        }

        throw new InvalidOperationException(
            $"paired device named '{want}' not found — pair the HC-06 in Quest Settings → Bluetooth first");
    }
#endif
}
