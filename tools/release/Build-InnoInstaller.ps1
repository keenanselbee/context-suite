[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Candidate,
    [Parameter(Mandatory)][string] $IdentityDirectory,
    [switch] $UnsignedInternal
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
& (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $Candidate
$candidateRoot = (Resolve-Path -LiteralPath $Candidate).Path
$identityRoot = (Resolve-Path -LiteralPath $IdentityDirectory).Path
$identity = Get-Content -LiteralPath (Join-Path $identityRoot 'identity-candidates.json') -Raw | ConvertFrom-Json
if ($identity.candidateManifestSha256 -ne (Get-FileHash -LiteralPath (Join-Path $candidateRoot 'release-manifest.json')).Hash -or
    $identity.version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or @($identity.packages).Count -ne 3) {
    throw 'Identity receipt does not match the release candidate or three-package contract.'
}
if (($identity.version.Split('.') | Where-Object { [int]$_ -gt 65535 }).Count) { throw 'Invalid package version.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$toolsSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$prefixes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($package in $identity.packages) {
    if ($package.name -notmatch '^([a-zA-Z0-9][a-zA-Z0-9.-]{2,45})\.(Analyze|Convert|Optimize)$' -or
        -not $names.Add($package.name) -or $package.file -cne ($package.name + '.msix')) {
        throw 'Unsafe or duplicate identity package name.'
    }
    $prefixes.Add($Matches[1]) | Out-Null
    if (-not $toolsSeen.Add($Matches[2])) { throw 'Duplicate Explorer tool.' }
    $path = Join-Path $identityRoot $package.file
    if ((Get-FileHash -LiteralPath $path).Hash -ne $package.sha256) { throw 'Identity package hash mismatch.' }
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entry = @($zip.Entries | Where-Object FullName -CEQ 'AppxManifest.xml')
        if ($entry.Count -ne 1 -or $entry[0].Length -gt 65536) { throw 'Invalid identity manifest.' }
        $reader = [IO.StreamReader]::new($entry[0].Open())
        try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($manifest.Package.Identity.Name -cne $package.name -or
            $manifest.Package.Identity.Publisher -cne $identity.publisher -or
            $manifest.Package.Identity.Version -cne $identity.version -or
            $manifest.Package.Identity.ProcessorArchitecture -cne 'x64' -or
            $manifest.Package.Applications.Application.Executable -cne 'ContextSuite.Application.exe') {
            throw 'Identity manifest differs from receipt or production application.'
        }
    } finally { $zip.Dispose() }
    if (-not $UnsignedInternal) {
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -cne $identity.publisher) {
            throw 'Trusted signed identities required. -UnsignedInternal only compiles a non-installable internal artifact.'
        }
    }
}
if ($prefixes.Count -ne 1) { throw 'All three tools must share one identity prefix.' }
$pins = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-inputs.json') -Raw | ConvertFrom-Json
$inputs = Join-Path $repository 'artifacts\installer-inputs'
$compiler = Join-Path $inputs ('inno-' + $pins.inno.version + '\ISCC.exe')
if ((Get-FileHash -LiteralPath $compiler).Hash -ne $pins.inno.compilerSha256 -or
    (Get-FileHash -LiteralPath (Join-Path (Split-Path $compiler -Parent) 'ISCmplr.dll')).Hash -ne $pins.inno.compilerEngineSha256 -or
    (Get-AuthenticodeSignature -LiteralPath $compiler).Status -ne 'Valid') { throw 'Verified Inno Setup 7.1.0 compiler required.' }
