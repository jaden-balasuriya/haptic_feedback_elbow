/*
 * TorquePrint — force (N) Quest→MCU, elbow angle (°) pot→Quest.
 *
 * Paths:
 *   Quest TCP → UNO Q Linux (python/main.py :7777) → Bridge RPC → MCU (force)
 *   Pot A0 → MCU → Bridge RPC → python/main.py → TCP → Quest HUD (angle)
 *
 * Force protocol: one ASCII line, e.g. "9.810\n"
 * Angle protocol: Linux sends "angleDeg=90.0\n" to Quest.
 *
 * Do NOT open Serial1 — RouterBridge owns it (/dev/ttyHS1 on Linux).
 * Pot: power from 3.3V only (A0 is not 5V-tolerant). Do not pinMode(A0).
 *
 * FQBN: arduino:zephyr:unoq
 */

#include <Arduino_RouterBridge.h>
#include <stdlib.h>

const int POT_PIN = A0;
const int ADC_BITS = 10;
const int ADC_MAX = (1 << ADC_BITS) - 1; // 1023

// Map raw ADC → elbow angle (degrees). Adjust after mounting the pot.
const float ANGLE_MIN_DEG = 0.0f;
const float ANGLE_MAX_DEG = 270.0f;
const float GRAVITY_MPS2 = 9.81f;

const size_t LINE_BUF_SIZE = 64;
const unsigned long STATUS_PRINT_MS = 250;

char pendingLine[LINE_BUF_SIZE];
volatile bool hasPending = false;

volatile int latestRaw = 0;
volatile float latestAngleDeg = 0.0f;

float latestForceN = 0.0f;
float latestMassKg = 0.0f;

float rawToAngle(int raw)
{
  if (raw < 0)
    raw = 0;
  if (raw > ADC_MAX)
    raw = ADC_MAX;
  return ANGLE_MIN_DEG
         + (ANGLE_MAX_DEG - ANGLE_MIN_DEG) * ((float)raw / (float)ADC_MAX);
}

// Called from Linux Python via Bridge.call("on_force_line", "...").
// Only copy data here — never call Monitor/Bridge from inside an RPC callback.
void on_force_line(String line)
{
  size_t n = line.length();
  if (n + 1 > LINE_BUF_SIZE)
    n = LINE_BUF_SIZE - 1;

  for (size_t i = 0; i < n; i++)
    pendingLine[i] = line.charAt(i);
  pendingLine[n] = '\0';
  hasPending = true;
}

// Python polls this for elbow angle (degrees). Keep short; no Monitor here.
float get_elbow_angle()
{
  return latestAngleDeg;
}

// Optional raw ADC (0..ADC_MAX) for calibration.
int get_elbow_raw()
{
  return latestRaw;
}

void setup()
{
  // Do not pinMode(A0, INPUT) on UNO Q — breaks analogRead for some cores.
  analogReadResolution(ADC_BITS);

  Bridge.begin();
  Monitor.begin();

  Bridge.provide_safe("on_force_line", on_force_line);
  Bridge.provide("get_elbow_angle", get_elbow_angle);
  Bridge.provide("get_elbow_raw", get_elbow_raw);

  Monitor.println(F("TorquePrint ready (force in + elbow angle out)"));
  Monitor.println(F("Pot on A0 (3.3V / GND / SIG). Waiting for Quest ..."));
}

void loop()
{
  int raw = analogRead(POT_PIN);
  float angle = rawToAngle(raw);
  latestRaw = raw;
  latestAngleDeg = angle;

  if (hasPending)
  {
    hasPending = false;
    latestForceN = strtof(pendingLine, nullptr);
    if (latestForceN < 0.0f)
      latestForceN = 0.0f;
    latestMassKg = latestForceN / GRAVITY_MPS2;
  }

  static unsigned long lastPrint = 0;
  unsigned long now = millis();
  if (now - lastPrint >= STATUS_PRINT_MS)
  {
    lastPrint = now;
    Monitor.print(F("angleDeg="));
    Monitor.print(angle, 1);
    Monitor.print(F(" massKg="));
    Monitor.print(latestMassKg, 1);
    Monitor.print(F(" forceN="));
    Monitor.println(latestForceN, 3);
  }
}
