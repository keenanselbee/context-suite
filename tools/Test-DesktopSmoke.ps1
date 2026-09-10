[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [switch] $Explorer,
    [switch] $Images,
    [switch] $SkipBuild,
    [string] $WorkerPath
)

$ErrorActionPreference = 'Stop'
if ($Images -and $Explorer) { throw 'Choose Images or Explorer, not both. Image UI tests use isolated test composition.' }
$repositoryRoot = Split-Path $PSScriptRoot -Parent
if ($Images) {
    if (-not $WorkerPath) { throw 'Image mode requires an explicit verified staged -WorkerPath.' }
    $worker = (Resolve-Path -LiteralPath $WorkerPath).Path
    $staging = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\production-staging')) + '\'
    if (-not $worker.StartsWith($staging, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($worker) -cne 'ContextSuite.Worker.exe') { throw 'Use a worker from isolated production staging.' }
    & (Join-Path $PSScriptRoot 'curated-engine\Test-ProductionPayload.ps1') -Payload (Split-Path $worker -Parent)
}
$scratch = Join-Path $repositoryRoot '.codex-temp\desktop-smoke'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$lock = $null
try {
    try {
        $lock = [IO.File]::Open((Join-Path $scratch 'run.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    } catch { throw 'Another desktop smoke test holds the lock. Wait for it to finish.' }
    if (Get-Process -Name 'ContextSuite.Application', 'ContextSuite.Application.TestHost', 'ContextSuite.Worker' -ErrorAction SilentlyContinue) {
        throw 'NOT RUN: close Context Suite before desktop smoke tests.'
    }
    if ($Explorer) {
        & (Join-Path $PSScriptRoot 'Test-InstalledShellPrototype.ps1')
        foreach ($name in 'Analyze', 'Convert', 'Optimize') {
            $package = Get-AppxPackage -Name "Keenan.ContextSuite.$name.ShellPrototype"
            $manifest = Get-AppxPackageManifest -Package $package
            if ($manifest.Package.Applications.Application.Executable -ne 'ContextSuite.Application.exe') {
                throw 'NOT RUN: installed shell targets the old prototype. Explicit production registration is required.'
            }
        }
    }
    if (-not $SkipBuild -and -not $Images) {
        & (Join-Path $PSScriptRoot 'Build-Production.ps1') -Configuration $Configuration -SkipShell
    }
    $app = Join-Path $repositoryRoot "artifacts\production\$Configuration\ContextSuite.Application.exe"
    if (-not $Images -and -not (Test-Path -LiteralPath $app)) { throw "NOT RUN: missing production application: $app" }
    $mode = if ($Images) { 'images' } elseif ($Explorer) { 'explorer' } else { 'wpf' }
    $arguments = @($app, $scratch, $mode)
    if ($Images) {
        & dotnet build (Join-Path $repositoryRoot 'tests\ContextSuite.Application.TestHost\ContextSuite.Application.TestHost.csproj') -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Isolated UI test host build failed.' }
        $hostPath = Join-Path $repositoryRoot "artifacts\managed\bin\ContextSuite.Application.TestHost\$Configuration\net10.0-windows\ContextSuite.Application.TestHost.exe"
        $arguments = @($hostPath, $scratch, $mode, $worker)
    }
    & dotnet run --project (Join-Path $repositoryRoot 'tests\ContextSuite.Desktop.SmokeTests\ContextSuite.Desktop.SmokeTests.csproj') -c $Configuration -- @arguments
    if ($LASTEXITCODE -eq 2) { throw 'Desktop smoke NOT RUN: interactive desktop/preconditions unavailable.' }
    if ($LASTEXITCODE -ne 0) { throw "Desktop smoke failed. See artifacts under $scratch." }
} finally {
    if ($lock) { $lock.Dispose() }
}
