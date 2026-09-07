[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $SkipBuild,

    [switch] $Production
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    if ($Production) {
        & (Join-Path $PSScriptRoot 'Build-Production.ps1') -Configuration $Configuration
    }
    else {
        & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration
    }
}

$externalLocation = Join-Path $repositoryRoot "artifacts\bin\x64\$Configuration"
if ($Production) { $externalLocation = Join-Path $repositoryRoot "artifacts\production\$Configuration" }
$executable = if ($Production) { 'ContextSuite.Application.exe' } else { 'ContextSuite.Host.exe' }
$packageDefinitions = @(
    @{ Name = 'Keenan.ContextSuite.Analyze.ShellPrototype'; Manifest = 'packaging\ContextSuite.Analyze.ShellPrototype\AppxManifest.xml' },
    @{ Name = 'Keenan.ContextSuite.Convert.ShellPrototype'; Manifest = 'packaging\ContextSuite.Convert.ShellPrototype\AppxManifest.xml' },
    @{ Name = 'Keenan.ContextSuite.Optimize.ShellPrototype'; Manifest = 'packaging\ContextSuite.Optimize.ShellPrototype\AppxManifest.xml' }
)
$requiredFiles = @(
    (Join-Path $externalLocation $executable),
    (Join-Path $externalLocation 'ContextSuite.Shell.dll'),
    (Join-Path $externalLocation 'Assets\StoreLogo.png'),
    (Join-Path $externalLocation 'Assets\Analyze.ico'),
    (Join-Path $externalLocation 'Assets\Convert.ico'),
    (Join-Path $externalLocation 'Assets\Optimize.ico')
)
if ($Production) {
    $requiredFiles += Join-Path $externalLocation 'ContextSuite.Worker.exe'
    $requiredFiles += Join-Path $externalLocation 'ContextSuite.Private.dll'
}
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Required shell test file is missing: $requiredFile"
    }
}

# Generate separate development manifests before changing installed state.
# Source manifests remain the independently usable native prototype definition.
foreach ($definition in $packageDefinitions) {
    $manifestPath = Join-Path $repositoryRoot $definition.Manifest
    if ($Production) {
        [xml] $document = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
        $document.Package.Applications.Application.Executable = $executable
        $generatedDirectory = Join-Path $repositoryRoot "artifacts\registration\production\$Configuration\$($definition.Name)"
        New-Item -ItemType Directory -Path $generatedDirectory -Force | Out-Null
        $manifestPath = Join-Path $generatedDirectory 'AppxManifest.xml'
        $document.Save($manifestPath)
    }
    $definition.RegisterManifest = $manifestPath
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
    $manifest = $definition.RegisterManifest
    Add-AppxPackage -Register $manifest -ExternalLocation $externalLocation
}

$installed = @($packageDefinitions | ForEach-Object { Get-AppxPackage -Name $_.Name })
if ($installed.Count -ne 3) {
    throw "Expected three Context Suite shell identity packages, but found $($installed.Count)."
}

$installed | Sort-Object Name | Select-Object Name, Version, Architecture, PackageFullName, InstallLocation
