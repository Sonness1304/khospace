$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../ops/preflight.ps1')
function Assert-Fails([scriptblock]$Action, [string]$Expected) {
    try { & $Action; throw 'Expected validation failure' } catch { if ($_.Exception.Message -notlike "*$Expected*") { throw } }
}
Assert-ToolVersions '10.0.403' 'v24.19.0' '11.17.0' '10.0.403' '24.19.0' '11.17.0'
Assert-Fails { Assert-ToolVersions '10.0.403' 'v24.18.0' '11.17.0' '10.0.403' '24.19.0' '11.17.0' } 'Node'
Assert-Fails { Assert-ToolVersions '10.0.403' 'v24.19.0' '11.16.0' '10.0.403' '24.19.0' '11.17.0' } 'npm'
Assert-Fails { Assert-ToolVersions '10.0.401' 'v24.19.0' '11.17.0' '10.0.403' '24.19.0' '11.17.0' } '.NET'
Write-Output 'Preflight version validation: 4 checks passed.'