foreach ($input in @($pins.desktopRuntime, $pins.visualCppRuntime)) {
    $path = Join-Path $inputs $input.file
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ((Get-FileHash -LiteralPath $path -Algorithm $input.algorithm).Hash -ne $input.hash -or
        $signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -ne $input.signer) {
        throw 'Verified pinned offline runtime inputs required; run Get-InstallerInputs.ps1.'
    }
}
$requirements = Get-Content -LiteralPath (Join-Path $candidateRoot 'requirements.json') -Raw | ConvertFrom-Json
if ([version]$pins.visualCppRuntime.version -lt [version]$requirements.minimumVisualCppRuntime) {
    throw 'Bundled Visual C++ runtime is older than the DDS toolset requirement.'
}
$output = Join-Path $repository ('artifacts\installer-candidates\' + [guid]::NewGuid().ToString('N'))
$staging = Join-Path $output 'source'
$installation = Join-Path $staging 'payload\installation'
$runtimes = Join-Path $staging 'payload\runtimes'
$releaseDirectory = Join-Path $staging 'payload\release'
New-Item -ItemType Directory -Path $installation, $runtimes, (Join-Path $releaseDirectory 'packages') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $candidateRoot 'app') -Destination $releaseDirectory -Recurse
$candidateManifest = Get-Content -LiteralPath (Join-Path $candidateRoot 'release-manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $candidateManifest.files | Where-Object { $_.path.StartsWith('app/') }) {
    $copy = Join-Path $releaseDirectory $entry.path
    if ((Get-FileHash -LiteralPath $copy).Hash -ne $entry.sha256) { throw 'Application changed while staging installer.' }
}
foreach ($name in 'ContextSuite.iss', 'Invoke-InstallerAction.ps1', 'ShellRegistration.ps1',
    'InstallationRecovery.ps1', 'InstallerLifecycle.ps1', 'Launch-Active.ps1') {
    $destination = if ($name -like '*.iss') { $staging } else { $installation }
    Copy-Item -LiteralPath (Join-Path $repository "packaging\inno\$name") -Destination $destination
}
foreach ($name in 'requirements.json', 'Test-Prerequisites.ps1', 'release-manifest.json') {
    Copy-Item -LiteralPath (Join-Path $candidateRoot $name) -Destination $installation
}
# Installer helpers are separately inventoried from the original application candidate.
# Use the current component-aware preflight even when wrapping an older candidate.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-Prerequisites.ps1') -Destination $installation -Force
foreach ($package in $identity.packages) { Copy-Item -LiteralPath (Join-Path $identityRoot $package.file) -Destination $installation }
foreach ($package in $identity.packages) { Copy-Item -LiteralPath (Join-Path $identityRoot $package.file) -Destination (Join-Path $releaseDirectory 'packages') }
Copy-Item -LiteralPath (Join-Path $candidateRoot 'app\Assets\Analyze.ico') -Destination $installation
foreach ($input in @($pins.desktopRuntime, $pins.visualCppRuntime)) {
    Copy-Item -LiteralPath (Join-Path $inputs $input.file) -Destination $runtimes
}
$utf8 = [Text.UTF8Encoding]::new($false)
$release = [ordered]@{
    schema = 1; id = [guid]::NewGuid().ToString('N'); publisher = $identity.publisher; version = $identity.version;
    packages = @($identity.packages | ForEach-Object {
        [ordered]@{ name = $_.name; file = 'packages/' + $_.file; sha256 = $_.sha256 }
    });
    files = @(Get-ChildItem -LiteralPath $releaseDirectory -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($releaseDirectory.Length + 1).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
}
[IO.File]::WriteAllText((Join-Path $releaseDirectory 'release.json'), ($release | ConvertTo-Json -Depth 12), $utf8)
$identity | Add-Member -NotePropertyName release -NotePropertyValue $release
[IO.File]::WriteAllText((Join-Path $installation 'installer.json'), ($identity | ConvertTo-Json -Depth 12), $utf8)
# Output path is generated under this repository, not accepted from external metadata.
if ($output -match '["\r\n]') { throw 'Unsupported build path.' }
$configuration = @(
    '#define SuiteVersion "' + $identity.version + '"'
    '#define OutputDirectory "' + $output + '"'
    '#define DesktopRuntimeFile "' + $pins.desktopRuntime.file + '"'
)
[IO.File]::WriteAllText((Join-Path $staging 'BuildConfig.iss'), ($configuration -join "`r`n"), $utf8)
& $compiler --no-ide-signtools (Join-Path $staging 'ContextSuite.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno compilation failed.' }
$installer = Join-Path $output "ContextSuite-$($identity.version)-win-x64-internal.exe"
$receipt = [ordered]@{
    schema = 1; status = 'unsigned-internal-lifecycle-candidate'; nativeUpgradeAdmission = $false;
    candidateManifestSha256 = $identity.candidateManifestSha256;
    identityReceiptSha256 = (Get-FileHash -LiteralPath (Join-Path $identityRoot 'identity-candidates.json')).Hash;
    unsignedIdentityInputs = [bool]$UnsignedInternal; innoVersion = $pins.inno.version;
    installerSha256 = (Get-FileHash -LiteralPath $installer).Hash; inputs = $pins;
    sourceFiles = @(Get-ChildItem -LiteralPath $staging -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($staging.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
}
[IO.File]::WriteAllText((Join-Path $output 'installer-receipt.json'), ($receipt | ConvertTo-Json -Depth 10), $utf8)
Write-Output "Internal installer compiled: $installer"
Write-Output 'Not installed, signed or approved for distribution. Unsigned identities are rejected at install time; upgrades remain gated.'
