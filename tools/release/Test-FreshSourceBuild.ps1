[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $EngineArchive,
    [string] $PaletteCandidateDirectory,
    [switch] $IncludeWorkingChanges
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $PaletteCandidateDirectory) { $PaletteCandidateDirectory = Join-Path $repository 'artifacts\engines\palette-win-x64' }
& (Join-Path $repository 'tools\palette-engine\Test-PaletteEngine.ps1') -Payload $PaletteCandidateDirectory
$engine = (Resolve-Path -LiteralPath $EngineArchive).Path
$run = Join-Path $repository ('.codex-temp\fresh-source-build\' + [guid]::NewGuid().ToString('N'))
$snapshot = Join-Path $run 'source'
New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
$receipt = [ordered]@{
    status = 'started'; includesWorkingChanges = [bool]$IncludeWorkingChanges;
    engineArchiveSha256 = (Get-FileHash -LiteralPath $engine).Hash;
    paletteExecutableSha256 = (Get-FileHash -LiteralPath (Join-Path $PaletteCandidateDirectory 'ContextSuite.Palette.exe')).Hash;
    paletteProvenance = 'Imported pinned native candidate, not rebuilt by this source-snapshot test.';
    environment = 'Fresh source/output directories on existing development machine; shared SDKs and NuGet cache. Not a clean Windows install.';
    sources = @()
}
$utf8 = [Text.UTF8Encoding]::new($false)
foreach ($entry in @(@{ source = $repository; destination = $snapshot; name = 'public' },
                     @{ source = (Join-Path $repository 'proprietary'); destination = (Join-Path $snapshot 'proprietary'); name = 'private' })) {
    $revision = & git -C $entry.source rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source revision.' }
    $archive = Join-Path $run ($entry.name + '-source.zip')
    & git -C $entry.source archive --format=zip "--output=$archive" $revision
    if ($LASTEXITCODE -ne 0) { throw 'Cannot archive committed source.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $entry.destination
    if ($IncludeWorkingChanges) {
        $changed = @(& git -C $entry.source diff HEAD --name-only --diff-filter=ACMRT)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect changed sources.' }
        $new = @(& git -C $entry.source ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect new sources.' }
        # This local verification includes authored changes, never incidental root logs.
        $new = @($new | Where-Object { $_ -match '^(src/|tests/|tools/|docs/|packaging/|assets/|\.github/)' })
        foreach ($relative in @($changed + $new | Sort-Object -Unique)) {
            $source = Join-Path $entry.source $relative
            if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source symlinks are not supported by this snapshot test.' }
            $target = [IO.Path]::GetFullPath((Join-Path $entry.destination $relative))
            if (-not $target.StartsWith($entry.destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Snapshot path escapes its root.' }
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $target -Force
        }
        $deleted = @(& git -C $entry.source diff HEAD --name-only --diff-filter=D)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect deleted sources.' }
        foreach ($relative in $deleted) {
            $target = [IO.Path]::GetFullPath((Join-Path $entry.destination $relative))
            if (-not $target.StartsWith($entry.destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Deletion escapes snapshot root.' }
            if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target }
        }
    }
    $files = @(Get-ChildItem -LiteralPath $entry.destination -Recurse -File | Sort-Object FullName | ForEach-Object {
        @{ path = $_.FullName.Substring($entry.destination.Length + 1).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    $receipt.sources += @{ name = $entry.name; commit = $revision; committedArchiveSha256 = (Get-FileHash -LiteralPath $archive).Hash; actualFiles = $files }
}
if ((Test-Path -LiteralPath (Join-Path $snapshot 'artifacts')) -or (Test-Path -LiteralPath (Join-Path $snapshot '.codex-temp'))) {
    throw 'Fresh snapshot already contains build/cache output.'
}
$receiptPath = Join-Path $run 'verification.json'
[IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 8), $utf8)
try {
    & (Join-Path $snapshot 'tools\curated-engine\Import-ProductionEngine.ps1') -Archive $engine
    & (Join-Path $snapshot 'tools\dds-engine\Build-DdsEngine.ps1')
    & (Join-Path $snapshot 'tools\png-engine\Stage-PngEngine.ps1')
    & (Join-Path $snapshot 'tools\palette-engine\Stage-PaletteEngine.ps1') -CandidateDirectory $PaletteCandidateDirectory
    & (Join-Path $snapshot 'tools\Build-Production.ps1') -Configuration Release
    & (Join-Path $snapshot 'tools\Test-DdsCodec.ps1') -SkipNativeBuild
    & (Join-Path $snapshot 'tools\Test-ImageConversion.ps1') -Configuration Release
    $receipt.status = 'passed'
    $receipt.productionInventorySha256 = (Get-FileHash -LiteralPath (Join-Path $snapshot 'artifacts\production\Release\payload-inventory.json')).Hash
} catch {
    $receipt.status = 'failed'
    throw
} finally {
    [IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 8), $utf8)
}
Write-Output "Fresh-source build and image/DDS contracts passed. Receipt: $receiptPath"
