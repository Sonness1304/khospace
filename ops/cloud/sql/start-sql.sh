#!/usr/bin/env sh
set -eu
umask 077
mountpoint -q /var/opt/mssql || { echo "Persistent SQL volume is required." >&2; exit 2; }
[ "${ACCEPT_EULA:-}" = Y ] || { echo "SQL license acceptance must be configured by the operator." >&2; exit 2; }
[ "${MSSQL_PID:-}" = Express ] || { echo "This deployment requires SQL Server Express." >&2; exit 2; }
password="${MSSQL_SA_PASSWORD:-}"
[ "${#password}" -ge 20 ] || { echo "A private SQL bootstrap password is required." >&2; exit 2; }
chown mssql:mssql /var/opt/mssql
chmod 700 /var/opt/mssql
exec setpriv --reuid mssql --regid mssql --init-groups /opt/mssql/bin/sqlservr
