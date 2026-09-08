[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release', [string] $Corpus, [switch] $PaletteComparison, [switch] $Refine, [switch] $Gradients, [switch] $PresetResearch,
    [switch] $NoPreviews, [ValidatePattern('^[a-zA-Z0-9-]+$')][string] $Fixture,
    [ValidateSet('rgb7-baseline','sRGB-512-fs10','sRGB-512-fs25','sRGB-1024-fs10')][string] $Candidate)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (($NoPreviews -or $Fixture -or $Candidate) -and -not $PresetResearch) { throw 'Preview/filter options require -PresetResearch.' }
if ($PresetResearch -and ($PaletteComparison -or $Refine -or $Gradients)) { throw '-PresetResearch cannot be combined with palette-comparison modes.' }
if ($PaletteComparison -and -not $Corpus) { throw 'Palette comparison requires copied source.png and reference.png in -Corpus.' }
if ($Refine -and -not $PaletteComparison) { throw '-Refine requires -PaletteComparison.' }
if ($Gradients -and (-not $PaletteComparison -or $Refine)) { throw '-Gradients requires -PaletteComparison and cannot be combined with -Refine.' }
if ($Corpus) {
    $Corpus = (Resolve-Path -LiteralPath $Corpus).Path
    $allowed = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.codex-temp\png-quantization')) + '\'
    if (-not $Corpus.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or $Corpus.Contains('"')) {
        throw 'Use an already-copied corpus beneath .codex-temp/png-quantization, never an original media folder.'
    }
}
$project = Join-Path $repositoryRoot 'proprietary\tests\ContextSuite.Image.ContractTests\ContextSuite.Image.ContractTests.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw 'PNG evaluation requires the compatible private checkout.' }
& dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'PNG evaluation build failed.' }
$buildOutput = Join-Path $repositoryRoot "artifacts\managed\bin\ContextSuite.Image.ContractTests\$Configuration\net10.0"
# Match production's root-file composition: never include NuGet runtime subfolders.
# Retain this isolated host with the report so the run does not depend on a mutable build directory.
$payload = Join-Path $repositoryRoot ('.codex-temp\png-quantization\host-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $payload | Out-Null
Get-ChildItem -LiteralPath $buildOutput -File | Copy-Item -Destination $payload
& "$PSScriptRoot\curated-engine\Test-ProductionEngine.ps1" -Payload $payload
& "$PSScriptRoot\png-engine\Test-PngEngine.ps1" -Payload $payload
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $payload 'ContextSuite.Image.ContractTests.exe'
$start.Arguments = '"' + (Join-Path $repositoryRoot '.codex-temp\png-quantization') + '" --quantization-evaluation'
if ($PaletteComparison) { $start.Arguments = '"' + (Join-Path $repositoryRoot '.codex-temp\png-quantization') + '" --palette-comparison' }
if ($Refine) { $start.Arguments = '"' + (Join-Path $repositoryRoot '.codex-temp\png-quantization') + '" --palette-refinement' }
if ($Gradients) { $start.Arguments = '"' + (Join-Path $repositoryRoot '.codex-temp\png-quantization') + '" --palette-gradients' }
if ($PresetResearch) { $start.Arguments = '"' + (Join-Path $repositoryRoot '.codex-temp\png-quantization') + '" --preset-research' }
if ($Corpus) { $start.Arguments += ' "' + $Corpus + '"' }
if ($PresetResearch) {
    if (-not $Corpus) { $start.Arguments += ' "-"' }
    $start.Arguments += ' ' + (-not $NoPreviews).ToString().ToLowerInvariant()
    $start.Arguments += ' "' + $(if ($Fixture) { $Fixture } else { '-' }) + '"'
    $start.Arguments += ' "' + $(if ($Candidate) { $Candidate } else { '-' }) + '"'
}
$start.WorkingDirectory = $repositoryRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [System.Diagnostics.Process]::Start($start)
try {
    $stderr = $process.StandardError.ReadToEndAsync()
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $line = $process.StandardOutput.ReadLineAsync()
    while ($true) {
        if ($line.Wait(200)) {
            $value = $line.GetAwaiter().GetResult()
            if ($null -eq $value) { break }
            Write-Output $value
            $line = $process.StandardOutput.ReadLineAsync()
        }
        if ($deadline.ElapsedMilliseconds -ge 300000) { break }
    }
    if (-not $process.WaitForExit([Math]::Max(0, 300000 - [int]$deadline.ElapsedMilliseconds))) {
        # The owned oxipng child is also bound to a kill-on-close Windows job.
        $process.Kill()
        $process.WaitForExit()
        throw 'PNG evaluation exceeded five minutes; the owned evaluation process was stopped.'
    }
    $diagnostics = $stderr.GetAwaiter().GetResult()
    if ($diagnostics) { Write-Output $diagnostics }
    if ($process.ExitCode -ne 0) { throw 'PNG evaluation failed. Retained scratch output is diagnostic, not acceptance evidence.' }
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
