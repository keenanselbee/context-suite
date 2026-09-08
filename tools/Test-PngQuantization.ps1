[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release', [string] $Corpus)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
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
if ($Corpus) { $start.Arguments += ' "' + $Corpus + '"' }
$start.WorkingDirectory = $repositoryRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [System.Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(300000)) {
        # The owned oxipng child is also bound to a kill-on-close Windows job.
        $process.Kill()
        $process.WaitForExit()
        throw 'PNG evaluation exceeded five minutes; the owned evaluation process was stopped.'
    }
    Write-Output ($stdout.GetAwaiter().GetResult() -split '\r?\n')
    $diagnostics = $stderr.GetAwaiter().GetResult()
    if ($diagnostics) { Write-Output $diagnostics }
    if ($process.ExitCode -ne 0) { throw 'PNG evaluation failed. Retained scratch output is diagnostic, not acceptance evidence.' }
}
finally { $process.Dispose() }
