[CmdletBinding()]
param([switch]$Https, [switch]$InspectOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'google-local.ps1')
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$pointer = Join-Path $root '.qa/runtime/current-p2-candidate.json'
$selection = Get-Content -LiteralPath $pointer -Raw | ConvertFrom-Json
& node (Join-Path $PSScriptRoot 'release-manifest.mjs') inspect --directory $selection.directory --revision $selection.sourceRevision
if ($LASTEXITCODE -ne 0) { throw 'Reviewed package hash/revision inspection failed.' }
$port = if ($Https) { 7443 } else { 5080 }
$url = if ($Https) { 'https://localhost:7443' } else { 'http://127.0.0.1:5080' }
Write-Output "Reviewed source: $($selection.sourceRevision); URL: $url"
if ($InspectOnly) { return }
if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw 'Selected port is occupied. Stop only your own old server manually.' }
if (Get-ChildItem Env: | Where-Object Name -Match '^(ASPNETCORE_|DOTNET_)?Kestrel__Endpoints__') { throw 'Explicit Kestrel endpoints are present. Clear them in this terminal before using the bounded local launcher.' }
$credentials = $null
if ($Https) {
    & dotnet dev-certs https --check --trust | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Trusted localhost certificate required. Run dotnet dev-certs https --trust yourself, then retry.' }
    $credentials = Read-LocalGoogleConfiguration -Path (Get-LocalGooglePath)
}
$keys = @('ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','ConnectionStrings__WarehouseRent','Payments__Vnpay__Mode','Payments__Momo__Mode','Payments__AllowLive','ASPNETCORE_URLS')
$prior = @{}
foreach ($key in $keys) { $prior[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
Push-Location -LiteralPath (Join-Path $selection.directory 'app')
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Demo'; $env:DOTNET_ENVIRONMENT = 'Demo'
    $env:ConnectionStrings__WarehouseRent = 'Server=localhost;Database=WarehouseRent_Demo_AtlasP0_20261028;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
    $env:Payments__Vnpay__Mode = 'Disabled'; $env:Payments__Momo__Mode = 'Disabled'; $env:Payments__AllowLive = 'false'
    $env:ASPNETCORE_URLS = $url
    Write-Output "Starting local demo at $url. Keep this terminal open; Ctrl+C stops it. No migration or seed."
    $start = {
        & dotnet './WarehouseRent.Api.dll' --urls $url
        if ($LASTEXITCODE -ne 0) { throw 'API exited with an error. Preserve the terminal output.' }
    }
    if ($Https) { Invoke-WithLocalGoogleEnvironment -Credentials $credentials -Action $start }
    else { & $start }
} finally {
    Pop-Location
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $prior[$key], 'Process') }
    $credentials = $null
}
