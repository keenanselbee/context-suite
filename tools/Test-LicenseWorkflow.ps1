[CmdletBinding()]
param([switch] $Launch, [switch] $DirectCommands, [switch] $Settings, [string] $WorkerPath,
    [switch] $AllowScratchWorker, [string] $RequestManifest)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repository 'tests\ContextSuite.Application.TestHost\ContextSuite.Application.TestHost.csproj'
if (Get-Process -Name ContextSuite.Application, ContextSuite.Application.TestHost, ContextSuite.Worker -ErrorAction SilentlyContinue) {
    throw 'Close Context Suite and other application tests first. The full-app harness shares the per-user router.'
}
if ($RequestManifest -and $Settings) { throw 'Choose a reviewed command or standalone Settings.' }
$manifest = if ($RequestManifest) { (Resolve-Path -LiteralPath $RequestManifest).Path } else { $null }
if ($Launch -or $DirectCommands -or $WorkerPath) {
    if (-not $WorkerPath) { throw 'This workflow requires an explicit verified staged WorkerPath.' }
    $worker = (Resolve-Path -LiteralPath $WorkerPath).Path
    & (Join-Path $repository 'tools\Test-WorkflowPayload.ps1') -WorkerPath $worker -AllowScratchWorker:$AllowScratchWorker
}
& dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'License workflow test host build failed.' }
& dotnet run --project $project -c Release --no-build -- --license-workflow-contracts
if ($LASTEXITCODE -ne 0) { throw 'License workflow harness checks failed.' }
& dotnet run --project $project -c Release --no-build -- --manual-workflow-contracts
if ($LASTEXITCODE -ne 0) { throw 'Manual workflow selection checks failed.' }
if ($manifest) {
    & dotnet run --project $project -c Release --no-build -- --verify-manual-selection $manifest
    if ($LASTEXITCODE -ne 0) { throw 'Reviewed workflow inputs failed verification.' }
}
if ($DirectCommands) {
    & dotnet run --project $project -c Release --no-build -- --direct-command-contracts $worker
    if ($LASTEXITCODE -ne 0) { throw 'Direct command workflow checks failed.' }
}
if (-not $Launch) {
    Write-Output 'Automated checks complete. No application window or Polar request.'
    if ($manifest) { Write-Output 'The reviewed command is ready. Add -Launch with the same RequestManifest and verified WorkerPath for manual review.' }
    else { Write-Output '-Launch opens direct TGA conversion with an expired test trial; add -Settings for standalone Settings.' }
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
$start.EnvironmentVariables.Remove('CONTEXTSUITE_TEST_MANUAL_SELECTION')
if ($manifest) { $start.EnvironmentVariables['CONTEXTSUITE_TEST_MANUAL_SELECTION'] = $manifest }
Write-Output "Test fixture: $root\fixture.png"
if ($manifest) { Write-Output "Reviewed inputs will be copied under $root\Inputs; request provenance is retained in manual-inputs.json." }
Write-Output 'Use TEST-ONLY, not a real license key. Avoid Explorer commands until the test app closes. No registration or installation changes.'
$process = [Diagnostics.Process]::Start($start)
try { $process.WaitForExit(); if ($process.ExitCode -ne 0) { throw "License workflow exited with code $($process.ExitCode)." } }
finally { $process.Dispose() }
Write-Output "Test files retained at $root. Interactive results must be recorded separately."
