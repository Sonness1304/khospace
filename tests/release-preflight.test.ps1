$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../ops/release-preflight.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('WarehouseRent_Release_Guard_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$checks = 0
foreach ($relative in @('', 'frontend/dist', 'frontend/node_modules', 'backend/WarehouseRent.Api/bin', 'backend/WarehouseRent.Infrastructure/obj', 'backend/WarehouseRent.Tests/bin', 'backend/WarehouseRent.Api/obj/nested')) {
    $case = Join-Path $fixture ([Guid]::NewGuid().ToString('N'))
    $source = Join-Path $case 'source'
    $outside = Join-Path $case 'outside'
    New-Item -ItemType Directory -Path $source,$outside | Out-Null
    $sentinel = Join-Path $outside 'keep.txt'
    Set-Content -LiteralPath $sentinel -Value 'Preserve linked target'
    if ($relative) {
        $link = Join-Path $source $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $link) | Out-Null
        New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
    }
    $rejected = $false
    try { Assert-ReleaseBuildPaths $source } catch { if ($_.Exception.Message -notlike '*reparse*') { throw }; $rejected = $true }
    if ($rejected -ne [bool]$relative) { throw 'Generated build path preflight returned an unexpected result.' }
    if ((Get-Content -LiteralPath $sentinel -Raw).Trim() -cne 'Preserve linked target') { throw 'Linked target was changed.' }
    $checks++
}
Write-Output "Release generated-path preflight: $checks checks passed; sentinel files preserved."
Write-Output "Synthetic fixtures retained without recursive cleanup: $fixture"
