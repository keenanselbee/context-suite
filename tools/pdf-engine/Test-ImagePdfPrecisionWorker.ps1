[CmdletBinding()]
param([Parameter(Mandatory)][string] $ProductionStage, [Parameter(Mandatory)][string] $FixtureDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = (Resolve-Path -LiteralPath $ProductionStage).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
if (-not $fixtures.StartsWith((Join-Path $repository '.codex-temp\image-pdf-precision-resources-'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use generated repository-local image-PDF precision fixtures.'
}
$inventory = Join-Path $PSScriptRoot '..\audio-engine\Stage-AudioPayload.py'
& python -B $inventory --payload $stage --inventory
if ($LASTEXITCODE -ne 0) { throw 'Use a complete verified isolated combined production stage.' }
$project = Join-Path $repository 'tests\ContextSuite.Core.ContractTests'
& dotnet run --project $project -c Release -- --image-pdf-precision-worker $fixtures (Join-Path $stage 'ContextSuite.Worker.exe')
if ($LASTEXITCODE -ne 0) { throw "Image-PDF precision workflows failed; evidence retained at $fixtures\worker" }
& python -B $inventory --payload $stage --inventory
if ($LASTEXITCODE -ne 0) { throw 'Production payload changed during precision workflows.' }
Write-Output "Image-PDF precision worker evidence: $fixtures\worker"
