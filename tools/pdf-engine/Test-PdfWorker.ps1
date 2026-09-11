[CmdletBinding()]
param([Parameter(Mandatory)][string] $ProductionStage, [Parameter(Mandatory)][string] $PreparedDirectory,
    [Parameter(Mandatory)][string] $FixtureDirectory, [string] $AudioPreparedDirectory, [string] $AudioFixtureDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = (Resolve-Path -LiteralPath $ProductionStage).Path
$stageRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts\production-staging')) + '\'
$pdfRoot = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\pdf-engine')) + '\'
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
if (-not $stage.StartsWith($stageRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $prepared.StartsWith($pdfRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $fixtures.StartsWith($pdfRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use isolated staging and generated PDF paths.' }
if ([bool]$AudioPreparedDirectory -ne [bool]$AudioFixtureDirectory) { throw 'Direct mixed-family checks require both generated audio paths.' }
if ($AudioPreparedDirectory) {
    $audioRoot = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\audio-engine')) + '\'
    $audioPrepared = (Resolve-Path -LiteralPath $AudioPreparedDirectory).Path
    $audioFixtures = (Resolve-Path -LiteralPath $AudioFixtureDirectory).Path
    if (-not $audioPrepared.StartsWith($audioRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not $audioFixtures.StartsWith($audioRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use isolated generated audio paths.' }
}
$scratch = Join-Path $prepared ('worker-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $scratch 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
Get-ChildItem -LiteralPath $stage -File | Where-Object Name -ne 'payload-inventory.json' | Copy-Item -Destination $payload
$pdf = Join-Path $payload 'pdf-engine'
New-Item -ItemType Directory -Path $pdf | Out-Null
Get-ChildItem -LiteralPath (Join-Path $prepared 'unpacked\qpdf-12.4.1-msvc64\bin') -File |
    Where-Object { $_.Name -eq 'qpdf.exe' -or $_.Extension -eq '.dll' } | Copy-Item -Destination $pdf
Copy-Item -LiteralPath (Join-Path $prepared 'evaluation.json') -Destination $pdf
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --pdf-worker (Join-Path $scratch 'results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
if ($LASTEXITCODE -ne 0) { throw 'Isolated PDF worker checks failed.' }
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --pdf-optimization-worker (Join-Path $scratch 'optimization-results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
if ($LASTEXITCODE -ne 0) { throw 'Isolated PDF optimization workflow checks failed.' }
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --pdf-failures (Join-Path $scratch 'failure-results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
if ($LASTEXITCODE -ne 0) { throw 'Isolated PDF failure checks failed.' }
if ($AudioPreparedDirectory) {
    $pin = Get-Content -LiteralPath (Join-Path $repository 'tools\audio-engine\evaluation.json') -Raw | ConvertFrom-Json
    $audio = Join-Path $payload 'audio-engine'
    New-Item -ItemType Directory -Path $audio | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $audioPrepared ('unpacked\' + $pin.archiveDirectory + '\bin')) -File |
        Where-Object Name -ne 'ffplay.exe' | Copy-Item -Destination $audio
    Copy-Item -LiteralPath (Join-Path $audioPrepared 'evaluation.json') -Destination $audio
    Copy-Item -LiteralPath (Join-Path $audioPrepared ('unpacked\' + $pin.archiveDirectory + '\LICENSE.txt')) -Destination $audio
    & dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --pdf-direct (Join-Path $scratch 'direct-results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures $audioFixtures
    if ($LASTEXITCODE -ne 0) { throw 'Isolated direct PDF checks failed.' }
}
Write-Output "Evaluation-only worker payload and results: $scratch"
