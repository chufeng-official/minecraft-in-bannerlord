param(
    [string]$GameDirectory,
    [switch]$ConfirmDeployment
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Common.ps1"
$GameDirectory = Resolve-GameDirectory $GameDirectory
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $GameDirectory 'Modules\BannerlordBlocks'
if (!$ConfirmDeployment) { throw "Confirm target first, then use -ConfirmDeployment: $target" }
Assert-GameClosed
if (Test-Path $target) { throw 'Target already exists. Refusing to overwrite any module.' }
$stage = "$root\artifacts\BannerlordBlocks"
$files = @('SubModule.xml', 'bin\Win64_Shipping_Client\BannerlordBlocks.dll')
foreach ($file in $files) { if (!(Test-Path (Join-Path $stage $file))) { throw "Build first; missing $file" } }
$manifest = foreach ($file in $files) {
    [pscustomobject]@{ Path = $file; Hash = (Get-FileHash (Join-Path $stage $file) -Algorithm SHA256).Hash }
}
New-Item (Join-Path $target 'bin\Win64_Shipping_Client') -ItemType Directory | Out-Null
# Write ownership record before copying so partial deployment can be removed safely.
$manifest | ConvertTo-Json | Set-Content (Join-Path $target 'deployment-manifest.json') -Encoding utf8
foreach ($file in $files) { Copy-Item (Join-Path $stage $file) (Join-Path $target $file) }
Write-Output "Created only: $target"
