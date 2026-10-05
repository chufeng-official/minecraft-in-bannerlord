param([string]$GameDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Common.ps1"
$GameDirectory = Resolve-GameDirectory $GameDirectory
$prefabPath = Join-Path $GameDirectory 'Modules\Native\Prefabs\editor_contents.xml'
[xml]$xml = Get-Content -LiteralPath $prefabPath -Raw
$cube = @($xml.prefabs.game_entity | Where-Object { $_.name -eq 'editor_cube' })
if ($cube.Count -ne 1) { throw 'Expected exactly one root editor_cube prefab' }
if ($cube[0].physics.shape -ne 'bo_editor_cube') { throw 'Unexpected physics shape' }
if ($cube[0].components.meta_mesh_component.name -ne 'editor_cube') { throw 'Unexpected mesh' }
if ($cube[0].scripts -or $cube[0].children) { throw 'Unexpected scripts or children' }
$package = Join-Path $GameDirectory 'Modules\Native\AssetPackages\editor_contents.tpac'
if (!(Test-Path -LiteralPath $package)) { throw 'Asset package missing' }
Write-Output "Candidate confirmed in XML: $prefabPath"
Write-Output 'prefab=editor_cube; mesh=editor_cube; shape=bo_editor_cube; no scripts or children'
Write-Output "Package exists: $package"
Write-Output 'Runtime availability, unit dimensions, player and missile collisions remain unverified.'
