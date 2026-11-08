[CmdletBinding()]
param([switch]$InitializeDemo)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    . (Join-Path $PSScriptRoot 'preflight.ps1')
    Invoke-ToolPreflight $projectRoot
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    dotnet restore backend/WarehouseRent.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked .NET restore failed.' }
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Local EF tool restore failed.' }
    npm --prefix frontend ci --ignore-scripts --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'Locked npm install failed.' }
    npm --prefix frontend run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    dotnet build backend/WarehouseRent.sln --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    if ($InitializeDemo) {
        if ($env:ASPNETCORE_ENVIRONMENT -cne 'Demo' -or [string]::IsNullOrWhiteSpace($env:ConnectionStrings__WarehouseRent)) {
            throw 'Set ASPNETCORE_ENVIRONMENT=Demo and an explicit synthetic connection string first.'
        }
        dotnet run --no-build --project backend/WarehouseRent.Api -- --seed-demo
        if ($LASTEXITCODE -ne 0) { throw 'Synthetic Demo initialization failed.' }
    }
    Write-Output 'Foundation ready. Start API and frontend in separate terminals using README commands.'
}
finally { Pop-Location }
