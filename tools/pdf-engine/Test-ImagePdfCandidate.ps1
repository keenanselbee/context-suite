[CmdletBinding()]
param([Parameter(Mandatory)][string] $PdfPreparedDirectory, [Parameter(Mandatory)][string] $PdfiumPreparedDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pdf = (Resolve-Path -LiteralPath $PdfPreparedDirectory).Path
$pdfium = (Resolve-Path -LiteralPath $PdfiumPreparedDirectory).Path
if (-not $pdf.StartsWith((Join-Path $repository '.codex-temp\pdf-engine\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $pdfium.StartsWith((Join-Path $repository '.codex-temp\pdfium-engine\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use independently prepared repository-local qpdf and PDFium evaluations.'
}
foreach ($entry in (Get-Content -LiteralPath (Join-Path $pdf 'inventory.json') -Raw | ConvertFrom-Json)) {
    $file = Join-Path $pdf ('unpacked\' + $entry.path)
    if ((Get-FileHash -LiteralPath $file).Hash -ne $entry.sha256) { throw 'qpdf evaluation inventory changed.' }
}
$build = Get-Content -LiteralPath (Join-Path $pdfium 'probe-build.json') -Raw | ConvertFrom-Json
$probe = Join-Path $pdfium 'probe-build\bin\Release\ContextSuite.Pdfium.Probe.exe'
if ((Get-FileHash -LiteralPath $probe).Hash -ne $build.sha256 -or
    (Get-FileHash -LiteralPath (Join-Path (Split-Path $probe -Parent) 'pdfium.dll')).Hash -ne $build.pdfiumSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'PdfiumProbe\Probe.cpp')).Hash -ne $build.bridgeSourceSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'PdfiumProbe\CMakeLists.txt')).Hash -ne $build.buildSourceSha256) {
    throw 'PDFium evaluation identity changed; rebuild the independently authored probe.'
}
$evidence = Join-Path $pdfium ('image-pdf-' + [guid]::NewGuid().ToString('N'))
$qpdf = Join-Path $pdf 'unpacked\qpdf-12.4.1-msvc64\bin\qpdf.exe'
& dotnet run --project (Join-Path $repository 'proprietary\tests\ContextSuite.Pdf.ContractTests\ContextSuite.Pdf.ContractTests.csproj') -c Release -- --image-pdf $qpdf $probe $evidence
if ($LASTEXITCODE -ne 0) { throw "Image-PDF candidate failed; evidence retained at $evidence" }
