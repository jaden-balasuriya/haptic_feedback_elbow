# Elbow joint torque calculation (Quest)

Quest-side model that turns **hand weight**, **upper-arm roll** (MPU6050), and **elbow angle** (potentiometer) into **elbow joint torque** \(\tau\) in **N·m**.

That torque is what the HUD shows and what is sent to the UNO Q as the TCP `forceN=` field (same wire format; the number means torque, not linear Newtons).

Implementation: `Assets/Scripts/ElbowTorque/ElbowTorqueOutput.cs`.

---

## Sensors and conventions

| Input | Source | Convention |
|-------|--------|------------|
| Hand mass \(m\) | Grabbed `BoxMass` (kg) | 0 when nothing held |
| Upper-arm roll \(\theta_u\) | MPU on upper arm → TCP `rollDeg=` | **0° = upper arm straight down**; increases as the upper arm raises forward |
| Elbow angle | Pot → TCP `angleDeg=` | **100° when straight**; **increases as the arm closes** |

Inspector knobs on `ElbowTorqueOutput`:

| Field | Default | Meaning |
|-------|---------|---------|
| `forearmLengthMeters` | `0.27` | Elbow → hand length \(L\) (m). ≈ average adult forearm + grip |
| `elbowExtendedDeg` | `100` | Pot reading at full extension. Flexion \(=\) `angleDeg` − this |
| `rollDownOffsetDeg` | `-90` | Calibration: `θ_u = rollDeg − offset` so 0 = straight down (this mount reads ≈ −90° raw when hanging) |
| `gravityMetersPerSecSq` | `9.81` | \(g\) |

If pitch or elbow angle has not been received yet, \(\tau = 0\) (cannot form the pose).

---

## Geometry (sagittal plane)

```text
        down (gravity)
           |
           v
           ● shoulder
          /
         /  θ_u  = upper arm from downward vertical
        ● elbow
         \
          \  θ_f = forearm from downward vertical
           ● hand  ← weight W = m g  (vertical)
```

1. **Upper arm from down**

   \[
   \theta_u = \texttt{rollDeg} - \texttt{rollDownOffsetDeg}
   \]

2. **Elbow flexion from full extension** (0° = straight; grows as the arm closes)

   \[
   \theta_{\mathrm{flex}} = \texttt{angleDeg} - \texttt{elbowExtendedDeg}
   \]

   Example: straight ≈ `100`, closing toward a right angle ≈ `190` → \(\theta_{\mathrm{flex}} \approx 90°\).

3. **Forearm from down**

   With the upper arm down and the elbow straight, the forearm continues downward (\(\theta_f = 0\)). Flexing the elbow rotates the forearm away from vertical by \(\theta_{\mathrm{flex}}\). Raising the upper arm adds \(\theta_u\):

   \[
   \theta_f = \theta_u + \theta_{\mathrm{flex}}
   \]

---

## Torque formula

Hand weight (vertical):

\[
W = m\, g
\]

Moment arm about the elbow for a vertical load is the **horizontal** distance from elbow to the hand’s line of action:

\[
d = L\, \lvert\sin\theta_f\rvert
\]

Elbow torque magnitude:

\[
\boxed{\tau = W\, L\, \lvert\sin\theta_f\rvert = m\, g\, L\, \lvert\sin\theta_f\rvert}
\]

Units: \(m\) in kg, \(g\) in m/s², \(L\) in m → \(\tau\) in **N·m**.

| Pose | \(\theta_f\) | \(\lvert\sin\theta_f\rvert\) | Torque |
|------|--------------|------------------------------|--------|
| Forearm vertical (hanging) | 0° | 0 | 0 |
| Forearm horizontal | ±90° | 1 | \(m g L\) (max for that mass) |
| Upper arm down, elbow 90° | 90° | 1 | \(m g L\) |

---

## Worked example

