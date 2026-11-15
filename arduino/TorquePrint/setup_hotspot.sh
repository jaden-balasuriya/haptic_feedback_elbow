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
nmcli connection modify Hotspot \
  connection.autoconnect yes \
  connection.autoconnect-priority 100 || true

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

APP_DIR="$(cd "$(dirname "$0")" && pwd)"
IMAGE="ghcr.io/arduino/app-bricks/python-apps-base:0.5.0"

# HTTP 204 responder on port 80 + TCP bridge (survive reboot via unless-stopped).
if command -v docker >/dev/null 2>&1; then
  docker rm -f torque-connshim torqueprint 2>/dev/null || true
  docker run -d --name torque-connshim --restart unless-stopped --network host --user 0:0 \
    -v "${APP_DIR}/python/connectivity_shim.py:/shim.py:ro" \
    --entrypoint python3 \
    "${IMAGE}" \
    /shim.py || true
  docker run -d --name torqueprint --restart unless-stopped --network host --privileged \
    -v "${APP_DIR}:/app" \
    -v /var/run/arduino-router.sock:/var/run/arduino-router.sock \
    --user 0:0 \
    "${IMAGE}" || true
fi

# Boot ensure (hotspot + containers) — system systemd unit.
if [ -f "${APP_DIR}/ensure_boot.sh" ]; then
  chmod +x "${APP_DIR}/ensure_boot.sh" || true
  cat >/etc/systemd/system/torque-boot.service <<EOF
[Unit]
Description=Ensure UNO-Q-Torque hotspot and torque docker apps
After=network.target NetworkManager.service docker.service arduino-router.service
Wants=NetworkManager.service docker.service

[Service]
Type=oneshot
User=arduino
Group=arduino
Environment=HOME=/home/arduino
ExecStart=${APP_DIR}/ensure_boot.sh
RemainAfterExit=yes

[Install]
WantedBy=multi-user.target
EOF
  cat >/etc/systemd/system/torque-boot.timer <<'EOF'
[Unit]
Description=Re-run Torque boot ensure after delayed boot settle

[Timer]
OnBootSec=1min
OnBootSec=3min
AccuracySec=10s
Unit=torque-boot.service

[Install]
WantedBy=timers.target
EOF
  systemctl daemon-reload || true
  systemctl enable --now torque-boot.service torque-boot.timer || true
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
echo "Boot persistence: Hotspot autoconnect + docker unless-stopped + torque-boot.service/timer"
