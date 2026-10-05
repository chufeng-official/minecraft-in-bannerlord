# Bannerlord Dynamic Blocks Prototype

English | [简体中文](README.zh-CN.md)

A singleplayer proof-of-concept that adds player-built, collidable, cleanly removable
blocks to *Mount & Blade II: Bannerlord* custom battles. It is the M0/M1 milestone of
[`TECHNICAL_PLAN.md`](TECHNICAL_PLAN.md): prove that the host can carry dynamic blocks
before any Minecraft integration.

This repository contains source only. It is **not** a Minecraft port, has no network
bridge, and does not modify vanilla game files or user saves.

## Status

Prototype, verified on one local `v1.4.8.119303` installation. Compilation success is
not the same as in-game acceptance; see [docs/M0-M1.md](docs/M0-M1.md) for what was and
was not tested.

Confirmed by local testing: module load, dynamic display, player/projectile collision,
selection and deletion, exit cleanup, riding/AI behavior, and a no-physics placement
preview. Not claimed: large-scale performance, dynamic navigation, arbitrary vanilla-surface
building, campaign persistence, or Minecraft integration.

## Features

- Place a one-metre block on aimed terrain within reach, or extend an existing block
  across any of its six faces.
- Shared one-metre vertical grid so adjacent blocks stay aligned across uneven ground.
- Green/red preview that follows your aim and reflects placement validity.
- Selection highlight, deletion, and clear-all with conservative overlap protection.
- Per-session diagnostics (counts, tick timing, managed memory) and clean scene teardown.

## Requirements

- Windows and a self-owned, licensed *Mount & Blade II: Bannerlord* installation.
- .NET SDK (see `global.json`); game assemblies are used only as local compile references.
- PowerShell 7 (`pwsh`).

No NuGet packages, Minecraft, Harmony, or Java are required.

## Build

The game directory is never hard-coded. Point the build at your own installation with an
environment variable or a script argument:

```powershell
$env:BANNERLORD_GAME_DIR = '<path to your Bannerlord installation>'
pwsh -File tools/Build.ps1
# or
pwsh -File tools/Build.ps1 -GameDirectory '<path to your Bannerlord installation>'
```

Output is staged under `artifacts/BannerlordBlocks` and contains only this project's DLL
and `SubModule.xml`.

### IDE setup

For VSCode / Rider, copy `src/BannerlordMod/GameDirectory.local.props.example` to
`GameDirectory.local.props` and fill in your installation root. That file is git-ignored, so
it never reaches the repository; it lets the IDE resolve game references and keeps
design-time builds from raising errors. The command-line scripts read the same file as a
fallback.

## Deploy and remove

These scripts touch the game install, so they require an explicit switch and a closed game.

```powershell
# Creates only Modules\BannerlordBlocks; refuses to overwrite an existing module.
pwsh -File tools/Deploy.ps1 -ConfirmDeployment

# Removes only files listed in the ownership manifest whose hashes are unchanged.
pwsh -File tools/Remove.ps1 -ConfirmRemoval
```

## In-game controls

Battle-mode only. Eye-based aiming is not the third-person screen centre.

| Key | Action |
| --- | --- |
| Insert | Place on aimed terrain, or extend an aimed block face |
| Ctrl + Insert | Toggle the placement preview |
| PageUp | Select an owned block (yellow highlight) |
| Delete | Delete the selected block |
| PageDown | Clear all blocks |
| Ctrl + PageUp | Print status and write a diagnostic sample |

See [docs/TEST_CHECKLIST.md](docs/TEST_CHECKLIST.md) for the guided test plan and
diagnostic field meanings.

## Tests

Engine-independent tests run without the game:

```powershell
dotnet run --project tests/Diagnostics/Diagnostics.csproj -c Release
pwsh -File tools/AuditSource.ps1
```

CI runs only these checks; it has no game licence or assemblies, so a green build does not
mean the mod loads or physics works.

## Diagnostics

Runtime log: `%LOCALAPPDATA%\BannerlordBlocks\prototype.log`.

```powershell
pwsh -File tools/ReadDiagnostics.ps1 -Tail 100
```

A `prefab.txt` beside the log overrides the default `editor_cube` prefab name.

## License and asset boundary

Original source, scripts and docs are licensed under the [MIT License](LICENSE). That
licence does **not** cover TaleWorlds/Bannerlord or Mojang/Minecraft content, names or
trademarks. Game assemblies and vanilla assets are not included or redistributed; the mod
references local game files and loads local `editor_cube` / `bo_editor_cube` resources at
runtime. See [NOTICE.md](NOTICE.md). This project is unaffiliated with and unendorsed by
TaleWorlds, Mojang or Microsoft.

## Contributing and security

- [CONTRIBUTING.md](CONTRIBUTING.md)
- [SECURITY.md](SECURITY.md) — report vulnerabilities privately, never in a public issue.
- [CHANGELOG.md](CHANGELOG.md)

## 中文文档

完整中文说明见 [README.zh-CN.md](README.zh-CN.md)。
