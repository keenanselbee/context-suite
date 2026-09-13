[CmdletBinding()]
param([Parameter(Mandatory)][string] $ProductionStage, [Parameter(Mandatory)][string] $FixtureDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = (Resolve-Path -LiteralPath $ProductionStage).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
if (-not $fixtures.StartsWith((Join-Path $repository '.codex-temp\image-pdf-resources-'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use generated repository-local image-PDF resource fixtures.'
}
$inventory = Join-Path $PSScriptRoot '..\audio-engine\Stage-AudioPayload.py'
& python -B $inventory --payload $stage --inventory
if ($LASTEXITCODE -ne 0) { throw 'Use a verified isolated combined production stage.' }
$evidence = Join-Path $repository ('.codex-temp\image-pdf-resource-worker-' + [guid]::NewGuid().ToString('N'))
$project = Join-Path $repository 'tests\ContextSuite.Core.ContractTests'
& dotnet run --project $project -c Release -- --image-pdf-resource-worker $evidence (Join-Path $stage 'ContextSuite.Worker.exe') $fixtures
if ($LASTEXITCODE -ne 0) { throw "Image-PDF resource workflows failed; evidence retained at $evidence" }
& python -B $inventory --payload $stage --inventory
if ($LASTEXITCODE -ne 0) { throw 'Production payload changed during resource workflows.' }
Write-Output "Staged image-PDF resource evidence: $evidence"
