#!/usr/bin/env bash
# Bring up Torque hotspot + containers after boot / when they fall over.
set -euo pipefail

SSID="${SSID:-UNO-Q-Torque}"
PASSWORD="${PASSWORD:-torquepilot}"
IFACE="${IFACE:-wlan0}"
APP_DIR="/home/arduino/ArduinoApps/torqueprint"
IMAGE="ghcr.io/arduino/app-bricks/python-apps-base:0.5.0"

log() { echo "[TorqueBoot] $*"; }

# Wait for NetworkManager + wlan0
for _ in $(seq 1 60); do
  if command -v nmcli >/dev/null && ip link show "$IFACE" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

nmcli radio wifi on || true

# Prefer Hotspot AP; recreate if missing
if ! nmcli -t -f NAME connection show | grep -qx Hotspot; then
  log "creating Hotspot '${SSID}'"
  nmcli device disconnect "$IFACE" 2>/dev/null || true
  nmcli device wifi hotspot ifname "$IFACE" ssid "$SSID" password "$PASSWORD" || true
fi

nmcli connection modify Hotspot connection.autoconnect yes connection.autoconnect-priority 100 2>/dev/null || true

if ! nmcli -t -f NAME,DEVICE connection show --active | grep -q "^Hotspot:"; then
  log "activating Hotspot"
  nmcli device disconnect "$IFACE" 2>/dev/null || true
  nmcli connection up Hotspot || nmcli device wifi hotspot ifname "$IFACE" ssid "$SSID" password "$PASSWORD" || true
fi

# Wait for docker
for _ in $(seq 1 60); do
  if docker info >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

ensure_container() {
  local name="$1"; shift
  if docker ps -a --format '{{.Names}}' | grep -qx "$name"; then
    docker start "$name" >/dev/null 2>&1 || true
  else
    log "creating $name"
    docker run -d --name "$name" --restart unless-stopped "$@"
  fi
}

# Wait briefly for arduino-router socket (Bridge)
for _ in $(seq 1 30); do
  if [ -S /var/run/arduino-router.sock ]; then
    break
  fi
  sleep 1
done

ensure_container torqueprint --network host --privileged \
  -v "${APP_DIR}:/app" \
  -v /var/run/arduino-router.sock:/var/run/arduino-router.sock \
  --user 0:0 \
  "$IMAGE"

ensure_container torque-connshim --network host --user 0:0 \
  -v "${APP_DIR}/python/connectivity_shim.py:/shim.py:ro" \
  --entrypoint python3 \
  "$IMAGE" /shim.py

log "hotspot=$(nmcli -t -f NAME,DEVICE connection show --active | grep Hotspot || echo down) containers=$(docker ps --format '{{.Names}}' | tr '\n' ' ')"
