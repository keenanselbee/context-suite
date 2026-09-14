[CmdletBinding()]
param([switch] $CreateDisposableProfile)
$ErrorActionPreference = 'Stop'
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
    cmakeSha256=(Get-FileHash -LiteralPath (Join-Path $source 'CMakeLists.txt')).Hash } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'build.json') -Encoding UTF8
Write-Output "Isolation evidence: $scratch"
$arguments = @((Join-Path $scratch 'case'))
if ($CreateDisposableProfile) { $arguments += '--create-disposable-profile' }
& $executable @arguments
if ($LASTEXITCODE) { throw "Isolation experiment failed; retain $scratch" }
Write-Output 'Generated native fixture only; Office compatibility, hostile documents and production isolation remain unverified.'
if (-not $CreateDisposableProfile) { Write-Output 'Preflight only: no AppContainer profile created and no isolated file/network result claimed.' }
