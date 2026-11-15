# Haptic Feedback Elbow

A wearable motorized arm system that adds physical resistance and stopping forces to arm movement, paired with custom games on the Meta Quest 3.

## Overview

This project explores grounded haptic feedback for VR: when you move your arm in a Quest 3 experience, a motor on the arm can push back, hold, or limit motion so in-game interactions feel more physical. Games are built in Unity and talk to the wearable hardware so virtual events can drive real force feedback.

## Goals

- Make VR arm interactions feel tangible through controlled resistance and stops
- Keep the wearable simple enough to prototype and iterate on
- Support custom Unity / Quest 3 games that use the hardware as part of gameplay

## Stack

- **Hardware** — motorized arm / elbow wearable
- **Headset** — Meta Quest 3
- **Software** — Unity 6 (URP) + OpenXR / Meta XR + XR Interaction Toolkit

## Quest 3 force MVP

Grab boxes with the right hand; command force (Newtons) from box mass:

```text
forceN = massKg * 9.81
```

| Path | Purpose |
|------|---------|
| [`Assets/Scripts/ElbowTorque/`](Assets/Scripts/ElbowTorque/) | C# scripts (`BoxMass`, `ElbowTorqueOutput`, grab bridge) |
| [`Docs/QUEST3_TORQUE_MVP_SETUP.md`](Docs/QUEST3_TORQUE_MVP_SETUP.md) | Packages, Editor wiring, Quest build checklist |
| [`Docs/LINUX_QUEST3_DEPLOY.md`](Docs/LINUX_QUEST3_DEPLOY.md) | Build APK on Linux and install/run on Quest 3 via adb |
| [`arduino/TorquePrint/`](arduino/TorquePrint/) | UNO Q MCU sketch + Wi-Fi hotspot TCP server (`python/main.py`) |
| [`Assets/Scripts/Brace/`](Assets/Scripts/Brace/) | Quest TCP client (`BraceTcpClient`) for the UNO Q hotspot |
| [`Docs/QUEST3_HC06_TORQUE.md`](Docs/QUEST3_HC06_TORQUE.md) | Hotspot + TCP setup, Quest join, verify prints |

**Brace path (bidirectional):**

- Quest → MCU: `ElbowTorqueOutput` → `BraceTcpClient.SendForce` → hotspot TCP → Bridge → MCU (`forceN=`)
- MCU → Quest: pot on **A0** → `get_elbow_angle` → TCP `angleDeg=` → HUD `Arm angle: … deg`

## Status

Early stage. Hardware, firmware, and Unity integration are under active development.
