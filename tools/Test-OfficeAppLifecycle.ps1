[CmdletBinding()]
param([Parameter(Mandatory)] [string] $WorkerPath, [string] $RetainedBuildReceipt,
    [switch] $AllowAudioCandidate, [switch] $AllowPdfCandidate)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name ContextSuite.Application, ContextSuite.Application.TestHost, ContextSuite.Worker -ErrorAction SilentlyContinue) {
    throw 'Close Context Suite first. This test uses the actual per-user application router.'
}
$worker = (Resolve-Path -LiteralPath $WorkerPath).Path
$staging = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts\production-staging')) + '\'
if ([IO.Path]::GetFileName($worker) -cne 'ContextSuite.Worker.exe') { throw 'Use a verified isolated worker.' }
if ($RetainedBuildReceipt) {
    $retained = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\office-worker')) + '\'
    if (-not $worker.StartsWith($retained, [StringComparison]::OrdinalIgnoreCase)) { throw 'Retained Office worker must stay in its isolated staging root.' }
    $receipt = Get-Content -LiteralPath $RetainedBuildReceipt -Raw | ConvertFrom-Json
    $sources = $receipt.sources
    if (-not $sources) { $sources = $receipt.source }
    $files = $receipt.workerFiles
    if (-not $files) { $files = $receipt.worker }
    if (-not $sources -or -not $files.'ContextSuite.Worker.exe') { throw 'Missing retained source/binary evidence.' }
    foreach ($source in $sources.PSObject.Properties) {
        $name = $source.Name.Replace('\', '/')
        if ($name -match '^(src/ContextSuite.Core/|src/ContextSuite.Worker/|src/Shared/|proprietary/src/)' -and
            (Get-FileHash -LiteralPath (Join-Path $repository $source.Name)).Hash -ne $source.Value) { throw "Retained implementation changed: $name" }
    }
    $actual = @(Get-ChildItem -LiteralPath (Split-Path $worker -Parent) -File)
    if ($actual.Count -ne @($files.PSObject.Properties).Count) { throw 'Retained worker file inventory changed.' }
    foreach ($file in $actual) {
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne $files.($file.Name)) { throw "Retained worker file changed: $($file.Name)" }
    }
    Write-Output 'Retained Office worker implementation and complete top-level binary inventory match their receipt.'
} else {
    if (-not $worker.StartsWith($staging, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an isolated production worker or an explicit retained build receipt.' }
    & (Join-Path $repository 'tools\curated-engine\Test-ProductionPayload.ps1') -Payload (Split-Path $worker -Parent) -AllowAudioCandidate:$AllowAudioCandidate -AllowPdfCandidate:$AllowPdfCandidate
}
$project = Join-Path $repository 'tests\ContextSuite.Application.TestHost\ContextSuite.Application.TestHost.csproj'
$inputs = @($PSCommandPath, (Join-Path $repository 'Directory.Build.props'), (Join-Path $repository 'Directory.Build.targets'))
foreach ($directory in 'src\ContextSuite.Application', 'src\ContextSuite.Core', 'src\Shared', 'tests\ContextSuite.Application.TestHost') {
    $inputs += @(Get-ChildItem -LiteralPath (Join-Path $repository $directory) -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.csproj', '.xaml' } | ForEach-Object { $_.FullName })
}
if ($RetainedBuildReceipt) { $inputs += (Resolve-Path -LiteralPath $RetainedBuildReceipt).Path }
$inputs += @(Get-ChildItem -LiteralPath (Split-Path $worker -Parent) -File | ForEach-Object { $_.FullName })
$hashes = [ordered]@{}
foreach ($path in ($inputs | Sort-Object -Unique)) {
    if (Test-Path -LiteralPath $path) { $hashes[$path] = (Get-FileHash -LiteralPath $path).Hash }
}
& dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Application lifecycle test host build failed.' }
$executable = Join-Path $repository 'artifacts\managed\bin\ContextSuite.Application.TestHost\Release\net10.0-windows\ContextSuite.Application.TestHost.exe'
$run = Join-Path $repository ('.codex-temp\office-app-lifecycle-run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = $executable
$start.Arguments = '--office-app-lifecycle "' + $worker + '"'
$start.UseShellExecute = $false
$start.WindowStyle = 'Hidden'
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [IO.File]::WriteAllText((Join-Path $run 'stdout.log'), $stdout.Result)
    [IO.File]::WriteAllText((Join-Path $run 'stderr.log'), $stderr.Result)
    Write-Output $stdout.Result
    if ($process.ExitCode -ne 0) { throw "Application lifecycle checks failed. Inspect $run before retrying." }
    foreach ($path in $hashes.Keys) {
        if ((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$path]) { throw "Lifecycle input changed during verification: $path" }
    }
    [IO.File]::WriteAllText((Join-Path $run 'verification.json'), (@{
        Passed = $true; Worker = $worker; Inputs = $hashes; Output = $stdout.Result
    } | ConvertTo-Json -Depth 5))
} finally { $process.Dispose() }
Write-Output 'Actual application lifecycle checks passed. No native Office profile creation, licensing provider, installation or registration. Visible usability was not tested.'
