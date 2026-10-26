#!/usr/bin/env bash
# Billcora installer and updater for Linux (needs systemd).
# Install or update:
#   curl -fsSL https://github.com/SINOBI-Melloflex/billkora/releases/latest/download/install.sh | sudo bash
set -euo pipefail

REPO="SINOBI-Melloflex/billkora"
BASE="https://github.com/${REPO}/releases/latest/download"
APP="${BILLCORA_DIR:-/opt/billcora}"
SVC="billcora"
NODE_MAJOR="22"
RT="${APP}/runtime"
BACKUP=""

say()  { printf '\033[36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[33m%s\033[0m\n' "$*"; }
die()  { printf '\033[31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

[ "$(uname -s)" = "Linux" ] || die "This installer is for Linux only."
[ "$(id -u)" -eq 0 ] || die "Please run as root. Example: curl -fsSL ${BASE}/install.sh | sudo bash"
command -v systemctl >/dev/null 2>&1 || die "systemd is required (systemctl was not found)."
[ -d /run/systemd/system ] || die "systemd is not running on this machine."
for c in curl tar sha256sum awk grep; do
  command -v "$c" >/dev/null 2>&1 || die "'$c' is required. Please install it and run again."
done
LDD="$(ldd --version 2>&1 || true)"
case "$LDD" in *musl*) die "musl based Linux (Alpine) is not supported. Use Ubuntu, Debian, Fedora, Rocky or similar." ;; esac
case "$(uname -m)" in
  x86_64|amd64)  NARCH="x64" ;;
  aarch64|arm64) NARCH="arm64" ;;
  *) die "Unsupported CPU: $(uname -m). A 64-bit x86 or ARM CPU is needed." ;;
esac
case "$APP" in ""|/|/usr|/etc|/bin|/var|/home|/root) die "Unsafe install folder: $APP" ;; esac

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

dl() { curl -fL --retry 3 --connect-timeout 20 -sS -o "$2" "$1" || die "Download failed: $1"; }
port_busy() { (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null; }

build_tools() {
  if command -v apt-get >/dev/null 2>&1; then
    DEBIAN_FRONTEND=noninteractive apt-get update -y >/dev/null 2>&1 || true
    DEBIAN_FRONTEND=noninteractive apt-get install -y python3 make g++ >/dev/null 2>&1 || true
  elif command -v dnf >/dev/null 2>&1; then
    dnf install -y python3 make gcc-c++ >/dev/null 2>&1 || true
  elif command -v yum >/dev/null 2>&1; then
    yum install -y python3 make gcc-c++ >/dev/null 2>&1 || true
  fi
}

install_deps() {
  ( cd "$APP" && export PATH="${RT}/bin:${PATH}" && { npm ci --omit=dev --no-audit --no-fund || npm install --omit=dev --no-audit --no-fund; } )
}

rollback() {
  warn "Restoring your previous version..."
  if [ -n "$BACKUP" ] && [ -d "$BACKUP" ]; then
    systemctl stop "$SVC" 2>/dev/null || true
    cp -a "${BACKUP}/." "${APP}/" || true
    install_deps >/dev/null 2>&1 || true
    chown -R billcora:billcora "$APP" 2>/dev/null || true
    systemctl start "$SVC" 2>/dev/null || true
    warn "Your old version was restored. Your data is safe. Backup folder: ${BACKUP}"
  fi
}

UPDATE=0
if [ -f "${APP}/server.js" ]; then UPDATE=1; fi
OLDVER="none"
if [ -f "${APP}/version.txt" ]; then OLDVER="$(tr -d '\r\n ' < "${APP}/version.txt")"; fi

PORT=""
if [ "$UPDATE" = 1 ] && [ -f "${APP}/port.txt" ]; then PORT="$(tr -d '\r\n ' < "${APP}/port.txt")"; fi
case "$PORT" in
  ''|*[!0-9]*) PORT=4010; while port_busy "$PORT" && [ "$PORT" -lt 4040 ]; do PORT=$((PORT + 1)); done ;;
esac

say "Checking the latest Billcora release on GitHub..."
dl "${BASE}/version.txt" "${TMP}/version.txt"
NEWVER="$(tr -d '\r\n ' < "${TMP}/version.txt")"
case "$NEWVER" in ''|*[!0-9A-Za-z._-]*) die "Bad version text received from GitHub." ;; esac
dl "${BASE}/billcora-linux.tar.gz" "${TMP}/app.tar.gz"
dl "${BASE}/billcora-linux.sha256" "${TMP}/app.sha256"
WANT="$(awk '{print tolower($1)}' "${TMP}/app.sha256" | head -n1)"
GOT="$(sha256sum "${TMP}/app.tar.gz" | awk '{print $1}')"
if [ -z "$WANT" ] || [ "$WANT" != "$GOT" ]; then die "Checksum mismatch. The download is damaged. Nothing was changed."; fi
if [ "$UPDATE" = 1 ]; then say "Existing Billcora found (${OLDVER}). Updating to ${NEWVER}."; else say "Fresh install of Billcora ${NEWVER}."; fi

