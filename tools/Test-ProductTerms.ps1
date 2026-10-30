[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$terms = Join-Path $Payload 'ContextSuite.License.md'
if (-not (Test-Path -LiteralPath $terms -PathType Leaf) -or
    (Get-Item -LiteralPath $terms -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Product license terms are missing or linked.'
}
if ((Get-FileHash -LiteralPath $terms).Hash -ne (Get-FileHash -LiteralPath (Join-Path $repository 'LICENSE.md')).Hash) {
    throw 'Product license terms are stale or changed.'
}
Write-Output 'Packaged product terms match the selected source.'
