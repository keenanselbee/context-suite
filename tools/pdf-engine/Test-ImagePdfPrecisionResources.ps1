[CmdletBinding()]
param([Parameter(Mandatory)][string] $ProductionStage)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = (Resolve-Path -LiteralPath $ProductionStage).Path
$inventory = Join-Path $PSScriptRoot 'Stage-PdfPayload.py'
& python -B $inventory --payload $stage
if ($LASTEXITCODE -ne 0) { throw 'Use a complete verified isolated PDF candidate stage.' }
$evidence = Join-Path $repository ('.codex-temp\image-pdf-precision-resources-' + [guid]::NewGuid().ToString('N'))
$project = Join-Path $repository 'proprietary\tests\ContextSuite.Pdf.ContractTests'
& dotnet run --project $project -c Release -- --image-pdf-precision-resources (Join-Path $stage 'pdf-validator') $evidence
if ($LASTEXITCODE -ne 0) { throw "Image-PDF precision resource checks failed; evidence retained at $evidence" }
& python -B $inventory --payload $stage
if ($LASTEXITCODE -ne 0) { throw 'PDF candidate payload changed during the precision resource checks.' }
Write-Output "Image-PDF precision resource evidence: $evidence"
