#!/bin/sh
# GeekRepository's entrypoint: opens the WireGuard tunnel to Mongo (docs: Geek-Receptionist plans/mongo-wireguard-runbook.md), then starts the app.
#
# Three states, and only the middle one is allowed to continue quietly:
#   - none of the WG_* variables set   -> the tunnel is not switched on yet. Said on stderr, then the app starts. Remove this branch in
#     the same change that closes Mongo's public port, so that an unset variable can no longer start the service.
#   - all of them set                  -> the tunnel must come up, or the container exits non-zero and Railway shows a failed deploy.
#   - some of them set                 -> a half-written configuration. Exit non-zero and name the missing variables.
set -eu

TUNNEL_VARS="WG_PRIVATE_KEY WG_PRESHARED_KEY WG_CLIENT_ADDRESS VPS_WG_PUBLIC_KEY VPS_WG_ENDPOINT WG_MONGO_HOST MONGO_CA_PEM"
set_names=""
missing_names=""
for name in $TUNNEL_VARS; do
  eval "value=\${$name:-}"
  if [ -n "$value" ]; then set_names="$set_names $name"; else missing_names="$missing_names $name"; fi
done

if [ -z "$set_names" ]; then
  echo "[WARN] entrypoint: no WireGuard variables are set, so the Mongo tunnel is NOT running and Mongo is reached directly." >&2
  exec "$@"
fi

if [ -n "$missing_names" ]; then
  echo "[FATAL] entrypoint: the WireGuard tunnel is half configured. Missing:$missing_names" >&2
  exit 1
fi

SHM_DIR=${WG_SHM_DIR:-/dev/shm}
[ -d "$SHM_DIR" ] && [ -w "$SHM_DIR" ] || { echo "[FATAL] entrypoint: $SHM_DIR is not writable, so the tunnel key cannot be kept off disk" >&2; exit 1; }
command -v wireproxy >/dev/null 2>&1 || { echo "[FATAL] entrypoint: wireproxy is not installed in this image" >&2; exit 1; }
command -v nc >/dev/null 2>&1 || { echo "[FATAL] entrypoint: nc is not installed in this image" >&2; exit 1; }

# The CA certificate is public. The C# Mongo driver does not read a tlsCAFile option (the connection string accepts it and the handshake then
# fails with UntrustedRoot), and .NET on Linux trusts the system store, so the CA is installed there. A failure stops the container.
command -v update-ca-certificates >/dev/null 2>&1 || { echo "[FATAL] entrypoint: update-ca-certificates is not installed in this image" >&2; exit 1; }
CA_FILE=${MONGO_CA_FILE:-/usr/local/share/ca-certificates/geek-mongo-ca.crt}
( umask 022; printf '%s\n' "$MONGO_CA_PEM" > "$CA_FILE" ) || { echo "[FATAL] entrypoint: cannot write $CA_FILE" >&2; exit 1; }
update-ca-certificates >&2 || { echo "[FATAL] entrypoint: update-ca-certificates failed, so the Mongo CA is not trusted" >&2; exit 1; }

umask 077
CONF=$(mktemp "$SHM_DIR/wp.XXXXXX")
cat > "$CONF" <<CONFEOF
[Interface]
PrivateKey = $WG_PRIVATE_KEY
Address = $WG_CLIENT_ADDRESS

[Peer]
PublicKey = $VPS_WG_PUBLIC_KEY
PreSharedKey = $WG_PRESHARED_KEY
Endpoint = $VPS_WG_ENDPOINT
AllowedIPs = $WG_MONGO_HOST/32
PersistentKeepalive = 25

[TCPClientTunnel]
BindAddress = 127.0.0.1:27017
Target = $WG_MONGO_HOST:27017
CONFEOF

# The tunnel's keys are in the file and nowhere else once the app starts.
unset WG_PRIVATE_KEY WG_PRESHARED_KEY MONGO_CA_PEM

wireproxy -n -c "$CONF" >&2 || { rm -f "$CONF"; echo "[FATAL] entrypoint: wireproxy refused its configuration" >&2; exit 1; }
wireproxy -c "$CONF" >&2 &
WP=$!

tries=0
until nc -z 127.0.0.1 27017 2>/dev/null; do
  kill -0 "$WP" 2>/dev/null || { rm -f "$CONF"; echo "[FATAL] entrypoint: wireproxy exited before the tunnel was listening" >&2; exit 1; }
  tries=$((tries + 1))
  if [ "$tries" -ge 20 ]; then
    rm -f "$CONF"
    kill "$WP" 2>/dev/null || true
    echo "[FATAL] entrypoint: the tunnel did not start listening on 127.0.0.1:27017 within 10 seconds" >&2
    exit 1
  fi
  sleep 0.5
done
rm -f "$CONF"
echo "[INFO] entrypoint: Mongo tunnel is listening on 127.0.0.1:27017 (wireproxy pid $WP)" >&2

exec "$@"
