/*
 * TorquePrint — Quest motor PWM → L298N; elbow angle + MPU pitch → Quest.
 *
 * Paths:
 *   Quest TCP → UNO Q Linux (python/main.py :7777) → Bridge RPC → MCU (motor PWM)
 *   Pot A0 / MPU6050 → MCU → Bridge RPC → python/main.py → TCP → Quest HUD
 *
 * Motor protocol: one ASCII line = PWM 0..255, e.g. "180.000\n"
 *   (Quest maps elbow torque N·m → PWM; see ElbowTorqueOutput motor tuning.)
 * Angle protocol: "angleDeg=90.0\n" and "pitchDeg=12.3\n"
 *
 * L298N (single motor):
 *   ENA → D9 (PWM)   IN1 → D7   IN2 → D8
 *   L298N GND → UNO Q GND (common)   L298N 5V/logic → UNO Q 5V
 *   Motor supply (VS) → separate pack (e.g. 12V); NOT from UNO Q 5V
 *
 * Do NOT open Serial1 — RouterBridge owns it (/dev/ttyHS1 on Linux).
 * Pot: 3.3V / GND / A0.  MPU: 3.3V / GND / SDA(D20) / SCL(D21).
 *
 * FQBN: arduino:zephyr:unoq
 */

#include <Arduino_RouterBridge.h>
#include <Wire.h>
#include <stdlib.h>
#include <math.h>

const int POT_PIN = A0;
const int ADC_BITS = 10;
const int ADC_MAX = (1 << ADC_BITS) - 1; // 1023

// L298N channel A — PWM + direction (one-way drive for brace motor).
const int L298_ENA_PIN = 9;  // PWM
const int L298_IN1_PIN = 7;
const int L298_IN2_PIN = 8;
const int MOTOR_PWM_MAX = 255;

// Map raw ADC → elbow angle (degrees). Adjust after mounting the pot.
const float ANGLE_MIN_DEG = 0.0f;
const float ANGLE_MAX_DEG = 270.0f;
const float RAD2DEG = 57.2957795f;

const uint8_t MPU_ADDR_LO = 0x68; // AD0 low / floating
const uint8_t MPU_ADDR_HI = 0x69; // AD0 high
const uint8_t MPU_REG_SMPLRT_DIV = 0x19;
const uint8_t MPU_REG_CONFIG = 0x1A;
const uint8_t MPU_REG_GYRO_CONFIG = 0x1B;
const uint8_t MPU_REG_ACCEL_CONFIG = 0x1C;
const uint8_t MPU_REG_ACCEL_XOUT_H = 0x3B;
const uint8_t MPU_REG_PWR_MGMT_1 = 0x6B;
const uint8_t MPU_REG_PWR_MGMT_2 = 0x6C;
const uint8_t MPU_REG_WHO_AM_I = 0x75;
const float ACCEL_LSB_PER_G = 16384.0f; // ±2 g

const size_t LINE_BUF_SIZE = 64;
const unsigned long STATUS_PRINT_MS = 250;

char pendingLine[LINE_BUF_SIZE];
volatile bool hasPending = false;

volatile int latestRaw = 0;
volatile float latestAngleDeg = 0.0f;
volatile float latestPitchDeg = 0.0f;

float latestMotorPwm = 0.0f;

bool mpuOk = false;
uint8_t mpuAddr = MPU_ADDR_LO;
uint8_t mpuWho = 0;
float accelG[3] = {0, 0, 0};
int mpuFailStreak = 0;
const int MPU_FAIL_BEFORE_DROP = 8;

float rawToAngle(int raw)
{
  if (raw < 0)
    raw = 0;
  if (raw > ADC_MAX)
    raw = ADC_MAX;
  return ANGLE_MIN_DEG
         + (ANGLE_MAX_DEG - ANGLE_MIN_DEG) * ((float)raw / (float)ADC_MAX);
}

bool mpuWriteReg(uint8_t reg, uint8_t value)
{
  Wire.beginTransmission(mpuAddr);
  Wire.write(reg);
  Wire.write(value);
  return Wire.endTransmission() == 0;
}

