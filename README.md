# Haptic Feedback Arm

A wearable elbow brace that applies motorized resistance in VR, driven by a Meta Quest 3 Unity app over Wi-Fi.

When you lift a virtual weight or draw a bow, the Quest estimates elbow load from pose + object mass (or bow draw), maps that to motor PWM, and the brace pushes back so the interaction feels physical.

## What it can do

- **Mass-based elbow torque** — Grab dumbbells / boxes; torque \(\tau = m\,g\,L\,|\sin\theta_f|\) from hand mass, upper-arm roll (MPU6050), and elbow angle (potentiometer). See [`Docs/ELBOW_TORQUE_CALCULATION.md`](Docs/ELBOW_TORQUE_CALCULATION.md).
- **Motorized resistance** — Torque maps to PWM for an L298N-driven motor on the brace.
- **Bow & arrow** — Left hand summons the bow; right-arm draw uses pot flexion for draw depth and motor load (no mass/IMU path).
- **Archery gallery** — Standing targets in a meadow play space; hit flash / tip / reset.
- **Bidirectional brace link** — Quest ↔ Arduino UNO Q over the board’s Wi-Fi hotspot (TCP).
  - Quest → MCU: PWM / force command lines
  - MCU → Quest: `angleDeg=` (elbow pot) and IMU orientation for the torque model + HUD
- **On-device HUD** — Head-locked readout for torque, arm angle, Wi-Fi status, etc.
- **Linux → Quest deploy** — Build an Android APK in Unity and install with `adb` ([`Docs/LINUX_QUEST3_DEPLOY.md`](Docs/LINUX_QUEST3_DEPLOY.md)).

## Stack

| Layer | Tech |
|-------|------|
| **Headset** | Meta Quest 3 (Android APK; no PC Link required) |
| **VR app** | Unity 6 (`6000.6.x`), URP |
| **XR** | OpenXR, Meta XR SDK, XR Interaction Toolkit 3.x, Input System |
| **Brace MCU** | Arduino UNO Q (Zephyr sketch + Linux side) |
| **Bridge** | Python on UNO Q (`arduino/TorquePrint/python/main.py`) via Arduino Router Bridge |
| **Link** | UNO Q Wi-Fi hotspot → TCP (`10.42.0.1:7777`) |
| **Actuation** | L298N motor driver (PWM + direction) |
| **Sensing** | Elbow potentiometer (A0), MPU6050 on the upper arm |

Bluetooth / HC-06 helpers still exist under `Assets/Scripts/Brace/`, but the supported path is the UNO Q hotspot + TCP.

## Repo map

| Path | Purpose |
|------|---------|
| [`Assets/Scripts/ElbowTorque/`](Assets/Scripts/ElbowTorque/) | Torque model, grab bridge, dumbbells, bow/arrow, archery, meadow bootstrap |
| [`Assets/Scripts/Brace/`](Assets/Scripts/Brace/) | `BraceTcpClient` (and legacy Bluetooth clients) |
| [`arduino/TorquePrint/`](arduino/TorquePrint/) | MCU sketch, hotspot setup, Python TCP ↔ Bridge |
| [`Docs/QUEST3_TORQUE_MVP_SETUP.md`](Docs/QUEST3_TORQUE_MVP_SETUP.md) | Unity packages, scene wiring, Quest build checklist |
| [`Docs/QUEST3_HC06_TORQUE.md`](Docs/QUEST3_HC06_TORQUE.md) | Hotspot + TCP setup and verification |
| [`Docs/ELBOW_TORQUE_CALCULATION.md`](Docs/ELBOW_TORQUE_CALCULATION.md) | Pose → \(\tau\) → PWM math |
| [`Docs/LINUX_QUEST3_DEPLOY.md`](Docs/LINUX_QUEST3_DEPLOY.md) | Build APK on Linux and install via adb |

## Data path (Wi-Fi)

```text
Quest 3 (Unity)
  ElbowTorqueOutput  →  BraceTcpClient  →  TCP 10.42.0.1:7777
        │
        v
  UNO Q Linux  (python/main.py)
        │  Bridge RPC
        v
  UNO Q MCU  (TorquePrint.ino)  →  L298N / Monitor
        │
        └── pot + MPU  →  angleDeg= / pitch  →  Quest HUD + torque model
```

## Status

Working MVP: VR interactions, bidirectional Wi-Fi link, and torque → motor mapping are in place and under active iteration (calibration, motor feel, and gameplay).
