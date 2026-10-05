# Lightweight publication guard, not a replacement for a dedicated secret scanner.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (Test-Path -LiteralPath (Join-Path $root '.git')) {
    # Audit tracked paths plus untracked-and-not-ignored paths, i.e. what could be committed.
    $paths = @(git -C $root ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate publishable files' }
    if ($paths.Count -eq 0) { throw 'No publishable files found; check repository state before committing' }
} else {
    $paths = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object {
        $_.FullName.Substring($root.Length + 1).Replace('\', '/')
    } | Where-Object {
        $_ -notmatch '(^|/)(bin|obj|artifacts|\.git|\.local|\.vs|\.idea)/' -and
        $_ -notmatch '\.local\.(props|json)$'
    })
}
$patterns = @(
    '(?<![A-Za-z0-9])[A-Za-z]:[\\/]',
    '-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
    'AKIA[0-9A-Z]{16}',
    'gh[pousr]_[A-Za-z0-9]{20,}',
    'github_pat_[A-Za-z0-9_]{20,}',
    'sk-[A-Za-z0-9_-]{20,}',
    '(?i)(?:api[_-]?key|access[_-]?token|client[_-]?secret|password|passwd)\s*[:=]\s*["''][^"'']{8,}["'']'
)
$issues = @()
foreach ($relative in $paths) {
    if ($relative -match '(?i)(^|/)(\.env(?:\..*)?|credentials\.json|secrets\.json)$' -and
        $relative -notmatch '(?i)(\.example|\.sample)$') { $issues += "$relative : credential-like file name" }
    if ($relative -match '(?i)\.(dll|exe|pdb|tpac|pem|key|pfx|p12|keystore)$') {
        $issues += "$relative : binary/game asset/private-key file is not allowed in source publication"
        continue
    }
    $path = Join-Path $root $relative
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    if ($relative -notmatch '(?i)\.(cs|java|gradle|properties|csproj|ps1|md|xml|json|yml|yaml|config|props|targets|txt|toml|ini)$') { continue }
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $path) {
        $lineNumber++
        foreach ($pattern in $patterns) {
            if ($line -cmatch $pattern) {
                # Never print the matching value: it might be a real secret.
                $issues += "${relative}:${lineNumber} : possible secret or machine-specific absolute path"
                break
            }
        }
    }
}
if ($issues.Count -gt 0) { $issues | ForEach-Object { Write-Output $_ }; throw 'Publication audit failed. Review findings before committing.' }
Write-Output "PASS: $($paths.Count) source files checked for common secrets, absolute paths and prohibited artifacts."
