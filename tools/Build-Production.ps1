[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug', [switch] $SkipShell,
    [guid] $StagingId = [guid]::Empty, [string] $AudioDistributionDirectory,
    [string] $QpdfPreparedDirectory, [string] $PdfiumPreparedDirectory, [string] $QpdfSourceArchive)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$productVersion = & (Join-Path $PSScriptRoot 'Test-ProductVersion.ps1')
if ($AudioDistributionDirectory -and $StagingId -eq [guid]::Empty) {
    throw 'The audio candidate requires a new StagingId; development and installed payloads must not be changed.'
}
$includePdf = [bool]$QpdfPreparedDirectory -or [bool]$PdfiumPreparedDirectory -or [bool]$QpdfSourceArchive
if ($includePdf -and ($StagingId -eq [guid]::Empty -or -not $QpdfPreparedDirectory -or
    -not $PdfiumPreparedDirectory -or -not $QpdfSourceArchive)) {
    throw 'The PDF candidate requires a new StagingId, both prepared engines and the pinned qpdf source archive.'
}
# Candidate builds get a new, non-reusable staging directory. They must never
# refresh the development payload that Explorer may currently have registered.
$output = if ($StagingId -eq [guid]::Empty) { Join-Path $repositoryRoot "artifacts\production\$Configuration" }
    else { Join-Path $repositoryRoot ('artifacts\production-staging\' + $StagingId.ToString('N')) }
if ($StagingId -ne [guid]::Empty -and (Test-Path -LiteralPath $output)) {
    throw 'Production staging already exists. Use a new StagingId; existing payloads are never overwritten.'
}
if ($StagingId -ne [guid]::Empty) {
    $receipts = Join-Path $repositoryRoot 'artifacts\production-version-receipts'
    New-Item -ItemType Directory -Path $receipts -Force | Out-Null
    $versionReceipt = Join-Path $receipts ($productVersion + '.json')
    if (Test-Path -LiteralPath $versionReceipt) { throw "Product version $productVersion is already reserved. Advance Version.props and the package templates; preserve the existing receipt." }
    foreach ($previous in Get-ChildItem -LiteralPath $receipts -Filter '*.json' -File) {
        if ([version]$productVersion -le [version]$previous.BaseName) { throw "Product version $productVersion must be newer than reserved version $($previous.BaseName)." }
    }
    $reservation = @{ version = $productVersion; stagingId = $StagingId.ToString('N'); state = 'reserved'; payload = $output }
    # Reserve the version before creating any candidate files. Failed attempts
    # retain this receipt; use the next version rather than replacing evidence.
    try { $receiptStream = [IO.File]::Open($versionReceipt, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None) }
    catch {
        if (Test-Path -LiteralPath $versionReceipt) { throw "Product version $productVersion is already reserved. Advance Version.props and the package templates; preserve the existing receipt." }
        throw
    }
    try {
        $receiptBytes = [Text.UTF8Encoding]::new($false).GetBytes(($reservation | ConvertTo-Json))
        $receiptStream.Write($receiptBytes, 0, $receiptBytes.Length)
    } finally { $receiptStream.Dispose() }
    # Reserve before the build; concurrent requests for the same ID cannot both
    # pass the earlier existence check and then overwrite one another's output.
    New-Item -ItemType Directory -Path $output -ErrorAction Stop | Out-Null
}
if ($AudioDistributionDirectory) {
    & python -B (Join-Path $PSScriptRoot 'audio-engine\Stage-AudioPayload.py') --distribution $AudioDistributionDirectory --payload $output
    if ($LASTEXITCODE -ne 0) { throw 'Audio candidate staging failed.' }
}
if ($includePdf) {
    & python -B (Join-Path $PSScriptRoot 'pdf-engine\Stage-PdfPayload.py') --payload $output `
        --qpdf-directory $QpdfPreparedDirectory --pdfium-directory $PdfiumPreparedDirectory --qpdf-source $QpdfSourceArchive
    if ($LASTEXITCODE -ne 0) { throw 'PDF candidate staging failed.' }
}
$privateProject = Join-Path $repositoryRoot 'proprietary\src\ContextSuite.Private\ContextSuite.Private.csproj'
if (-not (Test-Path -LiteralPath $privateProject)) {
    throw 'Production requires the compatible context-suite-private repository at proprietary/. There is no public demo build.'
}
if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'proprietary\src\ContextSuite.Commercial\ApplicationComposition.cs'))) {
    throw 'Production requires the compatible context-suite-private commercial composition. Update the private checkout before building.'
}
& (Join-Path $PSScriptRoot 'curated-engine\Test-ProductionEngine.ps1') -Payload (Join-Path $repositoryRoot 'artifacts\engines\curated-win-x64')
& (Join-Path $PSScriptRoot 'dds-engine\Test-DdsEngine.ps1') -Payload (Join-Path $repositoryRoot 'artifacts\engines\dds-win-x64')
& (Join-Path $PSScriptRoot 'png-engine\Test-PngEngine.ps1') -Payload (Join-Path $repositoryRoot 'artifacts\engines\png-win-x64')
& (Join-Path $PSScriptRoot 'palette-engine\Test-PaletteEngine.ps1') -Payload (Join-Path $repositoryRoot 'artifacts\engines\palette-win-x64')
& dotnet build (Join-Path $repositoryRoot 'ContextSuite.Production.slnx') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Production foundation build failed.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$dotnetRoot = Split-Path (Get-Command dotnet -CommandType Application).Source -Parent
foreach ($entry in @(@('LICENSE.txt', 'DotNet.License.txt'), @('ThirdPartyNotices.txt', 'DotNet.ThirdPartyNotices.txt'))) {
    $noticeSource = Join-Path $dotnetRoot $entry[0]
    if (-not (Test-Path -LiteralPath $noticeSource)) { throw "Missing SDK distribution notice: $noticeSource" }
    Copy-Item -LiteralPath $noticeSource -Destination (Join-Path $output $entry[1]) -Force
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'assets\THIRD-PARTY-NOTICES.txt') -Destination $output -Force
foreach ($project in @('ContextSuite.Application', 'ContextSuite.Worker')) {
    $source = Join-Path $repositoryRoot "artifacts\managed\bin\$project\$Configuration\net10.0-windows"
    Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $output -Force
}
if (Test-Path -LiteralPath (Join-Path $output 'Magick.NET-Q16-x64.dll')) {
    foreach ($required in 'Magick.Native-Q16-x64.dll', 'Magick.NET.Notice.txt') {
        if (-not (Test-Path -LiteralPath (Join-Path $output $required))) {
            throw "Image engine deployment is incomplete: $required"
        }
    }
}
& (Join-Path $PSScriptRoot 'curated-engine\Test-ProductionEngine.ps1') -Payload $output
$appDependencies = Get-Content -LiteralPath (Join-Path $output 'ContextSuite.Application.deps.json') -Raw | ConvertFrom-Json
if (@($appDependencies.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^(Magick\.|ContextSuite\.Private/)' }).Count) {
    throw 'The WPF application must not depend on private image-engine assemblies. The worker owns those dependencies.'
}
if (Get-ChildItem -LiteralPath $output -Filter '*TestHost*' -File) {
    throw 'Test-only application hosts must not be included in production output.'
}
if (-not $SkipShell) {
    $native = if ($StagingId -eq [guid]::Empty) { Join-Path $repositoryRoot "artifacts\bin\x64\$Configuration" }
        else { Join-Path $repositoryRoot ('.codex-temp\native-staging-' + $StagingId.ToString('N')) }
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration -OutputDirectory $native
    Copy-Item -LiteralPath (Join-Path $native 'ContextSuite.Shell.dll') -Destination $output -Force
    & (Join-Path $PSScriptRoot 'New-PrototypeAssets.ps1') -OutputDirectory $output
}
& (Join-Path $PSScriptRoot 'curated-engine\Test-ProductionPayload.ps1') -Payload $output `
    -AllowAudioCandidate:([bool]$AudioDistributionDirectory) -AllowPdfCandidate:$includePdf
$verifiedVersion = & (Join-Path $PSScriptRoot 'Test-ProductVersion.ps1') -Payload $output
if ($verifiedVersion -ne $productVersion) { throw 'Product version changed during staging.' }
$inventory = @(Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Name -ne 'payload-inventory.json' | ForEach-Object {
    @{ path = $_.FullName.Substring($output.Length + 1); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
[IO.File]::WriteAllText((Join-Path $output 'payload-inventory.json'), ($inventory | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
if ($StagingId -ne [guid]::Empty) {
    $reservation.state = 'verified'
    $reservation.inventorySha256 = (Get-FileHash -LiteralPath (Join-Path $output 'payload-inventory.json')).Hash
    [IO.File]::WriteAllText($versionReceipt, ($reservation | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}
Write-Output "Built production foundation at $output with curated-engine verification and file inventory. No Explorer packages were installed."
