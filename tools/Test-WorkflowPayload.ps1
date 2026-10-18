[CmdletBinding()]
param([Parameter(Mandatory)][string] $WorkerPath, [switch] $AllowScratchWorker)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$worker = (Resolve-Path -LiteralPath $WorkerPath).Path
$staging = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts\production-staging')) + '\'
$scratch = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp')) + '\'
if ([IO.Path]::GetFileName($worker) -cne 'ContextSuite.Worker.exe' -or
    (-not $worker.StartsWith($staging, [StringComparison]::OrdinalIgnoreCase) -and
     -not ($AllowScratchWorker -and $worker.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase)))) {
    throw 'Use isolated production staging, or explicitly allow a repository scratch worker.'
}
$entry = Get-Item -LiteralPath $worker -Force
while ($null -ne $entry) {
    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked workflow payload paths are refused.' }
    $entry = if ($entry -is [IO.FileInfo]) { $entry.Directory } else { $entry.Parent }
}
$payload = Split-Path $worker -Parent
if (-not (Test-Path -LiteralPath (Join-Path $payload 'payload-inventory.json') -PathType Leaf)) {
    throw 'The workflow requires a complete inventoried payload.'
}
$options = @{ Payload = $payload }
if (Test-Path -LiteralPath (Join-Path $payload 'audio-engine') -PathType Container) { $options.AllowAudioCandidate = $true }
if (Test-Path -LiteralPath (Join-Path $payload 'pdf-engine') -PathType Container) { $options.AllowPdfCandidate = $true }
if (Test-Path -LiteralPath (Join-Path $payload 'pdf-dark') -PathType Container) { $options.AllowPdfDarkCandidate = $true }
if (Test-Path -LiteralPath (Join-Path $payload 'office-engine') -PathType Container) { $options.AllowOfficeCandidate = $true }
if (Test-Path -LiteralPath (Join-Path $payload 'heif') -PathType Container) { $options.AllowHeifCandidate = $true }
if (Test-Path -LiteralPath (Join-Path $payload 'wem-engine') -PathType Container) { $options.AllowWemCandidate = $true }
if ((Test-Path -LiteralPath (Join-Path $payload 'avif') -PathType Container) -or
    (Test-Path -LiteralPath (Join-Path $payload 'svg') -PathType Container)) { $options.AllowImageExpansionCandidate = $true }
& (Join-Path $PSScriptRoot 'curated-engine\Test-ProductionPayload.ps1') @options
& python -B (Join-Path $PSScriptRoot 'audio-engine\Stage-AudioPayload.py') --payload $payload --inventory
if ($LASTEXITCODE -ne 0) { throw 'Workflow payload inventory verification failed.' }
Write-Output "Verified isolated workflow worker: $worker"
