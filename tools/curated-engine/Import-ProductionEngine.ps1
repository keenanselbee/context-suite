[CmdletBinding()]
param([Parameter(Mandatory)][string] $Archive, [switch] $ValidateOnly)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
if ((Get-Item -LiteralPath $archivePath).Length -gt 256MB) { throw 'Engine archive exceeds 256 MiB.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$scratch = Join-Path $repository ('.codex-temp\engine-import\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$required = @('Magick.Native-Q16-x64.dll', 'Magick.NET.Notice.txt')
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    if ($zip.Entries.Count -ne $required.Count) { throw 'Engine archive must contain exactly the two reviewed files.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -cnotin $required -or -not $seen.Add($entry.FullName) -or $entry.Length -le 0 -or $entry.Length -gt 256MB) {
            throw 'Invalid engine archive entry; paths, duplicates and additional files are forbidden.'
        }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $scratch $entry.FullName))
    }
} finally { $zip.Dispose() }
& (Join-Path $PSScriptRoot 'Test-ProductionEngine.ps1') -Payload $scratch
if (-not $ValidateOnly) {
    $destination = Join-Path $repository 'artifacts\engines\curated-win-x64'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($name in $required) { Copy-Item -LiteralPath (Join-Path $scratch $name) -Destination $destination -Force }
    & (Join-Path $PSScriptRoot 'Test-ProductionEngine.ps1') -Payload $destination
}
Write-Output "Reviewed engine archive verified. Scratch evidence: $scratch. Commercial clearance is separate."
