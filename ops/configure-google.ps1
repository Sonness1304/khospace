[CmdletBinding()]
param([switch]$Replace)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'google-local.ps1')
$path = Get-LocalGooglePath
if ((Test-Path -LiteralPath $path) -and -not $Replace) { throw 'Google configuration already exists. Use -Replace only if you intend to replace it.' }
Write-Output 'Enter your own Google OAuth Web application credentials privately. Do not paste them in chat.'
Write-Output 'Authorized redirect URI: https://localhost:7443/signin-google'
$clientId = Read-Host 'Google Client ID'
$secret = Read-Host 'Google Client secret (hidden)' -AsSecureString
try { Write-LocalGoogleConfiguration -Path $path -ClientId $clientId -ClientSecret $secret -Replace:$Replace }
finally { $secret.Dispose(); $clientId = $null }
Write-Output 'Saved with Windows CurrentUser DPAPI outside Git/OneDrive. Credentials were not printed.'
Write-Output 'Next: run ops/start-local-demo.ps1 -Https after trusting your localhost development certificate.'
