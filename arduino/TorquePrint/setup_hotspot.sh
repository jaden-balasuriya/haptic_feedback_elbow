#!/usr/bin/env bash
# Create a Wi-Fi hotspot on the UNO Q so the Quest can join a private LAN.
# Also installs a DNS override + HTTP 204 shim so Android validates the network
# (otherwise Quest apps often cannot open TCP to 10.42.0.1).

set -euo pipefail

SSID="${1:-UNO-Q-Torque}"
PASSWORD="${2:-torquepilot}"
IFACE="${3:-wlan0}"

if [ "${#PASSWORD}" -lt 8 ]; then
  echo "Password must be at least 8 characters" >&2
  exit 1
fi

echo "[TorqueWiFi] Enabling radio and starting hotspot '${SSID}' on ${IFACE}..."
nmcli radio wifi on
nmcli device disconnect "${IFACE}" 2>/dev/null || true
nmcli device wifi hotspot ifname "${IFACE}" ssid "${SSID}" password "${PASSWORD}"
nmcli connection modify Hotspot connection.autoconnect yes || true

# Point hotspot clients at this board for DNS so we can fake Android captive-portal checks.
mkdir -p /etc/NetworkManager/dnsmasq-shared.d
cat >/etc/NetworkManager/dnsmasq-shared.d/android-connectivity.conf <<'EOF'
# Make Android/Quest connectivity checks resolve to the UNO Q itself.
address=/connectivitycheck.gstatic.com/10.42.0.1
address=/www.google.com/10.42.0.1
address=/clients3.google.com/10.42.0.1
address=/clients4.google.com/10.42.0.1
address=/play.googleapis.com/10.42.0.1
EOF
nmcli connection up Hotspot || true

# HTTP 204 responder on port 80 (host network; needs root).
if command -v docker >/dev/null 2>&1; then
  docker rm -f torque-connshim 2>/dev/null || true
  docker run -d --name torque-connshim --restart unless-stopped --network host --user 0:0 \
    -v /home/arduino/ArduinoApps/torqueprint/python/connectivity_shim.py:/shim.py:ro \
    --entrypoint python3 \
    ghcr.io/arduino/app-bricks/python-apps-base:0.5.0 \
    /shim.py || true
fi

sleep 1
echo
echo "[TorqueWiFi] Hotspot up."
nmcli -f GENERAL,IP4 device show "${IFACE}" 2>/dev/null | sed -n '1,20p' || true
echo
IP="$(hostname -I 2>/dev/null | awk '{print $1}')"
echo "On Quest: Settings → Wi-Fi → join '${SSID}' (password: ${PASSWORD})"
echo "If Quest says 'No internet', tap the network → keep / use without internet once;"
echo "Unity BraceTcpClient host should be 10.42.0.1:7777."
echo "This board currently reports: ${IP:-unknown}"
