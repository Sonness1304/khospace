function Assert-PlainReleasePath([string]$Target) {
    $cursor = [IO.Path]::GetFullPath($Target)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Release packaging refuses a reparse/source/output path.' }
        }
        $parent = Split-Path -Parent $cursor
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}
function Assert-ReleaseBuildPaths([string]$Root) {
    $generated = @('frontend/dist', 'frontend/node_modules', 'backend/WarehouseRent.Api/bin', 'backend/WarehouseRent.Api/obj', 'backend/WarehouseRent.Infrastructure/bin', 'backend/WarehouseRent.Infrastructure/obj', 'backend/WarehouseRent.Tests/bin', 'backend/WarehouseRent.Tests/obj')
    foreach ($relative in $generated) {
        $target = Join-Path $Root $relative
        Assert-PlainReleasePath $target
        # npm owns package-internal links; validate node_modules root/ancestors before npm ci.
        if ($relative -eq 'frontend/node_modules' -or -not (Test-Path -LiteralPath $target)) { continue }
        $queue = [Collections.Generic.Queue[string]]::new()
        $queue.Enqueue($target)
        while ($queue.Count) {
            $directory = $queue.Dequeue()
            foreach ($entry in Get-ChildItem -LiteralPath $directory -Force) {
                if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Release packaging refuses a reparse path in generated build output.' }
                if ($entry.PSIsContainer) { $queue.Enqueue($entry.FullName) }
            }
        }
    }
}
