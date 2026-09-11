[CmdletBinding()]
param([Parameter(Mandatory)][string] $CandidateDirectory,
      [Parameter(Mandatory)][string] $FixtureDirectory,
      [Parameter(Mandatory)][string] $IndependentDecoderDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp')) + '\'
$audioRoot = Join-Path $scratchRoot 'audio-engine\'
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
$independent = (Resolve-Path -LiteralPath $IndependentDecoderDirectory).Path
if (-not $candidate.StartsWith($scratchRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $fixtures.StartsWith($audioRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $independent.StartsWith($audioRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an isolated candidate and retained generated audio evaluation inputs.'
}
& python -B (Join-Path $PSScriptRoot 'Check-AudioBuild.py') $candidate
if ($LASTEXITCODE -ne 0) { throw 'Candidate native build review failed.' }
$evidence = Join-Path $candidate ('adapter-' + [guid]::NewGuid().ToString('N'))
$project = Join-Path $repository 'proprietary\tests\ContextSuite.Audio.ContractTests\ContextSuite.Audio.ContractTests.csproj'
& dotnet run --project $project -c Release -- (Join-Path $candidate 'bin') $fixtures $evidence --independent-decoder $independent
if ($LASTEXITCODE -ne 0) { throw 'Curated audio adapter checks failed.' }
Write-Output "Curated audio adapter evidence: $evidence"
