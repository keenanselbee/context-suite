[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $PSScriptRoot 'DateProbe'
$scratch = Join-Path $repository ('.codex-temp\office-date-native\' + [guid]::NewGuid().ToString('N'))
$build = Join-Path $scratch 'build'
New-Item -ItemType Directory -Path $build | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio x64 C++ tools are required.' }
$cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
& $cmake -S $source -B $build -G 'Visual Studio 18 2026' -A x64 '-DCMAKE_SYSTEM_VERSION=10.0.26100.0'
if ($LASTEXITCODE) { throw 'Date probe configuration failed.' }
& $cmake --build $build --config Release --target ContextSuite.Office.DateProbe -- /m /p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false /verbosity:minimal
if ($LASTEXITCODE) { throw 'Date probe build failed.' }
$executable = Join-Path $build 'bin\Release\ContextSuite.Office.DateProbe.exe'
[ordered]@{ executable=$executable; sha256=(Get-FileHash -LiteralPath $executable).Hash;
    sourceSha256=(Get-FileHash -LiteralPath (Join-Path $source 'Probe.cpp')).Hash;
    cmakeSha256=(Get-FileHash -LiteralPath (Join-Path $source 'CMakeLists.txt')).Hash;
    scope='Authored passive date experiment; no production payload or isolation acceptance.' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'build.json') -Encoding UTF8
Write-Output "Native date probe evidence: $scratch"
