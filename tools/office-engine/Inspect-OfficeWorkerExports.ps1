[CmdletBinding()]
param([Parameter(Mandatory)][string] $WorkerResults, [string] $ControlDirectory,
    [Parameter(Mandatory)][string] $PdfPreparedDirectory, [Parameter(Mandatory)][string] $PdfiumPreparedDirectory,
    [switch] $ApplicationOutputs, [switch] $WordRevisions, [switch] $WorkbookCopy)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$report = (Resolve-Path -LiteralPath $WorkerResults).Path
if ($WorkbookCopy -and ($ApplicationOutputs -or $WordRevisions)) { throw 'Workbook follow-up inspection uses worker results only.' }
if ($WordRevisions -and ($ApplicationOutputs -or $ControlDirectory)) { throw 'Word revision inspection uses its own published clean controls.' }
if (-not $WordRevisions -and -not $ControlDirectory) { throw 'A retained control directory is required.' }
$control = if ($WordRevisions) { $null } else { (Resolve-Path -LiteralPath $ControlDirectory).Path }
$pdf = (Resolve-Path -LiteralPath $PdfPreparedDirectory).Path
$pdfium = (Resolve-Path -LiteralPath $PdfiumPreparedDirectory).Path
$evidenceDirectory = if ($ApplicationOutputs -or $WordRevisions) { '.codex-temp\office-execution\' } else { '.codex-temp\office-worker\' }
if (-not $report.StartsWith((Join-Path $repository $evidenceDirectory), [StringComparison]::OrdinalIgnoreCase) -or
    (-not $WordRevisions -and -not $control.StartsWith((Join-Path $repository '.codex-temp\office-isolation\'), [StringComparison]::OrdinalIgnoreCase)) -or
    -not $pdf.StartsWith((Join-Path $repository '.codex-temp\pdf-engine\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $pdfium.StartsWith((Join-Path $repository '.codex-temp\pdfium-engine\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use retained worker/control evidence and independently prepared PDF evaluation runtimes.'
}
foreach ($path in @($report, $control, $pdf, $pdfium).Where({ $_ })) {
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
$inspectionCommand = if ($WorkbookCopy) { '--inspect-workbook-following' } elseif ($WordRevisions) { '--inspect-word-publications' } elseif ($ApplicationOutputs) { '--inspect-office-publications' } else { '--inspect-worker-exports' }
$inspectionArguments = @($inspectionCommand, $report, $qpdf, $probe)
if ($control) { $inspectionArguments += $control }
& dotnet run --project (Join-Path $PSScriptRoot 'Probe\Office.Evaluation.csproj') -c Release -- @inspectionArguments
if ($LASTEXITCODE) { throw 'Worker PDF inspection failed; retain the evidence.' }
