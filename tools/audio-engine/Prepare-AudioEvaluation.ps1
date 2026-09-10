[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pinPath = Join-Path $PSScriptRoot 'evaluation.json'
$pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
$scratch = Join-Path $repository ('.codex-temp\audio-engine\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$archive = Join-Path $scratch 'upstream.zip'
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest $pin.url -OutFile $archive -UseBasicParsing
if ((Get-FileHash -LiteralPath $archive).Hash -ne $pin.archiveSha256) { throw 'Audio evaluation archive hash mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $scratch 'unpacked')
$payload = Join-Path (Join-Path $scratch 'unpacked') $pin.archiveDirectory
foreach ($name in @('ffmpeg.exe', 'ffprobe.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload ('bin\' + $name)))) { throw "Missing evaluation executable: $name" }
}
$inventory = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($payload.Length + 1); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
Copy-Item -LiteralPath $pinPath -Destination (Join-Path $scratch 'evaluation.json')
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $scratch 'inventory.json') -Encoding UTF8
Write-Output "Verified evaluation payload: $payload"
Write-Output "Archive and full inventory retained: $scratch"
Write-Output 'No production payload, installation, PATH or Explorer registrations changed.'
