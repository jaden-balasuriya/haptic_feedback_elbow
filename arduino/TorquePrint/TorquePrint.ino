/*
 * TorquePrint — receive elbow torque commands over HC-06 UART and print them.
 *
 * Target board: Arduino UNO Q (STM32U585 MCU + Qualcomm Linux MPU)
 *
 * Wiring (UNO Q — 3.3 V logic):
 *   HC-06 VCC  -> 3.3V
 *   HC-06 GND  -> GND
 *   HC-06 TXD  -> D0  (Serial1 RX)
 *   HC-06 RXD  -> D1  (Serial1 TX)
 *
 * Protocol: one ASCII line per command, newline-terminated, e.g. "5.000\n"
 * Debug output: Monitor (Router Bridge → App Lab / IDE Serial Monitor)
 * HC-06 UART: Serial1 @ 9600 8N1 (factory default)
 *
 * FQBN: arduino:zephyr:uno_q
 * Requires: Arduino_RouterBridge library
 */

#include <Arduino_RouterBridge.h>

const long BT_BAUD = 9600;
const size_t LINE_BUF_SIZE = 64;

char lineBuf[LINE_BUF_SIZE];
size_t lineLen = 0;

void setup()
{
  Monitor.begin();
  Serial1.begin(BT_BAUD);

  Monitor.println(F("TorquePrint ready (UNO Q)"));
  Monitor.println(F("Waiting for torque lines on Serial1 / HC-06 (9600)..."));
}

void loop()
{
  while (Serial1.available() > 0)
  {
    char c = (char)Serial1.read();

    if (c == '\n' || c == '\r')
    {
      if (lineLen == 0)
        continue;

      lineBuf[lineLen] = '\0';
      Monitor.print(F("torqueNm="));
      Monitor.println(lineBuf);
      lineLen = 0;
      continue;
    }

    if (lineLen + 1 < LINE_BUF_SIZE)
    {
      lineBuf[lineLen++] = c;
    }
    else
    {
      // Overflow: drop the line and resync on next newline.
      lineLen = 0;
    }
  }
}
