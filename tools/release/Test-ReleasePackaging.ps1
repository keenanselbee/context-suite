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
$engine = Join-Path $repository 'artifacts\engines\curated-win-x64'
$valid = Join-Path $scratch 'valid.zip'
Compress-Archive -LiteralPath (Join-Path $engine 'Magick.Native-Q16-x64.dll'), (Join-Path $engine 'Magick.NET.Notice.txt') -DestinationPath $valid
& $import -Archive $valid -ValidateOnly
$script:passed++
if ($Candidate) {
    & (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $Candidate
    $script:passed++
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
