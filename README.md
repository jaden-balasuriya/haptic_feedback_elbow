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

## Quest 3 torque MVP

Grab boxes with the right hand; command elbow torque from box mass:

```text
torqueNm = massKg * torquePerKg
```

| Path | Purpose |
|------|---------|
| [`Assets/Scripts/ElbowTorque/`](Assets/Scripts/ElbowTorque/) | C# scripts (`BoxMass`, `ElbowTorqueOutput`, grab bridge) |
| [`Docs/QUEST3_TORQUE_MVP_SETUP.md`](Docs/QUEST3_TORQUE_MVP_SETUP.md) | Packages, Editor wiring, Quest build checklist |
| [`Docs/LINUX_QUEST3_DEPLOY.md`](Docs/LINUX_QUEST3_DEPLOY.md) | Build APK on Linux and install/run on Quest 3 via adb |
| [`arduino/TorquePrint/`](arduino/TorquePrint/) | UNO Q sketch: HC-06 on `Serial1` → print torque via `Monitor` |
| [`Docs/QUEST3_HC06_TORQUE.md`](Docs/QUEST3_HC06_TORQUE.md) | Quest → HC-06 → UNO Q wiring, CLI upload, App Lab monitor |

**Brace integration hook:** `ElbowTorqueOutput.PublishTorque(float torqueNm)` — replace the Debug.Log / UI path with a Bluetooth SPP send to the HC-06 when the Unity client is ready.

## Status

Early stage. Hardware, firmware, and Unity integration are under active development.
