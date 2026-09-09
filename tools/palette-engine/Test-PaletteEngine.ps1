[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload)
$ErrorActionPreference = 'Stop'
$pinPath = Join-Path $PSScriptRoot 'production.json'
$pin = Get-Content -Raw -LiteralPath $pinPath | ConvertFrom-Json
foreach ($file in $pin.files.PSObject.Properties) {
    $path = Join-Path $Payload $file.Name
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.Value) {
        throw "Missing or changed palette dependency: $($file.Name)"
    }
}
if ((Get-FileHash -LiteralPath (Join-Path $Payload 'ContextSuite.Palette.Engine.json')).Hash -ne (Get-FileHash -LiteralPath $pinPath).Hash) {
    throw 'Palette identity mismatch.'
}
Write-Output 'Pinned palette executable and notices verified.'
