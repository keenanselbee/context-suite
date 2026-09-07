[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9-]+$')][string] $RunName = 'prototype-2')

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scratch = Join-Path $repository '.codex-temp\curated-engine'
$downloads = Join-Path $scratch 'downloads'
$workspace = Join-Path $scratch $RunName
if (Test-Path -LiteralPath $workspace) { throw "Use a fresh run name: $workspace" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$pins = @(
    @('native.zip', '411CCA83E48F49F0F0ADC9422F2570FFE35C9CEC2B81CEAE32B788D61B4B54EF', 'https://codeload.github.com/dlemstra/Magick.Native/zip/77c935e38d379b22d981770a91c466c16cba148b'),
    @('imagemagick.zip', '8E186548B506C5F1550A43F3C4D39D5CF34247BBA19EE976141347257CFBA970', 'https://codeload.github.com/ImageMagick/ImageMagick/zip/fb965f1b54a65ddb633f8c2eac4452c782c66d7f'),
    @('dependencies.zip', '28BF8CF9CD10DCDA5F7ED0018E8D708286C88A5D790618942D406199EB645B68', 'https://github.com/ImageMagick/Dependencies/releases/download/2026.09.01.0503/windows-x64-static-noOpenMP-linked-runtime.zip'),
    @('configure-files.zip', '8298EF37E0BB08453CDDFF46B0CC82E8634A9B703B3EDCE5E70BC81BDC3D36E9', 'https://github.com/ImageMagick/Configure/releases/download/2026.08.23.0743/files.zip'),
    @('Configure.Release.x64.exe', 'A7EF0F32A43EEA5E02EF7CA163280BF3469C4498B6CA8A3A9C61B35EEDC5CC9D', 'https://github.com/ImageMagick/Configure/releases/download/2026.08.23.0743/Configure.Release.x64.exe'),
    @('webp-PATENTS.txt', 'CC3273E0694EA5896145E0677699B53471B03EA43021DDC50E7923FBB9F5023C', 'https://raw.githubusercontent.com/ImageMagick/webp/b981ef267195cb12f2cb97e4dd23e12a1323a4ce/PATENTS')
)
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
foreach ($pin in $pins) {
    $path = Join-Path $downloads $pin[0]
    if (-not (Test-Path -LiteralPath $path)) {
        & curl.exe --fail --location --silent --show-error --max-time 300 --output $path $pin[2]
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $($pin[0])" }
    }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $pin[1]) { throw "Hash mismatch: $path" }
}

$utf8 = [Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $workspace | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dependency-sources.json') -Destination $workspace
# Do not inherit the application's output routing in the isolated upstream build.
[IO.File]::WriteAllText((Join-Path $workspace 'Directory.Build.props'), '<Project />', $utf8)
function Expand-SelectedArchive([string] $Name, [string] $Destination, [string] $StripPrefix = '', [scriptblock] $Include = { $true }) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $downloads $Name))
    try {
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.Replace('/', '\')
            if ($StripPrefix) {
                if (-not $relative.StartsWith($StripPrefix, [StringComparison]::Ordinal)) { continue }
                $relative = $relative.Substring($StripPrefix.Length)
            }
            if (-not $relative -or $relative.EndsWith('\') -or -not (& $Include $relative)) { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $relative))
            if (-not $target.StartsWith($Destination.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe archive path.' }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
    } finally { $archive.Dispose() }
}
$native = Join-Path $workspace 'native'
$imageRoot = Join-Path $native 'src\ImageMagick'
Expand-SelectedArchive 'native.zip' $native 'Magick.Native-77c935e38d379b22d981770a91c466c16cba148b\'
Expand-SelectedArchive 'imagemagick.zip' (Join-Path $imageRoot 'ImageMagick') 'ImageMagick-fb965f1b54a65ddb633f8c2eac4452c782c66d7f\'
Expand-SelectedArchive 'configure-files.zip' (Join-Path $imageRoot 'Configure')
Copy-Item -LiteralPath (Join-Path $downloads 'Configure.Release.x64.exe') -Destination (Join-Path $imageRoot 'Configure')
$libraries = @('jpeg-turbo', 'jpeg-turbo-12', 'jpeg-turbo-16', 'lcms', 'png', 'webp', 'xml', 'zlib')
$artifacts = Join-Path $imageRoot 'Artifacts'
Expand-SelectedArchive 'dependencies.zip' $artifacts '' {
    param($relative)
    if ($relative -match '^lib\\CORE_RL_(.+)_\.lib$') { return $matches[1] -in $libraries }
    if ($relative -match '^(config|license)\\(.+)\.(h|txt)$') { return $matches[2] -in $libraries }
    if ($relative -match '^include\\([^\\]+)\\') { return $matches[1] -in $libraries }
    return $false
}
[IO.File]::WriteAllLines((Join-Path $artifacts 'pre-build-libs.txt'), @($libraries | ForEach-Object { "CORE_RL_$($_)_.lib" }), $utf8)
# Mechanical link overlay only: keep upstream codec implementations unchanged.
$header = Join-Path $native 'src\Magick.Native\Stdafx.h'
$original = [IO.File]::ReadAllText($header)
$allowedLinks = $libraries + @('MagickCore', 'MagickWand', 'coders')
$patched = [regex]::Replace($original, '(?m)^  MAGICK_NATIVE_LINK_LIB\(([^)]+)\)\r?\n', {
    param($match)
    if ($match.Groups[1].Value -in $allowedLinks) { return $match.Value }
    return ''
})
[IO.File]::WriteAllText((Join-Path $workspace 'Stdafx.upstream.h'), $original, $utf8)
[IO.File]::WriteAllText($header, $patched, $utf8)
$targets = @'
<Project>
  <ItemDefinitionGroup>
    <Link>
      <GenerateMapFile>true</GenerateMapFile>
      <MapFileName>$(OutDir)$(TargetName).map</MapFileName>
      <AdditionalOptions>/VERBOSE:LIB %(AdditionalOptions)</AdditionalOptions>
    </Link>
  </ItemDefinitionGroup>
  <ItemDefinitionGroup Condition="'$(MSBuildProjectName)' == 'CORE_MagickCore'">
    <ClCompile>
      <PreprocessorDefinitions>%(PreprocessorDefinitions);MAGICKCORE_LIBRARY_NAME="Magick.Native-Q16-x64.dll"</PreprocessorDefinitions>
    </ClCompile>
  </ItemDefinitionGroup>
</Project>
'@
[IO.File]::WriteAllText((Join-Path $workspace 'Directory.Build.targets'), $targets, $utf8)
$manifest = [ordered]@{
    schemaVersion = 1; runName = $RunName; status = 'prepared-not-built'
    nativeRevision = '77c935e38d379b22d981770a91c466c16cba148b'
    imageMagickRevision = 'fb965f1b54a65ddb633f8c2eac4452c782c66d7f'
    dependencyRelease = '2026.09.01.0503'; configureRelease = '2026.08.23.0743'
    libraries = $libraries
    inputs = @($pins | ForEach-Object { @{ name = $_[0]; sha256 = $_[1]; url = $_[2] } })
    overlay = @{ file = 'src/Magick.Native/Stdafx.h'; upstreamHash = (Get-FileHash (Join-Path $workspace 'Stdafx.upstream.h')).Hash; patchedHash = (Get-FileHash $header).Hash }
}
[IO.File]::WriteAllText((Join-Path $workspace 'preparation.json'), ($manifest | ConvertTo-Json -Depth 6), $utf8)
Write-Output "Prepared isolated curated engine at $workspace"
