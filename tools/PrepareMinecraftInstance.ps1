param([switch]$ConfirmInstallation)
$ErrorActionPreference = 'Stop'
if (!$ConfirmInstallation) { throw 'Confirm the isolated instance and new launcher version before using -ConfirmInstallation.' }
if (@(Get-Process | Where-Object { $_.ProcessName -match 'Minecraft|^javaw$' }).Count -gt 0) { throw 'Close Minecraft and its launcher first; processes are never terminated automatically.' }
$root = Split-Path $PSScriptRoot -Parent
$instance = Join-Path $root '.local\minecraft-instance'
$mc = Join-Path $env:APPDATA '.minecraft'
$versionId = 'fabric-loader-0.19.5-26.3'
$versionDirectory = Join-Path $mc "versions\$versionId"
if (!(Test-Path -LiteralPath (Join-Path $mc 'versions\26.3\26.3.jar'))) { throw 'Launch vanilla Minecraft 26.3 once first.' }
if (Test-Path -LiteralPath $versionDirectory) { throw 'Fabric version already exists; refusing to overwrite it.' }
$mod = Join-Path $root 'minecraft\build\libs\bannerlord-minecraft-bridge-0.1.0.jar'
if (!(Test-Path -LiteralPath $mod)) { throw 'Build the Mod first.' }
if (Test-Path -LiteralPath $instance) { throw 'Instance already exists; refusing to overwrite user files.' }
$downloads = Join-Path $root '.local\minecraft-dev\instance-downloads'
New-Item -Path $downloads -ItemType Directory -Force | Out-Null
$apiName = 'fabric-api-0.161.0+26.3.jar'
$api = Join-Path $downloads $apiName
$expected = '86f16178a3cecc887a85a4cfe9a79d92fa7341d8f39b5951a4d6ad800ab657a6'
if (!(Test-Path -LiteralPath $api)) {
    Invoke-WebRequest -Uri "https://maven.fabricmc.net/net/fabricmc/fabric-api/fabric-api/0.161.0+26.3/$apiName" -OutFile "$api.partial" -TimeoutSec 120
    if ((Get-FileHash -LiteralPath "$api.partial" -Algorithm SHA256).Hash -ne $expected) { throw 'Fabric API checksum mismatch.' }
    Move-Item -LiteralPath "$api.partial" -Destination $api
}
if ((Get-FileHash -LiteralPath $api -Algorithm SHA256).Hash -ne $expected) { throw 'Cached Fabric API checksum mismatch.' }
$versionText = (Invoke-WebRequest -Uri 'https://meta.fabricmc.net/v2/versions/loader/26.3/0.19.5/profile/json' -TimeoutSec 60).Content
$version = $versionText | ConvertFrom-Json
if ($version.id -ne $versionId -or $version.inheritsFrom -ne '26.3' -or $version.mainClass -ne 'net.fabricmc.loader.impl.launch.knot.KnotClient') { throw 'Unexpected Fabric launcher version.' }
New-Item -Path (Join-Path $instance 'mods') -ItemType Directory -Force | Out-Null
Copy-Item -LiteralPath $mod -Destination (Join-Path $instance 'mods\bannerlord-minecraft-bridge-0.1.0.jar')
Copy-Item -LiteralPath $api -Destination (Join-Path $instance "mods\$apiName")
# Only brand-new test instance options; never touch ordinary game options or launcher profiles.
Set-Content -LiteralPath (Join-Path $instance 'options.txt') -Value 'pauseOnLostFocus:false' -Encoding utf8NoBOM
New-Item -Path $versionDirectory -ItemType Directory | Out-Null
Set-Content -LiteralPath (Join-Path $versionDirectory "$versionId.json") -Value $versionText -Encoding utf8NoBOM
$manifest = [pscustomobject]@{
    VersionId = $versionId
    VersionJsonHash = (Get-FileHash -LiteralPath (Join-Path $versionDirectory "$versionId.json") -Algorithm SHA256).Hash
    ModHash = (Get-FileHash -LiteralPath $mod -Algorithm SHA256).Hash
    ApiHash = $expected
    CreatedUtc = [DateTime]::UtcNow.ToString('O')
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $instance 'instance-manifest.json') -Encoding utf8NoBOM
Write-Output "Prepared test game directory: $instance"
Write-Output "Added only new version: $versionId"
Write-Output 'No launcher profiles, authentication files, ordinary saves or game processes were changed.'