bool mpuReadBytes(uint8_t reg, uint8_t *buf, size_t len)
{
  while (Wire.available() > 0)
    (void)Wire.read();

  Wire.beginTransmission(mpuAddr);
  Wire.write(reg);
  if (Wire.endTransmission(true) != 0)
    return false;

  size_t got = Wire.requestFrom((int)mpuAddr, (int)len);
  if (got != len)
    return false;
  for (size_t i = 0; i < len; i++)
  {
    int b = Wire.read();
    if (b < 0)
      return false;
    buf[i] = (uint8_t)b;
  }
  return true;
}

bool mpuWhoAccepted(uint8_t who)
{
  return who == 0x68 || who == 0x70 || who == 0x71 || who == 0x72
         || who == 0x73 || who == 0x98;
}

bool mpuProbeAddr(uint8_t addr)
{
  mpuAddr = addr;
  uint8_t who = 0;
  if (!mpuReadBytes(MPU_REG_WHO_AM_I, &who, 1))
    return false;
  mpuWho = who;
  return mpuWhoAccepted(who);
}

bool mpuReadAccel()
{
  uint8_t raw[6];
  if (!mpuReadBytes(MPU_REG_ACCEL_XOUT_H, raw, 6))
    return false;

  int16_t ax = (int16_t)((raw[0] << 8) | raw[1]);
  int16_t ay = (int16_t)((raw[2] << 8) | raw[3]);
  int16_t az = (int16_t)((raw[4] << 8) | raw[5]);
  accelG[0] = ((float)ax) / ACCEL_LSB_PER_G;
  accelG[1] = ((float)ay) / ACCEL_LSB_PER_G;
  accelG[2] = ((float)az) / ACCEL_LSB_PER_G;
  return true;
}

bool mpuBegin()
{
  Wire.begin();
  Wire.setClock(100000);
  delay(200);

  Monitor.println(F("MPU: scanning I2C 0x68/0x69 on Wire (SDA/SCL D20/D21) ..."));

  bool found = false;
  for (int attempt = 0; attempt < 5 && !found; attempt++)
  {
    found = mpuProbeAddr(MPU_ADDR_LO);
    if (!found)
      found = mpuProbeAddr(MPU_ADDR_HI);
    if (!found)
      delay(50);
  }

  if (!found)
  {
    for (uint8_t a = MPU_ADDR_LO; a <= MPU_ADDR_HI; a++)
    {
      Wire.beginTransmission(a);
      uint8_t st = Wire.endTransmission();
      Monitor.print(F("MPU: addr 0x"));
      Monitor.print(a, HEX);
      Monitor.print(F(" endTransmission="));
      Monitor.println(st);
    }
    Monitor.println(F("MPU6050 NOT FOUND — check VCC=3.3V, GND, SDA→D20, SCL→D21"));
    mpuFailStreak = 0;
    return false;
  }

  Monitor.print(F("MPU: found at 0x"));
  Monitor.print(mpuAddr, HEX);
  Monitor.print(F(" WHO_AM_I=0x"));
  Monitor.println(mpuWho, HEX);

  mpuWriteReg(MPU_REG_PWR_MGMT_1, 0x80);
  delay(100);
  if (!mpuWriteReg(MPU_REG_PWR_MGMT_1, 0x01))
  {
    Monitor.println(F("MPU: wake failed"));
    return false;
  }
  delay(50);
  mpuWriteReg(MPU_REG_PWR_MGMT_2, 0x00);
  mpuWriteReg(MPU_REG_SMPLRT_DIV, 0x04);
  mpuWriteReg(MPU_REG_CONFIG, 0x03);
  mpuWriteReg(MPU_REG_GYRO_CONFIG, 0x00);
  mpuWriteReg(MPU_REG_ACCEL_CONFIG, 0x00);
  delay(50);

  if (!mpuReadAccel())
  {
    Monitor.println(F("MPU: accel read failed"));
    return false;
  }
  mpuFailStreak = 0;
  Monitor.println(F("MPU: accel pitch ready (tilt vs ground)"));
  return true;
}

