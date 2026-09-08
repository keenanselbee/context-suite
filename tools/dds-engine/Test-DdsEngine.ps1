[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pins = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source.json') -Raw | ConvertFrom-Json
foreach ($name in 'ContextSuite.Dds.Native.dll', 'ContextSuite.Dds.Engine.json', 'DirectXTex.License.txt') {
    $file = Join-Path $Payload $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "DDS candidate deployment is incomplete: $name. Run tools/dds-engine/Build-DdsEngine.ps1."
    }
}
$identity = Get-Content -LiteralPath (Join-Path $Payload 'ContextSuite.Dds.Engine.json') -Raw | ConvertFrom-Json
if ($identity.revision -ne $pins.revision -or $identity.archiveSha256 -ne $pins.archiveSha256 -or
    $identity.configuration -ne $pins.configuration -or
    $identity.nativeSha256 -ne (Get-FileHash -LiteralPath (Join-Path $Payload 'ContextSuite.Dds.Native.dll')).Hash -or
    $identity.licenseSha256 -ne (Get-FileHash -LiteralPath (Join-Path $Payload 'DirectXTex.License.txt')).Hash -or
    $identity.bridgeSourceSha256 -ne (Get-FileHash -LiteralPath (Join-Path $repository 'proprietary\native\dds\Bridge.cpp')).Hash -or
    $identity.buildDefinitionSha256 -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'CMakeLists.txt')).Hash) {
    throw 'DDS candidate identity, source or notices are stale or modified; rebuild and verify the candidate.'
}
Write-Output 'DDS candidate provenance and payload hashes passed. This is not commercial release clearance.'
