[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration
}

$externalLocation = Join-Path $repositoryRoot "artifacts\bin\x64\$Configuration"
$packageDefinitions = @(
    @{ Name = 'Keenan.ContextSuite.Analyze.ShellPrototype'; Manifest = 'packaging\ContextSuite.Analyze.ShellPrototype\AppxManifest.xml' },
    @{ Name = 'Keenan.ContextSuite.Convert.ShellPrototype'; Manifest = 'packaging\ContextSuite.Convert.ShellPrototype\AppxManifest.xml' },
    @{ Name = 'Keenan.ContextSuite.Optimize.ShellPrototype'; Manifest = 'packaging\ContextSuite.Optimize.ShellPrototype\AppxManifest.xml' }
)
$requiredFiles = @(
    (Join-Path $externalLocation 'ContextSuite.Host.exe'),
    (Join-Path $externalLocation 'ContextSuite.Shell.dll'),
    (Join-Path $externalLocation 'Assets\StoreLogo.png'),
    (Join-Path $externalLocation 'Assets\Analyze.ico'),
    (Join-Path $externalLocation 'Assets\Convert.ico'),
    (Join-Path $externalLocation 'Assets\Optimize.ico')
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Required prototype file is missing: $requiredFile"
    }
}

$retiredPackageNames = @(
    'Keenan.ContextSuite.ShellPrototype',
    'Keenan.ContextSuite.Inspect.ShellPrototype'
)
foreach ($packageName in $retiredPackageNames + @($packageDefinitions | ForEach-Object { $_.Name })) {
    foreach ($package in @(Get-AppxPackage -Name $packageName)) {
        Remove-AppxPackage -Package $package.PackageFullName
    }
}

foreach ($definition in $packageDefinitions) {
    $manifest = Join-Path $repositoryRoot $definition.Manifest
    Add-AppxPackage -Register $manifest -ExternalLocation $externalLocation
}

$installed = @($packageDefinitions | ForEach-Object { Get-AppxPackage -Name $_.Name })
if ($installed.Count -ne 3) {
    throw "Expected three Context Suite shell identity packages, but found $($installed.Count)."
}

$installed | Sort-Object Name | Select-Object Name, Version, Architecture, PackageFullName, InstallLocation
