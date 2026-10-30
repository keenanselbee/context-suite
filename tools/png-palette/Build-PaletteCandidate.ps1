[CmdletBinding()]
param([Parameter(Mandatory)][string] $Rustc)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workspace = Join-Path $repository '.codex-temp\png-palette-build'
$revision = '6ef61711b07e58c107decbd4721d9d50e646dac8'
$archiveHash = '4480C3CD00A9AE2CB5CE7BB4DADAE696ADBFC8EB161AAB2756AE374555D8EC9D'
New-Item -ItemType Directory -Path $workspace -Force | Out-Null
$archive = Join-Path $workspace 'exoquant.zip'
if (-not (Test-Path -LiteralPath $archive)) {
    & curl.exe --fail --location --silent --show-error --max-time 60 --output $archive "https://codeload.github.com/exoticorn/exoquant-rs/zip/$revision"
    if ($LASTEXITCODE) { throw 'Pinned ExoQuant download failed.' }
}
if ((Get-FileHash -LiteralPath $archive).Hash -ne $archiveHash) { throw 'ExoQuant archive hash mismatch.' }
$source = Join-Path $workspace "source\exoquant-rs-$revision"
if (-not (Test-Path -LiteralPath $source)) { Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $workspace 'source') }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $prefix = "exoquant-rs-$revision/"
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        if (-not $entry.FullName.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Unexpected archive layout.' }
        $path = [IO.Path]::GetFullPath((Join-Path $source $entry.FullName.Substring($prefix.Length)))
        if (-not $path.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive escape.' }
        [void]$expected.Add($path)
        $stream = $entry.Open(); $hash = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $hash.Dispose() }
        if ((Get-FileHash -LiteralPath $path).Hash -ne $digest) { throw 'Modified upstream cache.' }
    }
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
        if (-not $expected.Contains($file.FullName)) { throw 'Unexpected upstream cache file.' }
    }
} finally { $zip.Dispose() }
$version = & $Rustc --version
if ($LASTEXITCODE -or $version -notmatch '^rustc 1\.90\.0 ') { throw 'Use the reviewed Rust 1.90.0 compiler.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vc = Get-ChildItem (Join-Path $vs 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdk = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Lib') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'um\x64\kernel32.lib') } | Sort-Object Name -Descending | Select-Object -First 1
$linker = Join-Path $vc.FullName 'bin\Hostx64\x64\link.exe'
$composition = ''
foreach ($module in 'color','quantizer','colormap','remapper','optimizer','colorspace','palettesort','basicapi','ditherer','testdata') {
    $path = (Join-Path $source "src\$module.rs").Replace('\','/')
    $composition += "#[path=`"$path`"] pub mod $module;`n"
}
$histogram = (Join-Path $PSScriptRoot 'histogram.rs').Replace('\','/')
$composition += "#[path=`"$histogram`"] mod histogram;`n"
$composition += 'pub use basicapi::{convert_to_indexed,generate_palette}; pub use color::*; pub use colormap::ColorMap; pub use colorspace::{ColorSpace,SimpleColorSpace}; pub use histogram::*; pub use palettesort::sort_palette; pub use quantizer::Quantizer; pub use remapper::Remapper;'
[IO.File]::WriteAllText((Join-Path $workspace 'lib.rs'), $composition, [Text.UTF8Encoding]::new($false))
$oldLib = $env:LIB
try {
    $env:LIB = (Join-Path $vc.FullName 'lib\x64') + ';' + (Join-Path $sdk.FullName 'um\x64') + ';' + (Join-Path $sdk.FullName 'ucrt\x64')
    & $Rustc --edition=2015 --crate-name exoquant --crate-type rlib -O (Join-Path $workspace 'lib.rs') -o (Join-Path $workspace 'libexoquant.rlib')
    if ($LASTEXITCODE) { throw 'Palette library build failed.' }
    & $Rustc --edition=2021 -O -C "linker=$linker" -C 'link-arg=/Brepro' --extern "exoquant=$(Join-Path $workspace 'libexoquant.rlib')" (Join-Path $PSScriptRoot 'main.rs') -o (Join-Path $workspace 'ContextSuite.Palette.exe')
    if ($LASTEXITCODE) { throw 'Palette helper build failed.' }
    & $Rustc --edition=2015 --test -O -C "linker=$linker" (Join-Path $workspace 'lib.rs') -o (Join-Path $workspace 'palette-contracts.exe')
    if ($LASTEXITCODE) { throw 'Palette contracts build failed.' }
    & (Join-Path $workspace 'palette-contracts.exe') stable_tests
    if ($LASTEXITCODE) { throw 'Palette histogram contracts failed.' }
} finally { $env:LIB = $oldLib }
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $workspace 'ExoQuant.License.txt') -Force
$rustNotice = Join-Path (Split-Path (Split-Path $Rustc -Parent) -Parent) 'share\doc\rust\COPYRIGHT-library.html'
if ((Get-FileHash -LiteralPath $rustNotice).Hash -ne '025C0DD1236493FCD5B6386B68453852E6528722BD5F8DCE5949E94D225E7A60') { throw 'Rust library notice mismatch.' }
Copy-Item -LiteralPath $rustNotice -Destination (Join-Path $workspace 'Rust.Library.Notices.html') -Force
$identity = [ordered]@{ schema=1; status='candidate-not-installed'; policy='dark4-anchor8-adaptive50-alpha16'; revision=$revision; archiveSha256=$archiveHash; compiler=$version; executableSha256=(Get-FileHash (Join-Path $workspace 'ContextSuite.Palette.exe')).Hash; sources=@(Get-ChildItem $PSScriptRoot -File | ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash $_.FullName).Hash} }) }
[IO.File]::WriteAllText((Join-Path $workspace 'candidate.json'), ($identity | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Output "Built pinned single-recipe candidate at $workspace. No production files or registrations changed."
