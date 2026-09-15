[CmdletBinding()]
param([Parameter(Mandatory)][guid] $StagingId, [Parameter(Mandatory)][string] $PdfPreparedDirectory,
    [Parameter(Mandatory)][string] $PdfiumPreparedDirectory, [ValidatePattern('^(case|cs[0-9]{1,8})$')][string] $CaseName = 'case',
    [ValidatePattern('^cs[0-9]{1,8}$')][string] $ControlCaseName)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = Join-Path $repository ('.codex-temp\office-isolation\' + $StagingId.ToString('N'))
$pdf = (Resolve-Path -LiteralPath $PdfPreparedDirectory).Path
$pdfium = (Resolve-Path -LiteralPath $PdfiumPreparedDirectory).Path
if (-not $pdf.StartsWith((Join-Path $repository '.codex-temp\pdf-engine\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $pdfium.StartsWith((Join-Path $repository '.codex-temp\pdfium-engine\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use the independently prepared PDF evaluation runtimes.'
}
foreach ($path in @($stage, $pdf, $pdfium)) {
    $item = Get-Item -LiteralPath $path
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked evaluation paths are not permitted.' }
        $item = if ($item -is [IO.DirectoryInfo]) { $item.Parent } else { $item.Directory }
    }
}
foreach ($entry in (Get-Content -LiteralPath (Join-Path $pdf 'inventory.json') -Raw | ConvertFrom-Json)) {
    if ((Get-FileHash -LiteralPath (Join-Path $pdf ('unpacked\' + $entry.path))).Hash -ne $entry.sha256) {
        throw 'qpdf evaluation payload changed.'
    }
}
$build = Get-Content -LiteralPath (Join-Path $pdfium 'probe-build.json') -Raw | ConvertFrom-Json
$probe = Join-Path $pdfium 'probe-build\bin\Release\ContextSuite.Pdfium.Probe.exe'
if ((Get-FileHash -LiteralPath $probe).Hash -ne $build.sha256 -or
    (Get-FileHash -LiteralPath (Join-Path (Split-Path $probe -Parent) 'pdfium.dll')).Hash -ne $build.pdfiumSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $repository 'tools\pdf-engine\PdfiumProbe\Probe.cpp')).Hash -ne $build.bridgeSourceSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $repository 'tools\pdf-engine\PdfiumProbe\CMakeLists.txt')).Hash -ne $build.buildSourceSha256) {
    throw 'PDFium evaluation probe changed.'
}
$qpdf = Join-Path $pdf 'unpacked\qpdf-12.4.1-msvc64\bin\qpdf.exe'
$inspection = @('--inspect-isolation-exports', $stage, $qpdf, $probe, $CaseName)
if ($ControlCaseName) { $inspection += $ControlCaseName }
& dotnet run --project (Join-Path $PSScriptRoot 'Probe\Office.Evaluation.csproj') -c Release -- @inspection
if ($LASTEXITCODE) { throw 'Isolated export inspection failed; retain all per-case results.' }
