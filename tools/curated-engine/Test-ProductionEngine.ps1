[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload)
$ErrorActionPreference = 'Stop'
$selection = Get-Content (Join-Path $PSScriptRoot 'production.json') -Raw | ConvertFrom-Json
$native = @(Get-ChildItem -LiteralPath $Payload -Recurse -File -Filter 'Magick.Native*')
if ($native.Count -ne 1 -or $native[0].Name -ne 'Magick.Native-Q16-x64.dll' -or
    (Get-FileHash -LiteralPath $native[0].FullName).Hash -ne $selection.nativeSha256) {
    throw 'Production must contain exactly the approved curated native engine; stock/alternate binaries are not accepted.'
}
$notice = Join-Path $Payload 'Magick.NET.Notice.txt'
if (-not (Test-Path -LiteralPath $notice) -or (Get-FileHash -LiteralPath $notice).Hash -ne $selection.noticeSha256) {
    throw 'Production curated-engine attribution is missing or does not match the reviewed notice.'
}
Write-Output 'Production curated engine and notice identities passed.'
