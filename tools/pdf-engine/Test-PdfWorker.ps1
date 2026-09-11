[CmdletBinding()]
param([Parameter(Mandatory)][string] $ProductionStage, [Parameter(Mandatory)][string] $PreparedDirectory,
    [Parameter(Mandatory)][string] $FixtureDirectory)
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
Write-Output "Evaluation-only worker payload and results: $scratch"
