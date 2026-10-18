[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload)

$ErrorActionPreference = 'Stop'
$tools = Split-Path $PSScriptRoot -Parent
$root = (Get-Item -LiteralPath $Payload).FullName
$selection = [ordered]@{
    audio = 'none'; pdf = $false; office = $false; heif = $false; voice = $false; wem = $false;
    imageExpansion = $false; pdfDark = $false
}
if (Test-Path -LiteralPath (Join-Path $root 'audio-engine')) {
    $probeHash = (Get-FileHash -LiteralPath (Join-Path $root 'audio-engine/ffprobe.exe')).Hash
    $matches = @()
    foreach ($profile in 'curated', 'expanded', 'video') {
        $name = if ($profile -eq 'curated') { 'payload-candidate.json' } else { "$profile-payload-candidate.json" }
        $recordPath = Join-Path $tools ('audio-engine/' + $name)
        if ($profile -eq 'video' -and -not (Test-Path -LiteralPath $recordPath)) { continue }
        $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
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
$selection.pdfDark = Test-Path -LiteralPath (Join-Path $root 'pdf-dark')
$selection.office = Test-Path -LiteralPath (Join-Path $root 'office-engine')
$selection.heif = Test-Path -LiteralPath (Join-Path $root 'heif')
$selection.voice = Test-Path -LiteralPath (Join-Path $root 'audio-engine/voice')
$selection.wem = Test-Path -LiteralPath (Join-Path $root 'wem-engine')
$avif = Test-Path -LiteralPath (Join-Path $root 'avif')
$svg = Test-Path -LiteralPath (Join-Path $root 'svg')
if ($avif -ne $svg) { throw 'Candidate AVIF/SVG engine selection is incomplete.' }
$selection.imageExpansion = $avif
if ($selection.office -and -not $selection.pdf) { throw 'Candidate Office requires the complete PDF engine selection.' }
if ($selection.pdfDark -and -not $selection.pdf) { throw 'Candidate PDF dark mode requires the complete PDF engine selection.' }
if ($selection.voice -and $selection.audio -notin 'expanded', 'video') { throw 'Candidate Windows Voice requires expanded or video audio.' }
if ($selection.audio -eq 'video' -and -not $selection.voice) { throw 'Candidate video audio requires Windows Voice to retain existing support.' }
if ($selection.imageExpansion -and -not $selection.heif) { throw 'Candidate AVIF output requires the HEIF decoder.' }
# This records the selected families. Their owning payload verifiers still check
# every binary, source binding, notice and exact engine membership.
[pscustomobject]$selection
