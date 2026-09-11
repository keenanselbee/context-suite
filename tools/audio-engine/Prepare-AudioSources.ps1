[CmdletBinding()]
param([string] $SourceDirectory, [switch] $VerifyOnly)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$temporary = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp'))
if (-not $SourceDirectory) {
    if ($VerifyOnly) { throw 'VerifyOnly requires an existing SourceDirectory.' }
    $SourceDirectory = Join-Path $temporary ('audio-source-' + [guid]::NewGuid().ToString('N'))
}
$directory = [IO.Path]::GetFullPath($SourceDirectory)
if (-not $directory.StartsWith($temporary + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Audio source retention must stay inside this repository .codex-temp directory.'
}
for ($ancestor = $directory; $ancestor -and $ancestor -ne $repository; $ancestor = Split-Path $ancestor -Parent) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Linked source-cache paths are not allowed.'
    }
}
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-inputs.json') -Raw | ConvertFrom-Json
if ($pin.schema -ne 2 -or $pin.archives.Count -ne 6) { throw 'Unsupported source inventory.' }
if (-not (Test-Path -LiteralPath $directory)) {
    if ($VerifyOnly) { throw 'Source directory does not exist.' }
    New-Item -ItemType Directory -Path $directory | Out-Null
}
Add-Type -AssemblyName System.IO.Compression
$ProgressPreference = 'SilentlyContinue'
foreach ($archive in $pin.archives) {
    if ($archive.file -notmatch '^[a-z-]+\.zip$' -or $archive.sha256 -notmatch '^[A-F0-9]{64}$' -or
        $archive.bytes -le 0 -or $archive.bytes -gt 128MB) {
        throw 'Invalid pinned source input.'
    }
    switch ($archive.downloadKind) {
        'github-zip' {
            if ($archive.revision -notmatch '^[a-f0-9]{40}$' -or
                $archive.url -notmatch ('^https://github\.com/[^/]+/[^/]+/archive/' + $archive.revision + '\.zip$')) {
                throw 'Invalid pinned GitHub source input.'
            }
        }
        'svn-http-export' {
            if ($archive.id -ne 'lame' -or $archive.file -ne 'lame.zip' -or $archive.revision -ne '6761' -or
                $archive.url -ne 'https://svn.code.sf.net/p/lame/svn/!svn/bc/6761/trunk/lame/' -or
                $archive.prefix -ne 'lame-r6761/') {
                throw 'Unsupported SVN source export.'
            }
        }
        default { throw 'Unsupported source download kind.' }
    }
    $path = Join-Path $directory $archive.file
    if (-not (Test-Path -LiteralPath $path)) {
        if ($VerifyOnly) { throw "Missing source archive: $($archive.file)" }
        if ($archive.downloadKind -eq 'svn-http-export') {
            & python (Join-Path $PSScriptRoot 'Download-LameSource.py') $path
            if ($LASTEXITCODE -ne 0) { throw 'Pinned LAME source export failed; partial evidence retained.' }
        }
        else {
            Invoke-WebRequest -Uri $archive.url -OutFile $path -UseBasicParsing
        }
    }
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked source archive is not allowed.' }
    $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -ne $archive.bytes) { throw "Source archive length changed: $($archive.file)" }
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
        finally { $hasher.Dispose() }
        if ($hash -ne $archive.sha256) { throw "Source archive identity changed: $($archive.file)" }
        $stream.Position = 0
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
        try {
            foreach ($entry in $archive.requiredEntries) {
                if ($null -eq $zip.GetEntry($archive.prefix + $entry)) { throw "Missing pinned source entry: $($archive.id)/$entry" }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $stream.Dispose() }
    Write-Output "Verified source input: $($archive.id) $($archive.revision)"
}
Write-Output "Source archives retained at $directory. Unresolved inputs remain; no upstream scripts executed, binaries adopted or release clearance implied."
