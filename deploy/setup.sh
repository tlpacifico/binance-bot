#!/bin/bash
set -euo pipefail

echo "=== Binance Bot Setup ==="
echo ""

# This script must be run as root on Contabo (or any VPS with root access)
if [ "$(whoami)" != "root" ]; then
  echo "ERROR: This script must be run as root."
  echo "  sudo bash deploy/setup.sh"
  exit 1
fi

# Must run from the project root
if [ ! -f "package.json" ]; then
  echo "ERROR: Run this script from the project root directory."
  echo "  cd /root/binance-bot && bash deploy/setup.sh"
  exit 1
fi

# Check .env exists
if [ ! -f ".env" ]; then
  echo "ERROR: .env file not found. Copy .env.example and fill in your values first."
  exit 1
fi

DEPLOY_USER="botuser"
BOT_DIR="/home/$DEPLOY_USER/binance-bot"

echo "[1/7] Creating dedicated user '$DEPLOY_USER'..."
if ! id "$DEPLOY_USER" &>/dev/null; then
  useradd -m -s /bin/bash "$DEPLOY_USER"
  echo "  Created user '$DEPLOY_USER'"
else
  echo "  User '$DEPLOY_USER' already exists"
fi

echo ""
echo "[2/7] Installing Node.js 20 LTS..."
if command -v node &> /dev/null && node -v | grep -q "v20"; then
  echo "  Node.js $(node -v) already installed, skipping."
else
  apt-get update -y
  curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
  apt-get install -y nodejs
fi
echo "  Node: $(node -v), npm: $(npm -v)"

echo ""
echo "[3/7] Installing build tools (for native modules)..."
apt-get install -y build-essential python3

echo ""
echo "[4/7] Copying project to $BOT_DIR..."
mkdir -p "$BOT_DIR"
cp -r . "$BOT_DIR/"
mkdir -p "$BOT_DIR/data"
chown -R "$DEPLOY_USER:$DEPLOY_USER" "$BOT_DIR"

echo ""
echo "[5/7] Installing dependencies and building..."
cd "$BOT_DIR"
# Install all deps (including devDependencies for tsc), build, then prune
sudo -u "$DEPLOY_USER" npm ci
sudo -u "$DEPLOY_USER" npm run build
# Remove devDependencies after build to save space
sudo -u "$DEPLOY_USER" npm prune --omit=dev

echo ""
echo "[6/7] Installing systemd service..."
sed "s/DEPLOY_USER/$DEPLOY_USER/g" deploy/binance-bot.service > /etc/systemd/system/binance-bot.service
systemctl daemon-reload
systemctl enable binance-bot
systemctl restart binance-bot

echo ""
echo "[7/7] Opening port 3000 in firewall..."
if command -v ufw &> /dev/null; then
  ufw allow 22/tcp 2>/dev/null || true
  ufw allow 3000/tcp 2>/dev/null || true
  ufw --force enable 2>/dev/null || true
  echo "  UFW: ports 22 and 3000 open"
else
  iptables -A INPUT -p tcp --dport 3000 -j ACCEPT 2>/dev/null || true
  echo "  iptables: port 3000 open"
fi

echo ""
echo "=== Setup complete! ==="
echo ""
echo "Check status:  systemctl status binance-bot"
echo "View logs:     journalctl -u binance-bot -f"
echo "Health check:  curl http://localhost:3000/api/health"
echo ""
echo "Your server IP for Binance API whitelist:"
curl -s ifconfig.me 2>/dev/null || echo "(could not detect)"
echo ""
