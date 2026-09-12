[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory, [Parameter(Mandatory)][string] $PdfPreparedDirectory,
    [Parameter(Mandatory)][string] $PdfiumPreparedDirectory, [switch] $ProfileMatrix, [switch] $ProfileLengths, [switch] $EnvironmentPaths, [switch] $LegacyAnalysis, [switch] $LegacyPdf, [switch] $ExcelCalculation, [switch] $FontSubstitution, [switch] $ExcelDates, [switch] $ExcelPrint)
$ErrorActionPreference = 'Stop'
if (@($ProfileMatrix, $ProfileLengths, $EnvironmentPaths, $LegacyAnalysis, $LegacyPdf, $ExcelCalculation, $FontSubstitution, $ExcelDates, $ExcelPrint).Where({ $_ }).Count -gt 1) { throw 'Choose one evaluation mode at a time.' }
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$office = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$pdf = (Resolve-Path -LiteralPath $PdfPreparedDirectory).Path
$pdfium = (Resolve-Path -LiteralPath $PdfiumPreparedDirectory).Path
if (-not $office.StartsWith((Join-Path $repository '.codex-temp\office-engine\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $pdf.StartsWith((Join-Path $repository '.codex-temp\pdf-engine\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $pdfium.StartsWith((Join-Path $repository '.codex-temp\pdfium-engine\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use independently prepared, repository-local evaluation payloads.'
}
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evaluation.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $office 'upstream.msi')).Hash -ne $pin.archiveSha256) { throw 'Office MSI changed.' }
$inventory = Get-Content -LiteralPath (Join-Path $office 'inventory.json') -Raw | ConvertFrom-Json
foreach ($entry in $inventory.Files) {
    $file = Join-Path $office ('unpacked\' + $entry.Path)
    if ((Get-FileHash -LiteralPath $file).Hash -ne $entry.Sha256) { throw "Office payload changed: $($entry.Path)" }
}
foreach ($entry in (Get-Content -LiteralPath (Join-Path $pdf 'inventory.json') -Raw | ConvertFrom-Json)) {
    $file = Join-Path $pdf ('unpacked\' + $entry.path)
    if ((Get-FileHash -LiteralPath $file).Hash -ne $entry.sha256) { throw 'qpdf evaluation inventory changed.' }
}
$build = Get-Content -LiteralPath (Join-Path $pdfium 'probe-build.json') -Raw | ConvertFrom-Json
$probe = Join-Path $pdfium 'probe-build\bin\Release\ContextSuite.Pdfium.Probe.exe'
if ((Get-FileHash -LiteralPath $probe).Hash -ne $build.sha256 -or
    (Get-FileHash -LiteralPath (Join-Path (Split-Path $probe -Parent) 'pdfium.dll')).Hash -ne $build.pdfiumSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $repository 'tools\pdf-engine\PdfiumProbe\Probe.cpp')).Hash -ne $build.bridgeSourceSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $repository 'tools\pdf-engine\PdfiumProbe\CMakeLists.txt')).Hash -ne $build.buildSourceSha256) {
    throw 'PDFium probe identity changed.'
}
$qpdf = Join-Path $pdf 'unpacked\qpdf-12.4.1-msvc64\bin\qpdf.exe'
$probeArguments = @($office, $qpdf, $probe)
if ($ProfileMatrix) { $probeArguments += 'ProfileMatrix' }
if ($ProfileLengths) { $probeArguments += 'ProfileLengths' }
if ($EnvironmentPaths) { $probeArguments += 'EnvironmentPaths' }
if ($LegacyAnalysis) { $probeArguments += 'LegacyAnalysis' }
if ($LegacyPdf) { $probeArguments += 'LegacyPdf' }
if ($ExcelCalculation) { $probeArguments += 'ExcelCalculation' }
if ($FontSubstitution) { $probeArguments += 'FontSubstitution' }
if ($ExcelDates) { $probeArguments += 'ExcelDates' }
if ($ExcelPrint) { $probeArguments += 'ExcelPrint' }
& dotnet run --project (Join-Path $PSScriptRoot 'Probe\Office.Evaluation.csproj') -c Release -- @probeArguments
if ($LASTEXITCODE) { throw 'Office evaluation failed; inspect retained scratch evidence.' }
Write-Output 'Authored passive Office fixtures and disposable exports only. No arbitrary-document isolation, installer or production acceptance implied.'
if ($ProfileMatrix -or $ProfileLengths -or $EnvironmentPaths) { Write-Output 'Profile-matrix completion records observations, including any failed conversions; inspect each result.' }

if ($ExcelDates) { Write-Output 'Date-system completion records display differences; inspect DateObservations before claiming fidelity.' }

if ($ExcelPrint) { Write-Output 'Print-layout completion records observations; inspect PrintObservation before claiming fidelity.' }
