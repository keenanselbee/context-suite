[CmdletBinding()]
param(
    [ValidatePattern('^[a-zA-Z0-9-]+$')][string] $RunName = 'prototype-2',
    [ValidatePattern('^[a-zA-Z0-9-]+$')][string] $TestName = 'verified-output',
    [switch] $Desktop
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workspace = Join-Path $repository ".codex-temp\curated-engine\$RunName"
$payload = Join-Path $workspace $TestName
$lockDirectory = Join-Path $repository '.codex-temp\desktop-smoke'
New-Item -ItemType Directory -Path $lockDirectory -Force | Out-Null
try { $lock = [IO.File]::Open((Join-Path $lockDirectory 'run.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
catch { throw 'Another desktop smoke test holds the shared test lock; wait for it to finish.' }
try {
if (Get-Process -Name 'ContextSuite.Application', 'ContextSuite.Worker', 'ContextSuite.Application.TestHost' -ErrorAction SilentlyContinue) {
    throw 'Close Context Suite before isolated integration tests; no existing process will be stopped.'
}
foreach ($directory in 'engine', 'production') {
    & (Join-Path $PSScriptRoot 'Test-CuratedPayload.ps1') -Workspace $workspace -Payload (Join-Path $payload $directory)
}
$evidence = Join-Path $workspace ('verification-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$jobs = @(
    @{ name = 'engine'; executable = 'engine\ContextSuite.Image.ContractTests.exe'; arguments = @((Join-Path $evidence 'engine')) },
    @{ name = 'foundation'; executable = 'foundation\ContextSuite.Core.ContractTests.exe'; arguments = @((Join-Path $evidence 'foundation'), (Join-Path $payload 'production\ContextSuite.Worker.exe'), (Join-Path $payload 'production\ContextSuite.Application.exe')) }
)
if ($Desktop) {
    $jobs += @{ name = 'desktop'; executable = 'desktop\ContextSuite.Desktop.SmokeTests.exe'; arguments = @((Join-Path $payload 'testhost\ContextSuite.Application.TestHost.exe'), (Join-Path $evidence 'desktop'), 'images', (Join-Path $payload 'production\ContextSuite.Worker.exe')) }
}
foreach ($job in $jobs) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $payload $job.executable
    $info.Arguments = ($job.arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.WorkingDirectory = $repository
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $finished = $process.WaitForExit(180000)
        if (-not $finished) { $process.Kill(); $process.WaitForExit() }
        [IO.File]::WriteAllText((Join-Path $evidence ($job.name + '.log')), $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult())
        if (-not $finished -or $process.ExitCode -ne 0) { throw "Curated $($job.name) verification failed/timed out; inspect $evidence. Check for owned child processes before retrying." }
        Write-Output "PASS: curated $($job.name); log retained in $evidence"
    } finally { $process.Dispose() }
}
[IO.File]::WriteAllText((Join-Path $evidence 'result.txt'), "Passed isolated suites: $($jobs.name -join ', '). Native SHA256: $((Get-FileHash (Join-Path $payload 'production\Magick.Native-Q16-x64.dll')).Hash)")
Write-Output "Curated verification complete: $evidence"
} finally { $lock.Dispose() }
