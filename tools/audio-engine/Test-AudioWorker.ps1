[CmdletBinding()]
param([Parameter(Mandatory)][string] $ProductionStage, [Parameter(Mandatory)][string] $PreparedDirectory,
    [Parameter(Mandatory)][string] $FixtureDirectory, [string] $ArtworkFixture, [switch] $IncludeOptimization, [switch] $IncludeConversion)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = (Resolve-Path -LiteralPath $ProductionStage).Path
$stageRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts\production-staging')) + '\'
$audioRoot = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\audio-engine')) + '\'
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
if (-not $stage.StartsWith($stageRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $prepared.StartsWith($audioRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $fixtures.StartsWith($audioRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use isolated staging and generated audio paths.' }
$artworkArguments = @()
if ($ArtworkFixture) {
    $artwork = (Resolve-Path -LiteralPath $ArtworkFixture).Path
    if (-not $artwork.StartsWith($audioRoot, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $artwork -PathType Leaf)) {
        throw 'Use a generated artwork fixture under isolated audio scratch.'
    }
    $artworkArguments = @($artwork)
}
$scratch = Join-Path $prepared ('worker-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $scratch 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
Get-ChildItem -LiteralPath $stage -File | Where-Object Name -ne 'payload-inventory.json' | Copy-Item -Destination $payload
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evaluation.json') -Raw | ConvertFrom-Json
$audio = Join-Path $payload 'audio-engine'
New-Item -ItemType Directory -Path $audio | Out-Null
Get-ChildItem -LiteralPath (Join-Path $prepared ('unpacked\' + $pin.archiveDirectory + '\bin')) -File |
    Where-Object Name -ne 'ffplay.exe' | Copy-Item -Destination $audio
Copy-Item -LiteralPath (Join-Path $prepared 'evaluation.json') -Destination $audio
Copy-Item -LiteralPath (Join-Path $prepared ('unpacked\' + $pin.archiveDirectory + '\LICENSE.txt')) -Destination $audio
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --audio-worker (Join-Path $scratch 'results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures @artworkArguments
if ($LASTEXITCODE -ne 0) { throw 'Isolated audio worker checks failed.' }
if ($IncludeOptimization) {
    & dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --flac-worker (Join-Path $scratch 'flac-results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'Isolated FLAC workflow checks failed.' }
    & dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --audio-direct (Join-Path $scratch 'direct-results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'Isolated direct audio checks failed.' }
}
if ($IncludeConversion) {
    & dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --audio-conversion-worker (Join-Path $scratch 'conversion-results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'Isolated audio conversion workflow checks failed.' }
    & dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --audio-conversion-direct (Join-Path $scratch 'conversion-direct-results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'Isolated direct audio conversion checks failed.' }
}
Write-Output "Evaluation-only worker payload and results: $scratch"
