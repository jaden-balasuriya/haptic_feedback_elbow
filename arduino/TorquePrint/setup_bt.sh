#!/usr/bin/env bash
# One-shot Bluetooth prep on the UNO Q Linux side (run via SSH or local terminal).
# Makes the board discoverable as Arduino-UNO-Q for Quest pairing.

set -euo pipefail

ALIAS="${1:-Arduino-UNO-Q}"
CHANNEL="${2:-1}"

echo "[TorqueBT] Enabling adapter and advertising as '${ALIAS}'..."
bluetoothctl power on
bluetoothctl pairable on
# 0 = stay discoverable until turned off (default BlueZ timeout is ~180s)
bluetoothctl discoverable-timeout 0
bluetoothctl discoverable on
bluetoothctl system-alias "${ALIAS}" || bluetoothctl system-alias "${ALIAS}"

if command -v sdptool >/dev/null 2>&1; then
  sdptool add --channel="${CHANNEL}" SP || true
  echo "[TorqueBT] SDP Serial Port profile registered on channel ${CHANNEL}"
else
  echo "[TorqueBT] sdptool not found — pair first, then rely on RFCOMM channel ${CHANNEL}"
fi

echo
echo "On Quest: Settings → Bluetooth → pair '${ALIAS}'"
echo "Then run: python3 python/main.py   (or App Lab Run)"
echo "Quest Settings may still say 'Not connected' until the Unity APK opens SPP — that is normal."
