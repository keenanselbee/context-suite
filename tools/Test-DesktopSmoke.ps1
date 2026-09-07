[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [switch] $Explorer,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $repositoryRoot '.codex-temp\desktop-smoke'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$lock = $null
try {
    try {
        $lock = [IO.File]::Open((Join-Path $scratch 'run.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    } catch { throw 'Another desktop smoke test holds the lock. Wait for it to finish.' }
    if (Get-Process -Name 'ContextSuite.Application', 'ContextSuite.Worker' -ErrorAction SilentlyContinue) {
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
    if (-not $SkipBuild) {
        & (Join-Path $PSScriptRoot 'Build-Production.ps1') -Configuration $Configuration -SkipShell
    }
    $app = Join-Path $repositoryRoot "artifacts\production\$Configuration\ContextSuite.Application.exe"
    if (-not (Test-Path -LiteralPath $app)) { throw "NOT RUN: missing production application: $app" }
    $mode = if ($Explorer) { 'explorer' } else { 'wpf' }
    & dotnet run --project (Join-Path $repositoryRoot 'tests\ContextSuite.Desktop.SmokeTests\ContextSuite.Desktop.SmokeTests.csproj') -c $Configuration -- $app $scratch $mode
    if ($LASTEXITCODE -eq 2) { throw 'Desktop smoke NOT RUN: interactive desktop/preconditions unavailable.' }
    if ($LASTEXITCODE -ne 0) { throw "Desktop smoke failed. See artifacts under $scratch." }
} finally {
    if ($lock) { $lock.Dispose() }
}
