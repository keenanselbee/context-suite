[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory, [switch] $DuringExport, [string] $PdfPreparedDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$office = (Resolve-Path -LiteralPath $PreparedDirectory).Path
if (-not $office.StartsWith((Join-Path $repository '.codex-temp\office-engine\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an independently prepared repository-local Office payload.'
}
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evaluation.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $office 'upstream.msi')).Hash -ne $pin.archiveSha256) { throw 'Office MSI changed.' }
$inventory = Get-Content -LiteralPath (Join-Path $office 'inventory.json') -Raw | ConvertFrom-Json
foreach ($entry in $inventory.Files) {
    if ((Get-FileHash -LiteralPath (Join-Path $office ('unpacked\' + $entry.Path))).Hash -ne $entry.Sha256) {
        throw "Office payload changed: $($entry.Path)"
    }
}
$scratch = Join-Path $office ('lifetime-' + [guid]::NewGuid().ToString('N'))
if ($DuringExport) {
    if (-not $PdfPreparedDirectory) { throw 'Export lifetime checks require a pinned qpdf evaluation directory.' }
    $pdf = (Resolve-Path -LiteralPath $PdfPreparedDirectory).Path
    if (-not $pdf.StartsWith((Join-Path $repository '.codex-temp\pdf-engine\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Use repository-local qpdf evaluation.' }
    foreach ($entry in (Get-Content -LiteralPath (Join-Path $pdf 'inventory.json') -Raw | ConvertFrom-Json)) {
        if ((Get-FileHash -LiteralPath (Join-Path $pdf ('unpacked\' + $entry.path))).Hash -ne $entry.sha256) { throw 'qpdf inventory changed.' }
    }
    $qpdf = Join-Path $pdf 'unpacked\qpdf-12.4.1-msvc64\bin\qpdf.exe'
    & dotnet run --project (Join-Path $PSScriptRoot 'Probe\Office.Evaluation.csproj') -c Release -- --engine-export-lifetime $office $qpdf $scratch
    if ($LASTEXITCODE) { throw 'Office export lifetime verification failed; retain its evidence.' }
    Write-Output 'Generated passive Word export only. No AppContainer or customer-converter acceptance.'
    return
}
& dotnet run --project (Join-Path $PSScriptRoot 'Probe\Office.Evaluation.csproj') -c Release -- --engine-lifetime $office $scratch
if ($LASTEXITCODE) { throw 'Office engine lifetime verification failed; retain its evidence.' }
Write-Output 'Actual engine startup/lifetime only, with disposable profiles. No documents, AppContainer or customer-converter acceptance.'
