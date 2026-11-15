# Quest 3 ↔ UNO Q Wi-Fi hotspot → force (N) + elbow angle (°)

Bidirectional link over a **Wi-Fi hotspot** hosted by the **Arduino UNO Q**:

- Quest → MCU: **force in Newtons** (from grabbed mass)
- MCU → Quest: **elbow angle in degrees** (from a potentiometer on A0)

No HC-06 / Bluetooth SPP required.

## Architecture

```text
Quest 3 Unity APK
  ElbowTorqueOutput  (forceN = massKg * 9.81, HUD elbow angle)
  BraceTcpClient     ↔  TCP 10.42.0.1:7777
        |
        v
  UNO Q Linux hotspot (SSID UNO-Q-Torque)
  python/main.py
        |  Bridge.call("on_force_line", "9.810")
        |  Bridge.call("get_elbow_angle") → "angleDeg=90.0"
        v
  MCU sketch (TorquePrint.ino)
        pot A0 → angleDeg   /   Monitor: forceN=...
```

Also on the UNO Q (for Quest networking):

- `setup_hotspot.sh` — NetworkManager AP + autoconnect
- `python/connectivity_shim.py` — HTTP `204` on `:80` so Android validates the hotspot
- DNS overrides under `/etc/NetworkManager/dnsmasq-shared.d/` for connectivity checks

## Potentiometer wiring (4-pin / 3-terminal)

**Critical:** UNO Q analog pins are **3.3 V only** (not 5 V tolerant). Power the pot from **3.3V**, never from 5V.

| Pot pin (typical 4-pin module) | Wire to UNO Q |
|--------------------------------|---------------|
| **VCC / + / 3V3** | **3.3V** |
| **GND / −** | **GND** |
| **SIG / OUT / S** (wiper) | **A0** |
| **NC / SW** (4th pin, if present) | Leave unconnected (or digital pin if it is a switch) |

On a bare 3-terminal potentiometer: one outer lug → 3.3V, other outer lug → GND, center (wiper) → A0.

Firmware maps raw ADC `0…1023` → `0.0…270.0` degrees (`ANGLE_MIN_DEG` / `ANGLE_MAX_DEG` in `TorquePrint.ino`). Adjust those after mounting the brace.

## Message protocol

**Quest → UNO Q (force):**

```text
9.810\n
```

- One ASCII line = force in **Newtons**
- `F = massKg × 9.81` (release → `0.000\n`)

**UNO Q → Quest (elbow angle):**

```text
angleDeg=90.0\n
```

- One ASCII line ≈ 10 Hz while Quest is connected
- HUD shows `Arm angle: 90.0 deg`

**Port:** use **7777** (not 9000). Quest SELinux blocks untrusted apps from connecting to port 9000 (`webserver_port_t`).
## 1. Flash the MCU sketch

From a PC with the board on USB:

```bash
cd arduino/TorquePrint
arduino-cli compile --fqbn arduino:zephyr:unoq .
arduino-cli upload  --fqbn arduino:zephyr:unoq -p /dev/ttyACM0 .
```

Confirm: `arduino-cli board list` → `arduino:zephyr:unoq`.

Watch MCU output:

```bash
cat /dev/ttyACM0
# or: tio /dev/ttyACM0
```

Expect `ForcePrint ready` / later `forceN=...`.

## 2. Deploy Linux app + hotspot on the UNO Q

Copy this folder onto the board (e.g. `/home/arduino/ArduinoApps/torqueprint/`), then over `adb shell` / SSH:

```bash
cd /home/arduino/ArduinoApps/torqueprint   # or your path
chmod +x setup_hotspot.sh
./setup_hotspot.sh                         # SSID=UNO-Q-Torque  password=torquepilot
```

Start the TCP + Bridge server (Docker example used in this project):

```bash
docker run -d --name torqueprint --restart unless-stopped --network host --privileged \
  -v /home/arduino/ArduinoApps/torqueprint:/app \
  -v /var/run/arduino-router.sock:/var/run/arduino-router.sock \
  --user 0:0 \
  ghcr.io/arduino/app-bricks/python-apps-base:0.5.0
```

Connectivity shim (HTTP 204 on port 80):

```bash
docker run -d --name torque-connshim --restart unless-stopped --network host --user 0:0 \
  -v /home/arduino/ArduinoApps/torqueprint/python/connectivity_shim.py:/shim.py:ro \
  --entrypoint python3 \
  ghcr.io/arduino/app-bricks/python-apps-base:0.5.0 /shim.py
```

You should see `[TorqueTCP] Listening on 0.0.0.0:7777`.

### Persistence after power cycle

| Piece | Survives reboot? |
|-------|------------------|
| Hotspot profile (`autoconnect yes`) | Yes |
| `torqueprint` / `torque-connshim` containers (`unless-stopped`) | Yes |
| App files under `ArduinoApps/torqueprint/` | Yes |
| Factory reset / OS reflash | No — redeploy |

## 3. Quest Wi-Fi

1. Quest: **Settings → Wi-Fi → UNO-Q-Torque** (password **`torquepilot`**).
2. If it says “No internet”, keep using the network anyway.
3. Quest IP is typically `10.42.0.x`; gateway **`10.42.0.1`**.

Do **not** rely on campus Visitor Wi-Fi for this link (client isolation).

## 4. Unity

| File | Role |
|------|------|
| `BraceTcpClient.cs` | TCP client → `10.42.0.1:7777`, sends force / receives `angleDeg=` |
| `ElbowTorqueOutput.cs` | `forceN = mass * 9.81`, HUD `Elbow: … deg` + `Force: … N` |
| `Assets/Plugins/Android/AndroidManifest.xml` | Internet / Wi-Fi permissions + GameActivity |
| `arduino/TorquePrint/*` | MCU + Linux TCP/hotspot |

In **SampleScene**, `ElbowTorqueSystem` has `ElbowTorqueOutput` + `BraceTcpClient` (`host=10.42.0.1`, `port=7777`).

Build a **native APK** and install on Quest ([`LINUX_QUEST3_DEPLOY.md`](LINUX_QUEST3_DEPLOY.md)). Editor Play Mode does not open the TCP brace link the same way.

## 5. Verify

1. HUD: `Arm angle: X.X deg`, `Force: X.X N`, and `WiFi: Connected`
2. Turn the pot → HUD elbow angle updates; Monitor shows `angleDeg=...`
3. Grab 1 kg dumbbell → about **9.8 N**
4. On PC: `cat /dev/ttyACM0` → `forceN=9.810` and `angleDeg=...`
5. Release → `forceN=0.000`

## Troubleshooting

| Symptom | Check |
|---------|--------|
| WiFi: Not connected / `Access denied` on :9000 | Must use port **7777** (rebuild APK + server) |
| WiFi: Not connected (timeout) | Rejoin hotspot; ensure `torqueprint` listening; connectivity shim on :80 |
| Hotspot missing after reboot | `nmcli connection modify Hotspot connection.autoconnect yes` |
| Bridge / Monitor silent | MCU sketch flashed; `arduino-router` running; Python `on_force_line` |
| Campus Visitor Wi-Fi | Use board hotspot instead |

## Checklist

- [ ] Pot wired **3.3V / GND / A0** (not 5V)
- [ ] `TorquePrint.ino` uploaded (`on_force_line` + `get_elbow_angle`)
- [ ] Hotspot + TCP `:7777` + connshim running on UNO Q (updated `python/main.py`)
- [ ] Quest joined `UNO-Q-Torque`
- [ ] APK built with `BraceTcpClient` port **7777**
- [ ] Grab → Monitor `forceN=...`; pot → HUD `Arm angle: … deg`
