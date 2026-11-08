$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../ops/google-local.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('WarehouseRent_Google_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$file = Join-Path $fixture 'google-oauth.json'
$secret = ConvertTo-SecureString 'synthetic-secret-never-live' -AsPlainText -Force
$id = '12345-synthetic.apps.googleusercontent.com'
Write-LocalGoogleConfiguration -Path $file -ClientId $id -ClientSecret $secret
$raw = [IO.File]::ReadAllText($file)
if ($raw.Contains($id) -or $raw.Contains('synthetic-secret-never-live')) { throw 'Plaintext credentials were persisted.' }
$loaded = Read-LocalGoogleConfiguration -Path $file
if ($loaded.ClientId -cne $id -or $loaded.ClientSecret -cne 'synthetic-secret-never-live') { throw 'Round-trip failed.' }
$rejected = $false
try { Write-LocalGoogleConfiguration -Path $file -ClientId $id -ClientSecret $secret } catch { $rejected = $true }
if (-not $rejected) { throw 'Existing credentials were overwritten implicitly.' }
foreach ($invalid in @('', 'not-google', 'https://bad.example', "123-synthetic.apps.googleusercontent.com`n")) {
    $rejected = $false
    try { Write-LocalGoogleConfiguration -Path (Join-Path $fixture ([Guid]::NewGuid().ToString('N'))) -ClientId $invalid -ClientSecret $secret } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid client ID accepted.' }
}
$rejected = $false
try { Write-LocalGoogleConfiguration -Path (Join-Path $fixture 'empty.json') -ClientId $id -ClientSecret (New-Object Security.SecureString) } catch { $rejected = $true }
if (-not $rejected) { throw 'Empty secret accepted.' }
$priorId = $env:Authentication__Google__ClientId
$priorSecret = $env:Authentication__Google__ClientSecret
try {
    $env:Authentication__Google__ClientId = 'prior-id'
    $env:Authentication__Google__ClientSecret = 'prior-secret'
    $rejected = $false
    try { Invoke-WithLocalGoogleEnvironment -Credentials $loaded -Action {
        if ($env:Authentication__Google__ClientId -cne $id -or $env:Authentication__Google__ClientSecret -cne 'synthetic-secret-never-live') { throw 'Injection failed.' }
        throw 'synthetic launch failure'
    } } catch { if ($_.Exception.Message -cne 'synthetic launch failure') { throw }; $rejected = $true }
    if (-not $rejected -or $env:Authentication__Google__ClientId -cne 'prior-id' -or $env:Authentication__Google__ClientSecret -cne 'prior-secret') { throw 'Process environment was not restored.' }
} finally {
    [Environment]::SetEnvironmentVariable('Authentication__Google__ClientId', $priorId, 'Process')
    [Environment]::SetEnvironmentVariable('Authentication__Google__ClientSecret', $priorSecret, 'Process')
}
[IO.File]::WriteAllText($file, '{"schemaVersion":1,"protectedCredentials":"corrupt"}')
$rejected = $false
try { Read-LocalGoogleConfiguration -Path $file | Out-Null } catch { $rejected = $true }
if (-not $rejected) { throw 'Corrupt DPAPI accepted.' }
'Google local synthetic checks: 10 passed; no provider, certificate trust or API process used.'
'Fixture retained: ' + $fixture
