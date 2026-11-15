# Quest 3 Force MVP — Unity Setup

Target editor: **Unity 6** (e.g. `6000.6.x`, as in `~/projects/HapticFeedbackArm`).

Simple VR scene: grab boxes with the **right hand**; force in Newtons = `massKg × 9.81`. Force is logged to the Console, shown on a head-locked HUD, and sent over Wi-Fi TCP to the UNO Q when `BraceTcpClient` is present.

## Packages required

In **Window → Package Manager**, ensure:

| Package | Notes |
|---------|--------|
| **XR Interaction Toolkit** | Primary grab path. Install 3.x (Unity 6). |
| **XR Plugin Management** + **OpenXR** | Already assumed for Quest. |
| **Meta XR SDK** / OpenXR Meta feature group | Already assumed. |

No separate **TextMeshPro** package. In Unity 6, TMP ships inside **Unity UI** (`com.unity.ugui`). The auto HUD uses built-in `TextMesh` by default.

**Required sample (Option A):**

1. Package Manager → **XR Interaction Toolkit** → **Samples** → import **Starter Assets**.
2. You should get something like: `Assets/Samples/XR Interaction Toolkit/3.7.0/Starter Assets/`.

If using Meta Building Blocks for the rig instead, skip Option A and use Option B / `BoxGrabTorqueEvents`.

## Scripts (copy into your Unity project)

Already in this repo under `Assets/Scripts/ElbowTorque/` and `Assets/Scripts/Brace/`:

| Script | Role |
|--------|------|
| `BoxMass.cs` | Mass (kg) on each cube; syncs `Rigidbody.mass`. |
| `ElbowTorqueOutput.cs` | Force owner — HUD + log + `BraceTcpClient.SendForce`. |
| `RightHandGrabTorqueBridge.cs` | Right-hand XR grab → held mass. |
| `BoxGrabTorqueEvents.cs` | Optional Inspector UnityEvent hooks. |
| `BraceTcpClient.cs` | Quest → UNO Q hotspot TCP (`10.42.0.1:7777`). |

**Hardware path:** follow [`QUEST3_HC06_TORQUE.md`](QUEST3_HC06_TORQUE.md) (UNO Q hotspot + TCP + MCU).

---

## Editor setup (step by step)

### 1. XR rig (right-hand grab)

**Option A — Starter Assets XR Origin prefab (recommended)**

In XR Interaction Toolkit 3 / Unity 6, **GameObject → XR → XR Origin (VR)** only creates a thin rig. Prefer the Starter Assets prefab that includes Near-Far Interactors.

**Option B — Meta Building Blocks**

If using Meta Building Blocks for the rig instead, skip Option A and use Option B / `BoxGrabTorqueEvents`.

### 2. Force output + on-screen HUD

1. Create empty GameObject: `ElbowTorqueSystem`.
2. Add **`ElbowTorqueOutput`**.
3. Keep **Auto Create Head Locked Hud** enabled.
   - Runtime HUD: `Force: X.X N` / `Mass: Y.Y kg` / `WiFi: …`
4. **Gravity** default `9.81` → 1 kg ⇒ `9.81 N`, 10 kg ⇒ `98.1 N`.
5. Add **`BraceTcpClient`** on the same GameObject (`host=10.42.0.1`, `port=7777`) and assign it on `ElbowTorqueOutput` (already done in `SampleScene`).

Grab alone drives Console + HUD without hardware. Network send needs a Quest APK joined to the UNO Q hotspot.

### 3. Create grabbable dumbbells

Add Rigidbody + XRGrabInteractable + `BoxMass` (+ optional `DumbbellVisual`) for 1 / 3 / 10 kg.

### 4. Right-hand grab → force

1. Select the right controller **Near-Far Interactor**.
2. Add **`RightHandGrabTorqueBridge`**.
3. Optionally assign `ElbowTorqueSystem` as **Torque Output**.

**Alternative:** `BoxGrabTorqueEvents` on each box (right-hand path only).

### 5. Play / verify in Editor

1. Enter Play Mode (headset or mock).
2. Grab a 1 kg box with the right hand.
3. Console: `[ElbowForce] 9.810 N (mass=1.0 kg)`
4. HUD: `Force: 9.8 N`
5. Release → `0.0 N`

Editor does **not** require the UNO Q. For end-to-end hardware, build the APK — see [`QUEST3_HC06_TORQUE.md`](QUEST3_HC06_TORQUE.md) and [`LINUX_QUEST3_DEPLOY.md`](LINUX_QUEST3_DEPLOY.md).

## Force formula (MVP)

```text
forceN = heldMassKg * gravityMetersPerSecSq   // default g = 9.81
```

When nothing is held: `heldMassKg = 0` → `forceN = 0`.

`PublishForce` forwards to `BraceTcpClient.SendForce` when present.

Public API:

- `ElbowTorqueOutput.Instance.CurrentForceN`
- `ElbowTorqueOutput.Instance.SetHeldMass(float)`
- `ElbowTorqueOutput.Instance.ClearHeld()`

## Checklist

- [ ] XR Origin + right-hand grab interactor
- [ ] Dumbbells: Rigidbody + XRGrabInteractable + BoxMass
- [ ] `ElbowTorqueOutput` (+ auto HUD)
- [ ] `RightHandGrabTorqueBridge` on right Near-Far Interactor
- [ ] Grab → Console + HUD show Newtons; release → 0
- [ ] (Optional) UNO Q hotspot + `BraceTcpClient` — [`QUEST3_HC06_TORQUE.md`](QUEST3_HC06_TORQUE.md)
