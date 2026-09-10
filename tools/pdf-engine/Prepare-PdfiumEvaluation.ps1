[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pinPath = Join-Path $PSScriptRoot 'pdfium-evaluation.json'
$pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
$scratch = Join-Path $repository ('.codex-temp\pdfium-engine\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$archive = Join-Path $scratch 'upstream.tgz'
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest $pin.url -OutFile $archive -UseBasicParsing
if ((Get-FileHash -LiteralPath $archive).Hash -ne $pin.archiveSha256) { throw 'PDFium archive hash mismatch.' }
$payload = Join-Path $scratch 'unpacked'
New-Item -ItemType Directory -Path $payload | Out-Null
& tar -xf $archive -C $payload
if ($LASTEXITCODE -ne 0) { throw 'PDFium archive extraction failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $payload 'bin\pdfium.dll'))) { throw 'PDFium DLL missing.' }
$inventory = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($payload.Length + 1); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
Copy-Item -LiteralPath $pinPath -Destination (Join-Path $scratch 'evaluation.json')
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $scratch 'inventory.json') -Encoding UTF8
Write-Output "Verified PDFium evaluation payload: $payload"
Write-Output "Archive and inventory retained: $scratch"
Write-Output 'No installation, production payload, PATH or Explorer-registration changes.'
