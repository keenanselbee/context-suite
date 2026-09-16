[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputPath)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$root = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\audio-review')) + '\'
$output = [IO.Path]::GetFullPath($OutputPath)
if (-not $output.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $output) -or -not (Test-Path -LiteralPath (Split-Path $output -Parent))) {
    throw 'Use a new speech file in the owned audio-review directory.'
}
Add-Type -AssemblyName System.Speech
$speech = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $voice = @($speech.GetInstalledVoices() | Where-Object {
        $_.Enabled -and $_.VoiceInfo.Name -in @('Microsoft David Desktop', 'Microsoft Zira Desktop')
    } | Sort-Object { $_.VoiceInfo.Name } | Select-Object -First 1)
    if ($voice.Count -ne 1) { throw 'A local Microsoft David or Zira desktop voice is required; no voice is downloaded.' }
    $speech.SelectVoice($voice[0].VoiceInfo.Name)
    $format = [System.Speech.AudioFormat.SpeechAudioFormatInfo]::new(
        48000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
        [System.Speech.AudioFormat.AudioChannel]::Mono)
    $speech.SetOutputToWaveFile($output, $format)
    $text = 'This is an audio conversion review. Listen to the start of each word, the S sounds, and the quiet pauses. Peter packed a paper parcel. Seven silver ships sailed slowly across the sea. Compare the converted recording with the original. Listen for clicks, missing words, or changes in the voice. Context Suite should keep the original recording unchanged.'
    $speech.Speak($text)
    $speech.SetOutputToNull()
    [pscustomobject]@{ Voice = $voice[0].VoiceInfo.Name; Text = $text; SampleRate = 48000; Channels = 1; Bits = 16 } |
        ConvertTo-Json | Set-Content -LiteralPath ($output + '.json') -Encoding UTF8
}
finally { $speech.Dispose() }
