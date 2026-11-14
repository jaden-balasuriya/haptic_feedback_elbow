# Quest 3 → HC-06 → Arduino UNO Q torque print

Send elbow torque commands from the Quest 3 app to an **Arduino UNO Q** over an **HC-06** Bluetooth classic serial bridge. The sketch in [`arduino/TorquePrint/`](../arduino/TorquePrint/) only prints received values to the App Lab / IDE monitor (no motor control yet).

## Architecture

```text
Quest 3 Unity APK  --Bluetooth Classic SPP-->  HC-06  --UART Serial1 D0/D1-->  UNO Q MCU
                                                                        |
                                                              Monitor / Router Bridge
                                                                        |
                                                              App Lab or IDE Serial Monitor
```

- Board is dual-core: MCU (Zephyr sketch) + Linux MPU. USB-C is on the MPU; MCU debug text goes through **Router Bridge** (`Monitor`), not a classic Uno-style USB CDC port.
- Run the torque app as a **native Quest APK** (see [`LINUX_QUEST3_DEPLOY.md`](LINUX_QUEST3_DEPLOY.md)). Pair the HC-06 to the **headset**, not a PC.
- Do **not** send every Unity frame from a future client — throttle to ~10–20 Hz or send on change. The Arduino sketch prints whatever lines arrive.

## Wiring (UNO Q)

UNO Q digital pins are **3.3 V**. Power the HC-06 from **3.3V**, not 5V, unless your module is explicitly 5V-only and level-shifted.

| HC-06 | UNO Q |
|-------|-------|
| VCC | **3.3V** |
| GND | GND |
| TXD | **D0** (`Serial1` RX) |
| RXD | **D1** (`Serial1` TX) |

Cross TX/RX: module **TXD → D0**, module **RXD ← D1**.

| Link | Details |
|------|---------|
| HC-06 ↔ MCU | `Serial1` @ **9600** 8N1 (HC-06 factory default) |
| MCU ↔ PC | `Monitor` via Router Bridge (App Lab / IDE Serial Monitor). Baud is ignored. |

Do **not** use `SoftwareSerial` or D10/D11 for this board.

## Message protocol

One ASCII line per command, newline-terminated:

```text
5.000\n
```

- Payload = elbow torque in Nm (same value as `ElbowTorqueOutput.CurrentTorqueNm`).
- No framing beyond a line.
- On release / zero torque, still send `0.000\n` so the brace can clear.

## Pair the HC-06 on Quest 3

1. Power the UNO Q + HC-06.
2. On the Quest: **Settings → Bluetooth**.
3. Pair the module (name often `HC-06`; PIN often `1234` or `0000`).
4. Leave it paired. “Connected” may drop until an app opens an SPP socket — that is normal for classic Bluetooth.

## Flash without the classic Arduino IDE

Install CLI tooling once:

```bash
arduino-cli config init
arduino-cli core update-index
arduino-cli core install arduino:zephyr
arduino-cli lib install Arduino_RouterBridge
```

Confirm the FQBN (some installs use `unoq` instead of `uno_q`):

```bash
arduino-cli board listall | grep -i 'uno q'
arduino-cli board list
```

Compile and upload:

```bash
cd arduino/TorquePrint
arduino-cli compile --fqbn arduino:zephyr:uno_q .
arduino-cli upload  --fqbn arduino:zephyr:uno_q -p /dev/ttyACM0 .
```

Replace `/dev/ttyACM0` with the port from `board list`.

You can also upload from **Arduino App Lab** or **Arduino IDE 2** (board: UNO Q).

## View the monitor

MCU prints go through **`Monitor`**, not a normal `tio /dev/ttyACM0` session.

1. Open **Arduino App Lab** (recommended) or **Arduino IDE 2** Serial Monitor connected to the UNO Q.
2. You should see `TorquePrint ready (UNO Q)`.
3. From a phone Bluetooth serial terminal (or later the Quest app), connect to the HC-06 and send:

   ```text
   5.000
   ```

4. The monitor should print:

   ```text
   torqueNm=5.000
   ```

`arduino-cli monitor` often reports **no monitor available** for this core; use App Lab / IDE for MCU output.

## Unity hook (later)

Not implemented yet. When adding a Bluetooth Classic SPP client:

1. Keep HUD / `Debug.Log` in [`ElbowTorqueOutput.PublishTorque`](../Assets/Scripts/ElbowTorque/ElbowTorqueOutput.cs).
2. Call something like `BraceBluetoothClient.SendTorque(torqueNm)` that writes `"{torqueNm:F3}\n"` over SPP.
3. Use a native APK on the Quest (pair HC-06 to the headset). Quest Link would require pairing to the PC instead — irrelevant for the Linux APK deploy path.

## Checklist

- [ ] HC-06 wired to UNO Q (**3.3V**, TXD→D0, RXD←D1)
- [ ] `Arduino_RouterBridge` installed; FQBN `arduino:zephyr:uno_q`
- [ ] `TorquePrint` uploaded; App Lab / IDE monitor shows ready
- [ ] HC-06 paired on Quest 3
- [ ] Phone/Quest send `5.000\n` → monitor prints `torqueNm=5.000`
