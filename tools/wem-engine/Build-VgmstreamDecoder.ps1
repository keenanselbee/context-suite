[CmdletBinding()]
param([string] $SourceArchive, [string] $OutputDirectory,
    [string] $SanitizedSourceArchive, [string] $SanitizedSourceHash)

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$commit = '71e2361042531fe767fb98300cf8c1ee95e539a0'
$expectedHash = '82D81CC5EE7A085427F75D9886703976B15F51309ED24532A72BE5A27467006B'
$vorbisHash = '5F330A20B19B3C70DC43EEDCC95891E65940FCA04A77155C573EFCDD4C2CA665'
if ($SanitizedSourceArchive -and $SourceArchive) { throw 'Choose either an upstream source archive or sanitized source archive.' }
if ($SanitizedSourceArchive) {
    if ($SanitizedSourceHash -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Supply the sanitized archive SHA-256 from its build receipt.' }
    $SourceArchive = $SanitizedSourceArchive
    $expectedInputHash = $SanitizedSourceHash
} else {
    if ($SanitizedSourceHash) { throw 'A sanitized source hash requires a sanitized archive.' }
    if (-not $SourceArchive) { $SourceArchive = Join-Path $repository ".codex-temp\wem-engine-research\vgmstream-$commit.zip" }
    $expectedInputHash = $expectedHash
}
$SourceArchive = (Resolve-Path -LiteralPath $SourceArchive).Path
if ((Get-FileHash -LiteralPath $SourceArchive -Algorithm SHA256).Hash -cne $expectedInputHash) {
    throw 'The vgmstream source archive does not match its expected SHA-256.'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repository ('.codex-temp\wem-engine-build\' + [guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
$scratch = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp')) + '\'
if (-not $OutputDirectory.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $OutputDirectory)) { throw 'Use a new output directory under repository .codex-temp.' }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio x64 C++ tools are required.' }
$cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path -LiteralPath $cmake)) { throw 'Visual Studio CMake is required.' }

