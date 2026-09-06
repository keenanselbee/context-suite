[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Installer\vswhere.exe was not found.'
}

$installationPath = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if (-not $installationPath) {
    throw 'A Visual Studio installation with the x64 C++ tools was not found.'
}

$msbuild = Join-Path $installationPath 'MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild was not found at $msbuild."
}

& $msbuild (Join-Path $repositoryRoot 'ContextSuite.sln') `
    /m `
    /nologo `
    /verbosity:minimal `
    /property:Configuration=$Configuration `
    /property:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw "Context Suite build failed with exit code $LASTEXITCODE."
}

$outputDirectory = Join-Path $repositoryRoot "artifacts\bin\x64\$Configuration"
& (Join-Path $PSScriptRoot 'New-PrototypeAssets.ps1') -OutputDirectory $outputDirectory

Write-Output "Built Context Suite shell prototype at $outputDirectory"
