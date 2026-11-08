[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
try {
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    pwsh -NoProfile -File tests/setup-smoke.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Setup preflight tests failed.' }
    pwsh -NoProfile -File tests/release-preflight.test.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Release generated-path preflight checks failed.' }
    pwsh -NoProfile -File tests/google-local.test.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Private Google configuration checks failed.' }
    node --test tests/release-manifest.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Release manifest regression checks failed.' }
    node --test tests/documentation.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Documentation checks failed.' }
    dotnet test backend/WarehouseRent.sln --collect:'XPlat Code Coverage'
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed (real SQL Server is required).' }
    npm --prefix frontend run lint
    if ($LASTEXITCODE -ne 0) { throw 'Frontend lint failed.' }
    npm --prefix frontend run test:coverage
    if ($LASTEXITCODE -ne 0) { throw 'Frontend tests/coverage failed.' }
    npm --prefix frontend run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
}
finally { Pop-Location }
