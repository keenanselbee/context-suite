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
$build = Get-Content -LiteralPath (Join-Path $prepared 'probe-build.json') -Raw | ConvertFrom-Json
$executable = Join-Path $prepared 'probe-build\bin\Release\ContextSuite.Pdfium.Probe.exe'
if ((Get-FileHash -LiteralPath $executable).Hash -ne $build.sha256 -or
    (Get-FileHash -LiteralPath (Join-Path (Split-Path $executable -Parent) 'pdfium.dll')).Hash -ne $build.pdfiumSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'PdfiumProbe\Probe.cpp')).Hash -ne $build.bridgeSourceSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'PdfiumProbe\CMakeLists.txt')).Hash -ne $build.buildSourceSha256) {
    throw 'PDFium probe, source or runtime identity changed; rebuild the evaluation.'
}
$scratch = Join-Path $prepared ('matrix-' + [guid]::NewGuid().ToString('N'))
& dotnet run --project (Join-Path $PSScriptRoot 'PdfiumTests\Pdfium.Evaluation.csproj') -c Release -- $executable $fixtures $scratch
if ($LASTEXITCODE) { throw "PDFium evaluation failed; evidence retained at $scratch" }
