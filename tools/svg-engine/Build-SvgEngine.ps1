param(
    [Parameter(Mandatory = $true)]
    [string] $CargoHome
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$privateSource = Join-Path $repositoryRoot 'proprietary/src/ContextSuite.Svg.Native'
$noticeRoot = Join-Path $repositoryRoot 'assets/third-party/svg'
$inventoryPath = Join-Path $noticeRoot 'dependency-inventory.json'
$verificationScript = Join-Path $PSScriptRoot 'Verify-CargoSources.py'
$expectedLockHash = 'BBC04B683ED06294D73D66AB25E0CD00B350A8FAAB98BEA8DB1C41657804E166'
$rustTarget = 'x86_64-pc-windows-msvc'

if (-not (Test-Path -LiteralPath $privateSource -PathType Container)) {
    throw 'Private SVG source is unavailable. Do not use a stocked binary.'
}
if (-not (Test-Path -LiteralPath $CargoHome -PathType Container)) {
    throw 'Provide a prepopulated Cargo home with the locked official crates.'
}
$lockHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $privateSource 'Cargo.lock')).Hash
if ($lockHash -ne $expectedLockHash) { throw "Unreviewed SVG dependency lock: $lockHash" }
$manifest = Get-Content -LiteralPath (Join-Path $privateSource 'Cargo.toml') -Raw
if (-not $manifest.Contains('resvg = { version = "=0.48.1"') -or
    -not $manifest.Contains('base64 = "=0.23.1"') -or
    -not $manifest.Contains('png = "=0.18.1"') -or
    -not $manifest.Contains('zune-jpeg = "=0.5.9"') -or
    -not $manifest.Contains('svgtypes = "=0.16.1"') -or
    -not $manifest.Contains('log = "=0.4.34"')) {
    throw 'The pinned SVG dependency manifest changed.'
}
$inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
if ($inventory.Count -ne 63) { throw 'Expected license evidence for 63 dependencies.' }
foreach ($dependency in $inventory) {
    if (-not $dependency.crateChecksum -or -not $dependency.licenseExpression -or
        -not $dependency.source.StartsWith('registry+https://github.com/rust-lang/crates.io-index')) {
        throw "Incomplete dependency evidence: $($dependency.name)"
    }
    foreach ($notice in $dependency.licenseFiles) {
        $path = Join-Path $noticeRoot $notice.file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ne $notice.sha256) {
            throw "Missing or changed license notice: $($notice.file)"
        }
    }
}
$rustVersion = (& rustc --version)
if ($LASTEXITCODE -ne 0 -or $rustVersion -notmatch '^rustc 1\.98\.1 ') {
    throw "Expected Rust 1.98.1; found: $rustVersion"
}
$rustDetails = (& rustc -vV)
if ($LASTEXITCODE -ne 0 -or -not ($rustDetails -contains "host: $rustTarget")) {
    throw "Expected the $rustTarget Rust host toolchain."
}
$targetLibDir = (& rustc --print target-libdir --target $rustTarget)
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $targetLibDir -PathType Container)) {
    throw "The $rustTarget Rust standard library is unavailable."
}
$pythonVersion = (& python --version)
if ($LASTEXITCODE -ne 0 -or $pythonVersion -notmatch '^Python 3\.(1[1-9]|[2-9][0-9])\.') {
    throw "Python 3.11 or newer is required for crate source verification: $pythonVersion"
}
$buildRoot = Join-Path $repositoryRoot ('.codex-temp/svg-engine-build/' + [guid]::NewGuid().ToString('N'))
$sourceSnapshot = Join-Path $buildRoot 'source'
$target = Join-Path $buildRoot 'target'
New-Item -ItemType Directory -Path $sourceSnapshot, $target -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $privateSource 'Cargo.toml'), (Join-Path $privateSource 'Cargo.lock') -Destination $sourceSnapshot
Copy-Item -LiteralPath (Join-Path $privateSource 'src') -Destination (Join-Path $sourceSnapshot 'src') -Recurse
$sourceFacts = @{}
foreach ($name in @('Cargo.toml', 'Cargo.lock', 'src/main.rs')) {
    $sourceFacts[$name] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $sourceSnapshot $name)).Hash
}

