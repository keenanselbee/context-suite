[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload)
$ErrorActionPreference = 'Stop'
$pinPath = Join-Path $PSScriptRoot 'production.json'
$pin = Get-Content $pinPath -Raw | ConvertFrom-Json
foreach ($file in @(@('oxipng.exe', $pin.executableSha256), @('Oxipng.License.txt', $pin.licenseSha256),
    @('ContextSuite.Png.Engine.json', (Get-FileHash $pinPath).Hash))) {
    $path = Join-Path $Payload $file[0]
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $file[1]) {
        throw "Missing or altered PNG engine input: $($file[0])"
    }
}
Write-Output 'Pinned PNG executable, identity and license hashes verified.'
