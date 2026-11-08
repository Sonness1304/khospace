[CmdletBinding()]
param(
    [ValidateSet('Plan', 'Inspect', 'Create')][string]$Action = 'Plan',
    [Parameter(Mandatory)][string]$Subscription,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]{1,80}$')][string]$ResourceGroup,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$')][string]$Server,
    [Parameter(Mandatory)][ValidatePattern('^WarehouseRent_Cloud_[A-Za-z0-9]{1,48}$')][string]$Database,
    [string]$AzureCli = 'az'
)
$ErrorActionPreference = 'Stop'
$guid = [Guid]::Empty
if (-not [Guid]::TryParseExact($Subscription, 'D', [ref]$guid) -or $guid -eq [Guid]::Empty) {
    throw 'An explicit nonempty subscription GUID is required.'
}
$createArguments = @('sql','db','create','--subscription',$Subscription,'--resource-group',$ResourceGroup,
    '--server',$Server,'--name',$Database,'--edition','GeneralPurpose','--family','Gen5','--capacity','2',
    '--compute-model','Serverless','--use-free-limit','true','--free-limit-exhaustion-behavior','AutoPause',
    '--max-size','32GB','--backup-storage-redundancy','Local','--only-show-errors','--output','json')
if ($Action -eq 'Plan') {
    [ordered]@{ action = $Action; cloudExecuted = $false; requiresExistingLogicalServer = $true;
        createArguments = $createArguments; budget = 'Free-only; suspend at monthly allowance; no paid fallback' } | ConvertTo-Json -Depth 5
    exit 0
}
$command = Get-Command $AzureCli -ErrorAction Stop
function Invoke-AzureJson([string[]]$Arguments) {
    # Resource commands never contain secrets. Provider errors are kept out of the output/receipt.
    $raw = & $command.Source @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Azure request refused or unavailable; no billable fallback was attempted.' }
    return ($raw -join "`n" | ConvertFrom-Json -Depth 30)
}
function Assert-FreeDatabase($State) {
    if ($State.useFreeLimit -ne $true -or $State.freeLimitExhaustionBehavior -ne 'AutoPause' -or $State.name -ne $Database -or
         $State.sku.tier -ne 'GeneralPurpose' -or $State.sku.family -ne 'Gen5' -or
         $State.currentBackupStorageRedundancy -ne 'Local') {
        throw 'Database free-only settings could not be verified. Stop; do not enable billing or retry creation.'
    }
}
$account = Invoke-AzureJson @('account','show','--subscription',$Subscription,'--output','json','--only-show-errors')
if ($account.id -ne $Subscription -or $account.state -ne 'Enabled') { throw 'Authenticated enabled target subscription required.' }
$logical = Invoke-AzureJson @('sql','server','show','--subscription',$Subscription,'--resource-group',$ResourceGroup,'--name',$Server,'--output','json','--only-show-errors')
if ($logical.name -ne $Server -or $logical.minimalTlsVersion -notin @('1.2','1.3')) { throw 'An existing TLS-protected logical SQL server must be verified first.' }
$databases = @(Invoke-AzureJson @('sql','db','list','--subscription',$Subscription,'--resource-group',$ResourceGroup,'--server',$Server,'--output','json','--only-show-errors'))
$existing = @($databases | Where-Object name -eq $Database)
if ($existing.Count -gt 0) {
    if ($existing.Count -ne 1) { throw 'Ambiguous existing database state.' }
    Assert-FreeDatabase $existing[0]
    [ordered]@{ action = 'Inspect'; cloudExecuted = $true; created = $false; name = $Database; freeVerified = $true;
        exhaustionBehavior = 'AutoPause'; subscription = $Subscription } | ConvertTo-Json
    exit 0
}
if ($Action -eq 'Inspect') { throw 'Requested cloud database does not exist; no resource created.' }
$help = & $command.Source sql db create --help 2>$null
if ($LASTEXITCODE -ne 0 -or ($help -join "`n") -notmatch '--use-free-limit' -or ($help -join "`n") -notmatch '--free-limit-exhaustion-behavior') {
    throw 'Installed Azure CLI does not support free-only flags. No resource created.'
}
$null = Invoke-AzureJson $createArguments
$state = Invoke-AzureJson @('sql','db','show','--subscription',$Subscription,'--resource-group',$ResourceGroup,'--server',$Server,'--name',$Database,'--output','json','--only-show-errors')
Assert-FreeDatabase $state
[ordered]@{ action = 'Create'; cloudExecuted = $true; created = $true; name = $Database; freeVerified = $true;
    exhaustionBehavior = 'AutoPause'; subscription = $Subscription } | ConvertTo-Json