$env:CARGO_HOME = (Resolve-Path -LiteralPath $CargoHome).Path
$env:CARGO_TARGET_DIR = $target
$env:RUSTFLAGS = '-C target-feature=+crt-static'
$sourceVerificationPath = Join-Path $buildRoot 'source-verification.json'
& python $verificationScript --lock (Join-Path $sourceSnapshot 'Cargo.lock') --inventory $inventoryPath --cargo-home $env:CARGO_HOME --output $sourceVerificationPath
if ($LASTEXITCODE -ne 0) { throw 'Cached crate archive or extracted source verification failed.' }
$sourceVerification = Get-Content -LiteralPath $sourceVerificationPath -Raw | ConvertFrom-Json
if ($sourceVerification.DependencyCount -ne 63 -or $sourceVerification.LockSha256.ToUpperInvariant() -ne $lockHash) {
    throw 'Crate verification receipt differs from the source lockfile.'
}
$metadata = (& cargo metadata --offline --locked --format-version 1 --manifest-path (Join-Path $sourceSnapshot 'Cargo.toml') | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $metadata.packages.Count -ne 64) {
    throw 'Offline Cargo metadata did not resolve the 63 locked dependencies.'
}
$lockedDependencies = @($metadata.packages | Where-Object { $_.name -ne 'context-suite-svg' })
foreach ($dependency in $lockedDependencies) {
    $match = @($inventory | Where-Object { $_.name -eq $dependency.name -and $_.version -eq $dependency.version })
    $expectedSourceSuffix = "\$($dependency.name)-$($dependency.version)\Cargo.toml"
    if ($match.Count -ne 1 -or
        $dependency.source -ne 'registry+https://github.com/rust-lang/crates.io-index' -or
        -not $dependency.manifest_path.StartsWith((Join-Path $env:CARGO_HOME 'registry\src\index.crates.io-')) -or
        -not $dependency.manifest_path.EndsWith($expectedSourceSuffix)) {
        throw "Dependency differs from reviewed inventory: $($dependency.name) $($dependency.version)"
    }
}
& cargo build --offline --locked --release --manifest-path (Join-Path $sourceSnapshot 'Cargo.toml')
if ($LASTEXITCODE -ne 0) { throw 'Fresh offline SVG build failed.' }
foreach ($name in $sourceFacts.Keys) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $sourceSnapshot $name)).Hash -ne $sourceFacts[$name]) {
        throw "SVG source changed during build: $name"
    }
}
$nativeOutput = Join-Path $target 'release/context-suite-svg.exe'
if (-not (Test-Path -LiteralPath $nativeOutput -PathType Leaf)) { throw 'Fresh native output is missing.' }
$output = Join-Path $buildRoot 'ContextSuite.Svg.exe'
Copy-Item -LiteralPath $nativeOutput -Destination $output
$result = [ordered]@{
    Executable = $output
    Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $output).Hash
    Target = $rustTarget
    RustFlags = $env:RUSTFLAGS
    RustVersion = $rustVersion
    CargoVersion = (& cargo --version)
    PythonVersion = $pythonVersion
    LockSha256 = $lockHash
    InventorySha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $inventoryPath).Hash
    SourceSha256 = $sourceFacts
    DependencyCount = $lockedDependencies.Count
    ExtractedCrateFileCount = $sourceVerification.ExtractedFileCount
    SourceVerificationSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourceVerificationPath).Hash
    SourceVerifierSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $verificationScript).Hash
    BuildScriptSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $PSCommandPath).Hash
}
$provenancePath = Join-Path $buildRoot 'build-provenance.json'
$json = $result | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($provenancePath, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
$result['Provenance'] = $provenancePath
$result | ConvertTo-Json -Depth 5
