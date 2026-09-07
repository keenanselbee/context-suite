[CmdletBinding()]
param(
    [ValidatePattern('^[a-zA-Z0-9-]+$')][string] $RunName = 'prototype-2',
    [ValidatePattern('^[a-zA-Z0-9-]+$')][string] $TestName = 'test-output'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workspace = Join-Path $repository ".codex-temp\curated-engine\$RunName"
$tests = Join-Path $workspace $TestName
if (Test-Path -LiteralPath $tests) { throw 'Test output already exists; use a fresh workspace or inspect it explicitly.' }
$candidate = Join-Path $workspace 'native\src\Magick.Native\bin\ReleaseQ16\x64\Magick.Native-Q16-x64.dll'
& (Join-Path $PSScriptRoot 'New-CuratedNotices.ps1') -Workspace $workspace
$sources = [ordered]@{
    engine = 'artifacts\managed\bin\ContextSuite.Image.ContractTests\Release\net10.0'
    foundation = 'artifacts\managed\bin\ContextSuite.Core.ContractTests\Release\net10.0-windows'
    desktop = 'artifacts\managed\bin\ContextSuite.Desktop.SmokeTests\Release\net10.0-windows'
    testhost = 'artifacts\managed\bin\ContextSuite.Application.TestHost\Release\net10.0-windows'
    production = 'artifacts\production\Release'
}
foreach ($name in $sources.Keys) {
    $source = Join-Path $repository $sources[$name]
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing previously built test input: $source" }
    $destination = Join-Path $tests $name
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    # Flattened Windows payload deliberately excludes NuGet runtime subfolders.
    Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -notlike 'Magick.Native*' } | Copy-Item -Destination $destination
    if ($name -in @('engine', 'production')) {
        Copy-Item -LiteralPath $candidate -Destination $destination
        Copy-Item -LiteralPath (Join-Path $workspace 'Magick.NET.Notice.txt') -Destination $destination -Force
        & (Join-Path $PSScriptRoot 'Test-CuratedPayload.ps1') -Workspace $workspace -Payload $destination
    }
}
Write-Output "Prepared isolated test output at $tests; no production files changed."
