Set-StrictMode -Version Latest

function Assert-LocalGoogleCredential {
    param([string]$ClientId, [string]$ClientSecret)
    if ($ClientId -cnotmatch '\A[0-9]+-[A-Za-z0-9_-]+\.apps\.googleusercontent\.com\z' -or $ClientId.Length -gt 256) {
        throw 'Enter a Google OAuth Web application client ID.'
    }
    if ([string]::IsNullOrWhiteSpace($ClientSecret) -or $ClientSecret.Length -gt 1024 -or $ClientSecret -match '[\x00-\x1f\x7f]') {
        throw 'Enter a nonempty Google client secret without control characters.'
    }
}

function Assert-PlainGooglePath {
    param([string]$Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Google configuration paths must not traverse a link or junction.'
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}

function Get-LocalGooglePath {
    if (-not $IsWindows -or -not $env:LOCALAPPDATA) { throw 'Private local Google configuration requires Windows CurrentUser DPAPI.' }
    Join-Path $env:LOCALAPPDATA 'WarehouseRent/google-oauth.json'
}

function Write-LocalGoogleConfiguration {
    param([string]$Path, [string]$ClientId, [Security.SecureString]$ClientSecret, [switch]$Replace)
    if (-not $IsWindows) { throw 'Windows CurrentUser DPAPI is required.' }
    $plainSecret = [Net.NetworkCredential]::new('', $ClientSecret).Password
    Assert-LocalGoogleCredential -ClientId $ClientId -ClientSecret $plainSecret
    Assert-PlainGooglePath $Path
    if ((Test-Path -LiteralPath $Path) -and -not $Replace) { throw 'Configuration exists. Use -Replace only to replace it explicitly.' }
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($Path))
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $json = @{ clientId = $ClientId; clientSecret = $plainSecret } | ConvertTo-Json -Compress
    $secure = ConvertTo-SecureString $json -AsPlainText -Force
    $encrypted = ConvertFrom-SecureString $secure
    $payload = @{ schemaVersion = 1; protectedCredentials = $encrypted } | ConvertTo-Json
    $temporary = Join-Path $parent ([Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, $payload)
        Assert-PlainGooglePath $Path
        [IO.File]::Move($temporary, [IO.Path]::GetFullPath($Path), [bool]$Replace)
    } finally {
        $plainSecret = $null; $json = $null
        $secure.Dispose()
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

function Read-LocalGoogleConfiguration {
    param([string]$Path)
    if (-not $IsWindows) { throw 'Windows CurrentUser DPAPI is required.' }
    Assert-PlainGooglePath $Path
    try {
        if ((Get-Item -LiteralPath $Path).Length -gt 32768) { throw 'Size' }
        $record = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
        if ($record.schemaVersion -ne 1) { throw 'Version' }
        $secure = ConvertTo-SecureString $record.protectedCredentials
        try { $value = [Net.NetworkCredential]::new('', $secure).Password | ConvertFrom-Json }
        finally { $secure.Dispose() }
        Assert-LocalGoogleCredential -ClientId $value.clientId -ClientSecret $value.clientSecret
        return @{ ClientId = $value.clientId; ClientSecret = $value.clientSecret }
    } catch { throw 'Private Google configuration could not be read by this Windows user. Run configure-google.ps1 privately.' }
}

function Invoke-WithLocalGoogleEnvironment {
    param([hashtable]$Credentials, [scriptblock]$Action)
    Assert-LocalGoogleCredential -ClientId $Credentials.ClientId -ClientSecret $Credentials.ClientSecret
    $oldId = [Environment]::GetEnvironmentVariable('Authentication__Google__ClientId', 'Process')
    $oldSecret = [Environment]::GetEnvironmentVariable('Authentication__Google__ClientSecret', 'Process')
    try {
        $env:Authentication__Google__ClientId = $Credentials.ClientId
        $env:Authentication__Google__ClientSecret = $Credentials.ClientSecret
        & $Action
    } finally {
        [Environment]::SetEnvironmentVariable('Authentication__Google__ClientId', $oldId, 'Process')
        [Environment]::SetEnvironmentVariable('Authentication__Google__ClientSecret', $oldSecret, 'Process')
    }
}
