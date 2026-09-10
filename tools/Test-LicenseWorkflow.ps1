[CmdletBinding()]
param([switch] $Launch, [switch] $DirectCommands, [switch] $Settings, [string] $WorkerPath)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repository 'tests\ContextSuite.Application.TestHost\ContextSuite.Application.TestHost.csproj'
if (Get-Process -Name ContextSuite.Application, ContextSuite.Application.TestHost, ContextSuite.Worker -ErrorAction SilentlyContinue) {
    throw 'Close Context Suite and other application tests first. The full-app harness shares the per-user router.'
}
if ($Launch -or $DirectCommands) {
    if (-not $WorkerPath) { throw 'This workflow requires an explicit verified staged WorkerPath.' }
    $worker = (Resolve-Path -LiteralPath $WorkerPath).Path
    $staging = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts\production-staging')) + '\'
    if (-not $worker.StartsWith($staging, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($worker) -cne 'ContextSuite.Worker.exe') { throw 'Use a worker from isolated production staging.' }
    & (Join-Path $repository 'tools\curated-engine\Test-ProductionPayload.ps1') -Payload (Split-Path $worker -Parent)
}
& dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'License workflow test host build failed.' }
& dotnet run --project $project -c Release --no-build -- --license-workflow-contracts
if ($LASTEXITCODE -ne 0) { throw 'License workflow harness checks failed.' }
if ($DirectCommands) {
    & dotnet run --project $project -c Release --no-build -- --direct-command-contracts $worker
    if ($LASTEXITCODE -ne 0) { throw 'Direct command workflow checks failed.' }
}
if (-not $Launch) {
    Write-Output 'Automated checks complete. -Launch opens direct TGA conversion with a simulated provider and expired test trial; add -Settings to review standalone launch. No Polar requests.'
    return
}
$root = Join-Path $repository ('.codex-temp\license-workflow\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$executable = Join-Path $repository 'artifacts\managed\bin\ContextSuite.Application.TestHost\Release\net10.0-windows\ContextSuite.Application.TestHost.exe'
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = $executable
if (-not $Settings) { $start.Arguments = '--activation-file "' + (Join-Path $root ('ActivationCleanup\' + [guid]::NewGuid().ToString('D') + '.request')) + '"' }
$start.UseShellExecute = $false
$start.WindowStyle = 'Normal'
$start.EnvironmentVariables['CONTEXTSUITE_TEST_ROOT'] = $root
$start.EnvironmentVariables['CONTEXTSUITE_TEST_WORKER'] = $worker
$start.EnvironmentVariables['CONTEXTSUITE_TEST_LICENSE_WORKFLOW'] = '1'
Write-Output "Test fixture: $root\fixture.png"
Write-Output 'Use TEST-ONLY, not a real license key. Avoid Explorer commands until the test app closes. No registration or installation changes.'
$process = [Diagnostics.Process]::Start($start)
try { $process.WaitForExit(); if ($process.ExitCode -ne 0) { throw "License workflow exited with code $($process.ExitCode)." } }
finally { $process.Dispose() }
Write-Output "Test files retained at $root. Interactive results must be recorded separately."
