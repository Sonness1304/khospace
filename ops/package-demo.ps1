[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot 'release-preflight.ps1')
function Read-CleanRevision {
    $root = & git rev-parse --show-toplevel
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($root.Trim()) -ine $projectRoot) { throw 'The package script must belong to the current application Git root.' }
    $changes = & git status --porcelain=v1 --untracked-files=normal
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit or preserve source changes before packaging. A clean source checkout is required.' }
    $revision = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $revision -cnotmatch '^[0-9a-f]{40}$') { throw 'Could not determine the clean source revision.' }
    return $revision.Trim()
}
Push-Location -LiteralPath $projectRoot
$previousCertificateSetting = $env:DOTNET_GENERATE_ASPNET_CERTIFICATE
try {
    Assert-PlainReleasePath $projectRoot
    $revision = Read-CleanRevision
    Assert-ReleaseBuildPaths $projectRoot
    . (Join-Path $PSScriptRoot 'preflight.ps1')
    Invoke-ToolPreflight $projectRoot
    # Check tracked source paths before invoking the existing build tools. No source symlink/junction is traversed.
    $tracked = & git ls-files
    if ($LASTEXITCODE -ne 0) { throw 'Source inventory unavailable.' }
    foreach ($relative in $tracked) { Assert-PlainReleasePath (Join-Path $projectRoot $relative) }
    $releaseRoot = Join-Path $projectRoot '.qa/releases'
    Assert-PlainReleasePath $releaseRoot
    if (-not (Test-Path -LiteralPath $releaseRoot)) { New-Item -ItemType Directory -Path $releaseRoot | Out-Null }
    $candidate = Join-Path $releaseRoot ($revision.Substring(0, 12) + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $candidate | Out-Null
    $payload = Join-Path $candidate 'app'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    dotnet restore backend/WarehouseRent.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked .NET restore failed; candidate is incomplete.' }
    npm --prefix frontend ci --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'Locked frontend install failed; candidate is incomplete.' }
    npm --prefix frontend run build
    if ($LASTEXITCODE -ne 0) { throw 'Fresh frontend build failed; candidate is incomplete.' }
    dotnet publish backend/WarehouseRent.Api -c Release --no-restore --output $payload -p:UseAppHost=false "-p:SourceRevisionId=$revision"
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed; candidate is incomplete.' }
    if ((Read-CleanRevision) -cne $revision) { throw 'Source revision changed during packaging; no verified candidate.' }
    $migrations = @(& git ls-files 'backend/WarehouseRent.Infrastructure/Persistence/Migrations/*_*.cs' | Where-Object { $_ -notlike '*.Designer.cs' } | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) } | Sort-Object)
    if ($LASTEXITCODE -ne 0 -or $migrations.Count -eq 0) { throw 'Expected migration inventory unavailable.' }
    $sdk = (Get-Content -LiteralPath global.json -Raw | ConvertFrom-Json).sdk.version
    $package = Get-Content -LiteralPath frontend/package.json -Raw | ConvertFrom-Json
    & node (Join-Path $PSScriptRoot 'release-manifest.mjs') create --directory $candidate --revision $revision --migrations ($migrations -join ',') --dotnet $sdk --node $package.engines.node --npm $package.engines.npm
    if ($LASTEXITCODE -ne 0) { throw 'Candidate inspection failed. Output is retained but must not be used.' }
    if ((Read-CleanRevision) -cne $revision) { throw 'Source changed after inventory; repackage from a stable clean revision.' }
    Write-Output "Candidate directory: $candidate"
    Write-Output 'Payload is a local candidate only. No API was started, no database was migrated, and no browser/deployment acceptance is claimed.'
}
finally {
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = $previousCertificateSetting
    Pop-Location
}
