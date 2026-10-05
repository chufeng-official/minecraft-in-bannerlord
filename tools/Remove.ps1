param(
    [string]$GameDirectory,
    [switch]$ConfirmRemoval
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Common.ps1"
$GameDirectory = Resolve-GameDirectory $GameDirectory
$target = Join-Path $GameDirectory 'Modules\BannerlordBlocks'
if (!$ConfirmRemoval) { throw "Close game and confirm removal with -ConfirmRemoval: $target" }
Assert-GameClosed
$manifestPath = Join-Path $target 'deployment-manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$allowed = @('SubModule.xml', 'bin\Win64_Shipping_Client\BannerlordBlocks.dll')
if (@($manifest).Count -ne 2 -or @($manifest.Path | Select-Object -Unique).Count -ne 2) {
    throw 'Invalid ownership manifest; refusing removal'
}
foreach ($entry in $manifest) {
    if ($entry.Path -notin $allowed) { throw 'Unexpected manifest path; refusing removal' }
    $path = Join-Path $target $entry.Path
    if ((Test-Path $path) -and (Get-FileHash $path -Algorithm SHA256).Hash -ne $entry.Hash) {
        throw "Modified file retained; refusing removal: $path"
    }
}
foreach ($entry in $manifest) {
    $path = Join-Path $target $entry.Path
    if (Test-Path $path) { Remove-Item -LiteralPath $path }
}
Remove-Item -LiteralPath $manifestPath
foreach ($directory in @("$target\bin\Win64_Shipping_Client", "$target\bin", $target)) {
    if ((Test-Path $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $directory
    }
}
