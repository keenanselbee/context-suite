[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$prepared = [IO.Path]::GetFullPath($PreparedDirectory).TrimEnd('\')
$allowed = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\pdf-engine')).TrimEnd('\') + '\'
if (-not $prepared.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use repository-local PDF evaluation staging.' }
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evaluation.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $prepared 'upstream.zip')).Hash -ne $pin.archiveSha256) { throw 'Evaluation archive identity changed.' }
$payload = Join-Path $prepared 'unpacked'
$inventory = Get-Content -LiteralPath (Join-Path $prepared 'inventory.json') -Raw | ConvertFrom-Json
foreach ($item in $inventory) {
    $path = [IO.Path]::GetFullPath((Join-Path $payload $item.path))
    if (-not $path.StartsWith($payload.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid inventory path.' }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $item.sha256) { throw "Evaluation file identity changed: $($item.path)" }
}
$executables = @(Get-ChildItem -LiteralPath $payload -Filter 'qpdf.exe' -File -Recurse)
if ($executables.Count -ne 1) { throw 'Expected one qpdf executable.' }
$scratch = Join-Path $prepared ('matrix-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
& dotnet run --project (Join-Path $PSScriptRoot 'Probe\PdfEngine.Probe.csproj') -c Release -- $executables[0].FullName $scratch
if ($LASTEXITCODE -ne 0) { throw "PDF evaluation failed; evidence retained at $scratch" }
Write-Output "PDF evaluation evidence: $scratch"
