# Quest 3 → HC-06 → Arduino UNO Q torque

Send elbow torque from the Quest 3 Unity app to an **Arduino UNO Q** over an **HC-06** Bluetooth classic serial bridge. The MCU sketch prints received values; motor control is not included yet.

## Architecture

```text
Quest 3 Unity APK
  BraceBluetoothClient (SPP)
        |
        v
     HC-06  --UART Serial1 D0/D1-->  UNO Q MCU (TorquePrint)
                                        |
                              Monitor / Router Bridge
                                        |
                         PC: tio /dev/ttyACM0  (or App Lab / IDE)
```

- Pair the HC-06 to the **Quest headset**, then run a **native APK** (see [`LINUX_QUEST3_DEPLOY.md`](LINUX_QUEST3_DEPLOY.md)). Quest Link is not used here — Link would require pairing to the PC instead.
- Unity sends ASCII lines at ~10 Hz (configurable), not every frame.

## Wiring (UNO Q)

UNO Q digital pins are **3.3 V**. Power the HC-06 from **3.3V**.

| HC-06 | UNO Q |
|-------|-------|
| VCC | **3.3V** |
| GND | GND |
| TXD | **D0** (`Serial1` RX) |
| RXD | **D1** (`Serial1` TX) |

| Link | Details |
|------|---------|
| HC-06 ↔ MCU | `Serial1` @ **9600** 8N1 |
| MCU ↔ PC | `Monitor` → USB (`/dev/ttyACM0` on the host). Baud ignored for USB. |

## Message protocol

```text
5.000\n
```

- One ASCII line per command = elbow torque in Nm.
- Zero on release: `0.000\n`.

## 1. Flash TorquePrint on the UNO Q

```bash
arduino-cli core install arduino:zephyr
arduino-cli lib install Arduino_RouterBridge

cd arduino/TorquePrint
arduino-cli compile --fqbn arduino:zephyr:unoq .
arduino-cli upload  --fqbn arduino:zephyr:unoq -p /dev/ttyACM0 .
```

Confirm the board/FQBN with:

```bash
arduino-cli board list
# expect: Arduino UNO Q    arduino:zephyr:unoq
```

Watch MCU output on the host:

```bash
cat /dev/ttyACM0
# or: tio /dev/ttyACM0
```

You should see `TorquePrint ready (UNO Q)`.

## 2. Pair the HC-06 on Quest 3

1. Power UNO Q + HC-06 (LED on the HC-06 should blink when idle).
2. On the Quest: **Settings → Bluetooth**.
3. Put on the headset (or use Quest Link casting / phone app if needed to navigate settings).
4. Select the module (name often **`HC-06`**). PIN is usually **`1234`** or **`0000`**.
5. Leave it **paired**. Status may show “Not connected” until the Unity app opens an SPP socket — that is normal.

If several HC-06 modules are nearby, note the MAC in Bluetooth settings and paste it into the Unity component (below).

## 3. Unity scene wiring

Scripts live under [`Assets/Scripts/Brace/`](../Assets/Scripts/Brace/):

| File | Role |
|------|------|
| `BraceBluetoothSpp.cs` | Android SPP socket (Java APIs via `AndroidJavaObject`) |
| `BraceBluetoothClient.cs` | MonoBehaviour: connect, throttle, send lines |
| `Assets/Plugins/Android/AndroidManifest.xml` | Named `UnityPlayerGameActivity` + `BLUETOOTH_CONNECT` (+ legacy) permissions |

In **`SampleScene`**, **`ElbowTorqueSystem`** already has:

- `ElbowTorqueOutput` with **Brace Bluetooth** assigned
- `BraceBluetoothClient` (**Device Name** = `HC-06`, **Auto Connect On Start** on, **Send Hz** = 10)

Inspector tweaks if needed:

- **Device Name** must match the Quest paired name, **or**
- **Mac Address** `AA:BB:CC:DD:EE:FF` to pin a specific module

`PublishTorque` calls `braceBluetooth.SendTorque(torqueNm)`.

## 4. Build and run on the Quest

1. Build an **Android APK** and install on the Quest ([`LINUX_QUEST3_DEPLOY.md`](LINUX_QUEST3_DEPLOY.md)).
2. First launch may prompt for **Nearby devices / Bluetooth** permission — allow it.
3. Check `adb logcat` for:

   ```text
   [BraceBT] Connecting to 'HC-06' ...
   [BraceBT] Connected to HC-06 (...)
   ```

4. On the PC, keep `cat /dev/ttyACM0` (or `tio`) open.
5. In VR, grab a dumbbell with the **right** hand → monitor should show e.g. `torqueNm=5.000`.
6. Release → `torqueNm=0.000`.

### Quick verify without VR grab

With the APK connected, you can also confirm the Arduino path alone using a phone Bluetooth serial terminal to the HC-06 sending `5.000` — same print on the UNO Q monitor.

## Troubleshooting

| Symptom | What to check |
|---------|----------------|
| `paired device named 'HC-06' not found` | Pair in Quest Settings first; name must match exactly (or set MAC). |
| Permission denied / no connect prompt | Rebuild with the project `AndroidManifest.xml`; allow Bluetooth on first run. |
| Connected in Settings but no torque lines | App must open SPP (our client). “Connected” in Settings is not enough by itself. |
| Connect fails after UUID error | Client already retries insecure RFCOMM; power-cycle HC-06 and re-pair. |
| Works in Editor? | **No.** SPP only runs on the Quest APK (`UNITY_ANDROID`). |
| Quest Link | Do **not** use Link for this path; pair HC-06 to the headset and run the APK. |

## Checklist

- [ ] HC-06 on UNO Q (**3.3V**, TXD→D0, RXD←D1)
- [ ] `TorquePrint` uploaded (`arduino:zephyr:unoq`); `cat /dev/ttyACM0` shows ready
- [ ] HC-06 paired on Quest 3
- [ ] `ElbowTorqueSystem` has `BraceBluetoothClient` (already in SampleScene)
- [ ] Native APK installed; Bluetooth permission allowed
- [ ] Grab in VR → UNO Q monitor prints `torqueNm=...`
