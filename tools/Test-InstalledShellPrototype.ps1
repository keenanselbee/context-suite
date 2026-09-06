[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

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

Write-Output 'All three shell identity packages are healthy, and their packaged COM classes activate.'
