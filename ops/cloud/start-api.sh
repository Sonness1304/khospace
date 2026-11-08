#!/usr/bin/env sh
set -eu
umask 077
mountpoint -q /data || { echo "Persistent application volume /data is required." >&2; exit 2; }
[ "${CloudHosting__DataProtectionPath:-}" = /data/keys ] || { echo "Unexpected persistent key path." >&2; exit 2; }
install -d -m 700 -o app -g app /data/keys
if [ "${KHOSPACE_INITIALIZE_CLOUD_ON_START:-0}" = 1 ]; then
  setpriv --reuid app --regid app --init-groups dotnet /app/WarehouseRent.Api.dll --initialize-cloud
fi
unset CloudHosting__OperatorPassword CloudHosting__OperatorEmail
port="${PORT:-8080}"
case "$port" in ''|*[!0-9]*) echo "Invalid service port." >&2; exit 2 ;; esac
exec setpriv --reuid app --regid app --init-groups dotnet /app/WarehouseRent.Api.dll --urls "http://0.0.0.0:$port"
