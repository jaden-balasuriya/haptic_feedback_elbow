/*
 * TorquePrint — receive force (Newtons) over UNO Q Wi-Fi and print it.
 *
 * Path:
 *   Quest TCP → UNO Q Linux (python/main.py :7777) → Bridge RPC → this MCU sketch → Monitor
 *
 * Protocol: one ASCII line per command, e.g. "9.810\n"
 * Do NOT open Serial1 here — RouterBridge owns it (/dev/ttyHS1 on Linux).
 *
 * FQBN: arduino:zephyr:unoq
 */

#include <Arduino_RouterBridge.h>

const size_t LINE_BUF_SIZE = 64;

char pendingLine[LINE_BUF_SIZE];
volatile bool hasPending = false;

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

void setup()
{
  Bridge.begin();
  Monitor.begin();

  Bridge.provide_safe("on_force_line", on_force_line);

  Monitor.println(F("ForcePrint ready (UNO Q Wi-Fi / Bridge)"));
  Monitor.println(F("Waiting for force lines (N) from python/main.py ..."));
}

void loop()
{
  if (!hasPending)
    return;

  hasPending = false;
  Monitor.print(F("forceN="));
  Monitor.println(pendingLine);
}
