[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
if (-not $prepared.StartsWith((Join-Path $repository '.codex-temp\pdf-engine\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use the independently prepared repository-local qpdf SDK.'
}
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evaluation.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $prepared 'upstream.zip')).Hash -ne $pin.archiveSha256) { throw 'qpdf archive identity changed.' }
$unpacked = Join-Path $prepared 'unpacked'
foreach ($entry in (Get-Content -LiteralPath (Join-Path $prepared 'inventory.json') -Raw | ConvertFrom-Json)) {
    $path = [IO.Path]::GetFullPath((Join-Path $unpacked $entry.path))
    if (-not $path.StartsWith($unpacked + '\', [StringComparison]::OrdinalIgnoreCase) -or
        (Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256) { throw 'qpdf SDK inventory changed.' }
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio x64 C++ tools are required.' }
$cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$source = Join-Path $repository 'proprietary\src\ContextSuite.ImagePdfValidator.Native'
$payload = Join-Path $unpacked 'qpdf-12.4.1-msvc64'
$build = Join-Path $prepared 'image-validator-build'
& $cmake -S $source -B $build -G 'Visual Studio 18 2026' -A x64 "-DQPDF_PAYLOAD=$payload" '-DCMAKE_SYSTEM_VERSION=10.0.26100.0'
if ($LASTEXITCODE) { throw 'Image PDF validator configuration failed.' }
& $cmake --build $build --config Release --target ContextSuite.ImagePdfValidator -- /m /p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false /verbosity:minimal
if ($LASTEXITCODE) { throw 'Image PDF validator build failed.' }
$executable = Join-Path $build 'bin\Release\ContextSuite.ImagePdfValidator.exe'
$instanceLine = Get-Content -LiteralPath (Join-Path $build 'CMakeCache.txt') | Where-Object { $_ -like 'CMAKE_GENERATOR_INSTANCE:INTERNAL=*' } | Select-Object -First 1
if (-not $instanceLine) { throw 'CMake did not record the actual Visual Studio instance.' }
[ordered]@{ executable=$executable; sha256=(Get-FileHash -LiteralPath $executable).Hash;
    bridgeSourceSha256=(Get-FileHash -LiteralPath (Join-Path $source 'Validator.cpp')).Hash;
    buildSourceSha256=(Get-FileHash -LiteralPath (Join-Path $source 'CMakeLists.txt')).Hash;
    qpdfSha256=(Get-FileHash -LiteralPath (Join-Path $payload 'bin\qpdf30.dll')).Hash;
    visualStudio=$instanceLine.Substring($instanceLine.IndexOf('=') + 1); cmake=$cmake; windowsSdk='10.0.26100.0' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $prepared 'image-validator-build.json') -Encoding UTF8
Write-Output "Isolated image PDF validator: $executable"
