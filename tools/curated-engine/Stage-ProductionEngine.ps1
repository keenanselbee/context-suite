[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9-]+$')][string] $RunName = 'prototype-2')
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workspace = Join-Path $repository ".codex-temp\curated-engine\$RunName"
$selection = Get-Content (Join-Path $PSScriptRoot 'production.json') -Raw | ConvertFrom-Json
$preparation = Get-Content (Join-Path $workspace 'preparation.json') -Raw | ConvertFrom-Json
foreach ($field in 'nativeRevision','imageMagickRevision','dependencyRelease') {
    if ($preparation.$field -ne $selection.$field) { throw "Unreviewed build input: $field" }
}
& (Join-Path $PSScriptRoot 'New-CuratedNotices.ps1') -Workspace $workspace
$staging = Join-Path $workspace ('production-stage-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -LiteralPath (Join-Path $workspace 'native\src\Magick.Native\bin\ReleaseQ16\x64\Magick.Native-Q16-x64.dll'), (Join-Path $workspace 'Magick.NET.Notice.txt') -Destination $staging
& (Join-Path $PSScriptRoot 'Test-CuratedPayload.ps1') -Workspace $workspace -Payload $staging
& (Join-Path $PSScriptRoot 'Test-ProductionEngine.ps1') -Payload $staging
$destination = Join-Path $repository 'artifacts\engines\curated-win-x64'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $staging 'Magick.Native-Q16-x64.dll'), (Join-Path $staging 'Magick.NET.Notice.txt') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $workspace 'preparation.json'), (Join-Path $workspace 'build-result.json') -Destination $destination -Force
& (Join-Path $PSScriptRoot 'Test-ProductionEngine.ps1') -Payload $destination
Write-Output 'Staged reviewed engine for local production builds. No installation or release was performed.'
