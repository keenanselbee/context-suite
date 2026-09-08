[CmdletBinding()]
param([Parameter(Mandatory)][string] $Corpus)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$corpusRoot = (Resolve-Path -LiteralPath $Corpus).Path
$scratch = Join-Path $repository '.codex-temp\png-quantization'
if (-not $corpusRoot.StartsWith($scratch + '\', [StringComparison]::OrdinalIgnoreCase) -or $corpusRoot.Contains('"')) {
    throw 'Use already-copied PNGs beneath .codex-temp/png-quantization, never original media.'
}
& dotnet build (Join-Path $repository 'proprietary\tests\ContextSuite.Image.ContractTests\ContextSuite.Image.ContractTests.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Corpus host build failed.' }
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = Join-Path $repository 'artifacts\managed\bin\ContextSuite.Image.ContractTests\Release\net10.0\ContextSuite.Image.ContractTests.exe'
$start.Arguments = '"' + $scratch + '" --optimization-corpus "' + $corpusRoot + '"'
$start.WorkingDirectory = $repository
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(600000)) { $process.Kill(); $process.WaitForExit(); throw 'Corpus exceeded ten minutes.' }
    Write-Output $stdout.GetAwaiter().GetResult()
    Write-Output $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw 'Corpus validation failed; retained output is diagnostic evidence.' }
} finally { $process.Dispose() }
