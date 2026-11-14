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

Optional samples (recommended once):

1. Package Manager → XR Interaction Toolkit → **Samples** → import **Starter Assets**.
2. If using Meta Building Blocks for the rig, you can still add XRIT interactors, or wire grab via `BoxGrabTorqueEvents` UnityEvents (see below).

## Scripts (copy into your Unity project)

Already in this repo under `Assets/Scripts/ElbowTorque/`:

| Script | Role |
|--------|------|
| `BoxMass.cs` | Mass (kg) on each cube; syncs `Rigidbody.mass`. |
| `ElbowTorqueOutput.cs` | **Single torque owner** — UI + `Debug.Log` + future brace send. |
| `RightHandGrabTorqueBridge.cs` | Listens to right-hand XR grab → drives torque. |
| `BoxGrabTorqueEvents.cs` | Optional Inspector UnityEvent hooks (alternative to the bridge). |

**Hardware hook:** open `ElbowTorqueOutput.PublishTorque()` — that is the one method to replace with a network send.

---

## Editor setup (step by step)

### 1. XR rig (right-hand grab)

Use whichever you already have for Quest:

**Option A — XR Interaction Toolkit XR Origin (recommended for this MVP)**

1. Hierarchy → right-click → **XR → XR Origin (XR Rig)** (or use Meta Camera Rig + add XRIT interactors).
2. Under **Camera Offset → Right Controller**, ensure there is an **XR Direct Interactor** (near-grab) and/or **XR Ray Interactor**.
3. Add a **XR Interaction Manager** in the scene if one is not created automatically.
4. Confirm **Project Settings → XR Plug-in Management → Android** has OpenXR enabled, with Meta Quest support / Meta feature group as you already use.

**Option B — Meta XR Building Blocks**

1. Use Meta’s controller/hand building blocks so you can grab objects.
2. Skip `RightHandGrabTorqueBridge` and instead wire each box’s grab events to `BoxGrabTorqueEvents` (section 4b).

### 2. Torque output + on-screen HUD

1. Create empty GameObject: `ElbowTorqueSystem`.
2. Add component **`ElbowTorqueOutput`**.
3. Leave **Torque Label** empty and keep **Auto Create Head Locked Hud** enabled (default).
   - At runtime a HUD appears in front of the headset: `Elbow Torque: X.X Nm`.
   - Optional: assign your own world-space text label instead; that disables the need for auto HUD.
4. Set **Torque Per Kg** (e.g. `0.5` → 10 kg box ⇒ `5.0 Nm`).
5. Set **Log Interval Seconds** to `0.25` (or `0` for every frame).

No brace hardware is required for this MVP: VR grab alone drives Console + HUD torque.

### 3. Create 4 grabbable boxes

For each mass (1, 3, 5, 10 kg):

1. **3D Object → Cube**, name e.g. `Box_1kg`, `Box_3kg`, …
2. Scale / color them differently so they are easy to tell apart.
3. Add **`Rigidbody`**:
   - Use Gravity: on
   - Mass: match the box (1 / 3 / 5 / 10) — also set on `BoxMass`
4. Add **`Box Collider`** (default cube already has one).
5. Add **`XRGrabInteractable`** (XR Interaction Toolkit):
   - Interaction Layer Mask: Default (or your grab layer)
   - Movement Type: Instantaneous or Velocity Tracking
6. Add **`BoxMass`**:
   - **Mass Kg**: `1` / `3` / `5` / `10`
   - **Sync Rigidbody Mass**: enabled
7. Place boxes on a table / floor within reach of the right hand.

### 4a. Wire right-hand grab (automatic — preferred)

1. Select the **Right Controller** GameObject that has **XR Direct Interactor** (or the interactor you use to grab).
2. Add **`RightHandGrabTorqueBridge`**.
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
2. Run in Editor with Quest Link / Meta XR Simulator, or **Build and Run** to the headset.
3. Grab a box with the **right** controller:
   - Console: `[ElbowTorque] 5.000 Nm (mass=10.0 kg)` (example)
   - Label: `Elbow Torque: 5.0 Nm`
4. Release: torque / label go to `0.0 Nm`.

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

In `ElbowTorqueOutput.cs`:

```csharp
void PublishTorque(float torqueNm)
{
    UpdateLabel(torqueNm);
    MaybeLog(torqueNm);

    // TODO: BraceClient.SendElbowTorque(torqueNm);
}
```

Public API for other scripts:

- `ElbowTorqueOutput.Instance.CurrentTorqueNm`
- `ElbowTorqueOutput.Instance.SetHeldMass(float)`
- `ElbowTorqueOutput.Instance.ClearHeld()`

---

## Quick checklist

- [ ] Unity 6 project; XR Interaction Toolkit installed; XR Origin + right Direct/Ray Interactor
- [ ] `ElbowTorqueOutput` in scene (auto HUD on, or your own label assigned)
- [ ] 4 cubes: Rigidbody + XRGrabInteractable + BoxMass (1/3/5/10)
- [ ] `RightHandGrabTorqueBridge` on right interactor
- [ ] Grab right-handed → Console + HUD show non-zero torque; release → 0
