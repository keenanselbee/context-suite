[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory)
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
& dotnet run --project (Join-Path $PSScriptRoot 'Probe\Office.Evaluation.csproj') -c Release -- --engine-lifetime $office $scratch
if ($LASTEXITCODE) { throw 'Office engine lifetime verification failed; retain its evidence.' }
Write-Output 'Actual engine startup/lifetime only, with disposable profiles. No documents, AppContainer or customer-converter acceptance.'
