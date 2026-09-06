[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug', [switch] $SkipShell)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$privateProject = Join-Path $repositoryRoot 'proprietary\src\ContextSuite.Private\ContextSuite.Private.csproj'
if (-not (Test-Path -LiteralPath $privateProject)) {
    throw 'Production requires the compatible context-suite-private repository at proprietary/. There is no public demo build.'
}
& dotnet build (Join-Path $repositoryRoot 'ContextSuite.Production.slnx') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Production foundation build failed.' }
$output = Join-Path $repositoryRoot "artifacts\production\$Configuration"
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($project in @('ContextSuite.Application', 'ContextSuite.Worker')) {
    $source = Join-Path $repositoryRoot "artifacts\managed\bin\$project\$Configuration\net10.0-windows"
    Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $output -Force
}
if (-not $SkipShell) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration
    $native = Join-Path $repositoryRoot "artifacts\bin\x64\$Configuration"
    Copy-Item -LiteralPath (Join-Path $native 'ContextSuite.Shell.dll') -Destination $output -Force
    & (Join-Path $PSScriptRoot 'New-PrototypeAssets.ps1') -OutputDirectory $output
}
Write-Output "Built production foundation at $output. No Explorer packages were installed."