mkdir -p "$APP"
if [ ! -x "${RT}/bin/node" ]; then
  say "Downloading Node.js ${NODE_MAJOR} (official build from nodejs.org)..."
  NBASE="https://nodejs.org/dist/latest-v${NODE_MAJOR}.x"
  dl "${NBASE}/SHASUMS256.txt" "${TMP}/node.sums"
  NFILE="$(awk -v a="linux-${NARCH}.tar.gz" '$2 ~ ("^node-v[0-9.]+-" a "$") {print $2; exit}' "${TMP}/node.sums")"
  if [ -z "$NFILE" ]; then die "Could not find a Node.js build for this CPU."; fi
  NSUM="$(awk -v f="$NFILE" '$2 == f {print $1; exit}' "${TMP}/node.sums")"
  dl "${NBASE}/${NFILE}" "${TMP}/node.tgz"
  if [ "$(sha256sum "${TMP}/node.tgz" | awk '{print $1}')" != "$NSUM" ]; then die "Node.js checksum mismatch."; fi
  mkdir -p "${TMP}/node"
  tar -xzf "${TMP}/node.tgz" -C "${TMP}/node" --strip-components=1
  rm -rf "$RT"
  mv "${TMP}/node" "$RT"
fi

if [ "$UPDATE" = 1 ]; then
  say "Stopping Billcora and taking a safety backup..."
  systemctl stop "$SVC" 2>/dev/null || true
  BACKUP="${APP}/backups/update-before-${NEWVER}-$(date +%Y%m%d-%H%M%S)"
  mkdir -p "$BACKUP"
  if ! ( cd "$APP" && tar --exclude=./node_modules --exclude=./runtime --exclude=./backups --exclude=./logs -cf - . ) | ( cd "$BACKUP" && tar -xf - ); then
    systemctl start "$SVC" 2>/dev/null || true
    die "Backup failed. Nothing was changed."
  fi
fi

say "Installing program files..."
mkdir -p "${TMP}/new"
tar -xzf "${TMP}/app.tar.gz" -C "${TMP}/new"
rm -f "${TMP}"/new/billing.db* "${TMP}/new/.secret" "${TMP}/new/port.txt"
if [ -f "${APP}/config.js" ]; then rm -f "${TMP}/new/config.js"; fi
cp -a "${TMP}/new/." "${APP}/"

if ! id billcora >/dev/null 2>&1; then
  NOLOGIN="$(command -v nologin || echo /bin/false)"
  useradd --system --home-dir "$APP" --shell "$NOLOGIN" billcora
fi
mkdir -p "${APP}/logs" "${APP}/backups"

DEPS="$(cat "${APP}/package.json" "${APP}/package-lock.json" 2>/dev/null | sha256sum | awk '{print $1}')"
OLDDEPS=""
if [ -f "${APP}/.deps-hash" ]; then OLDDEPS="$(cat "${APP}/.deps-hash")"; fi
if [ ! -d "${APP}/node_modules/better-sqlite3" ] || [ "$OLDDEPS" != "$DEPS" ]; then
  say "Installing the program's libraries (needs internet, takes a minute)..."
  if ! install_deps; then
    warn "That failed. Installing build tools and trying once more..."
    build_tools
    if ! install_deps; then
      if [ "$UPDATE" = 1 ]; then rollback; fi
      die "Could not install the libraries. Check the internet connection and run again."
    fi
  fi
  printf '%s' "$DEPS" > "${APP}/.deps-hash"
fi

if [ ! -f "${APP}/.secret" ]; then head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n' > "${APP}/.secret"; fi
printf '%s' "$PORT" > "${APP}/port.txt"
printf '%s' "$NEWVER" > "${APP}/version.txt"
chown -R billcora:billcora "$APP"
chmod 600 "${APP}/.secret"

cat > "/etc/systemd/system/${SVC}.service" <<EOF
[Unit]
Description=Billcora billing software
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=billcora
WorkingDirectory=${APP}
Environment=PORT=${PORT}
ExecStart=${RT}/bin/node server.js
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF

say "Starting Billcora..."
systemctl daemon-reload
systemctl enable "$SVC" >/dev/null 2>&1 || true
systemctl restart "$SVC"
OK=0
for _ in $(seq 1 30); do
  if curl -fs -m 3 -o /dev/null "http://127.0.0.1:${PORT}/login"; then OK=1; break; fi
  sleep 2
done

if [ "$OK" != 1 ]; then
  warn "Billcora did not start. Last log lines:"
  journalctl -u "$SVC" -n 20 --no-pager 2>/dev/null || true
  if [ "$UPDATE" = 1 ]; then rollback; fi
  die "Billcora could not start. Please send the log lines above to support."
fi

if [ "${BILLCORA_OPEN_FIREWALL:-0}" = "1" ]; then
  if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
    ufw allow "${PORT}/tcp" >/dev/null || true
  elif command -v firewall-cmd >/dev/null 2>&1 && firewall-cmd --state >/dev/null 2>&1; then
    firewall-cmd --permanent --add-port="${PORT}/tcp" >/dev/null || true
    firewall-cmd --reload >/dev/null || true
  fi
fi

IP="$(hostname -I 2>/dev/null | awk '{print $1}' || true)"
say "Billcora ${NEWVER} is running."
echo "  On this computer : http://localhost:${PORT}"
if [ -n "$IP" ]; then echo "  Other PCs        : http://${IP}:${PORT}"; fi
echo "  Other PCs cannot connect? Set host to '0.0.0.0' in ${APP}/config.js, then: sudo systemctl restart ${SVC}"
echo "  Firewall on? On a private network only: sudo ufw allow ${PORT}/tcp  (or run this installer with BILLCORA_OPEN_FIREWALL=1)"
if [ "$UPDATE" = 1 ]; then
  echo "  Your customers, bills and settings were kept. Backup: ${BACKUP}"
else
  echo "  First login: admin / admin123  (change the password right after login)"
fi
echo "  Logs: journalctl -u ${SVC} -f      Remove: sudo systemctl disable --now ${SVC} && sudo rm -rf ${APP} /etc/systemd/system/${SVC}.service"