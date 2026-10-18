[CmdletBinding()]
param([switch] $AllowDirty, [string] $AudioDistributionDirectory,
    [string] $QpdfPreparedDirectory, [string] $PdfiumPreparedDirectory, [string] $QpdfSourceArchive,
    [string] $OfficeEngineDirectory, [string] $HeifDistributionDirectory, [string] $VoiceDistributionDirectory,
    [string] $WemDistributionDirectory, [string] $AvifBuildDirectory, [string] $AvifSourceDirectory,
    [string] $SvgBuildDirectory, [string] $SvgCargoHome, [string] $PdfDarkBuildDirectory)
$ErrorActionPreference = 'Stop'
function Remove-VerifiedArchiveExtraction([string] $Output, [string] $Extraction) {
    $outputPath = [IO.Path]::GetFullPath($Output).TrimEnd('\')
    $expected = [IO.Path]::GetFullPath((Join-Path $outputPath 'archive-verification'))
    $extractionPath = [IO.Path]::GetFullPath($Extraction)
    if (-not [string]::Equals($extractionPath, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Archive verification cleanup must target the extraction inside this candidate output.'
    }
    $path = $outputPath
    while ($true) {
        $item = Get-Item -LiteralPath $path -Force
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Unsafe archive verification cleanup directory: $path"
        }
        $parent = Split-Path -Path $path -Parent
        if (-not $parent -or [string]::Equals($parent, $path, [StringComparison]::OrdinalIgnoreCase)) { break }
        $path = $parent
    }
    $item = Get-Item -LiteralPath $extractionPath -Force
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Unsafe archive verification cleanup directory: $extractionPath"
    }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($extractionPath)
    while ($pending.Count) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Archive verification extraction contains a reparse point: $($item.FullName)"
            }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
    Remove-Item -LiteralPath $extractionPath -Recurse -Force
}
function Complete-ArchiveVerification([string] $Output, [string] $Extraction,
    [string] $Archive, [string] $Verifier, [Text.Encoding] $Encoding) {
    & $Verifier -Candidate (Join-Path $Extraction 'ContextSuite') | Out-Host
    if (-not (Test-Path -LiteralPath $Archive -PathType Leaf)) { throw 'Candidate archive is missing.' }
    $archiveHash = (Get-FileHash -LiteralPath $Archive -ErrorAction Stop).Hash
    if ($archiveHash -notmatch '^[a-fA-F0-9]{64}$') { throw 'Cannot hash candidate archive.' }
    [IO.File]::WriteAllText((Join-Path $Output 'archive.sha256'), "$archiveHash  ContextSuite-win-x64-unsigned.zip`n", $Encoding)
    Remove-VerifiedArchiveExtraction -Output $Output -Extraction $Extraction
    return $archiveHash
}
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$revisions = @()
$dirty = $false
foreach ($repo in @($repository, (Join-Path $repository 'proprietary'))) {
    $revision = & git -C $repo rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[a-f0-9]{40}$') { throw 'Both source revisions are required.' }
    $changes = @(& git -C $repo status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source status.' }
    $dirty = $dirty -or $changes.Count -ne 0
    $revisions += $revision
}
if ($dirty -and -not $AllowDirty) { throw 'Candidate requires clean public/private checkouts. -AllowDirty is local verification only and records dirty provenance.' }
# Reuse DDS only when its binary, source, build definition and notices agree.
# Rebuild explicitly with Build-DdsEngine.ps1 when that check reports stale inputs.
# Managed application and shell output still come from a fresh production stage.
$stagingId = [guid]::NewGuid()
& (Join-Path $repository 'tools\dds-engine\Test-DdsEngine.ps1') -Payload (Join-Path $repository 'artifacts\engines\dds-win-x64')
$buildArguments = @{ Configuration = 'Release'; StagingId = $stagingId }
foreach ($name in 'AudioDistributionDirectory', 'QpdfPreparedDirectory', 'PdfiumPreparedDirectory',
    'QpdfSourceArchive', 'OfficeEngineDirectory', 'HeifDistributionDirectory', 'VoiceDistributionDirectory',
    'WemDistributionDirectory', 'AvifBuildDirectory', 'AvifSourceDirectory', 'SvgBuildDirectory', 'SvgCargoHome',
    'PdfDarkBuildDirectory') {
    if ($PSBoundParameters.ContainsKey($name)) { $buildArguments[$name] = $PSBoundParameters[$name] }
}
& (Join-Path $repository 'tools\Build-Production.ps1') @buildArguments
for ($index = 0; $index -lt 2; $index++) {
    $repo = @($repository, (Join-Path $repository 'proprietary'))[$index]
    if ((& git -C $repo rev-parse HEAD) -ne $revisions[$index] -or
        (-not $dirty -and @(& git -C $repo status --porcelain=v1 --untracked-files=all).Count)) {
        throw 'Source revisions or clean state changed during the build; discard this attempt and rebuild.'
    }
}
$source = Join-Path $repository ('artifacts\production-staging\' + $stagingId.ToString('N'))
$output = Join-Path $repository ('artifacts\release-candidates\' + [guid]::NewGuid().ToString('N'))
$candidate = Join-Path $output 'ContextSuite'
$app = Join-Path $candidate 'app'
New-Item -ItemType Directory -Path $app -Force | Out-Null
foreach ($directory in Get-ChildItem -LiteralPath $source -Recurse -Directory) {
    New-Item -ItemType Directory -Path (Join-Path $app $directory.FullName.Substring($source.Length + 1)) -Force | Out-Null
}
foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
    if ($file.Extension -eq '.pdb' -or $file.Name -eq 'payload-inventory.json') { continue }
    $destination = Join-Path $app $file.FullName.Substring($source.Length + 1)
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-Prerequisites.ps1') -Destination $candidate
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vcVersion = (Get-Content -LiteralPath (Join-Path $vs 'VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
$ddsIdentity = Get-Content -LiteralPath (Join-Path $app 'ContextSuite.Dds.Engine.json') -Raw | ConvertFrom-Json
if ($ddsIdentity.minimumVisualCppRuntime -notmatch '^14\.\d+\.\d+\.\d+$') { throw 'DDS runtime prerequisite evidence is missing.' }
$requirements = [ordered]@{
    minimumWindowsBuild = 26100; architecture = 'x64'; deployment = 'framework-dependent';
    desktopRuntime = 'Microsoft.WindowsDesktop.App 10.0.x x64'; minimumVisualCppRuntime = $ddsIdentity.minimumVisualCppRuntime;
    desktopRuntimeUrl = 'https://dotnet.microsoft.com/en-us/download/dotnet/10.0';
    visualCppRuntimeUrl = 'https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist';
    installer = 'Not included. No shell registration or installation is performed by this candidate.'
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $candidate 'requirements.json'), ($requirements | ConvertTo-Json), $utf8)
$files = @(Get-ChildItem -LiteralPath $candidate -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($candidate.Length + 1).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
$manifest = [ordered]@{
    schema = 1; status = 'unsigned-internal-candidate'; publicCommit = $revisions[0]; privateCommit = $revisions[1];
    dirtySources = $dirty; dotnetSdk = (& dotnet --version); visualCppToolset = $vcVersion;
    configuration = 'Release'; productionStagingId = $stagingId.ToString('N');
    optionalEngines = (& (Join-Path $PSScriptRoot 'Get-CandidateEngines.ps1') -Payload $app);
    dependencies = (Get-Content -LiteralPath (Join-Path $app 'ContextSuite.Worker.deps.json') -Raw | ConvertFrom-Json).libraries;
    applicationDependencies = (Get-Content -LiteralPath (Join-Path $app 'ContextSuite.Application.deps.json') -Raw | ConvertFrom-Json).libraries;
    curatedEngine = (Get-Content -LiteralPath (Join-Path $app 'ContextSuite.Engine.json') -Raw | ConvertFrom-Json);
    ddsEngine = (Get-Content -LiteralPath (Join-Path $app 'ContextSuite.Dds.Engine.json') -Raw | ConvertFrom-Json);
    pngEngine = (Get-Content -LiteralPath (Join-Path $app 'ContextSuite.Png.Engine.json') -Raw | ConvertFrom-Json);
    paletteEngine = (Get-Content -LiteralPath (Join-Path $app 'ContextSuite.Palette.Engine.json') -Raw | ConvertFrom-Json);
    files = $files
}
[IO.File]::WriteAllText((Join-Path $candidate 'release-manifest.json'), ($manifest | ConvertTo-Json -Depth 12), $utf8)
& (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $candidate
$archive = Join-Path $output 'ContextSuite-win-x64-unsigned.zip'
Compress-Archive -LiteralPath $candidate -DestinationPath $archive
$roundtrip = Join-Path $output 'archive-verification'
Expand-Archive -LiteralPath $archive -DestinationPath $roundtrip
$archiveHash = Complete-ArchiveVerification -Output $output -Extraction $roundtrip -Archive $archive `
    -Verifier (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Encoding $utf8
Write-Output "Candidate archive: $archive"
Write-Output "SHA256: $archiveHash"
