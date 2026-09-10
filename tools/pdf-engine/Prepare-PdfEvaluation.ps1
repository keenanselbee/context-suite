[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pinPath = Join-Path $PSScriptRoot 'evaluation.json'
$pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
$scratch = Join-Path $repository ('.codex-temp\pdf-engine\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$archive = Join-Path $scratch 'upstream.zip'
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest $pin.url -OutFile $archive -UseBasicParsing
if ((Get-FileHash -LiteralPath $archive).Hash -ne $pin.archiveSha256) { throw 'PDF evaluation archive hash mismatch.' }
$payload = Join-Path $scratch 'unpacked'
Expand-Archive -LiteralPath $archive -DestinationPath $payload
$executables = @(Get-ChildItem -LiteralPath $payload -Filter 'qpdf.exe' -File -Recurse)
if ($executables.Count -ne 1) { throw 'Expected one qpdf evaluation executable.' }
$inventory = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($payload.Length + 1); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
Copy-Item -LiteralPath $pinPath -Destination (Join-Path $scratch 'evaluation.json')
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $scratch 'inventory.json') -Encoding UTF8
Write-Output "Verified evaluation executable: $($executables[0].FullName)"
Write-Output "Archive and full inventory retained: $scratch"
Write-Output 'No installation, production payload, PATH or Explorer-registration changes.'
