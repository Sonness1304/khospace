#!/usr/bin/env sh
set -eu
umask 077
fail() { echo "$1" >&2; exit 2; }
[ "${ASPNETCORE_ENVIRONMENT:-}" = Production ] || fail "Production environment is required."
[ "${CloudHosting__Enabled:-}" = true ] || fail "Cloud hosting boundary must be enabled."
case "${Database__Provider:-SqlServer}/${CloudHosting__DataProtectionStore:-}" in
 SqlServer/SqlServer|PostgreSQL/PostgreSQL) ;;
 *) fail "An explicitly matching database provider and durable encrypted key store are required." ;;
esac
[ -n "${CloudHosting__DataProtectionEncryptionKey:-}" ] || fail "Private key encryption configuration is required."
[ -n "${ConnectionStrings__WarehouseRent:-}" ] || fail "Private SQL connection configuration is required."

# Render's default HTTP health Host is this public subdomain, not localhost.
# Custom domains require a separately reviewed health Host configuration.
host="${RENDER_EXTERNAL_HOSTNAME:-}"
case "$host" in ''|*[!a-z0-9.-]*) fail "An exact Render service hostname is required." ;; esac
printf '%s\n' "$host" | grep -Eq '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.onrender\.com$' || fail "An exact Render service hostname is required."
[ "${CloudHosting__BackendHost:-$host}" = "$host" ] || fail "Backend host does not match Render."
[ "${CloudHosting__HealthCheckHost:-$host}" = "$host" ] || fail "Health check host does not match Render."
export CloudHosting__BackendHost="$host" CloudHosting__HealthCheckHost="$host"
unset CloudHosting__DataProtectionPath
port="${PORT:-10000}"
case "$port" in ''|*[!0-9]*) fail "Invalid service port." ;; esac
[ "${#port}" -le 5 ] && [ "$port" -ge 1 ] && [ "$port" -le 65535 ] || fail "Invalid service port."
case "${KHOSPACE_INITIALIZE_CLOUD_ON_START:-0}" in
  0) ;;
  1) setpriv --reuid app --regid app --init-groups dotnet /app/WarehouseRent.Api.dll --initialize-cloud ;;
  *) fail "Initialization requires an explicit 0 or 1 flag." ;;
esac
unset CloudHosting__OperatorPassword CloudHosting__OperatorEmail KHOSPACE_INITIALIZE_CLOUD_ON_START ConnectionStrings__WarehouseRentMigration
exec setpriv --reuid app --regid app --init-groups dotnet /app/WarehouseRent.Api.dll --urls "http://0.0.0.0:$port"
