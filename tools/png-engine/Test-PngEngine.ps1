[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload, [switch] $RequireDependencyNotices)
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
if ($RequireDependencyNotices) {
    $review = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'notice-review.json') -Raw | ConvertFrom-Json
    if ($review.binarySha256 -ne $pin.executableSha256) { throw 'PNG dependency notices describe another executable.' }
    foreach ($file in $review.files.PSObject.Properties) {
        $path = Join-Path $Payload $file.Name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.Value) {
            throw "Missing or altered PNG dependency notice: $($file.Name)"
        }
    }
}
Write-Output 'Pinned PNG executable, identity and license hashes verified.'
