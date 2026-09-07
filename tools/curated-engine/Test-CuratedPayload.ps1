[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Workspace,
    [Parameter(Mandatory)][string] $Payload
)

$ErrorActionPreference = 'Stop'
$expectedLibraries = @('coders', 'jpeg-turbo', 'jpeg-turbo-12', 'jpeg-turbo-16', 'lcms', 'MagickCore', 'MagickWand', 'png', 'webp', 'xml', 'zlib')
$expectedDelegates = @('JPEG', 'LCMS', 'PNG', 'WEBP', 'WEBPMUX', 'WINGDI32', 'XML', 'ZLIB')
$stockHash = '14A0992B54E236E37603DA18AE7B9936E3B3F490EADCC6D99BD13CCC1470B2C6'
$result = Get-Content -LiteralPath (Join-Path $Workspace 'build-result.json') -Raw | ConvertFrom-Json
if ($result.sha256 -eq $stockHash -or $result.sha256 -notmatch '^[0-9A-F]{64}$') { throw 'Stock or invalid candidate identity.' }
$nativeOutput = Join-Path $Workspace 'native\src\Magick.Native\bin\ReleaseQ16\x64'
$map = Get-Content -LiteralPath (Join-Path $nativeOutput 'Magick.Native-Q16-x64.map') -Raw
$libraries = @([regex]::Matches($map, 'CORE_RL_([\w+-]+)_:') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
if (Compare-Object $expectedLibraries $libraries) { throw "Unexpected or missing linked libraries: $($libraries -join ', ')" }
$systemLibraries = @('advapi32', 'bcrypt', 'Delayimp', 'gdi32', 'gdiplus', 'kernel32', 'LIBCMT', 'libucrt', 'libvcruntime', 'urlmon', 'user32')
$mapOwners = @([regex]::Matches($map, '(?m)^\s+[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}\s+.+\s([^\s:]+):[^\s:]+\s*$') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$allowedOwners = $systemLibraries + @($expectedLibraries | ForEach-Object { "CORE_RL_$($_)_" })
if (-not $mapOwners.Count -or @($mapOwners | Where-Object { $_ -notin $allowedOwners }).Count) { throw 'Unexpected library owner in native linker map.' }
$configuration = Get-Content -LiteralPath (Join-Path $Workspace 'native\src\ImageMagick\ImageMagick\MagickCore\magick-baseconfig.h') -Raw
$delegates = @([regex]::Matches($configuration, '(?m)^#define MAGICKCORE_(\w+)_DELEGATE\b') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
if (Compare-Object $expectedDelegates $delegates) { throw "Unexpected or missing configured dependencies: $($delegates -join ', ')" }
$binaries = @(Get-ChildItem -LiteralPath $Payload -Recurse -File -Filter 'Magick.Native*')
if ($binaries.Count -ne 1 -or $binaries[0].Name -ne 'Magick.Native-Q16-x64.dll') { throw 'Payload must contain exactly one Windows native engine, without alternate runtime copies.' }
if ((Get-FileHash -LiteralPath $binaries[0].FullName).Hash -ne $result.sha256) { throw 'Payload engine does not match the audited candidate.' }
if ((Get-FileHash -LiteralPath (Join-Path $nativeOutput 'Magick.Native-Q16-x64.dll')).Hash -ne $result.sha256) { throw 'Built engine changed after its result was recorded.' }
$notice = Join-Path $Payload 'Magick.NET.Notice.txt'
if (-not (Test-Path -LiteralPath $notice) -or
    (Get-FileHash -LiteralPath $notice).Hash -ne (Get-FileHash -LiteralPath (Join-Path $Workspace 'Magick.NET.Notice.txt')).Hash) {
    throw 'Missing or mismatched curated notices.'
}
Write-Output "Curated native payload matches candidate hash, linker map, and dependency configuration: $Payload"
