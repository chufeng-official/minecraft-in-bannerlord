$script:RepoRoot = Split-Path $PSScriptRoot -Parent

function Resolve-GameDirectory {
    param([string]$GameDirectory)
    if ([string]::IsNullOrWhiteSpace($GameDirectory)) { $GameDirectory = $env:BANNERLORD_GAME_DIR }
    if ([string]::IsNullOrWhiteSpace($GameDirectory)) {
        $propsPath = Join-Path $script:RepoRoot 'src\BannerlordMod\GameDirectory.local.props'
        if (Test-Path -LiteralPath $propsPath) {
            [xml]$props = Get-Content -LiteralPath $propsPath -Raw
            $GameDirectory = [string]$props.Project.PropertyGroup.GameDirectory
        }
    }
    if ([string]::IsNullOrWhiteSpace($GameDirectory)) {
        throw 'Set BANNERLORD_GAME_DIR, pass -GameDirectory, or copy src/BannerlordMod/GameDirectory.local.props.example to GameDirectory.local.props. No game path is assumed.'
    }
    $resolved = (Resolve-Path -LiteralPath $GameDirectory -ErrorAction Stop).ProviderPath
    if (!(Test-Path -LiteralPath (Join-Path $resolved 'Modules') -PathType Container)) {
        throw 'GameDirectory must be the Bannerlord installation root containing Modules.'
    }
    return $resolved
}

function Assert-GameClosed {
    if (@(Get-Process | Where-Object { $_.ProcessName -match 'Bannerlord|TaleWorlds' }).Count -gt 0) {
        throw 'Close Bannerlord and its launcher before deploying or removing the module.'
    }
}
