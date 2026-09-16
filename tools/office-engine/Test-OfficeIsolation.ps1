[CmdletBinding()]
param([switch] $CreateDisposableProfile, [string] $PreparedOfficeDirectory, [switch] $PassiveExports,
    [switch] $StartupDiagnostics, [switch] $EmbeddedStartup, [switch] $EmbeddedExports, [switch] $FontCallbacks)
$ErrorActionPreference = 'Stop'
if ($FontCallbacks) {
    if (-not $PreparedOfficeDirectory -or $PassiveExports -or $StartupDiagnostics -or $EmbeddedStartup -or $EmbeddedExports) {
        throw 'Choose font callbacks with the pinned runtime and without other startup/export modes.'
    }
    $EmbeddedExports = $true
}
if ($PreparedOfficeDirectory -and -not $CreateDisposableProfile) { throw 'Office isolation requires the authorized disposable profile test.' }
if ($PassiveExports -and -not $PreparedOfficeDirectory) { throw 'Passive exports require the pinned Office runtime parameter.' }
if ($StartupDiagnostics -and (-not $PreparedOfficeDirectory -or $PassiveExports)) { throw 'Choose startup diagnostics with the pinned runtime and without passive exports.' }
if ($EmbeddedStartup -and (-not $PreparedOfficeDirectory -or $PassiveExports -or $StartupDiagnostics -or $EmbeddedExports)) {
    throw 'Choose embedded startup with the pinned runtime and without the other startup/export modes.'
}
if ($EmbeddedExports -and (-not $PreparedOfficeDirectory -or $PassiveExports -or $StartupDiagnostics -or $EmbeddedStartup)) {
    throw 'Choose embedded exports with the pinned runtime and without other startup/export modes.'
}
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scratch = Join-Path $repository ('.codex-temp\office-isolation\' + [guid]::NewGuid().ToString('N'))
$build = Join-Path $scratch 'build'
New-Item -ItemType Directory -Path $build | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio x64 C++ tools are required.' }
$cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$source = Join-Path $PSScriptRoot 'IsolationProbe'
& $cmake -S $source -B $build -G 'Visual Studio 18 2026' -A x64 '-DCMAKE_SYSTEM_VERSION=10.0.26100.0'
if ($LASTEXITCODE) { throw 'Isolation probe configuration failed.' }
& $cmake --build $build --config Release --target ContextSuite.Office.IsolationProbe -- /m /p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false /verbosity:minimal
if ($LASTEXITCODE) { throw 'Isolation probe build failed.' }
$executable = Join-Path $build 'bin\Release\ContextSuite.Office.IsolationProbe.exe'
[ordered]@{ executable=$executable; sha256=(Get-FileHash -LiteralPath $executable).Hash;
    sourceSha256=(Get-FileHash -LiteralPath (Join-Path $source 'Probe.cpp')).Hash;
    environmentSha256=(Get-FileHash -LiteralPath (Join-Path $source 'Environment.h')).Hash;
    officeVersionSha256=(Get-FileHash -LiteralPath (Join-Path $source 'OfficeVersion.h')).Hash;
    officeExportsSha256=(Get-FileHash -LiteralPath (Join-Path $source 'OfficeExports.h')).Hash;
    startupDiagnosticsSha256=(Get-FileHash -LiteralPath (Join-Path $source 'OfficeStartupDiagnostics.h')).Hash;
    embeddedStartupSha256=(Get-FileHash -LiteralPath (Join-Path $source 'OfficeEmbeddedStartup.h')).Hash;
    jobObservationSha256=(Get-FileHash -LiteralPath (Join-Path $source 'JobObservation.h')).Hash;
    cmakeSha256=(Get-FileHash -LiteralPath (Join-Path $source 'CMakeLists.txt')).Hash } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'build.json') -Encoding UTF8
Write-Output "Isolation evidence: $scratch"
$arguments = @((Join-Path $scratch 'case'))
if ($CreateDisposableProfile) { $arguments += '--create-disposable-profile' }
if ($PreparedOfficeDirectory) {
    & python -B (Join-Path $PSScriptRoot 'Prepare-OfficeIsolation.py') $PreparedOfficeDirectory (Join-Path $scratch 'runtime\office')
    if ($LASTEXITCODE) { throw 'Office isolation copy verification failed; no Office process launched.' }
    if ($PassiveExports -or $StartupDiagnostics -or $EmbeddedStartup -or $EmbeddedExports) {
        $fixtureMode = if ($FontCallbacks) { '--isolation-font-fixtures' } else { '--isolation-fixtures' }
        & dotnet run --project (Join-Path $PSScriptRoot 'Probe\Office.Evaluation.csproj') -c Release -- $fixtureMode (Join-Path $scratch 'office-fixtures')
        if ($LASTEXITCODE) { throw 'Authored isolation fixture generation failed; no Office process launched.' }
        $arguments += $(if ($FontCallbacks) { '--office-font-callbacks' } elseif ($EmbeddedExports) { '--office-embedded-exports' }
            elseif ($EmbeddedStartup) { '--office-embedded-startup' }
            elseif ($StartupDiagnostics) { '--office-startup-diagnostics' } else { '--office-exports' })
    } else { $arguments += '--office-version' }
}
$sourceLeases = @()
try {
    if ($EmbeddedExports) {
        $fixtures = Join-Path $scratch 'office-fixtures'
        foreach ($fixture in @(@('Word', 'docx'), @('Excel', 'xlsx'), @('PowerPoint', 'pptx'))) {
            $names = if ($FontCallbacks) { @('{0} font control.{1}' -f $fixture[0], $fixture[1]); @('{0} font missing.{1}' -f $fixture[0], $fixture[1]) }
                else { '{0} {1}.{2}' -f $fixture[0], [char]0x00fc, $fixture[1] }
            foreach ($name in $names) {
                $sourceLeases += [System.IO.File]::Open((Join-Path $fixtures $name), [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
            }
        }
        $preflightMode = if ($FontCallbacks) { '--preflight-isolation-font-fixtures' } else { '--preflight-isolation-fixtures' }
        & dotnet run --no-build --project (Join-Path $PSScriptRoot 'Probe\Office.Evaluation.csproj') -c Release -- $preflightMode $fixtures |
            Set-Content -LiteralPath (Join-Path $scratch 'source-preflight.json') -Encoding UTF8
        if ($LASTEXITCODE) { throw 'Office source preflight refused an input; no isolated test process launched.' }
    }
    & $executable @arguments
    if ($LASTEXITCODE) { throw "Isolation experiment failed; retain $scratch" }
} finally {
    foreach ($lease in $sourceLeases) { $lease.Dispose() }
}
if ($FontCallbacks) {
    & python -B (Join-Path $PSScriptRoot 'Verify-OfficeIsolationCopy.py') $scratch |
        Set-Content -LiteralPath (Join-Path $scratch 'runtime-after.json') -Encoding UTF8
    if ($LASTEXITCODE) { throw 'Office runtime changed during the font callback evaluation.' }
}
Write-Output 'Authored native fixtures and optional fixed Office evaluation commands only; customer compatibility, hostile inputs and production isolation remain unverified.'
if (-not $CreateDisposableProfile) { Write-Output 'Preflight only: no AppContainer profile created and no isolated file/network result claimed.' }
