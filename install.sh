#!/usr/bin/env bash
set -euo pipefail

API_URL="${1:-}"
SERVICE_NAME="telemetry-collector-agent"
INSTALL_DIR="/opt/telemetry-collector-agent"
ASSET_URL="https://github.com/vlad595/TelemetryCollector-Service/releases/latest/download/telemetry-agent-linux-x64.tar.gz"

if [[ -z "$API_URL" || ! "$API_URL" =~ ^https?:// ]]; then
  echo "Usage: sudo bash install.sh https://api.example.com/metrics" >&2
  exit 2
fi
if [[ "$API_URL" == *$'\n'* || "$API_URL" == *$'\r'* ]]; then
  echo "ApiUrl must be a single-line URL." >&2
  exit 2
fi
if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run as root, for example: curl -fsSL <installer-url> | sudo bash -s -- https://api.example.com/metrics" >&2
  exit 1
fi
if ! command -v systemctl >/dev/null; then
  echo "This installer requires a systemd based Linux distribution." >&2
  exit 1
fi

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT
curl -fsSL "$ASSET_URL" -o "$tmp_dir/agent.tar.gz"
install -d -m 0755 "$INSTALL_DIR"
tar -xzf "$tmp_dir/agent.tar.gz" -C "$INSTALL_DIR"
chmod 0755 "$INSTALL_DIR/TelemetryCollectorService.Agent"

cat > /etc/telemetry-collector-agent.env <<EOF
ApiUrl=$API_URL
EOF
chmod 0600 /etc/telemetry-collector-agent.env

cat > "/etc/systemd/system/${SERVICE_NAME}.service" <<EOF
[Unit]
Description=Telemetry Collector Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=exec
EnvironmentFile=/etc/telemetry-collector-agent.env
ExecStart=${INSTALL_DIR}/TelemetryCollectorService.Agent
Restart=always
RestartSec=5
User=root

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now "$SERVICE_NAME"
echo "Installed and started $SERVICE_NAME."