- \(m = 1\,\mathrm{kg}\), \(g = 9.81\), \(L = 0.27\,\mathrm{m}\)
- Upper arm horizontal: \(\theta_u = 90°\)
- Elbow flexed 90° from straight: \(\theta_{\mathrm{flex}} = 90°\) → pot `angleDeg = 190` if `elbowExtendedDeg = 100`
- \(\theta_f = 180°\), \(\lvert\sin 180°\rvert = 0\) → \(\tau = 0\) (forearm pointing straight up)

Another case — upper arm down, elbow 90°:

- \(\theta_u = 0\), \(\theta_{\mathrm{flex}} = 90°\), \(\theta_f = 90°\)
- \(\tau = 1 \times 9.81 \times 0.27 \times 1 \approx 2.65\,\mathrm{N·m}\)

---

## Motor mapping (Quest → L298N)

After \(\tau\) is computed, Quest maps it to a **PWM command** \(u \in [0,\,u_{\max}]\):

\[
u = \mathrm{clamp}\!\left(\frac{\tau}{\tau_{\mathrm{sat}}}\, u_{\max},\; 0,\; u_{\max}\right)
\]

| Inspector field | Symbol | Default | Meaning |
|-----------------|--------|---------|---------|
| `torqueAtMaxMotorNm` | \(\tau_{\mathrm{sat}}\) | `5` | Torque (N·m) at which motor hits max |
| `maxMotorOutput` | \(u_{\max}\) | `100` | Max PWM (0–255) sent to the UNO Q |

- \(\tau = 0\) → motor off  
- \(0 < \tau < \tau_{\mathrm{sat}}\) → linear  
- \(\tau \ge \tau_{\mathrm{sat}}\) → held at \(u_{\max}\)

TCP still sends one ASCII number per line; that number is now **PWM**, not N·m. HUD still shows torque.

### L298N wiring (UNO Q)

| L298N | UNO Q / supply |
|-------|----------------|
| **ENA** | **D9** (PWM) |
| **IN1** | **D7** |
| **IN2** | **D8** |
| **GND** | **GND** (common with motor pack) |
| **5V** (logic) | **5V** on UNO Q |
| **VS** / VMS (motor) | Separate motor supply (e.g. 12 V) — **not** board 5V |
| **OUT1 / OUT2** | Motor leads |

Direction is fixed in firmware (`IN1=HIGH`, `IN2=LOW`). Swap motor leads or invert those levels to reverse.

## Output path

```text
Grab mass (kg)
  + rollDeg (MPU upper arm)
  + angleDeg (elbow pot)
       │
       ▼
ElbowTorqueOutput.ComputeElbowTorqueNm()  →  τ (N·m)
       │
       ▼
TorqueToMotorOutput()  →  PWM 0..maxMotorOutput
       │
       ├─ HUD: "Torque: … N·m" / "Motor: …%"
       └─ BraceTcpClient.SendForce(PWM)  →  ASCII over TCP
              └─ MCU analogWrite(D9, PWM) → L298N ENA
```

---

## Calibration tips

1. **Roll zero:** Hold the upper arm straight down. Raw `rollDeg` on this mount is about **−90°**; default `rollDownOffsetDeg = -90` makes \(\theta_u \approx 0\). Adjust the offset if your zero drifts.
2. **Elbow extended:** Fully straighten the elbow; set `elbowExtendedDeg` to the pot’s `angleDeg` reading (this brace ≈ **100**).
3. **Forearm length:** Measure elbow axis → center of the held object; put that in `forearmLengthMeters` if 0.27 m is off.

---

## Assumptions / limits

- Sagittal-plane model only (no shoulder rotation / abduction in the torque).
- Weight acts at the hand along gravity; forearm mass and dynamics are ignored (static gravity torque on the held mass).
- Pot increases as the arm closes; calibrate `elbowExtendedDeg` to the straight-arm reading.
- TCP field name remains `forceN` for MCU compatibility; semantics on Quest are **joint torque (N·m)**.
