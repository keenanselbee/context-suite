[CmdletBinding()]
param([Parameter(Mandatory)][string] $ProductionStage)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = (Resolve-Path -LiteralPath $ProductionStage).Path
if (-not $stage.StartsWith((Join-Path $repository 'artifacts\production-staging\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an isolated production stage inside this repository.'
}
if (Get-Process -Name 'ContextSuite*' -ErrorAction SilentlyContinue) { throw 'Close Context Suite before worker acceptance checks.' }
$inventory = Join-Path $PSScriptRoot 'Stage-PdfPayload.py'
& python -B $inventory --payload $stage
if ($LASTEXITCODE -ne 0) { throw 'Use a complete verified isolated PDF candidate stage.' }
$evidence = Join-Path $repository ('.codex-temp\pdf-page-geometry-' + [guid]::NewGuid().ToString('N'))
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests') -c Release -- --pdf-page-geometry $evidence (Join-Path $stage 'ContextSuite.Worker.exe')
if ($LASTEXITCODE -ne 0) { throw "PDF page geometry checks failed; evidence retained at $evidence" }
& python -B $inventory --payload $stage
if ($LASTEXITCODE -ne 0) { throw 'PDF payload changed during geometry checks.' }
Write-Output "PDF page geometry evidence: $evidence"
