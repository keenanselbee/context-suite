[CmdletBinding()]
param([Parameter(Mandatory)][string] $Workspace)

$ErrorActionPreference = 'Stop'
$native = Join-Path $Workspace 'native'
$artifacts = Join-Path $native 'src\ImageMagick\Artifacts'
$notice = @"
Context Suite isolated curated-engine prototype (not a release clearance).
Magick.NET / Magick.Native: Copyright Dirk Lemstra.
Native build overlay removes unused link inputs; codec algorithms are unchanged.
The managed Magick.NET wrapper remains unmodified at 14.17.1.
ImageMagick and dependency notices follow unchanged from the curated build inputs.

"@
$notice += [IO.File]::ReadAllText((Join-Path $native 'License.txt'))
$notice += "`n`n" + [IO.File]::ReadAllText((Join-Path $artifacts 'NOTICE.txt'))
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$patents = Join-Path $repository '.codex-temp\curated-engine\downloads\webp-PATENTS.txt'
if ((Get-FileHash -LiteralPath $patents).Hash -ne 'CC3273E0694EA5896145E0677699B53471B03EA43021DDC50E7923FBB9F5023C') {
    throw 'Pinned WebP patent grant is missing or changed; rerun preparation with a fresh run name.'
}
$notice += "`n`n[ WebP additional patent grant ]`n" + [IO.File]::ReadAllText($patents)
$path = Join-Path $Workspace 'Magick.NET.Notice.txt'
[IO.File]::WriteAllText($path, $notice, [Text.UTF8Encoding]::new($false))
Write-Output "Prepared curated notices at $path"
