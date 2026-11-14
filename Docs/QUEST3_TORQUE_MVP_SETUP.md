# Quest 3 Elbow Torque MVP — Unity Setup

Target editor: **Unity 6** (e.g. `6000.6.x`, as in `~/projects/HapticFeedbackArm`).

Simple VR scene: grab boxes with the **right hand**; elbow torque = `mass × torquePerKg`. Torque is logged to the Console and shown on a floating head-locked HUD. Later, replace the publish hook with a network send to the brace.

## Packages required

In **Window → Package Manager**, ensure:

| Package | Notes |
|---------|--------|
| **XR Interaction Toolkit** | Primary grab path. Install 3.x (Unity 6). |
| **XR Plugin Management** + **OpenXR** | Already assumed for Quest. |
| **Meta XR SDK** / OpenXR Meta feature group | Already assumed. |

No separate **TextMeshPro** package. In Unity 6, TMP ships inside **Unity UI** (`com.unity.ugui`). The auto HUD uses it at runtime; if Unity prompts once for TMP Essential Resources, import them — otherwise you can ignore TMP entirely in Package Manager.

**Required sample (Option A):**

1. Package Manager → **XR Interaction Toolkit** → **Samples** → import **Starter Assets**.
2. You should get something like: `Assets/Samples/XR Interaction Toolkit/3.7.0/Starter Assets/`.

If using Meta Building Blocks for the rig instead, skip Option A and use Option B / `BoxGrabTorqueEvents`.

## Scripts (copy into your Unity project)

Already in this repo under `Assets/Scripts/ElbowTorque/`:

| Script | Role |
|--------|------|
| `BoxMass.cs` | Mass (kg) on each cube; syncs `Rigidbody.mass`. |
| `ElbowTorqueOutput.cs` | **Single torque owner** — UI + `Debug.Log` + HC-06 send via `BraceBluetoothClient`. |
| `RightHandGrabTorqueBridge.cs` | Listens to right-hand XR grab → drives torque. |
| `BoxGrabTorqueEvents.cs` | Optional Inspector UnityEvent hooks (alternative to the bridge). |

**Hardware:** add `BraceBluetoothClient` on `ElbowTorqueSystem` and follow [`QUEST3_HC06_TORQUE.md`](QUEST3_HC06_TORQUE.md) to pair the HC-06 on Quest.

---

## Editor setup (step by step)

### 1. XR rig (right-hand grab)

**Option A — Starter Assets XR Origin prefab (recommended for this MVP)**

In XR Interaction Toolkit 3 / Unity 6, **GameObject → XR → XR Origin (VR)** only creates:

```text
XR Origin (VR)
  └── Camera Offset
        └── Main Camera
```

That is expected — **no Left/Right Controller**. Do **not** use that menu item for this MVP.

Instead:

1. Import **Starter Assets** (see above) if you have not already.
2. Delete the scene’s default **Main Camera** (the prefab brings its own).
3. Project window → drag this prefab into the Hierarchy:
   - `Assets/Samples/XR Interaction Toolkit/<version>/Starter Assets/Prefabs/XR Origin (XR Rig)`
4. Confirm Hierarchy looks like:

```text
XR Origin (XR Rig)
  └── Camera Offset
        ├── Main Camera
        ├── Left Controller
        └── Right Controller   ← grab lives here
```

5. Expand **Right Controller**. You should see interactors from the sample, including **Near-Far Interactor** (XRIT 3’s grab/ray combo; older docs said “Direct / Ray”).
   - For near-only grab (no long-range ray grab): on **Near-Far Interactor**, disable **Enable Far Casting** (this project’s Starter Assets prefabs already have it off).
6. Hierarchy → **XR → Interaction Manager** if the scene does not already have one (Starter Assets often expects it).
7. **Project Settings → XR Plug-in Management → Android**: OpenXR enabled, with Meta Quest support / Meta feature group as you already use.

**Option B — Meta XR Building Blocks**

1. Use Meta’s controller/hand building blocks so you can grab objects.
2. Skip `RightHandGrabTorqueBridge` and instead wire each box’s grab events to `BoxGrabTorqueEvents` (section 4b).

### 2. Torque output + on-screen HUD

1. Create empty GameObject: `ElbowTorqueSystem`.
2. Add component **`ElbowTorqueOutput`**.
3. Leave **Torque Label** empty and keep **Auto Create Head Locked Hud** enabled (default).
   - At runtime a head-locked panel appears in view: `Torque: X.X Nm` / `Mass: Y.Y kg`.
   - Optional: assign your own TMP label instead to skip the auto HUD.
