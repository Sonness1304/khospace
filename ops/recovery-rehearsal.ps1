[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
# This test constructs its own GUID databases; it does not accept an owner database target.
Push-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
try {
    dotnet test backend/WarehouseRent.sln --filter FullyQualifiedName~SqlRecoveryTests --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic SQL restore rehearsal failed. Inspect the test result; do not alter server permissions as a workaround.' }
}
finally { Pop-Location }
