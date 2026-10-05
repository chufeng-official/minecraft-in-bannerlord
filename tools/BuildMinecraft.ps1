param([switch]$ConfirmDependencyDownload)
$ErrorActionPreference = 'Stop'
if (!$ConfirmDependencyDownload) { throw 'First and subsequent builds may download dependencies. Use -ConfirmDependencyDownload after confirmation.' }
$root = Split-Path $PSScriptRoot -Parent
$local = Join-Path $root '.local\minecraft-dev'
$jdk = Join-Path $local 'jdk25\jdk-25.0.4.1+1'
$gradle = Join-Path $local 'gradle971\gradle-9.7.1\bin\gradle.bat'
if (!(Test-Path -LiteralPath (Join-Path $jdk 'bin\javac.exe')) -or !(Test-Path -LiteralPath $gradle)) { throw 'Run PrepareMinecraftDev.ps1 with download confirmation first.' }
$oldJavaHome = $env:JAVA_HOME
$oldGradleHome = $env:GRADLE_USER_HOME
try {
    $env:JAVA_HOME = $jdk
    $env:GRADLE_USER_HOME = Join-Path $local 'gradle-cache'
    $fixtures = Join-Path $root 'artifacts\bridge-fixtures'
    dotnet run --project (Join-Path $root 'tests\Bridge\Bridge.csproj') -c Release -- --write-fixtures $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'C# interop fixture generation failed.' }
    & $gradle --no-daemon --console=plain --warning-mode=all -p (Join-Path $root 'minecraft') build
    if ($LASTEXITCODE -ne 0) { throw 'Minecraft Mod build failed.' }
    dotnet run --project (Join-Path $root 'tests\Bridge\Bridge.csproj') -c Release -- --verify-java-fixture $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'Java/C# interop check failed.' }
} finally {
    $env:JAVA_HOME = $oldJavaHome
    $env:GRADLE_USER_HOME = $oldGradleHome
}
Write-Output 'Compiled only; no launcher installation, game launch or world creation.'
