# Quest 3 → HC-06 → Arduino torque print

Send elbow torque commands from the Quest 3 app to an Arduino over an **HC-06** Bluetooth classic serial bridge. The sketch in [`arduino/TorquePrint/`](../arduino/TorquePrint/) only prints received values to the USB Serial Monitor (no motor control yet).

## Architecture

```text
Quest 3 Unity APK  --Bluetooth Classic SPP-->  HC-06  --UART-->  Arduino  --USB Serial.print-->  PC Serial Monitor
```

- Run the torque app as a **native Quest APK** (see [`LINUX_QUEST3_DEPLOY.md`](LINUX_QUEST3_DEPLOY.md)). Pair the HC-06 to the **headset**, not a PC.
- Do **not** send every Unity frame from a future client — throttle to ~10–20 Hz or send on change. The Arduino sketch prints whatever lines arrive.

## Wiring

| HC-06 | Arduino (Uno / Nano-style) |
|-------|----------------------------|
| VCC | 5V (or 3.3V if your module is 3.3V-only — check the board) |
| GND | GND |
| TXD | **D10** (SoftwareSerial RX) |
| RXD | **D11** via a **1k / 2k** voltage divider if the Arduino is 5V (HC-06 RX is usually 3.3V max) |

Cross TX/RX: module **TXD → Arduino RX**, module **RXD ← Arduino TX**.

| Link | Baud |
|------|------|
| HC-06 ↔ Arduino (`SoftwareSerial`) | **9600** 8N1 (HC-06 factory default) |
| Arduino ↔ PC (USB Serial Monitor) | **115200** |

USB Serial stays free for programming and the monitor.

## Message protocol

One ASCII line per command, newline-terminated:

```text
5.000\n
```

- Payload = elbow torque in Nm (same value as `ElbowTorqueOutput.CurrentTorqueNm`).
- No framing beyond a line.
- On release / zero torque, still send `0.000\n` so the brace can clear.

## Pair the HC-06 on Quest 3

1. Power the Arduino + HC-06.
2. On the Quest: **Settings → Bluetooth**.
3. Pair the module (name often `HC-06`; PIN often `1234` or `0000`).
4. Leave it paired. “Connected” may drop until an app opens an SPP socket — that is normal for classic Bluetooth.

## Flash and verify the Arduino sketch

1. Open [`arduino/TorquePrint/TorquePrint.ino`](../arduino/TorquePrint/TorquePrint.ino) in the Arduino IDE.
2. Select your board + port, upload.
3. Open **Serial Monitor** at **115200**. You should see `TorquePrint ready`.
4. From a phone Bluetooth serial terminal (or later the Quest app), connect to the HC-06 and send:

   ```text
   5.000
   ```

5. The monitor should print:

   ```text
   torqueNm=5.000
   ```

## Unity hook (later)

Not implemented in this change. When adding a Bluetooth Classic SPP client:

1. Keep HUD / `Debug.Log` in [`ElbowTorqueOutput.PublishTorque`](../Assets/Scripts/ElbowTorque/ElbowTorqueOutput.cs).
2. Call something like `BraceBluetoothClient.SendTorque(torqueNm)` that writes `"{torqueNm:F3}\n"` over SPP.
3. Use a native APK on the Quest (pair HC-06 to the headset). Quest Link would require pairing to the PC instead — irrelevant for the Linux APK deploy path.

## Checklist

- [ ] HC-06 wired (TXD→D10, RXD←D11 with divider if needed)
- [ ] `TorquePrint` uploaded; Serial Monitor @ 115200 shows ready
- [ ] HC-06 paired on Quest 3
- [ ] Phone/Quest send `5.000\n` → monitor prints `torqueNm=5.000`
