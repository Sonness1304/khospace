function Assert-ToolVersions(
    [string]$DotnetVersion, [string]$NodeVersion, [string]$NpmVersion,
    [string]$ExpectedDotnet, [string]$ExpectedNode, [string]$ExpectedNpm
) {
    if ($DotnetVersion.Trim() -cne $ExpectedDotnet) { throw ".NET SDK version must be $ExpectedDotnet; found $DotnetVersion." }
    if ($NodeVersion.Trim().TrimStart('v') -cne $ExpectedNode) { throw "Node version must be $ExpectedNode; found $NodeVersion." }
    if ($NpmVersion.Trim() -cne $ExpectedNpm) { throw "npm version must be $ExpectedNpm; found $NpmVersion." }
}

function Invoke-ToolPreflight([string]$Root) {
    $sdk = (Get-Content -LiteralPath (Join-Path $Root 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $package = Get-Content -LiteralPath (Join-Path $Root 'frontend/package.json') -Raw | ConvertFrom-Json
    $dotnetVersion = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the required .NET SDK version.' }
    $nodeVersion = & node --version
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the Node version.' }
    $npmVersion = & npm --version
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the npm version.' }
    Assert-ToolVersions $dotnetVersion $nodeVersion $npmVersion $sdk $package.engines.node $package.engines.npm
    Write-Output 'Pinned .NET, Node and npm versions verified.'
}
