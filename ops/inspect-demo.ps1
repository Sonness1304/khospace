[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Directory,
    [string]$ExpectedRevision
)
$ErrorActionPreference = 'Stop'
$arguments = @((Join-Path $PSScriptRoot 'release-manifest.mjs'), 'inspect', '--directory', [IO.Path]::GetFullPath($Directory))
if ($ExpectedRevision) { $arguments += @('--revision', $ExpectedRevision) }
& node @arguments
if ($LASTEXITCODE -ne 0) { throw 'Demo candidate inspection failed. Do not start or migrate from this artifact.' }
