$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$validatedRoot = Join-Path $projectRoot 'Temp\Phase1Validation'
$sceneFile = Join-Path $projectRoot 'Assets\PDT\Scenes\DrivingTestV2.unity'
$original = [IO.File]::ReadAllText($sceneFile)
$validated = [IO.File]::ReadAllText((Join-Path $validatedRoot 'Assets\PDT\Scenes\DrivingTestV2.unity'))
$blockPattern = '(?ms)^--- !u!\d+ &(-?\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)'
$sourceBlocks = [regex]::Matches($validated, $blockPattern)
$originalBlocks = [regex]::Matches($original, $blockPattern)
$prefabMeta = [IO.File]::ReadAllText((Join-Path $validatedRoot 'Assets\PDT\Environment\PDT Scenic Road.prefab.meta'))
$prefabGuid = [regex]::Match($prefabMeta, 'guid: ([a-f0-9]+)').Groups[1].Value
$environment = @($sourceBlocks | Where-Object { $_.Value -match "m_SourcePrefab:.*guid: $prefabGuid," })
if ($environment.Count -ne 1) { throw 'Expected one validated environment prefab instance.' }
$instanceId = $environment[0].Groups[1].Value
if (@($originalBlocks | Where-Object { $_.Groups[1].Value -eq $instanceId }).Count -gt 0) { throw 'Scene ID collision; do not install twice.' }
$lighting = @($sourceBlocks | Where-Object { $_.Value -match '(?m)^RenderSettings:' })[0].Value
$oldLighting = @($originalBlocks | Where-Object { $_.Value -match '(?m)^RenderSettings:' })[0].Value
$ground = @($originalBlocks | Where-Object { $_.Value -match '(?m)^  m_Name: Ground\s*$' })
if ($ground.Count -ne 1) { throw 'Expected one original Ground object.' }
$roots = @($originalBlocks | Where-Object { $_.Value -match '(?m)^SceneRoots:' })[0].Value
$updatedRoots = $roots.TrimEnd() + "`n  - {fileID: $instanceId}`n"
$updated = $original.Replace($oldLighting, $lighting).Replace($ground[0].Value, $ground[0].Value.Replace('  m_IsActive: 1', '  m_IsActive: 0')).Replace($roots, $environment[0].Value + $updatedRoots)

# Ensure every existing block outside environment lighting/Ground/root list is byte-identical.
$allowed = @('2', $ground[0].Groups[1].Value, '9223372036854775807')
$updatedBlocks = [regex]::Matches($updated, $blockPattern)
foreach ($block in $originalBlocks) {
    if ($block.Groups[1].Value -in $allowed) { continue }
    $after = @($updatedBlocks | Where-Object { $_.Groups[1].Value -eq $block.Groups[1].Value })
    if ($after.Count -ne 1 -or $after[0].Value -cne $block.Value) { throw "Protected scene block changed: $($block.Groups[1].Value)" }
}

Copy-Item -LiteralPath (Join-Path $validatedRoot 'Assets\PDT\Environment') -Destination (Join-Path $projectRoot 'Assets\PDT') -Recurse -Force
foreach ($relative in @('Assets\PDT\Environment.meta', 'Assets\PDT\Editor.meta', 'Assets\PDT\Editor\ScenicRoadBuilder.cs.meta', 'Assets\PDT\Scripts\ScenicRoadSettings.cs.meta')) {
    Copy-Item -LiteralPath (Join-Path $validatedRoot $relative) -Destination (Join-Path $projectRoot $relative) -Force
}
[IO.File]::WriteAllText($sceneFile, $updated, [Text.UTF8Encoding]::new($false))
Copy-Item -Path (Join-Path $validatedRoot 'Logs\Environment-*.png'), (Join-Path $validatedRoot 'Logs\Environment-metrics.txt'), (Join-Path $validatedRoot 'Logs\Environment-results.txt') -Destination $PSScriptRoot -Force
Write-Output 'Installed validated environment. All other existing scene blocks are byte-identical.'
