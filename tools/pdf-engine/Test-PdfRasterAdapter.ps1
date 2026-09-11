[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory, [Parameter(Mandatory)][string] $FixtureDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
if (-not $prepared.StartsWith((Join-Path $repository '.codex-temp\pdfium-engine\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $fixtures.StartsWith((Join-Path $repository '.codex-temp\pdf-engine\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use prepared repository-local PDFium and generated qpdf fixture directories.'
}
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'pdfium-evaluation.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $prepared 'upstream.tgz')).Hash -ne $pin.archiveSha256) { throw 'PDFium archive identity changed.' }
foreach ($kind in @('probe', 'renderer')) {
    $build = Get-Content -LiteralPath (Join-Path $prepared ($kind + '-build.json')) -Raw | ConvertFrom-Json
    $source = if ($kind -eq 'probe') { Join-Path $PSScriptRoot 'PdfiumProbe' } else { Join-Path $repository 'proprietary\src\ContextSuite.PdfRenderer.Native' }
    $sourceFile = if ($kind -eq 'probe') { 'Probe.cpp' } else { 'Renderer.cpp' }
    $hostName = if ($kind -eq 'probe') { 'ContextSuite.Pdfium.Probe.exe' } else { 'ContextSuite.PdfRenderer.exe' }
    $bin = Join-Path $prepared ($kind + '-build\bin\Release')
    if ((Get-FileHash -LiteralPath (Join-Path $bin $hostName)).Hash -ne $build.sha256 -or
        (Get-FileHash -LiteralPath (Join-Path $bin 'pdfium.dll')).Hash -ne $build.pdfiumSha256 -or
        (Get-FileHash -LiteralPath (Join-Path $source $sourceFile)).Hash -ne $build.bridgeSourceSha256 -or
        (Get-FileHash -LiteralPath (Join-Path $source 'CMakeLists.txt')).Hash -ne $build.buildSourceSha256) {
        throw 'PDFium host, source or runtime identity changed; rebuild the evaluation.'
    }
}
# Generate comparison pixels afresh from the separate evaluation bridge. Do not
# accept an arbitrary golden-output directory or stale fixture hashes.
$reference = Join-Path $prepared ('raster-reference-' + [guid]::NewGuid().ToString('N'))
$probe = Join-Path $prepared 'probe-build\bin\Release\ContextSuite.Pdfium.Probe.exe'
& dotnet run --project (Join-Path $PSScriptRoot 'PdfiumTests\Pdfium.Evaluation.csproj') -c Release -- $probe $fixtures $reference
if ($LASTEXITCODE -ne 0) { throw "PDFium comparison evaluation failed; evidence retained at $reference" }
$evidence = Join-Path $prepared ('raster-adapter-' + [guid]::NewGuid().ToString('N'))
$binary = Join-Path $prepared 'renderer-build\bin\Release'
& dotnet run --project (Join-Path $repository 'proprietary\tests\ContextSuite.Pdf.ContractTests\ContextSuite.Pdf.ContractTests.csproj') -c Release -- --raster $binary $fixtures $reference $evidence
if ($LASTEXITCODE -ne 0) { throw "Private PDF raster checks failed; evidence retained at $evidence" }
