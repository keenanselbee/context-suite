[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetsDirectory = Join-Path $OutputDirectory 'Assets'
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null

foreach ($retiredName in @(
    'Inspect.ico',
    'InspectSquare44x44Logo.png',
    'InspectSquare150x150Logo.png'
)) {
    $retiredPath = Join-Path $assetsDirectory $retiredName
    if (Test-Path -LiteralPath $retiredPath) {
        Remove-Item -LiteralPath $retiredPath -Force
    }
}

& (Join-Path $PSScriptRoot 'New-ApplicationAssets.ps1') -OutputDirectory $assetsDirectory

if (-not ('CompactIcons' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'CompactIcons.cs') -ReferencedAssemblies System.Drawing
}
foreach ($name in [CompactIcons]::Names) {
    [CompactIcons]::WriteIcon((Join-Path $assetsDirectory "$name.ico"), $name, $false)
    [CompactIcons]::WriteIcon((Join-Path $assetsDirectory "${name}Dark.ico"), $name, $true)
}

& (Join-Path $PSScriptRoot 'Test-IconAssets.ps1') -AssetsDirectory $assetsDirectory
