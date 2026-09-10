[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory, [switch] $PreservationOnly)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\audio-engine')) + '\'
if (-not $prepared.StartsWith($scratchRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a prepared repository-local audio evaluation directory.' }
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evaluation.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $prepared 'upstream.zip')).Hash -ne $pin.archiveSha256) { throw 'Audio evaluation archive hash mismatch.' }
$payload = Join-Path (Join-Path $prepared 'unpacked') $pin.archiveDirectory
foreach ($entry in (Get-Content -LiteralPath (Join-Path $prepared 'inventory.json') -Raw | ConvertFrom-Json)) {
    $path = [IO.Path]::GetFullPath((Join-Path $payload $entry.path))
    if (-not $path.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Inventory path escapes payload.' }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256) { throw "Changed evaluation input: $($entry.path)" }
}
$scratch = Join-Path $prepared ('matrix-' + [guid]::NewGuid().ToString('N'))
$probeArguments = @($payload, $scratch)
if ($PreservationOnly) { $probeArguments += '--flac-preservation' }
& dotnet run --project (Join-Path $PSScriptRoot 'Probe\AudioEngine.Probe.csproj') -c Release -- @probeArguments
if ($LASTEXITCODE -ne 0) { throw "Audio evaluation has failures; retain and inspect evidence at $scratch" }
