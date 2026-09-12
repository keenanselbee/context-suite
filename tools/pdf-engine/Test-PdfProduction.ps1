[CmdletBinding()]
param([Parameter(Mandatory)][string] $ProductionStage,
    [Parameter(Mandatory)][string] $PdfFixtureDirectory,
    [Parameter(Mandatory)][string] $ImagePdfFixtureDirectory,
    [Parameter(Mandatory)][string] $AudioFixtureDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = (Resolve-Path -LiteralPath $ProductionStage).Path
$pdfFixtures = (Resolve-Path -LiteralPath $PdfFixtureDirectory).Path
$imageFixtures = (Resolve-Path -LiteralPath $ImagePdfFixtureDirectory).Path
$audioFixtures = (Resolve-Path -LiteralPath $AudioFixtureDirectory).Path
foreach ($pair in @(@($stage, 'artifacts\production-staging\'),
    @($pdfFixtures, '.codex-temp\pdf-engine\'), @($imageFixtures, '.codex-temp\pdfium-engine\'),
    @($audioFixtures, '.codex-temp\audio-engine\'))) {
    if (-not $pair[0].StartsWith((Join-Path $repository $pair[1]), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Use a fresh combined production stage and generated media fixtures.'
    }
}
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'curated-engine\Test-ProductionPayload.ps1') `
    -Payload $stage -AllowPdfCandidate -AllowAudioCandidate
$inventoryTool = Join-Path (Split-Path $PSScriptRoot -Parent) 'audio-engine\Stage-AudioPayload.py'
& python -B $inventoryTool --payload $stage --inventory
if ($LASTEXITCODE -ne 0) { throw 'Combined production inventory verification failed.' }
$scratch = Join-Path $repository ('.codex-temp\pdf-engine\production-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$project = Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj'
$worker = Join-Path $stage 'ContextSuite.Worker.exe'
$cases = @(
    @('--pdf-worker', 'analysis', $pdfFixtures),
    @('--pdf-optimization-worker', 'optimization', $pdfFixtures),
    @('--pdf-failures', 'failures', $pdfFixtures),
    @('--pdf-direct', 'direct', $pdfFixtures, $audioFixtures),
    @('--pdf-page-worker', 'pages', $pdfFixtures),
    @('--pdf-page-failures', 'page-failures', $pdfFixtures),
    @('--pdf-page-direct', 'pages-direct', $pdfFixtures),
    @('--image-pdf-worker', 'combined', $imageFixtures),
    @('--image-pdf-direct', 'combined-direct', $imageFixtures)
)
foreach ($case in $cases) {
    $arguments = @($case[0], (Join-Path $scratch $case[1]), $worker) + @($case[2..($case.Count - 1)])
    & dotnet run --project $project -c Release -- @arguments
    if ($LASTEXITCODE -ne 0) { throw "Packaged PDF workflow failed: $($case[0]); results retained at $scratch" }
}
& python -B $inventoryTool --payload $stage --inventory
if ($LASTEXITCODE -ne 0) { throw 'Combined production payload changed during PDF workflows.' }
Write-Output "Actual packaged PDF worker: $worker; results: $scratch"
