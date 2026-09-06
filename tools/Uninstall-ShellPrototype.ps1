[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$packageNames = @(
    'Keenan.ContextSuite.ShellPrototype',
    'Keenan.ContextSuite.Inspect.ShellPrototype',
    'Keenan.ContextSuite.Analyze.ShellPrototype',
    'Keenan.ContextSuite.Convert.ShellPrototype',
    'Keenan.ContextSuite.Optimize.ShellPrototype'
)
$packages = @($packageNames | ForEach-Object { Get-AppxPackage -Name $_ })
if (-not $packages) {
    Write-Output 'No Context Suite shell prototype identity package is installed.'
    return
}

foreach ($package in $packages) {
    Remove-AppxPackage -Package $package.PackageFullName
    Write-Output "Removed $($package.PackageFullName)"
}
