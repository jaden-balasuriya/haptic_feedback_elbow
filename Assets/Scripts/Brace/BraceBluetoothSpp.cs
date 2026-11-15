using System;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Thin Android Bluetooth Classic SPP client for the Arduino UNO Q onboard radio.
/// Uses stock Android APIs via AndroidJavaObject (no Asset Store plugin).
/// Editor / non-Android builds are no-ops.
///
/// Quest Settings often does not list SPP boards even when a phone can see them.
/// Prefer a MAC address so the APK can bond/connect without Settings discovery.
/// </summary>
public sealed class BraceBluetoothSpp : IDisposable
{
    public const string DefaultSppUuid = "00001101-0000-1000-8000-00805F9B34FB";
    const int BondTimeoutMs = 30000;

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
    /// Connect on a background thread. Prefers MAC when set; otherwise bonded name.
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

        string want = string.IsNullOrWhiteSpace(deviceName) ? "Arduino-UNO-Q" : deviceName.Trim();
        AndroidJavaObject device = ResolveDevice(adapter, want, macAddress);
        if (device == null)
            throw new InvalidOperationException($"Bluetooth device '{want}' not found");

        try
        {
            EnsureBonded(device);
            OpenSppSocket(adapter, device);
        }
        finally
        {
            device.Dispose();
        }
    }

    static AndroidJavaObject ResolveDevice(AndroidJavaObject adapter, string deviceName, string macAddress)
    {
        string mac = (macAddress ?? string.Empty).Trim().ToUpperInvariant();
        if (mac.Length > 0)
        {
            Debug.Log($"[BraceBT] Using MAC {mac} (Quest Settings discovery not required)");
            return adapter.Call<AndroidJavaObject>("getRemoteDevice", mac);
        }

        AndroidJavaObject bonded = FindBondedByName(adapter, deviceName);
        if (bonded != null)
        {
            Debug.Log($"[BraceBT] Found bonded device '{deviceName}'");
            return bonded;
        }

        throw new InvalidOperationException(
            $"'{deviceName}' is not paired on this Quest. " +
            "Quest Settings often will not list UNO Q / SPP boards even when a phone can see them. " +
            "Set BraceBluetoothClient.macAddress (from `bluetoothctl show` on the UNO Q) and rebuild — " +
            "the APK will pair when it connects.");
    }

    static AndroidJavaObject FindBondedByName(AndroidJavaObject adapter, string deviceName)
    {
        using var bonded = adapter.Call<AndroidJavaObject>("getBondedDevices");
        using var iterator = bonded.Call<AndroidJavaObject>("iterator");

        while (iterator.Call<bool>("hasNext"))
        {
            var candidate = iterator.Call<AndroidJavaObject>("next");
            string name = candidate.Call<string>("getName") ?? string.Empty;
            if (string.Equals(name, deviceName, StringComparison.OrdinalIgnoreCase))
                return candidate;

            candidate.Dispose();
        }

        return null;
    }

    static void EnsureBonded(AndroidJavaObject device)
    {
        int bond = device.Call<int>("getBondState");
        // 10 = NONE, 11 = BONDING, 12 = BONDED
        if (bond == 12)
            return;

        Debug.Log("[BraceBT] Not bonded yet — requesting createBond (accept the Quest prompt)");
        try
        {
            device.Call<bool>("createBond");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BraceBT] createBond threw: {e.Message}");
        }

        int waited = 0;
        const int stepMs = 250;
        while (waited < BondTimeoutMs)
        {
            Thread.Sleep(stepMs);
            waited += stepMs;
            bond = device.Call<int>("getBondState");
            if (bond == 12)
            {
                Debug.Log("[BraceBT] Bonded");
                return;
            }
        }

        Debug.LogWarning("[BraceBT] Bond wait timed out; attempting SPP connect anyway");
    }

    void OpenSppSocket(AndroidJavaObject adapter, AndroidJavaObject device)
    {
        using var uuidClass = new AndroidJavaClass("java.util.UUID");
        using var uuid = uuidClass.CallStatic<AndroidJavaObject>("fromString", DefaultSppUuid);

        Exception last = null;
        AndroidJavaObject socket = null;

        try
        {
            socket = device.Call<AndroidJavaObject>("createRfcommSocketToServiceRecord", uuid);
            adapter.Call("cancelDiscovery");
            socket.Call("connect");
        }
        catch (Exception first)
        {
            last = first;
            Debug.LogWarning($"[BraceBT] Secure SPP failed ({first.Message}); trying insecure");
            try { socket?.Call("close"); } catch { /* ignore */ }
            socket?.Dispose();
            socket = null;

            try
            {
                socket = device.Call<AndroidJavaObject>("createInsecureRfcommSocketToServiceRecord", uuid);
                adapter.Call("cancelDiscovery");
                socket.Call("connect");
                last = null;
            }
            catch (Exception second)
            {
                last = second;
                try { socket?.Call("close"); } catch { /* ignore */ }
                socket?.Dispose();
                socket = null;
            }
        }

        if (socket == null)
        {
            // BlueZ often listens on RFCOMM channel 1; UUID lookup can fail without SDP.
            Debug.LogWarning("[BraceBT] UUID SPP failed; trying RFCOMM channel 1 via reflection");
            socket = CreateRfcommChannelSocket(device, 1);
            adapter.Call("cancelDiscovery");
            try
            {
                socket.Call("connect");
                last = null;
            }
            catch (Exception third)
            {
                last = third;
                try { socket.Call("close"); } catch { /* ignore */ }
                socket.Dispose();
                throw new InvalidOperationException($"SPP connect failed: {last.Message}", last);
            }
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

    static AndroidJavaObject CreateRfcommChannelSocket(AndroidJavaObject device, int channel)
    {
        // device.getClass().getMethod("createRfcommSocket", int.class).invoke(device, channel)
        using var deviceClass = device.Call<AndroidJavaObject>("getClass");
        using var integerClass = new AndroidJavaClass("java.lang.Integer");
        using var intType = integerClass.GetStatic<AndroidJavaObject>("TYPE");
        using var method = deviceClass.Call<AndroidJavaObject>("getMethod", "createRfcommSocket", intType);
        return method.Call<AndroidJavaObject>("invoke", device, channel);
    }
#endif
}
