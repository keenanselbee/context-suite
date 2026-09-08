[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $repository 'artifacts\engines\curated-win-x64'
& (Join-Path $PSScriptRoot 'Test-ProductionEngine.ps1') -Payload $source
$output = Join-Path $repository ('artifacts\engine-transfer\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$archive = Join-Path $output 'curated-win-x64.zip'
Compress-Archive -LiteralPath (Join-Path $source 'Magick.Native-Q16-x64.dll'), (Join-Path $source 'Magick.NET.Notice.txt') -DestinationPath $archive
& (Join-Path $PSScriptRoot 'Import-ProductionEngine.ps1') -Archive $archive -ValidateOnly
Get-FileHash -LiteralPath $archive | Select-Object Path, Hash
