[CmdletBinding()]
param([string] $CandidateDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $CandidateDirectory) { $CandidateDirectory = Join-Path $repository '.codex-temp\png-palette-build' }
$pin = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'production.json') | ConvertFrom-Json
foreach ($file in $pin.files.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path $CandidateDirectory $file.Name)).Hash -ne $file.Value) {
        throw "Palette candidate does not match the reviewed pin: $($file.Name). Review rebuilt candidates before changing production.json."
    }
}
$output = Join-Path $repository 'artifacts\engines\palette-win-x64'
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($file in $pin.files.PSObject.Properties) { Copy-Item -LiteralPath (Join-Path $CandidateDirectory $file.Name) -Destination $output -Force }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'production.json') -Destination (Join-Path $output 'ContextSuite.Palette.Engine.json') -Force
& (Join-Path $PSScriptRoot 'Test-PaletteEngine.ps1') -Payload $output
