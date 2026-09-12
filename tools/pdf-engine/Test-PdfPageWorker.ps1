[CmdletBinding()]
param([Parameter(Mandatory)][string] $ProductionStage, [Parameter(Mandatory)][string] $PreparedDirectory,
    [Parameter(Mandatory)][string] $FixtureDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = (Resolve-Path -LiteralPath $ProductionStage).Path
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
if (-not $stage.StartsWith((Join-Path $repository 'artifacts\production-staging\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $prepared.StartsWith((Join-Path $repository '.codex-temp\pdfium-engine\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $fixtures.StartsWith((Join-Path $repository '.codex-temp\pdf-engine\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use isolated production staging, prepared PDFium and generated qpdf fixtures.'
}
$build = Get-Content -LiteralPath (Join-Path $prepared 'renderer-build.json') -Raw | ConvertFrom-Json
$binary = Join-Path $prepared 'renderer-build\bin\Release'
$source = Join-Path $repository 'proprietary\src\ContextSuite.PdfRenderer.Native'
if ((Get-FileHash -LiteralPath (Join-Path $binary 'ContextSuite.PdfRenderer.exe')).Hash -ne $build.sha256 -or
    (Get-FileHash -LiteralPath (Join-Path $binary 'pdfium.dll')).Hash -ne $build.pdfiumSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $source 'Renderer.cpp')).Hash -ne $build.bridgeSourceSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $source 'CMakeLists.txt')).Hash -ne $build.buildSourceSha256) {
    throw 'Renderer source or binary identity changed; rebuild the isolated host.'
}
$scratch = Join-Path $prepared ('page-worker-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $scratch 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
Get-ChildItem -LiteralPath $stage -File | Where-Object Name -ne 'payload-inventory.json' | Copy-Item -Destination $payload
$renderer = Join-Path $payload 'pdf-renderer'
New-Item -ItemType Directory -Path $renderer | Out-Null
foreach ($name in @('ContextSuite.PdfRenderer.exe', 'pdfium.dll')) {
    Copy-Item -LiteralPath (Join-Path $binary $name) -Destination $renderer
}
Copy-Item -LiteralPath (Join-Path $prepared 'renderer-build.json') -Destination $renderer
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --pdf-page-worker (Join-Path $scratch 'results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
if ($LASTEXITCODE -ne 0) { throw "PDF page workflow failed; evidence retained at $scratch" }
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --pdf-page-failures (Join-Path $scratch 'interruptions') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
if ($LASTEXITCODE -ne 0) { throw "PDF page interruption workflow failed; evidence retained at $scratch" }
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --pdf-page-direct (Join-Path $scratch 'direct-results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
if ($LASTEXITCODE -ne 0) { throw "Direct PDF page workflow failed; evidence retained at $scratch" }
Write-Output "Evaluation-only PDF page worker: $scratch"
