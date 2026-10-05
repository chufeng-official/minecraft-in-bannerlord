param([switch]$ConfirmDownload)
$ErrorActionPreference = 'Stop'
if (!$ConfirmDownload) { throw 'Downloads require explicit confirmation: -ConfirmDownload. No system Java or PATH changes.' }
$root = Split-Path $PSScriptRoot -Parent
$local = Join-Path $root '.local\minecraft-dev'
New-Item -Path $local -ItemType Directory -Force | Out-Null

function Install-VerifiedZip {
    param([string]$Name, [string]$Url, [string]$Hash, [string]$ExpectedDirectory, [string]$Executable)
    $archive = Join-Path $local "$Name.zip"
    $destination = Join-Path $local $Name
    $toolHome = Join-Path $destination $ExpectedDirectory
    if (!(Test-Path -LiteralPath $archive)) {
        $partial = "$archive.partial"
        Invoke-WebRequest -Uri $Url -OutFile $partial -TimeoutSec 600
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $Hash) { throw "$Name checksum mismatch; untrusted archive not extracted." }
        Move-Item -LiteralPath $partial -Destination $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $Hash) { throw "$Name cached archive checksum mismatch." }
    if (!(Test-Path -LiteralPath $destination)) { Expand-Archive -LiteralPath $archive -DestinationPath $destination }
    if (!(Test-Path -LiteralPath (Join-Path $toolHome $Executable))) { throw "$Name extraction incomplete or unexpected layout; refusing to overwrite existing directory." }
    return $toolHome
}

$jdk = Install-VerifiedZip -Name 'jdk25' `
    -Url 'https://github.com/adoptium/temurin25-binaries/releases/download/jdk-25.0.4.1%2B1/OpenJDK25U-jdk_x64_windows_hotspot_25.0.4.1_1.zip' `
    -Hash '00c847d804f4a78e9f04f2683faf14fed898535b177b7fc704486cb0284e9283' `
    -ExpectedDirectory 'jdk-25.0.4.1+1' -Executable 'bin\javac.exe'
$gradle = Install-VerifiedZip -Name 'gradle971' `
    -Url 'https://services.gradle.org/distributions/gradle-9.7.1-bin.zip' `
    -Hash 'acd53f1edaf02f1a8ff99879f8a34b302661a057d9b063ae9e35b552f804d20a' `
    -ExpectedDirectory 'gradle-9.7.1' -Executable 'bin\gradle.bat'
& (Join-Path $jdk 'bin\java.exe') -version
if ($LASTEXITCODE -ne 0) { throw 'Local Java validation failed.' }
& (Join-Path $jdk 'bin\javac.exe') -version
if ($LASTEXITCODE -ne 0) { throw 'Local javac validation failed.' }
Write-Output "Prepared isolated development tools: $local"
