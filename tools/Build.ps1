param([string]$GameDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Common.ps1"
$GameDirectory = Resolve-GameDirectory $GameDirectory
$root = Split-Path $PSScriptRoot -Parent
dotnet build "$root\src\BannerlordMod\BannerlordMod.csproj" -c Release --ignore-failed-sources "-p:GameDirectory=$GameDirectory"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$stage = "$root\artifacts\BannerlordBlocks"
New-Item "$stage\bin\Win64_Shipping_Client" -ItemType Directory -Force | Out-Null
Copy-Item "$root\src\BannerlordMod\bin\Release\net472\BannerlordBlocks.dll" "$stage\bin\Win64_Shipping_Client\BannerlordBlocks.dll"
Copy-Item "$root\src\BannerlordMod\SubModule.xml" "$stage\SubModule.xml"
Write-Output "Staged only; game untouched: $stage"
