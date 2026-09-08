[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pin = Get-Content (Join-Path $PSScriptRoot 'production.json') -Raw | ConvertFrom-Json
$scratch = Join-Path $repository ('.codex-temp\png-engine\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$archive = Join-Path $scratch 'oxipng.zip'
Invoke-WebRequest $pin.url -OutFile $archive -UseBasicParsing
if ((Get-FileHash -LiteralPath $archive).Hash -ne $pin.archiveSha256) { throw 'PNG engine archive hash mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $scratch 'unpacked')
$source = Join-Path (Join-Path $scratch 'unpacked') $pin.archiveDirectory
if ((Get-FileHash (Join-Path $source 'oxipng.exe')).Hash -ne $pin.executableSha256 -or
    (Get-FileHash (Join-Path $source 'LICENSE.txt')).Hash -ne $pin.licenseSha256) { throw 'PNG engine file hash mismatch.' }
$output = Join-Path $repository 'artifacts\engines\png-win-x64'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item (Join-Path $source 'oxipng.exe') (Join-Path $output 'oxipng.exe') -Force
Copy-Item (Join-Path $source 'LICENSE.txt') (Join-Path $output 'Oxipng.License.txt') -Force
Copy-Item (Join-Path $PSScriptRoot 'production.json') (Join-Path $output 'ContextSuite.Png.Engine.json') -Force
& (Join-Path $PSScriptRoot 'Test-PngEngine.ps1') -Payload $output
Write-Output "Staged pinned PNG development engine. Upstream archive retained at $archive; redistribution review remains required."
