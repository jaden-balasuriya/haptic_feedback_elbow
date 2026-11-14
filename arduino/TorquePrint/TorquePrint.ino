/*
 * TorquePrint — receive elbow torque commands over HC-06 UART and print them.
 *
 * Wiring (Uno/Nano-style):
 *   HC-06 VCC  -> 5V (or 3.3V if module requires it)
 *   HC-06 GND  -> GND
 *   HC-06 TXD  -> D10 (SoftwareSerial RX)
 *   HC-06 RXD  -> D11 via 1k/2k divider if Arduino is 5V (SoftwareSerial TX)
 *
 * Protocol: one ASCII line per command, newline-terminated, e.g. "5.000\n"
 * USB Serial Monitor: 115200 baud
 * HC-06 UART: 9600 8N1 (factory default)
 */

#include <SoftwareSerial.h>

const int BT_RX_PIN = 10;  // Arduino RX <- HC-06 TXD
const int BT_TX_PIN = 11;  // Arduino TX -> HC-06 RXD
const long BT_BAUD = 9600;
const long USB_BAUD = 115200;
const size_t LINE_BUF_SIZE = 64;

SoftwareSerial bt(BT_RX_PIN, BT_TX_PIN);

char lineBuf[LINE_BUF_SIZE];
size_t lineLen = 0;

void setup()
{
  Serial.begin(USB_BAUD);
  bt.begin(BT_BAUD);

  Serial.println(F("TorquePrint ready"));
  Serial.println(F("Waiting for torque lines on HC-06 (9600)..."));
}

void loop()
{
  while (bt.available() > 0)
  {
    char c = (char)bt.read();

    if (c == '\n' || c == '\r')
    {
      if (lineLen == 0)
        continue;

      lineBuf[lineLen] = '\0';
      Serial.print(F("torqueNm="));
      Serial.println(lineBuf);
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
