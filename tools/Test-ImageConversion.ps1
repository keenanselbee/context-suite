[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repositoryRoot 'proprietary\tests\ContextSuite.Image.ContractTests\ContextSuite.Image.ContractTests.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw 'Real-image contracts require the compatible private checkout.' }
$scratch = Join-Path $repositoryRoot '.codex-temp\image-tests'
& dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Real-image contract build failed.' }
$executable = Join-Path $repositoryRoot "artifacts\managed\bin\ContextSuite.Image.ContractTests\$Configuration\net10.0\ContextSuite.Image.ContractTests.exe"
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $executable
$startInfo.Arguments = '"' + $scratch + '"'
$startInfo.WorkingDirectory = $repositoryRoot
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$process = [System.Diagnostics.Process]::Start($startInfo)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(120000)) {
        $process.Kill()
        $process.WaitForExit()
        throw 'Real-image contracts exceeded 120 seconds; the owned test process was stopped.'
    }
    Write-Output ($stdout.GetAwaiter().GetResult() -split '\r?\n')
    $diagnostics = $stderr.GetAwaiter().GetResult()
    if ($diagnostics) { Write-Output $diagnostics }
    if ($process.ExitCode -ne 0) { throw 'Real-image contracts failed.' }
}
finally { $process.Dispose() }