void updatePitch()
{
  if (!mpuOk)
    return;
  if (!mpuReadAccel())
  {
    mpuFailStreak++;
    if (mpuFailStreak >= MPU_FAIL_BEFORE_DROP)
    {
      mpuOk = false;
      Monitor.println(F("MPU: reads failing — will retry"));
    }
    return;
  }
  mpuFailStreak = 0;

  // Pitch vs ground from gravity. Sideways mount OK — tip up/down to change it.
  // +pitch ≈ nose up when +X points "forward" on the breakout.
  // Sitting flat (Z up) → pitch ≈ 0 by definition.
  float ax = accelG[0];
  float ay = accelG[1];
  float az = accelG[2];
  float pitch = atan2f(-ax, sqrtf(ay * ay + az * az)) * RAD2DEG;
  latestPitchDeg = pitch;
}

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

float get_elbow_angle()
{
  return latestAngleDeg;
}

int get_elbow_raw()
{
  return latestRaw;
}

float get_pitch_deg()
{
  return latestPitchDeg;
}

void applyMotorPwm(float cmd)
{
  if (cmd < 0.0f)
    cmd = 0.0f;
  if (cmd > (float)MOTOR_PWM_MAX)
    cmd = (float)MOTOR_PWM_MAX;
  latestMotorPwm = cmd;

  // Fixed direction; swap IN1/IN2 wiring or levels to reverse.
  digitalWrite(L298_IN1_PIN, HIGH);
  digitalWrite(L298_IN2_PIN, LOW);
  analogWrite(L298_ENA_PIN, (int)(cmd + 0.5f));
}

void setup()
{
  analogReadResolution(ADC_BITS);

  pinMode(L298_ENA_PIN, OUTPUT);
  pinMode(L298_IN1_PIN, OUTPUT);
  pinMode(L298_IN2_PIN, OUTPUT);
  applyMotorPwm(0.0f);

  Bridge.begin();
  Monitor.begin();

  Bridge.provide_safe("on_force_line", on_force_line);
  Bridge.provide("get_elbow_angle", get_elbow_angle);
  Bridge.provide("get_elbow_raw", get_elbow_raw);
  Bridge.provide("get_pitch_deg", get_pitch_deg);

  mpuOk = mpuBegin();

  Monitor.println(F("TorquePrint ready (motor PWM in + elbow/pitch out)"));
  Monitor.println(F("L298N: ENA=D9 IN1=D7 IN2=D8 | Pot A0 | MPU SDA/SCL D20/D21"));
}

void loop()
{
  int raw = analogRead(POT_PIN);
  float angle = rawToAngle(raw);
  latestRaw = raw;
  latestAngleDeg = angle;

  updatePitch();

  if (hasPending)
  {
    hasPending = false;
    float cmd = strtof(pendingLine, nullptr);
    applyMotorPwm(cmd);
  }

  static unsigned long lastPrint = 0;
  static unsigned long lastMpuRetryMs = 0;
  unsigned long now = millis();

  // Retry init every 2s if MPU was missing / dropped.
  if (!mpuOk && (now - lastMpuRetryMs) >= 2000UL)
  {
    lastMpuRetryMs = now;
    mpuOk = mpuBegin();
  }

  if (now - lastPrint >= STATUS_PRINT_MS)
  {
    lastPrint = now;
    Monitor.print(F("mpu="));
    Monitor.print(mpuOk ? F("ok") : F("FAIL"));
    Monitor.print(F(" angleDeg="));
    Monitor.print(angle, 1);
    Monitor.print(F(" pitchDeg="));
    if (mpuOk)
      Monitor.print(latestPitchDeg, 1);
    else
      Monitor.print(F("--.-"));
    Monitor.print(F(" motorPwm="));
    Monitor.println(latestMotorPwm, 0);
  }
}