4. Set **Torque Per Kg** (e.g. `0.5` → 10 kg box ⇒ `5.0 Nm`).
5. Set **Log Interval Seconds** to `0.25` (or `0` for every frame).
6. **Brace Bluetooth:** Add **`BraceBluetoothClient`** on the same GameObject and assign it on `ElbowTorqueOutput` (already done in `SampleScene`). See [`QUEST3_HC06_TORQUE.md`](QUEST3_HC06_TORQUE.md).

No brace hardware is required for the VR MVP: grab alone drives Console + HUD torque. Bluetooth send only runs on a Quest APK.

### 3. Create grabbable dumbbells

For each mass (1, 3, 10 kg in the current scene):

1. Create an empty or cube GameObject, name e.g. `Dumbbell_1kg`, …
2. Add **`Rigidbody`** (gravity on; mass matches kg).
3. Add **`Box Collider`** (resized at runtime by `DumbbellVisual`).
4. Add **`XRGrabInteractable`**.
5. Add **`BoxMass`** (`Mass Kg` = 1 / 3 / 10, sync Rigidbody on).
6. Add **`DumbbellVisual`** — builds handle + weight plates from primitives; plate size/color scales with mass.
7. Place them on a table within near-grab reach of the right hand.

### 4a. Wire right-hand grab (automatic — preferred)

1. Under **XR Origin (XR Rig) → Camera Offset → Right Controller**, select the **Near-Far Interactor** (or whichever child has the grab `XRBaseInteractor` you use).
2. Add **`RightHandGrabTorqueBridge`** to that same GameObject.
3. Optionally drag `ElbowTorqueSystem` into **Torque Output** (otherwise it uses the singleton Instance).

That’s it — grab/release updates torque.

### 4b. Alternative: UnityEvents on each box

If you are not using XRIT interactors (e.g. Meta grab only):

1. On each box add **`BoxGrabTorqueEvents`** (needs `BoxMass`).
2. On the grab component’s **Select Entered** / **Select Exited** (or Meta equivalent UnityEvent):
   - Entered → `BoxGrabTorqueEvents.OnGrabbed`
   - Exited → `BoxGrabTorqueEvents.OnReleased`
3. Only wire the **right** hand’s grab path so left-hand grabs do not drive torque.

### 5. Build & run on Quest 3

1. **File → Build Settings → Android**, platform Switch if needed.
2. Run in Editor with Quest Link / Meta XR Simulator (Windows), or **Build and Run** to the headset.
3. Grab a box with the **right** controller:
   - Console: `[ElbowTorque] 5.000 Nm (mass=10.0 kg)` (example)
   - Label: `Elbow Torque: 5.0 Nm`
4. Release: torque / label go to `0.0 Nm`.

**Linux:** Quest Link is not available. See [`LINUX_QUEST3_DEPLOY.md`](LINUX_QUEST3_DEPLOY.md) for APK build + `adb install`.

---

## Torque formula (MVP)

```text
torqueNm = heldMassKg * torquePerKg
```

When nothing is held by the right hand: `heldMassKg = 0` → `torqueNm = 0`.

Later you can change the line in `ElbowTorqueOutput.Update()` to something like:

```csharp
currentTorqueNm = heldMassKg * torquePerKg * Mathf.Sin(elbowAngleRad);
```

---

## Finding the brace send hook

`PublishTorque` already forwards to `BraceBluetoothClient.SendTorque` when that component is present. Pairing / APK steps: [`QUEST3_HC06_TORQUE.md`](QUEST3_HC06_TORQUE.md).

Public API for other scripts:

- `ElbowTorqueOutput.Instance.CurrentTorqueNm`
- `ElbowTorqueOutput.Instance.SetHeldMass(float)`
- `ElbowTorqueOutput.Instance.ClearHeld()`

---

## Quick checklist

- [ ] Unity 6 project; XRIT Starter Assets imported
- [ ] Scene uses **XR Origin (XR Rig)** prefab (not bare XR Origin (VR)) — Right Controller present
- [ ] `ElbowTorqueOutput` in scene (auto HUD on, or your own label assigned)
- [ ] Dumbbells: Rigidbody + XRGrabInteractable + BoxMass + DumbbellVisual (1/3/10)
- [ ] `RightHandGrabTorqueBridge` on Right Controller’s **Near-Far Interactor**
- [ ] Grab right-handed → Console + HUD show non-zero torque; release → 0
- [ ] (Optional) HC-06 paired on Quest + `BraceBluetoothClient` on `ElbowTorqueSystem` — see [`QUEST3_HC06_TORQUE.md`](QUEST3_HC06_TORQUE.md)
