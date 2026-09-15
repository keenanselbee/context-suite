[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $repository 'proprietary\src\ContextSuite.OfficeHost.Native'
if (-not (Test-Path -LiteralPath (Join-Path $source 'Host.cpp'))) { throw 'The private Office host source is required.' }
$scratch = Join-Path $repository ('.codex-temp\office-host\' + [guid]::NewGuid().ToString('N'))
$build = Join-Path $scratch 'build'
New-Item -ItemType Directory -Path $build | Out-Null
$snapshot = Join-Path $scratch 'source'
New-Item -ItemType Directory -Path $snapshot | Out-Null
foreach ($name in @('CMakeLists.txt', 'Host.cpp', 'EmbeddedOffice.h')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $snapshot $name)
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio x64 C++ tools are required.' }
$cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
& $cmake -S $snapshot -B $build -G 'Visual Studio 18 2026' -A x64 '-DCMAKE_SYSTEM_VERSION=10.0.26100.0'
if ($LASTEXITCODE) { throw 'Office host configuration failed.' }
& $cmake --build $build --config Release --target ContextSuite.OfficeHost -- /m /p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false /verbosity:minimal
if ($LASTEXITCODE) { throw 'Office host build failed.' }
$executable = Join-Path $build 'bin\Release\ContextSuite.OfficeHost.exe'
[ordered]@{
    executable = $executable
    sha256 = (Get-FileHash -LiteralPath $executable).Hash
    source = @(Get-ChildItem -LiteralPath $snapshot -File | ForEach-Object {
        [ordered]@{ name=$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    scope = 'Fixed-purpose Office host candidate only; no runtime adoption, installation or customer conversion enabled.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $scratch 'build.json') -Encoding UTF8
Write-Output "Office host evidence: $scratch"
