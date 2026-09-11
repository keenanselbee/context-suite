[CmdletBinding()]
param([Parameter(Mandatory)][string] $SourceDirectory,
      [Parameter(Mandatory)][ValidateSet('Opus', 'OggVorbis', 'Lame', 'LameStable', 'Make', 'Nasm', 'Zlib')][string] $Dependency)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
& (Join-Path $PSScriptRoot 'Prepare-AudioSources.ps1') -SourceDirectory $SourceDirectory -VerifyOnly -BuildInputsOnly:($Dependency -ne 'Lame')
$pins = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-inputs.json') -Raw | ConvertFrom-Json).archives
switch ($Dependency) {
    'Zlib' {
        $sourceIds = @('zlib')
        $recipeName = 'zlib'
        $testCount = 1
        $artifacts = @('zlib/Release/zs.lib', 'zlib/zconf.h', 'Release/ContextSuite.Zlib.Example.exe')
    }
    'Nasm' {
        $sourceIds = @('nasm')
        $recipeName = 'nasm'
        $testCount = 2
        $artifacts = @('Release/nasm.exe', 'Release/ContextSuite.Nasm.Probe.exe', 'probe.obj')
    }
    'Make' {
        $sourceIds = @('make')
        $recipeName = 'make'
        $testCount = 2
        $artifacts = @('Release/gnumake.exe')
    }
    'Opus' {
        $sourceIds = @('opus')
        $recipeName = 'opus'
        $testCount = 6
        $artifacts = @('opus/Release/opus.lib', 'Release/ContextSuite.Opus.Version.exe') +
            @('test_opus_decode', 'test_opus_padding', 'test_opus_api', 'test_opus_encode', 'test_opus_extensions' | ForEach-Object { "opus/Release/$_.exe" })
        $opus = $pins | Where-Object id -eq 'opus'
        if ($opus.revision -ne '3da9f7a6db1c05c3996cb363a9d1931a978bf1be' -or
            $opus.packageVersion -ne '1.6.1-contextsuite-g3da9f7a6db1c') { throw 'Unsupported Opus source/version pin.' }
    }
    'OggVorbis' {
        $sourceIds = @('ogg', 'vorbis')
        $recipeName = 'ogg-vorbis'
        $testCount = 4
        $artifacts = @('ogg/Release/ogg.lib', 'vorbis/lib/Release/vorbis.lib', 'vorbis/lib/Release/vorbisenc.lib',
            'vorbis/lib/Release/vorbisfile.lib', 'ogg/Release/test_bitwise.exe', 'ogg/Release/test_framing.exe',
            'vorbis/test/Release/vorbis_test.exe', 'vorbis/test/Release/test_codebook.exe')
    }
    { $_ -in 'Lame', 'LameStable' } {
        $sourceIds = if ($Dependency -eq 'LameStable') { @('lame-stable') } else { @('lame') }
        $recipeName = 'lame'
        $testCount = 1
        $artifacts = @('Release/mp3lame.lib', 'Release/ContextSuite.Lame.Probe.exe', 'lame-vbr2.mp3')
    }
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -or -not $visualStudio) { throw 'Existing Visual Studio x64 C++ build tools are required.' }
$cmakeDirectory = Join-Path $visualStudio 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin'
$cmake = Join-Path $cmakeDirectory 'cmake.exe'
$ctest = Join-Path $cmakeDirectory 'ctest.exe'
if (-not (Test-Path -LiteralPath $cmake) -or -not (Test-Path -LiteralPath $ctest)) { throw 'Existing Visual Studio CMake/CTest tools are required.' }
$workspace = Join-Path $repository ('.codex-temp/audio-' + $recipeName + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace | Out-Null
Write-Output "$Dependency dependency evidence: $workspace"
$sources = @{}
$sourceFiles = @()
foreach ($id in $sourceIds) {
    $pin = $pins | Where-Object id -eq $id
    $source = Join-Path $workspace ('source/' + $id)
    $sources[$id] = $source
    $archivePath = Join-Path ([IO.Path]::GetFullPath($SourceDirectory)) $pin.file
    $stream = [IO.File]::Open($archivePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $archiveHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
        finally { $hasher.Dispose() }
        if ($stream.Length -ne $pin.bytes -or $archiveHash -ne $pin.sha256) { throw 'Source archive changed before extraction.' }
        if ($pin.downloadKind -eq 'release-tar') {
            $result = & python (Join-Path $PSScriptRoot 'Read-SourceTar.py') $SourceDirectory $id $source
            if ($LASTEXITCODE -ne 0) { throw 'Pinned release source extraction failed.' }
            $sourceFiles += ($result | ConvertFrom-Json).files
            continue
        }
        New-Item -ItemType Directory -Path $source | Out-Null
        $stream.Position = 0
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
        try {
            foreach ($entry in $zip.Entries) {
                if ($entry.FullName.EndsWith('/')) { continue }
                if (-not $entry.FullName.StartsWith($pin.prefix, [StringComparison]::Ordinal)) { throw 'Unexpected source archive prefix.' }
                $relative = $entry.FullName.Substring($pin.prefix.Length)
                $path = [IO.Path]::GetFullPath((Join-Path $source $relative))
                if (-not $path.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Source path escapes its root.' }
                [IO.Directory]::CreateDirectory((Split-Path $path -Parent)) | Out-Null
                $entryInput = $entry.Open()
                try {
                    $entryOutput = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    try { $entryInput.CopyTo($entryOutput) }
                    finally { $entryOutput.Dispose() }
                }
                finally { $entryInput.Dispose() }
                $sourceFiles += [ordered]@{ path=($id + '/' + $relative); bytes=(Get-Item -LiteralPath $path).Length; sha256=(Get-FileHash -LiteralPath $path).Hash }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $stream.Dispose() }
}
$sourceFiles | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspace 'source-inventory.json') -Encoding UTF8
$build = Join-Path $workspace 'build'
$recipe = Join-Path $PSScriptRoot $recipeName
$configure = @('-S', $recipe, '-B', $build, '-G', 'Visual Studio 18 2026', '-A', 'x64',
    "-DCMAKE_GENERATOR_INSTANCE=$visualStudio", '-DCMAKE_SYSTEM_VERSION=10.0.26100.0',
    '-DCMAKE_VS_GLOBALS=ImportDirectoryBuildProps=false;ImportDirectoryBuildTargets=false'
)
foreach ($id in $sourceIds) {
    $variable = if ($id -eq 'lame-stable') { 'LAME' } else { $id.ToUpperInvariant() }
    $configure += "-D${variable}_SOURCE=$($sources[$id])"
}
if ($Dependency -eq 'LameStable') { $configure += '-DLAME_STABLE=ON' }
if ($Dependency -eq 'Opus') { $configure += "-DOPUS_PACKAGE_VERSION=$($opus.packageVersion)" }

function Invoke-BuildStep([string] $Executable, [string[]] $Arguments, [string] $LogName) {
    $log = Join-Path $workspace $LogName
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Native executable is missing: $Executable" }
    # Windows PowerShell wraps native stderr as ErrorRecord even for diagnostics.
    $ErrorActionPreference = 'Continue'
    $global:LASTEXITCODE = $null
    & $Executable @Arguments > $log 2>&1
    $code = $global:LASTEXITCODE
    if ($null -eq $code -or $code -ne 0) { throw "Native step failed ($code). Inspect $log" }
}

Invoke-BuildStep $cmake $configure 'configure.log'
Invoke-BuildStep $cmake @('--build', $build, '--config', 'Release', '--', '/m:2',
    '/p:ImportDirectoryBuildProps=false', '/p:ImportDirectoryBuildTargets=false', '/verbosity:minimal') 'build.log'
$reviewedDiagnostics = @()
foreach ($diagnostic in @(Select-String -LiteralPath (Join-Path $workspace 'build.log') -Pattern '\bwarning [A-Z]+\d+:|\berror [A-Z]+\d+:')) {
    $knownNasmWarning = if ($Dependency -eq 'Nasm') {
        '^' + [regex]::Escape((Join-Path $sources['nasm'] 'output\outmacho.c')) + '\(1295,25\): warning C4319:'
    } else { $null }
    if ($knownNasmWarning -and $diagnostic.Line -match $knownNasmWarning -and $reviewedDiagnostics.Count -eq 0) {
        $reviewedDiagnostics += [ordered]@{ diagnostic=$diagnostic.Line;
            review='NASM 3.02 Mach-O relocation-offset alignment widens a 32-bit mask. Retained upstream warning; this build tool is tested for Win64 COFF, not Mach-O or offsets above 4 GiB.' }
    }
    else { throw 'Compiler diagnostics require review before accepting this dependency build.' }
}
Invoke-BuildStep $ctest @('--test-dir', $build, '-C', 'Release', '--timeout', '120', '--output-on-failure', '-j', '1') 'tests.log'
if (-not (Select-String -LiteralPath (Join-Path $workspace 'tests.log') -SimpleMatch "100% tests passed, 0 tests failed out of $testCount")) {
    throw "The expected $testCount dependency tests did not all pass."
}
if ($Dependency -eq 'Opus') { Invoke-BuildStep (Join-Path $build 'Release/ContextSuite.Opus.Version.exe') @() 'version.log' }
foreach ($file in $sourceFiles) {
    $path = Join-Path (Join-Path $workspace 'source') $file.path
    if ((Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw "Upstream source changed during the build: $($file.path)" }
}
if (@(Get-ChildItem -LiteralPath (Join-Path $workspace 'source') -Recurse -File).Count -ne $sourceFiles.Count) { throw 'The build added upstream source files.' }
$compilerFiles = @(Get-ChildItem -LiteralPath (Join-Path $build 'CMakeFiles') -Recurse -Filter CMakeCCompiler.cmake)
if ($compilerFiles.Count -ne 1) { throw 'Cannot identify the dependency compiler.' }
$compilerText = Get-Content -LiteralPath $compilerFiles[0].FullName -Raw
if ($compilerText -notmatch 'set\(CMAKE_C_COMPILER "([^"]+)"\)') { throw 'Compiler path not recorded.' }
$compilerPath = $Matches[1]
$inventory = @($artifacts | ForEach-Object {
    $path = Join-Path $build $_
    [ordered]@{ path=$_; bytes=(Get-Item -LiteralPath $path).Length; sha256=(Get-FileHash -LiteralPath $path).Hash }
})
[ordered]@{ status='Isolated dependency build; not production adoption'; dependency=$Dependency; inputs=@($pins | Where-Object { $_.id -in $sourceIds }); sourceFiles=$sourceFiles.Count; sourceInventorySha256=(Get-FileHash -LiteralPath (Join-Path $workspace 'source-inventory.json')).Hash;
    configureArguments=$configure; visualStudio=$visualStudio; windowsSdk='10.0.26100.0';
    compiler=$compilerPath; compilerSha256=(Get-FileHash -LiteralPath $compilerPath).Hash;
    cmake=$cmake; cmakeSha256=(Get-FileHash -LiteralPath $cmake).Hash;
    cacheSha256=(Get-FileHash -LiteralPath (Join-Path $build 'CMakeCache.txt')).Hash;
    testLogSha256=(Get-FileHash -LiteralPath (Join-Path $workspace 'tests.log')).Hash;
    recipeSha256=(Get-FileHash -LiteralPath (Join-Path $recipe 'CMakeLists.txt')).Hash;
    recipeFiles=@(Get-ChildItem -LiteralPath $recipe -Recurse -File | ForEach-Object { [ordered]@{ path=$_.FullName.Substring($recipe.Length + 1); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash } });
    scriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash; reviewedDiagnostics=$reviewedDiagnostics;
    sourceReaderSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Read-SourceTar.py')).Hash; artifacts=$inventory } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $workspace 'dependency-build.json') -Encoding UTF8
Write-Output "Verified $Dependency dependency build: $workspace"
