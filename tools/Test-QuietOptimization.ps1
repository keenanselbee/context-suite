[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release', [switch] $SkipBuild, [switch] $Convert, [string] $WorkerPath,
    [ValidateRange(1, 20)][int] $Rounds = 1, [ValidateRange(0, 2000)][int] $SpacingMilliseconds = 0)

# Real application, isolated settings/trial and generated images. No input automation,
# registration, Explorer restart or customer media. It may show delayed progress.
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name 'ContextSuite.Application', 'ContextSuite.Application.TestHost', 'ContextSuite.Worker' -ErrorAction SilentlyContinue) {
    throw 'Close Context Suite and wait for other integration tests before running quiet smoke.'
}
if (-not $WorkerPath) { throw 'Provide an explicit worker from isolated production staging with -WorkerPath.' }
$worker = (Resolve-Path -LiteralPath $WorkerPath).Path
$staging = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\production-staging')) + '\'
if (-not $worker.StartsWith($staging, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($worker) -cne 'ContextSuite.Worker.exe') { throw 'Use a worker from isolated production staging.' }
& (Join-Path $PSScriptRoot 'curated-engine\Test-ProductionPayload.ps1') -Payload (Split-Path $worker -Parent)
& dotnet build (Join-Path $repositoryRoot 'tests\ContextSuite.Application.TestHost\ContextSuite.Application.TestHost.csproj') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Quiet test host build failed.' }
$runRoot = Join-Path $repositoryRoot ('.codex-temp\quiet-smoke\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$requestRoot = Join-Path $runRoot 'ActivationCleanup'
New-Item -ItemType Directory -Path $requestRoot -Force | Out-Null
$testHost = Join-Path $repositoryRoot "artifacts\managed\bin\ContextSuite.Application.TestHost\$Configuration\net10.0-windows\ContextSuite.Application.TestHost.exe"
Add-Type -AssemblyName System.Drawing
$source = Join-Path $runRoot 'test image.png'
$bitmap = New-Object System.Drawing.Bitmap 32,32
try {
    for ($y = 0; $y -lt 32; $y++) {
        for ($x = 0; $x -lt 32; $x++) { $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $x * 7, $y * 7, ($x + $y) * 4)) }
    }
    $bitmap.Save($source, [System.Drawing.Imaging.ImageFormat]::Png)
} finally { $bitmap.Dispose() }
$before = (Get-FileHash -LiteralPath $source).Hash
[IO.File]::WriteAllText((Join-Path $runRoot 'settings.json'), '{"SchemaVersion":1,"Convert":{"AllowReplacingOriginals":false},"Optimize":{"AllowReplacingOriginals":true},"PlayCompletionSound":false}')
$requests = @()
$children = @()
$diagnostics = @{}
$operation = if ($Convert) { 'convert' } else { 'optimize' }
$actions = if ($Convert) { @('webp', 'jpeg') } else { @('auto', 'lossless', 'balanced', 'smallest') }
try {
    for ($round = 0; $round -lt $Rounds; $round++) {
    foreach ($action in $actions) {
        $id = [guid]::NewGuid().ToString('D')
        $request = Join-Path $requestRoot ($id + '.request')
        $requests += $request
        [IO.File]::WriteAllText($request, "ContextSuiteActivation/1`nrequestId=$id`noperation=$operation`naction=$action`npathCount=1`npath=$source`n", [Text.UTF8Encoding]::new($false))
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $testHost
        $start.Arguments = '--activation-file "' + $request + '"'
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardError = $true
        $start.WorkingDirectory = $repositoryRoot
        $start.EnvironmentVariables['CONTEXTSUITE_TEST_ROOT'] = $runRoot
        $start.EnvironmentVariables['CONTEXTSUITE_TEST_WORKER'] = $worker
        $start.EnvironmentVariables['CONTEXTSUITE_TEST_QUIET_TRACE'] = '1'
        $child = [Diagnostics.Process]::Start($start)
        $children += $child
        $diagnostics[$child.Id] = $child.StandardError.ReadToEndAsync()
        if ($SpacingMilliseconds -gt 0) { Start-Sleep -Milliseconds $SpacingMilliseconds }
    }
    }
    foreach ($child in $children) {
        $deadlineMilliseconds = 30000 + 3000 * $actions.Count * $Rounds
        if (-not $child.WaitForExit($deadlineMilliseconds)) { throw 'Quiet application did not exit within the bounded batch deadline.' }
        if ($child.ExitCode -ne 0) { throw "Quiet application exited with $($child.ExitCode)." }
    }
    $reports = @(Get-ChildItem -LiteralPath $runRoot -Filter 'quiet-*.json' | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json })
    $states = @($reports | ForEach-Object { $_.States })
    if ($states.Count -ne $actions.Count * $Rounds -or @($states | Where-Object { $_ -notin @('Succeeded','Unchanged') }).Count) { throw 'Not every quick action completed exactly once.' }
    $completedActions = @($reports | ForEach-Object { $_.Actions })
    foreach ($action in $actions) {
        if (@($completedActions | Where-Object { $_ -eq $action }).Count -ne $Rounds) { throw "Wrong completion count for $action." }
    }
    if (@($reports | ForEach-Object { $_.ShownAtSeconds } | Where-Object { $_ -lt 2 }).Count) { throw 'A window appeared before the delayed-progress threshold.' }
    if ((Get-FileHash -LiteralPath $source).Hash -ne $before) { throw 'Original changed during quick action.' }
    if (@($requests | Where-Object { Test-Path -LiteralPath $_ }).Count) { throw 'An activation was not acknowledged.' }
    if (@($reports | ForEach-Object { $_.Outputs }).Count -eq 0) { throw 'Quiet smoke did not exercise publication.' }
    if (@(Get-CimInstance Win32_Process -Filter "Name = 'ContextSuite.Worker.exe'" | Where-Object { $_.CommandLine -like ('*' + $runRoot + '*') }).Count) { throw 'Owned worker remained after quiet shutdown.' }
    $outputs = @($reports | ForEach-Object { $_.Outputs })
    if (@($outputs | Select-Object -Unique).Count -ne $outputs.Count -or @($outputs | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count) {
        throw 'Quiet output collision or missing publication.'
    }
    Write-Output "Quiet smoke passed: $($actions.Count * $Rounds) direct $operation actions, complete results, no early window, original intact, automatic exit. Evidence: $runRoot"
} finally {
    foreach ($child in $children) {
        if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
        if ($diagnostics.ContainsKey($child.Id)) {
            [IO.File]::WriteAllText((Join-Path $runRoot ('stderr-' + $child.Id + '.txt')), $diagnostics[$child.Id].GetAwaiter().GetResult())
        }
        $child.Dispose()
    }
    foreach ($request in $requests) { if (Test-Path -LiteralPath $request) { Remove-Item -LiteralPath $request } }
}
