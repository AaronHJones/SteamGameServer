#!/usr/bin/env bash
# deploy.sh — Build and deploy Game Server Monitor
#
# LOCAL  (run directly on the Linux server):
#   ./deploy/deploy.sh --local
#
# REMOTE (run from Windows/WSL, SSH into the server):
#   ./deploy/deploy.sh [hostname]   # hostname defaults to your SSH alias or IP
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$PROJECT_ROOT"

APP_DIR="/opt/gameserver-ui"
PUBLISH_DIR="/tmp/gameserver-ui-publish"

# ── Detect local vs remote mode ──────────────────────────────────────────────
LOCAL=false
HOST=""
if [[ "${1:-}" == "--local" ]]; then
    LOCAL=true
else
    HOST="${1:-your-server}"
fi

# ── Build ────────────────────────────────────────────────────────────────────
echo "==> Building release..."
dotnet publish -c Release -r linux-x64 --self-contained false -o "$PUBLISH_DIR"

# ── Deploy ───────────────────────────────────────────────────────────────────
if $LOCAL; then
    echo "==> Local deploy to $APP_DIR ..."
    sudo mkdir -p "$APP_DIR"
    sudo chown "$(whoami):$(whoami)" "$APP_DIR"
    sudo rsync -a --delete "$PUBLISH_DIR/" "$APP_DIR/"

    echo "==> Installing systemd service..."
    sudo cp "$SCRIPT_DIR/gameserver-ui.service" /etc/systemd/system/
    sudo systemctl daemon-reload
    sudo systemctl enable gameserver-ui
    sudo systemctl restart gameserver-ui

    echo "==> Installing nginx config..."
    sudo cp "$SCRIPT_DIR/nginx.conf" /etc/nginx/sites-available/gameserver-ui.conf
    sudo ln -sf /etc/nginx/sites-available/gameserver-ui.conf \
                /etc/nginx/sites-enabled/gameserver-ui.conf
    sudo nginx -t && sudo systemctl reload nginx

else
    echo "==> Copying to $HOST:$APP_DIR ..."
    ssh "$HOST" "sudo mkdir -p $APP_DIR && sudo chown \$(whoami):\$(whoami) $APP_DIR"
    rsync -av --delete "$PUBLISH_DIR/" "$HOST:$APP_DIR/"

    echo "==> Installing systemd service..."
    scp "$SCRIPT_DIR/gameserver-ui.service" "$HOST:/tmp/gameserver-ui.service"
    ssh "$HOST" "sudo mv /tmp/gameserver-ui.service /etc/systemd/system/ && \
                 sudo systemctl daemon-reload && \
                 sudo systemctl enable gameserver-ui && \
                 sudo systemctl restart gameserver-ui"

    echo "==> Installing nginx config..."
    scp "$SCRIPT_DIR/nginx.conf" "$HOST:/tmp/gameserver-ui-nginx.conf"
    ssh "$HOST" "sudo cp /tmp/gameserver-ui-nginx.conf /etc/nginx/sites-available/gameserver-ui.conf && \
                 sudo ln -sf /etc/nginx/sites-available/gameserver-ui.conf \
                             /etc/nginx/sites-enabled/gameserver-ui.conf && \
                 sudo nginx -t && sudo systemctl reload nginx"
fi

# ── Post-install notes ───────────────────────────────────────────────────────
echo ""
echo "==> Done! App available at http://${HOST:-localhost}:5050"
echo "    (or http://${HOST:-localhost} if nginx is proxying port 80)"
echo ""
echo "--- To allow the app to start/stop game services without a password ---"
echo "Run: sudo visudo -f /etc/sudoers.d/gameserver-ui"
echo "Add: YOUR_USER ALL=(ALL) NOPASSWD: /bin/systemctl start *, /bin/systemctl stop *, /bin/systemctl restart *"
echo ""
echo "--- Or use user services instead (no sudo needed) ---"
echo "Run: loginctl enable-linger YOUR_USER"
echo "Move .service files to: ~/.config/systemd/user/"
echo "Then: systemctl --user enable rust-server"
