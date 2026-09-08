[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pins = Get-Content (Join-Path $PSScriptRoot 'source.json') -Raw | ConvertFrom-Json
$workspace = Join-Path $repository '.codex-temp\dds-engine'
$downloads = Join-Path $workspace 'downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
$archive = Join-Path $downloads 'directxtex-mar2026.zip'
if (-not (Test-Path -LiteralPath $archive)) {
    & curl.exe --fail --location --silent --show-error --max-time 120 --output $archive "https://codeload.github.com/microsoft/DirectXTex/zip/$($pins.revision)"
    if ($LASTEXITCODE) { throw 'DirectXTex source download failed.' }
}
if ((Get-FileHash -LiteralPath $archive).Hash -ne $pins.archiveSha256) { throw 'DirectXTex source archive identity mismatch.' }
$source = Join-Path $workspace "source\DirectXTex-$($pins.revision)"
if (-not (Test-Path -LiteralPath $source)) { Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $workspace 'source') }
# Reusing an extracted cache must not permit silently modified upstream source.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $expectedFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $prefix = "DirectXTex-$($pins.revision)/"
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        if (-not $entry.FullName.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Unexpected pinned source archive layout.' }
        $relative = $entry.FullName.Substring($prefix.Length).Replace('/', '\')
        $path = [IO.Path]::GetFullPath((Join-Path $source $relative))
        if (-not $path.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Source archive path escapes its root.' }
        [void]$expectedFiles.Add($path)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Pinned source cache is incomplete: $relative" }
        $stream = $entry.Open(); $hash = [Security.Cryptography.SHA256]::Create()
        try { $expected = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $hash.Dispose() }
        if ((Get-FileHash -LiteralPath $path).Hash -ne $expected) { throw "Pinned source cache was modified: $relative" }
    }
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
        if (-not $expectedFiles.Contains($file.FullName)) { throw "Unexpected file in pinned source cache: $($file.FullName)" }
    }
} finally { $zip.Dispose() }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio x64 C++ build tools are required.' }
$cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path -LiteralPath $cmake)) { throw 'Visual Studio CMake tools are required.' }
$build = Join-Path $workspace 'build'
& $cmake -S $PSScriptRoot -B $build -G 'Visual Studio 18 2026' -A x64 "-DDIRECTXTEX_SOURCE=$source" '-DCMAKE_SYSTEM_VERSION=10.0.26100.0'
if ($LASTEXITCODE) { throw 'DDS native configuration failed.' }
& $cmake --build $build --config Release --target ContextSuite.Dds.Native -- /m /p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false /verbosity:minimal
if ($LASTEXITCODE) { throw 'DDS native build failed.' }
$dll = Join-Path $build 'bin\Release\ContextSuite.Dds.Native.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'DDS bridge output is missing.' }
$destination = Join-Path $repository 'artifacts\engines\dds-win-x64'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $destination 'DirectXTex.License.txt') -Force
$generatorInstance = (Select-String -LiteralPath (Join-Path $build 'CMakeCache.txt') -Pattern '^CMAKE_GENERATOR_INSTANCE:INTERNAL=(.*)$').Matches.Groups[1].Value
$identity = @{ revision = $pins.revision; archiveSha256 = $pins.archiveSha256; nativeSha256 = (Get-FileHash -LiteralPath $dll).Hash;
    bridgeSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $repository 'proprietary\native\dds\Bridge.cpp')).Hash;
    licenseSha256 = (Get-FileHash -LiteralPath (Join-Path $source 'LICENSE')).Hash;
    buildDefinitionSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'CMakeLists.txt')).Hash;
    configuration = $pins.configuration; windowsSdk = '10.0.26100.0'; visualStudio = $generatorInstance }
[IO.File]::WriteAllText((Join-Path $destination 'ContextSuite.Dds.Engine.json'), ($identity | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output "Built DDS development candidate at $destination. No installation or commercial release approval."