New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$sourceParent = Join-Path $OutputDirectory 'source'
New-Item -ItemType Directory -Path $sourceParent | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($SourceArchive)
try {
    $prefix = "vgmstream-$commit/"
    $totalBytes = 0L
    foreach ($entry in $archive.Entries) {
        if (-not $entry.FullName.StartsWith($prefix, [StringComparison]::Ordinal) -or
            $entry.FullName.Contains('\') -or $entry.FullName.Contains(':') -or
            @($entry.FullName.Split('/') | Where-Object { $_ -eq '..' }).Count -or
            (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
            throw "Unsafe source archive entry: $($entry.FullName)"
        }
        $totalBytes += $entry.Length
        if ($totalBytes -gt 250MB) { throw 'Source archive exceeds the extraction limit.' }
        $destination = Join-Path $sourceParent $entry.FullName.Replace('/', '\')
        if ($entry.FullName.EndsWith('/')) {
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            continue
        }
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination)
    }
} finally { $archive.Dispose() }
$source = Join-Path $sourceParent "vgmstream-$commit"
foreach ($required in 'CMakeLists.txt', 'version-make.bat', 'COPYING',
    'ext_libs\dll-x64\libvorbis.dll', 'ext_libs\libvorbis.def',
    'ext_libs\licenses\libvorbis-1.3.7.COPYING', 'ext_libs\licenses\libogg-1.3.5.COPYING') {
    if (-not (Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) { throw "Incomplete vgmstream source: $required" }
}
$vorbisDll = Join-Path $source 'ext_libs\dll-x64\libvorbis.dll'
if ((Get-FileHash -LiteralPath $vorbisDll -Algorithm SHA256).Hash -cne $vorbisHash) {
    throw 'The x64 Vorbis decoder does not match the pinned source archive.'
}
if ($SanitizedSourceArchive) {
    if (-not (Select-String -LiteralPath (Join-Path $source 'src\vgmstream_init.c') -Pattern '#ifdef VGM_WEM_ONLY' -SimpleMatch -Quiet) -or
        (Test-Path -LiteralPath (Join-Path $source 'src\coding\libs\relic_mixfft.c')) -or
        (Test-Path -LiteralPath (Join-Path $source 'src\coding\libs\compresswave_lib.c')) -or
        (Test-Path -LiteralPath (Join-Path $source 'src\coding\mio_decoder.c'))) {
        throw 'Sanitized source is missing its WEM-only patch or contains excluded codec source.'
    }
    $sanitizedArchive = $SourceArchive
} else {
    & python -B (Join-Path $PSScriptRoot 'patches\Apply-WemOnlyPatch.py') $source
    if ($LASTEXITCODE -ne 0) { throw 'The WEM-only source patch failed.' }
    $sanitizedArchive = Join-Path $OutputDirectory "vgmstream-$commit-wem-only-source.zip"
    & python -B (Join-Path $PSScriptRoot 'Package-SanitizedSource.py') --source $source --output $sanitizedArchive
    if ($LASTEXITCODE -ne 0) { throw 'The sanitized corresponding-source archive failed.' }
}
$build = Join-Path $OutputDirectory 'build'
$install = Join-Path $OutputDirectory 'install'
$configure = @('-S', $source, '-B', $build, '-G', 'Visual Studio 18 2026', '-A', 'x64',
    "-DCMAKE_INSTALL_PREFIX=$install", '-DCMAKE_EXE_LINKER_FLAGS_RELEASE=/MAP', '-DBUILD_CLI=ON', '-DBUILD_FB2K=OFF',
    '-DBUILD_WINAMP=OFF', '-DBUILD_XMPLAY=OFF', '-DVGM_WEM_ONLY=ON', '-DUSE_VORBIS=ON', '-DUSE_MPEG=OFF',
    '-DUSE_FFMPEG=OFF', '-DUSE_G7221=OFF', '-DUSE_G719=OFF', '-DUSE_ATRAC9=OFF',
    '-DUSE_CELT=OFF', '-DUSE_SPEEX=OFF')
& $cmake @configure
if ($LASTEXITCODE -ne 0) { throw 'vgmstream CMake configure failed.' }
$buildLog = Join-Path $OutputDirectory 'build.log'
& $cmake --build $build --config Release --target vgmstream_cli --parallel 4 *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $buildLog -Tail 60
    throw 'vgmstream CLI build failed.'
}
$linkMap = Join-Path $build 'cli\Release\vgmstream-cli.map'
if (-not (Test-Path -LiteralPath $linkMap -PathType Leaf)) { throw 'The WEM-only linker map was not generated.' }
$prohibited = 'relic_mixfft.obj', 'relic_lib.obj', 'relic_decoder.obj',
    'compresswave_lib.obj', 'compresswave_decoder.obj', 'mio_decoder.obj',
    'mio_erisacontext.obj', 'mio_erisafile.obj', 'mio_erisamatrix.obj', 'mio_erisasound.obj'
foreach ($object in $prohibited) {
    if (Select-String -LiteralPath $linkMap -Pattern $object -SimpleMatch -Quiet) {
        throw "Unwanted codec object linked into the WEM decoder: $object"
    }
}
foreach ($object in 'wwise.obj', 'pcm_decoder.obj', 'vorbis_custom_utils_wwise.obj') {
    if (-not (Select-String -LiteralPath $linkMap -Pattern $object -SimpleMatch -Quiet)) {
        throw "Required WEM decoder object absent from the linker map: $object"
    }
}
& $cmake --install $build --config Release
if ($LASTEXITCODE -ne 0) { throw 'vgmstream CLI install into scratch failed.' }
$installedVorbisDll = Join-Path $install 'bin\libvorbis.dll'
if (-not (Test-Path -LiteralPath $installedVorbisDll -PathType Leaf) -or
    (Get-FileHash -LiteralPath $installedVorbisDll -Algorithm SHA256).Hash -cne $vorbisHash) {
    throw 'CMake did not install the pinned x64 Vorbis DLL.'
}
$dumpbin = Get-ChildItem -LiteralPath (Join-Path $visualStudio 'VC\Tools\MSVC') -Filter dumpbin.exe -Recurse |
    Where-Object { $_.FullName -match '\\Hostx64\\x64\\' } |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $dumpbin) { throw 'Visual Studio x64 dumpbin is required for the dependency receipt.' }
& python -B (Join-Path $PSScriptRoot 'Assemble-DecoderProvenance.py') --source $source `
    --archive $sanitizedArchive --install $install --recipe $PSScriptRoot --link-map $linkMap --dumpbin $dumpbin
if ($LASTEXITCODE -ne 0) { throw 'Decoder provenance collection failed.' }
Write-Output "Built pinned vgmstream CLI at $install"
