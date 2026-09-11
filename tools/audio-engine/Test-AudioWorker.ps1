[CmdletBinding(DefaultParameterSetName = 'Evaluation')]
param([Parameter(Mandatory)][string] $ProductionStage,
    [Parameter(Mandatory, ParameterSetName = 'Evaluation')][string] $PreparedDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Curated')][string] $CandidateDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Packaged')][switch] $Packaged,
    [Parameter(Mandatory)][string] $FixtureDirectory, [string] $ArtworkFixture, [switch] $IncludeOptimization, [switch] $IncludeConversion)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = (Resolve-Path -LiteralPath $ProductionStage).Path
$stageRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts\production-staging')) + '\'
$audioRoot = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\audio-engine')) + '\'
$isCurated = $PSCmdlet.ParameterSetName -eq 'Curated'
$prepared = if ($Packaged) { $stage } else {
    (Resolve-Path -LiteralPath $(if ($isCurated) { $CandidateDirectory } else { $PreparedDirectory })).Path
}
$candidateRoot = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp')) + '\'
$allowedRoot = if ($Packaged) { $stageRoot } elseif ($isCurated) { $candidateRoot } else { $audioRoot }
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
if (-not $stage.StartsWith($stageRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $prepared.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $fixtures.StartsWith($audioRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use isolated staging and generated audio paths.' }
$artworkArguments = @()
if ($ArtworkFixture) {
    $artwork = (Resolve-Path -LiteralPath $ArtworkFixture).Path
    if ((-not $artwork.StartsWith($audioRoot, [StringComparison]::OrdinalIgnoreCase) -and
         -not ($isCurated -and $artwork.StartsWith($prepared + '\', [StringComparison]::OrdinalIgnoreCase)) -and
         -not ($Packaged -and $artwork.StartsWith($candidateRoot, [StringComparison]::OrdinalIgnoreCase))) -or
        -not (Test-Path -LiteralPath $artwork -PathType Leaf)) {
        throw 'Use a generated artwork fixture under isolated audio scratch.'
    }
    $artworkArguments = @($artwork)
}
if ($isCurated) {
    & python -B (Join-Path $PSScriptRoot 'Check-AudioBuild.py') $prepared
    if ($LASTEXITCODE -ne 0) { throw 'Candidate native build review failed.' }
}
$scratch = Join-Path $(if ($Packaged) { $audioRoot } else { $prepared }) ('worker-' + [guid]::NewGuid().ToString('N'))
if ($Packaged) {
    & (Join-Path (Split-Path $PSScriptRoot -Parent) 'curated-engine\Test-ProductionPayload.ps1') -Payload $stage -AllowAudioCandidate
    & python -B (Join-Path $PSScriptRoot 'Stage-AudioPayload.py') --payload $stage --inventory
    if ($LASTEXITCODE -ne 0) { throw 'Packaged audio inventory verification failed.' }
    $payload = $stage
    New-Item -ItemType Directory -Path $scratch | Out-Null
}
else {
    $payload = Join-Path $scratch 'payload'
    New-Item -ItemType Directory -Path $payload | Out-Null
    Get-ChildItem -LiteralPath $stage -File | Where-Object Name -ne 'payload-inventory.json' | Copy-Item -Destination $payload
    $audio = Join-Path $payload 'audio-engine'
    New-Item -ItemType Directory -Path $audio | Out-Null
    if ($isCurated) {
        $manifest = Join-Path $PSScriptRoot 'curated-candidate.json'
        $pin = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
        foreach ($file in $pin.files) {
            $inputFile = Join-Path (Join-Path $prepared 'bin') $file.path
            if ((Get-Item -LiteralPath $inputFile).Length -ne $file.bytes -or
                (Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash -ne $file.sha256) {
                throw 'Candidate differs from the reviewed runtime pin.'
            }
            Copy-Item -LiteralPath $inputFile -Destination $audio
        }
        Copy-Item -LiteralPath $manifest -Destination $audio
    }
    else {
        $pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evaluation.json') -Raw | ConvertFrom-Json
        Get-ChildItem -LiteralPath (Join-Path $prepared ('unpacked\' + $pin.archiveDirectory + '\bin')) -File |
            Where-Object Name -ne 'ffplay.exe' | Copy-Item -Destination $audio
        Copy-Item -LiteralPath (Join-Path $prepared 'evaluation.json') -Destination $audio
        Copy-Item -LiteralPath (Join-Path $prepared ('unpacked\' + $pin.archiveDirectory + '\LICENSE.txt')) -Destination $audio
    }
}
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
if ($Packaged) {
    & python -B (Join-Path $PSScriptRoot 'Stage-AudioPayload.py') --payload $stage --inventory
    if ($LASTEXITCODE -ne 0) { throw 'Packaged payload changed during worker checks.' }
}
Write-Output "Isolated audio worker payload: $payload; results: $scratch"
