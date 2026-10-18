[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if (-not ('ContextSuiteInstalledResourceLookup' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class ContextSuiteInstalledResourceLookup
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int SHLoadIndirectString(string source, StringBuilder output, uint length, IntPtr reserved);
}
'@
}
$shell = New-Object -ComObject Shell.Application
$applications = $shell.NameSpace('shell:AppsFolder')

$commands = @(
    @{
        Name = 'Analyze'
        PackageName = 'Keenan.ContextSuite.Analyze.ShellPrototype'
        ClassId = '7F71BFC7-0125-47B6-A163-54EADDB56E8B'
    },
    @{
        Name = 'Convert'
        PackageName = 'Keenan.ContextSuite.Convert.ShellPrototype'
        ClassId = 'C0A78640-7313-4AD7-870C-2F0B838A28C2'
    },
    @{
        Name = 'Optimize'
        PackageName = 'Keenan.ContextSuite.Optimize.ShellPrototype'
        ClassId = '8C4CA2A6-99A2-436A-9021-3D004687FC4C'
    }
)

foreach ($command in $commands) {
    $package = Get-AppxPackage -Name $command.PackageName
    if (-not $package) {
        throw "$($command.Name) shell identity package is not installed."
    }
    if ($package.Status -ne 'Ok') {
        throw "$($command.Name) shell identity package status is $($package.Status)."
    }

    $instance = $null
    if ([version]$package.Version -ge [version]'1.3.7.0') {
        $manifest = Get-AppxPackageManifest -Package $package.PackageFullName
        if ($manifest.Package.Applications.Application.VisualElements.BackgroundColor -ne 'transparent') {
            throw "$($command.Name) still declares a colored icon background."
        }
        if ($manifest.Package.Applications.Application.Executable -eq 'ContextSuite.Application.exe') {
            $appId = $package.PackageFamilyName + '!' + $manifest.Package.Applications.Application.Id
            $item = $applications.ParseName($appId)
            $externalLocation = $item.ExtendedProperty('System.AppUserModel.PackageInstallPath')
            if (-not $externalLocation) { throw "$($command.Name) has no resolved installation directory." }
            $buffer = [Text.StringBuilder]::new(2048)
            # Use the package's default map: three sparse identities share one
            # external app directory. Checking a PRI beside the manifest missed
            # the 1.3.7 regression because Windows never loaded that file.
            $uri = 'ms-resource:///Files/Assets/' + $command.Name + 'Square44x44Logo.png'
            $result = [ContextSuiteInstalledResourceLookup]::SHLoadIndirectString(
                ('@{' + $package.PackageFullName + '?' + $uri + '}'), $buffer, 2048, [IntPtr]::Zero)
            $resolved = $buffer.ToString()
            if ($result -ne 0 -or -not $resolved -or
                -not $resolved.StartsWith($externalLocation.TrimEnd('\') + '\Assets\', [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
                throw ("{0} cannot resolve its installed taskbar resource (HRESULT {1:X8})." -f $command.Name, $result)
            }
        }
    }
    try {
        $type = [type]::GetTypeFromCLSID([guid] $command.ClassId, $true)
        $instance = [Activator]::CreateInstance($type)
        if (-not $instance) {
            throw "$($command.Name) returned no COM object."
        }
    }
    finally {
        if ($instance) {
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($instance) | Out-Null
        }
    }
}

Write-Output 'All three shell identity packages are healthy, installed taskbar resources resolve, and packaged COM classes activate.'
