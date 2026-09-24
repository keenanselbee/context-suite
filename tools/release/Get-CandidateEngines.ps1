[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload)

$ErrorActionPreference = 'Stop'
$tools = Split-Path $PSScriptRoot -Parent
$root = (Get-Item -LiteralPath $Payload).FullName
$selection = [ordered]@{
    audio = 'none'; pdf = $false; office = $false; heif = $false; voice = $false
}
if (Test-Path -LiteralPath (Join-Path $root 'audio-engine')) {
    $probeHash = (Get-FileHash -LiteralPath (Join-Path $root 'audio-engine/ffprobe.exe')).Hash
    $matches = @()
    foreach ($profile in 'curated', 'expanded') {
        $name = if ($profile -eq 'expanded') { 'expanded-payload-candidate.json' } else { 'payload-candidate.json' }
        $record = Get-Content -LiteralPath (Join-Path $tools ('audio-engine/' + $name)) -Raw | ConvertFrom-Json
        $probe = @($record.files | Where-Object path -eq 'ffprobe.exe')
        if ($probe.Count -eq 1 -and $probe[0].sha256 -eq $probeHash) { $matches += $profile }
    }
    if ($matches.Count -ne 1) { throw 'Candidate audio profile is not a reviewed selection.' }
    $selection.audio = $matches[0]
}
$pdfDirectories = @('pdf-engine', 'pdf-renderer', 'pdf-validator' | Where-Object {
    Test-Path -LiteralPath (Join-Path $root $_)
})
if ($pdfDirectories.Count -notin 0, 3) { throw 'Candidate PDF engine selection is incomplete.' }
$selection.pdf = $pdfDirectories.Count -eq 3
$selection.office = Test-Path -LiteralPath (Join-Path $root 'office-engine')
$selection.heif = Test-Path -LiteralPath (Join-Path $root 'heif')
$selection.voice = Test-Path -LiteralPath (Join-Path $root 'audio-engine/voice')
if ($selection.office -and -not $selection.pdf) { throw 'Candidate Office requires the complete PDF engine selection.' }
if ($selection.voice -and $selection.audio -ne 'expanded') { throw 'Candidate Windows Voice requires expanded audio.' }
# This records the selected families. Their owning payload verifiers still check
# every binary, source binding, notice and exact engine membership.
[pscustomobject]$selection
