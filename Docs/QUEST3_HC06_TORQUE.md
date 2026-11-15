# Quest 3 → UNO Q Wi-Fi hotspot → torque

Send elbow torque from the Quest 3 Unity app to an **Arduino UNO Q** over a **Wi-Fi hotspot** hosted by the board. No HC-06 / Bluetooth SPP required.

## Architecture

```text
Quest 3 Unity APK
  BraceTcpClient  →  TCP 10.42.0.1:9000
        |
        v
  UNO Q Linux hotspot + python/main.py
        |
        |  Bridge.call("on_torque_line", "5.000")
        v
  UNO Q MCU sketch (TorquePrint) → Monitor
```

- Quest joins the UNO Q AP (`UNO-Q-Torque`), then opens a TCP socket.
- Avoid campus Visitor Wi-Fi for this link (client isolation). Use the board hotspot instead.
- Deploy the APK over USB/`adb` as usual ([`LINUX_QUEST3_DEPLOY.md`](LINUX_QUEST3_DEPLOY.md)).

## Message protocol

```text
5.000\n
```

## 1. Flash the MCU sketch

```bash
cd arduino/TorquePrint
arduino-cli compile --fqbn arduino:zephyr:unoq .
arduino-cli upload  --fqbn arduino:zephyr:unoq -p /dev/ttyACM0 .
```

## 2. Start hotspot + TCP server on the UNO Q

```bash
cd arduino/TorquePrint
chmod +x setup_hotspot.sh
./setup_hotspot.sh                 # SSID=UNO-Q-Torque  password=torquepilot
# then run python/main.py via App Lab, or:
docker run -d --name torqueprint --restart unless-stopped --network host --privileged \
  -v "$PWD":/app \
  -v /var/run/arduino-router.sock:/var/run/arduino-router.sock \
  --user 0:0 \
  ghcr.io/arduino/app-bricks/python-apps-base:0.5.0
```

You should see `[TorqueTCP] Listening on 0.0.0.0:9000`.

## 3. Quest Wi-Fi

1. Quest: **Settings → Wi-Fi → UNO-Q-Torque** (password `torquepilot`).
2. Confirm you are on that network (Quest will drop Princeton Visitor while connected).

## 4. Unity

| File | Role |
|------|------|
| `BraceTcpClient.cs` | TCP client (default `10.42.0.1:9000`) |
| `ElbowTorqueOutput.cs` | Sends torque + HUD `WiFi: Connected` |
| `arduino/TorquePrint/python/main.py` | TCP → Bridge |
| `arduino/TorquePrint/setup_hotspot.sh` | `nmcli` hotspot helper |

`SampleScene` already has `BraceTcpClient` on `ElbowTorqueSystem`.

## 5. Verify

1. Build/install APK, allow network if prompted.
2. HUD: `WiFi: Connected`
3. Grab dumbbell → UNO Q logs `forwarded: …` / Monitor `torqueNm=…`

## Troubleshooting

| Symptom | Check |
|---------|--------|
| WiFi: Not connected | Quest joined hotspot? Server listening? Host IP still `10.42.0.1`? |
| Hotspot missing | Re-run `./setup_hotspot.sh` over `adb shell` |
| Bridge silent | MCU sketch uploaded; `arduino-router` running |

## Checklist

- [ ] `TorquePrint.ino` uploaded
- [ ] Hotspot + TCP server running on UNO Q
- [ ] Quest joined `UNO-Q-Torque`
- [ ] APK built; HUD shows WiFi connected
- [ ] Grab → torque lines on UNO Q
