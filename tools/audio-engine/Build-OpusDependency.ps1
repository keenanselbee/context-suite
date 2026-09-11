[CmdletBinding()]
param([Parameter(Mandatory)][string] $SourceDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
& (Join-Path $PSScriptRoot 'Prepare-AudioSources.ps1') -SourceDirectory $SourceDirectory -VerifyOnly
$pin = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-inputs.json') -Raw | ConvertFrom-Json).archives | Where-Object id -eq 'opus'
if ($pin.revision -ne '3da9f7a6db1c05c3996cb363a9d1931a978bf1be' -or
    $pin.packageVersion -ne '1.6.1-contextsuite-g3da9f7a6db1c') { throw 'Unsupported Opus source/version pin.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -or -not $visualStudio) { throw 'Existing Visual Studio x64 C++ build tools are required.' }
$cmakeDirectory = Join-Path $visualStudio 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin'
$cmake = Join-Path $cmakeDirectory 'cmake.exe'
$ctest = Join-Path $cmakeDirectory 'ctest.exe'
if (-not (Test-Path -LiteralPath $cmake) -or -not (Test-Path -LiteralPath $ctest)) { throw 'Existing Visual Studio CMake/CTest tools are required.' }
$workspace = Join-Path $repository ('.codex-temp/audio-opus-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace | Out-Null
Write-Output "Opus dependency evidence: $workspace"
$source = Join-Path $workspace 'source'
New-Item -ItemType Directory -Path $source | Out-Null
$archivePath = Join-Path ([IO.Path]::GetFullPath($SourceDirectory)) $pin.file
$stream = [IO.File]::Open($archivePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
$sourceFiles = @()
try {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $archiveHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
    finally { $hasher.Dispose() }
    if ($stream.Length -ne $pin.bytes -or $archiveHash -ne $pin.sha256) { throw 'Opus archive changed before extraction.' }
    $stream.Position = 0
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            if (-not $entry.FullName.StartsWith($pin.prefix, [StringComparison]::Ordinal)) { throw 'Unexpected Opus archive prefix.' }
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
            $sourceFiles += [ordered]@{ path=$relative; bytes=(Get-Item -LiteralPath $path).Length; sha256=(Get-FileHash -LiteralPath $path).Hash }
        }
    }
    finally { $zip.Dispose() }
}
finally { $stream.Dispose() }
$sourceFiles | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspace 'source-inventory.json') -Encoding UTF8
$build = Join-Path $workspace 'build'
$recipe = Join-Path $PSScriptRoot 'opus'
$configure = @('-S', $recipe, '-B', $build, '-G', 'Visual Studio 18 2026', '-A', 'x64',
    "-DCMAKE_GENERATOR_INSTANCE=$visualStudio", '-DCMAKE_SYSTEM_VERSION=10.0.26100.0',
    '-DCMAKE_VS_GLOBALS=ImportDirectoryBuildProps=false;ImportDirectoryBuildTargets=false',
    "-DOPUS_SOURCE=$source", "-DOPUS_PACKAGE_VERSION=$($pin.packageVersion)")

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
if (Select-String -LiteralPath (Join-Path $workspace 'build.log') -Pattern '\bwarning [A-Z]+\d+:|\berror [A-Z]+\d+:') {
    throw 'Compiler diagnostics require review before accepting this dependency build.'
}
Invoke-BuildStep $ctest @('--test-dir', $build, '-C', 'Release', '--timeout', '120', '--output-on-failure', '-j', '1') 'tests.log'
if (-not (Select-String -LiteralPath (Join-Path $workspace 'tests.log') -SimpleMatch '100% tests passed, 0 tests failed out of 6')) {
    throw 'The expected five codec tests and linked-version test did not all pass.'
}
Invoke-BuildStep (Join-Path $build 'Release/ContextSuite.Opus.Version.exe') @() 'version.log'
foreach ($file in $sourceFiles) {
    $path = Join-Path $source $file.path
    if ((Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw "Upstream source changed during the build: $($file.path)" }
}
if (@(Get-ChildItem -LiteralPath $source -Recurse -File).Count -ne $sourceFiles.Count) { throw 'The build added upstream source files.' }
$compilerFiles = @(Get-ChildItem -LiteralPath (Join-Path $build 'CMakeFiles') -Recurse -Filter CMakeCCompiler.cmake)
if ($compilerFiles.Count -ne 1) { throw 'Cannot identify the dependency compiler.' }
$compilerText = Get-Content -LiteralPath $compilerFiles[0].FullName -Raw
if ($compilerText -notmatch 'set\(CMAKE_C_COMPILER "([^"]+)"\)') { throw 'Compiler path not recorded.' }
$compilerPath = $Matches[1]
$artifacts = @('opus/Release/opus.lib', 'Release/ContextSuite.Opus.Version.exe') +
    @('test_opus_decode', 'test_opus_padding', 'test_opus_api', 'test_opus_encode', 'test_opus_extensions' | ForEach-Object { "opus/Release/$_.exe" })
$inventory = @($artifacts | ForEach-Object {
    $path = Join-Path $build $_
    [ordered]@{ path=$_; bytes=(Get-Item -LiteralPath $path).Length; sha256=(Get-FileHash -LiteralPath $path).Hash }
})
[ordered]@{ status='Isolated dependency build; not production adoption'; revision=$pin.revision; packageVersion=$pin.packageVersion;
    archiveSha256=$archiveHash; sourceFiles=$sourceFiles.Count; sourceInventorySha256=(Get-FileHash -LiteralPath (Join-Path $workspace 'source-inventory.json')).Hash;
    configureArguments=$configure; visualStudio=$visualStudio; windowsSdk='10.0.26100.0';
    compiler=$compilerPath; compilerSha256=(Get-FileHash -LiteralPath $compilerPath).Hash;
    cmake=$cmake; cmakeSha256=(Get-FileHash -LiteralPath $cmake).Hash;
    cacheSha256=(Get-FileHash -LiteralPath (Join-Path $build 'CMakeCache.txt')).Hash;
    testLogSha256=(Get-FileHash -LiteralPath (Join-Path $workspace 'tests.log')).Hash;
    recipeSha256=(Get-FileHash -LiteralPath (Join-Path $recipe 'CMakeLists.txt')).Hash;
    versionCheckSha256=(Get-FileHash -LiteralPath (Join-Path $recipe 'Version.c')).Hash;
    scriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash; artifacts=$inventory } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $workspace 'dependency-build.json') -Encoding UTF8
Write-Output "Verified Opus dependency build: $workspace"
