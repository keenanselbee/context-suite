[CmdletBinding()]
param([string] $Candidate)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scratch = Join-Path $repository ('.codex-temp\release-packaging\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$script:passed = 0
function Expect-Rejection([scriptblock] $Action, [string] $Name) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Expected rejection: $Name" }
    $script:passed++
    Write-Output "PASS: $Name"
}
# Exercise real identity validation independently of native engine availability.
$identityTokens = $null
$identityErrors = $null
$identityAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1'), [ref]$identityTokens, [ref]$identityErrors)
if ($identityErrors.Count) { throw 'Candidate verifier has parse errors.' }
$identityFunction = @($identityAst.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-ReleaseIdentity' }, $true))
if ($identityFunction.Count -ne 1) { throw 'Expected one release identity validator.' }
Invoke-Expression $identityFunction[0].Extent.Text
$sha = 'a' * 40
if (Assert-ReleaseIdentity ([pscustomobject]@{ schema=2; status='unsigned-internal-candidate'; sourceCommit=$sha })) { throw 'Free source identity was classified as legacy.' }
$script:passed++
if (-not (Assert-ReleaseIdentity ([pscustomobject]@{ schema=1; status='unsigned-internal-candidate'; publicCommit=$sha; privateCommit=$sha }))) { throw 'Historical dual-source identity was rejected.' }
$script:passed++
foreach ($identity in @(
    @{ schema=2; status='unsigned-internal-candidate' },
    @{ schema=2; status='unsigned-internal-candidate'; sourceCommit='main' },
    @{ schema=2; status='unsigned-internal-candidate'; sourceCommit=$sha; privateCommit=$sha },
    @{ schema=2; status='unsigned-internal-candidate'; sourceCommit=$sha; publicCommit=$sha },
    @{ schema=1; status='unsigned-internal-candidate'; publicCommit=$sha },
    @{ schema=3; status='unsigned-internal-candidate'; sourceCommit=$sha },
    @{ schema=2; status='unverified'; sourceCommit=$sha },
    @{ schema=2; status='unsigned-internal-candidate'; sourceCommit=@($sha, $sha) },
    @{ schema=1; status='unsigned-internal-candidate'; publicCommit=@($sha); privateCommit=$sha },
    @{ schema=1; status='unsigned-internal-candidate'; publicCommit=$sha; privateCommit=@($sha) },
    @{ schema='2'; status='unsigned-internal-candidate'; sourceCommit=$sha },
    @{ schema='1'; status='unsigned-internal-candidate'; publicCommit=$sha; privateCommit=$sha },
    @{ schema=@(2); status='unsigned-internal-candidate'; sourceCommit=$sha },
    @{ schema=2.0; status='unsigned-internal-candidate'; sourceCommit=$sha },
    @{ schema=2; status=@('unsigned-internal-candidate'); sourceCommit=$sha }
)) { Expect-Rejection { Assert-ReleaseIdentity ([pscustomobject]$identity) } 'malformed release source identity' }
$termsPayload = Join-Path $scratch 'product-terms'
New-Item -ItemType Directory -Path $termsPayload | Out-Null
$termsVerifier = Join-Path $repository 'tools/Test-ProductTerms.ps1'
Expect-Rejection { & $termsVerifier -Payload $termsPayload } 'missing product terms'
$termsFile = Join-Path $termsPayload 'ContextSuite.License.md'
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE.md') -Destination $termsFile
& $termsVerifier -Payload $termsPayload
$script:passed++
[IO.File]::AppendAllText($termsFile, 'altered disposable terms')
Expect-Rejection { & $termsVerifier -Payload $termsPayload } 'changed product terms body'
function New-Archive([string] $Name, [string[]] $Entries) {
    $path = Join-Path $scratch $Name
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $Entries) {
            $stream = $zip.CreateEntry($name).Open()
            try { $stream.WriteByte(1) } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
    return $path
}
$import = Join-Path $repository 'tools\curated-engine\Import-ProductionEngine.ps1'
foreach ($case in @(
    @{ name='missing'; entries=@('Magick.Native-Q16-x64.dll') },
    @{ name='duplicate'; entries=@('Magick.Native-Q16-x64.dll','Magick.Native-Q16-x64.dll') },
    @{ name='traversal'; entries=@('../Magick.Native-Q16-x64.dll','Magick.NET.Notice.txt') },
    @{ name='alternate-case'; entries=@('magick.native-q16-x64.dll','Magick.NET.Notice.txt') },
    @{ name='extra'; entries=@('Magick.Native-Q16-x64.dll','Magick.NET.Notice.txt','secret.txt') },
    @{ name='wrong-hash'; entries=@('Magick.Native-Q16-x64.dll','Magick.NET.Notice.txt') }
)) {
    $archive = New-Archive ($case.name + '.zip') $case.entries
    Expect-Rejection { & $import -Archive $archive -ValidateOnly } $case.name
}
# Historical DDS definitions remain accepted only by the explicit legacy path.
$ddsFixture = Join-Path $scratch 'legacy-dds'
New-Item -ItemType Directory -Path $ddsFixture | Out-Null
foreach ($name in 'ContextSuite.Dds.Native.dll', 'ContextSuite.Dds.Engine.json', 'DirectXTex.License.txt') {
    Copy-Item -LiteralPath (Join-Path $repository "artifacts/engines/dds-win-x64/$name") -Destination $ddsFixture
}
$ddsReceipt = Join-Path $ddsFixture 'ContextSuite.Dds.Engine.json'
$ddsIdentity = Get-Content -LiteralPath $ddsReceipt -Raw | ConvertFrom-Json
$ddsIdentity.buildDefinitionSha256 = 'DB154FAF0C020DD50DD669CDB8D990E388B3FBC5D95BB19BBA3618D030B7492A'
[IO.File]::WriteAllText($ddsReceipt, ($ddsIdentity | ConvertTo-Json))
$ddsVerifier = Join-Path $repository 'tools/dds-engine/Test-DdsEngine.ps1'
& $ddsVerifier -Payload $ddsFixture -AllowLegacyBuildDefinition
$script:passed++
Expect-Rejection { & $ddsVerifier -Payload $ddsFixture } 'historical DDS definition in a current free build'
$ddsIdentity.buildDefinitionSha256 = '0' * 64
[IO.File]::WriteAllText($ddsReceipt, ($ddsIdentity | ConvertTo-Json))
Expect-Rejection { & $ddsVerifier -Payload $ddsFixture -AllowLegacyBuildDefinition } 'unreviewed DDS definition in historical compatibility mode'
$engine = Join-Path $repository 'artifacts\engines\curated-win-x64'
$valid = Join-Path $scratch 'valid.zip'
Compress-Archive -LiteralPath (Join-Path $engine 'Magick.Native-Q16-x64.dll'), (Join-Path $engine 'Magick.NET.Notice.txt') -DestinationPath $valid
& $import -Archive $valid -ValidateOnly
$script:passed++
$engineSelection = Join-Path $PSScriptRoot 'Get-CandidateEngines.ps1'
$engineFixture = Join-Path $scratch 'image-engine-selection'
New-Item -ItemType Directory -Path $engineFixture | Out-Null
if ((& $engineSelection -Payload $engineFixture).imageExpansion) { throw 'Base candidate unexpectedly selects AVIF/SVG.' }
$script:passed++
New-Item -ItemType Directory -Path (Join-Path $engineFixture 'avif') | Out-Null
Expect-Rejection { & $engineSelection -Payload $engineFixture } 'AVIF without SVG'
Remove-Item -LiteralPath (Join-Path $engineFixture 'avif')
New-Item -ItemType Directory -Path (Join-Path $engineFixture 'svg') | Out-Null
Expect-Rejection { & $engineSelection -Payload $engineFixture } 'SVG without AVIF'
New-Item -ItemType Directory -Path (Join-Path $engineFixture 'avif') | Out-Null
Expect-Rejection { & $engineSelection -Payload $engineFixture } 'AVIF without HEIF decoder'
New-Item -ItemType Directory -Path (Join-Path $engineFixture 'heif') | Out-Null
if (-not (& $engineSelection -Payload $engineFixture).imageExpansion) { throw 'Complete image expansion candidate was not selected.' }
$script:passed++
if ($Candidate) {
    & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $Candidate
    $script:passed++
    $candidateManifest = Get-Content -LiteralPath (Join-Path $Candidate 'release-manifest.json') -Raw | ConvertFrom-Json
    if ($candidateManifest.productionStagingId) {
        $stageId = [guid]::ParseExact($candidateManifest.productionStagingId, 'N')
        if (Test-Path -LiteralPath (Join-Path $repository ('artifacts\production-staging\' + $stageId.ToString('N')))) {
            Expect-Rejection { & (Join-Path $repository 'tools\Build-Production.ps1') -StagingId $stageId } 'existing isolated staging cannot be overwritten'
        }
    }
    $copy = Join-Path $scratch 'candidate'
    Copy-Item -LiteralPath $Candidate -Destination $copy -Recurse
    $sentinel = Join-Path $copy 'app\unexpected.pdb'
    [IO.File]::WriteAllText($sentinel, 'disposable test material')
    Expect-Rejection { & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $copy } 'extra candidate file'
    Remove-Item -LiteralPath $sentinel
    $notice = Join-Path $copy 'app\THIRD-PARTY-NOTICES.txt'
    [IO.File]::AppendAllText($notice, 'altered disposable notice')
    Expect-Rejection { & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $copy } 'changed candidate hash'
    Copy-Item -LiteralPath (Join-Path $Candidate 'app\THIRD-PARTY-NOTICES.txt') -Destination $notice -Force
    $worker = Join-Path $copy 'app\ContextSuite.Worker.exe'
    Remove-Item -LiteralPath $worker
    Expect-Rejection { & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $copy } 'missing worker'
    Copy-Item -LiteralPath (Join-Path $Candidate 'app\ContextSuite.Worker.exe') -Destination $worker
    $manifestPath = Join-Path $copy 'release-manifest.json'
    $implementation = Join-Path $copy 'app\ContextSuite.Private.dll'
    Remove-Item -LiteralPath $implementation
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.files = @($manifest.files | Where-Object path -ne 'app/ContextSuite.Private.dll')
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 12))
    Expect-Rejection { & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $copy } 'missing implementation runtime and inventory entry'
    Copy-Item -LiteralPath (Join-Path $Candidate 'app\ContextSuite.Private.dll') -Destination $implementation
    Copy-Item -LiteralPath (Join-Path $Candidate 'release-manifest.json') -Destination $manifestPath -Force
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.applicationDependencies = [pscustomobject]@{}
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 12))
    Expect-Rejection { & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $copy } 'missing application dependency evidence'
    Copy-Item -LiteralPath (Join-Path $Candidate 'release-manifest.json') -Destination $manifestPath -Force
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($null -eq $manifest.optionalEngines) {
        $manifest | Add-Member -NotePropertyName 'optionalEngines' -NotePropertyValue ([pscustomobject]@{ imageExpansion = $true })
    } elseif ($manifest.optionalEngines.imageExpansion) {
        $manifest.optionalEngines.PSObject.Properties.Remove('imageExpansion')
    } else {
        $manifest.optionalEngines | Add-Member -NotePropertyName 'imageExpansion' -NotePropertyValue $true -Force
    }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 12))
    Expect-Rejection { & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $copy } 'inconsistent image expansion manifest'
    Copy-Item -LiteralPath (Join-Path $Candidate 'release-manifest.json') -Destination $manifestPath -Force
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    # Deleting a file AND its receipt entry must not bypass required-file checks.
    $icon = Join-Path $copy 'app\Assets\Analyze.ico'
    Remove-Item -LiteralPath $icon
    $manifest.files = @($manifest.files | Where-Object path -ne 'app/Assets/Analyze.ico')
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 12))
    Expect-Rejection { & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $copy } 'missing required Explorer asset and inventory entry'
    Copy-Item -LiteralPath (Join-Path $Candidate 'app\Assets\Analyze.ico') -Destination $icon
    $manifest = Get-Content -LiteralPath (Join-Path $Candidate 'release-manifest.json') -Raw | ConvertFrom-Json
    $manifest.files += $manifest.files[0]
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 12))
    Expect-Rejection { & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $copy } 'duplicate inventory'
    $manifest.files[0].path = '../escape'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 12))
    Expect-Rejection { & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $copy } 'unsafe inventory path'
}
Write-Output "$script:passed release packaging checks passed. Evidence: $scratch"
