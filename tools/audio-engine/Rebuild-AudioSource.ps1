[CmdletBinding()]
param([string] $SourceDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $repository '.codex-temp/audio-source-retained' }
& (Join-Path $PSScriptRoot 'Prepare-AudioSources.ps1') -SourceDirectory $SourceDirectory -VerifyOnly -BuildInputsOnly
$builds = @{}
foreach ($dependency in @('Opus', 'OggVorbis', 'LameStable', 'Make', 'Nasm', 'Zlib')) {
    Write-Output "Building retained audio dependency: $dependency"
    & (Join-Path $PSScriptRoot 'Build-AudioDependency.ps1') -SourceDirectory $SourceDirectory -Dependency $dependency |
        Tee-Object -Variable dependencyOutput
    $prefix = "Verified $dependency dependency build: "
    $completed = @($dependencyOutput | Where-Object { $_ -is [string] -and $_.StartsWith($prefix, [StringComparison]::Ordinal) })
    if ($completed.Count -ne 1) { throw "No unique completed dependency result: $dependency" }
    $builds[$dependency] = $completed[0].Substring($prefix.Length)
}
& python -B (Join-Path $PSScriptRoot 'Build-AudioEngine.py') --sources $SourceDirectory --opus $builds.Opus `
    --ogg-vorbis $builds.OggVorbis --lame $builds.LameStable --make $builds.Make --nasm $builds.Nasm --zlib $builds.Zlib |
    Tee-Object -Variable engineOutput
if ($LASTEXITCODE -ne 0) { throw 'Retained-source FFmpeg build failed.' }
$prefix = 'FFmpeg candidate evidence: '
$completed = @($engineOutput | Where-Object { $_ -is [string] -and $_.StartsWith($prefix, [StringComparison]::Ordinal) })
if ($completed.Count -ne 1) { throw 'No unique FFmpeg build workspace.' }
$candidate = $completed[0].Substring($prefix.Length)
& python -B (Join-Path $PSScriptRoot 'Check-AudioBuild.py') $candidate
if ($LASTEXITCODE -ne 0) { throw 'Rebuilt audio native review failed.' }
Write-Output "Retained-source audio rebuild verified: $candidate"
