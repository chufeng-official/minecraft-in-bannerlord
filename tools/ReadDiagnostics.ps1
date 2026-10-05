param([int]$Tail = 100)
$ErrorActionPreference = 'Stop'
if ($Tail -lt 1 -or $Tail -gt 10000) { throw 'Tail must be between 1 and 10000' }
$path = Join-Path $env:LOCALAPPDATA 'BannerlordBlocks\prototype.log'
if (!(Test-Path -LiteralPath $path)) { throw "No runtime log yet: $path" }
Get-Content -LiteralPath $path | Where-Object {
    $_ -match 'Submodule loaded|Test begin:|Test end:|Sample:|Manual status:|Rejected:|Cleanup remaining=|Disabled after|highlight unavailable|Before activation:|After activation:|Preview |Grid anchored:'
} | Select-Object -Last $Tail
