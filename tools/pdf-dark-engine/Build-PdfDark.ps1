[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scratch = Join-Path $repository '.codex-temp\pdf-engine'
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
if (-not $prepared.StartsWith($scratch + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use a repository-local prepared qpdf SDK.'
}
foreach ($part in @($prepared, (Join-Path $prepared 'upstream.zip'), (Join-Path $prepared 'inventory.json'),
    (Join-Path $prepared 'unpacked'))) {
    if ((Get-Item -LiteralPath $part -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw "Linked PDF SDK input is not allowed: $part"
    }
}
$pin = Get-Content -LiteralPath (Join-Path $repository 'tools\pdf-engine\evaluation.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $prepared 'upstream.zip')).Hash -ne $pin.archiveSha256) {
    throw 'qpdf archive identity changed.'
}
$unpacked = Join-Path $prepared 'unpacked'
$inventory = Get-Content -LiteralPath (Join-Path $prepared 'inventory.json') -Raw | ConvertFrom-Json
if ($inventory.Count -eq 0) { throw 'qpdf SDK inventory is empty.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $inventory) {
    if ($entry.path -isnot [string] -or $entry.sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'Invalid qpdf SDK inventory entry.'
    }
    $relative = $entry.path.Replace('\', '/')
    if ($relative.Contains(':') -or
        @($relative.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0 -or
        -not $seen.Add($relative)) { throw 'Invalid qpdf SDK inventory path.' }
    $path = [IO.Path]::GetFullPath((Join-Path $unpacked $relative))
    if (-not $path.StartsWith($unpacked + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256) { throw 'qpdf SDK inventory changed.' }
}
$actual = @(Get-ChildItem -LiteralPath $unpacked -Recurse -Force | Where-Object {
    if ($_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Linked qpdf SDK entry is not allowed.' }
    -not $_.PSIsContainer
})
if ($actual.Count -ne $inventory.Count) { throw 'qpdf SDK membership changed.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio x64 C++ tools are required.' }
$cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$source = Join-Path $repository 'proprietary\src\ContextSuite.PdfDark.Native'
$payload = Join-Path $unpacked 'qpdf-12.4.1-msvc64'
foreach ($required in @('include\qpdf\QPDF.hh', 'lib\qpdf.lib', 'bin\qpdf30.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) {
        throw "Incomplete prepared qpdf SDK: $required"
    }
}
$build = Join-Path $prepared ('pdf-dark-build-' + [guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $build) { throw 'PDF dark-mode scratch build already exists.' }
& $cmake -S $source -B $build -G 'Visual Studio 18 2026' -A x64 "-DQPDF_PAYLOAD=$payload" '-DCMAKE_SYSTEM_VERSION=10.0.26100.0'
if ($LASTEXITCODE) { throw 'PDF dark-mode configuration failed.' }
& $cmake --build $build --config Release --target ContextSuite.PdfDark -- /m /p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false /verbosity:minimal
if ($LASTEXITCODE) { throw 'PDF dark-mode build failed.' }
$executable = Join-Path $build 'bin\Release\ContextSuite.PdfDark.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'PDF dark-mode build produced no host executable.' }
$instanceLine = Get-Content -LiteralPath (Join-Path $build 'CMakeCache.txt') | Where-Object { $_ -like 'CMAKE_GENERATOR_INSTANCE:INTERNAL=*' } | Select-Object -First 1
if (-not $instanceLine) { throw 'CMake did not record the Visual Studio instance.' }
[ordered]@{
    executable = $executable
    sha256 = (Get-FileHash -LiteralPath $executable).Hash
    sourceSha256 = (Get-FileHash -LiteralPath (Join-Path $source 'PdfDark.cpp')).Hash
    imagePolicySha256 = (Get-FileHash -LiteralPath (Join-Path $source 'PdfDarkImagePolicy.h')).Hash
    diagramPolicySha256 = (Get-FileHash -LiteralPath (Join-Path $source 'PdfDarkDiagramPolicy.h')).Hash
    structurePolicySha256 = (Get-FileHash -LiteralPath (Join-Path $source 'PdfDarkStructurePolicy.h')).Hash
    shadingPolicySha256 = (Get-FileHash -LiteralPath (Join-Path $source 'PdfDarkShadingPolicy.h')).Hash
    retainedImagePolicySha256 = (Get-FileHash -LiteralPath (Join-Path $source 'PdfDarkRetainedImagePolicy.h')).Hash
    fontPolicySha256 = (Get-FileHash -LiteralPath (Join-Path $source 'PdfDarkFontPolicy.h')).Hash
    systemFontPolicySha256 = (Get-FileHash -LiteralPath (Join-Path $source 'PdfDarkSystemFontPolicy.h')).Hash
    buildSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $source 'CMakeLists.txt')).Hash
    qpdfSha256 = (Get-FileHash -LiteralPath (Join-Path $payload 'bin\qpdf30.dll')).Hash
    visualStudio = $instanceLine.Substring($instanceLine.IndexOf('=') + 1)
    windowsSdk = '10.0.26100.0'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $build 'build.json') -Encoding UTF8
Write-Output "Isolated PDF dark-mode host: $executable"
