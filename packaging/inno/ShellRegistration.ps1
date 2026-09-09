# Shared by the installer entry point and isolated platform-mock contracts.
# No actions occur when this file is dot-sourced.
. (Join-Path $PSScriptRoot 'ClassicShellRegistration.ps1')
function Get-SuitePackages {
    param($Metadata)
    $result = @()
    foreach ($definition in $Metadata.packages) {
        $result += @(Get-AppxPackage -Name $definition.name -ErrorAction Stop)
    }
    return $result
}

function Assert-SuiteInstallAvailable {
    param($Metadata)
    # Existing installations are never implicitly removed or upgraded by this
    # first-install candidate. Upgrade/repair needs separately verified recovery.
    if (@(Get-SuitePackages $Metadata).Count -ne 0) {
        throw 'An existing or partial Context Suite installation was found. Upgrade/repair is not enabled in this internal installer.'
    }
    foreach ($name in @('Keenan.ContextSuite.ShellPrototype', 'Keenan.ContextSuite.Inspect.ShellPrototype',
        'Keenan.ContextSuite.Analyze.ShellPrototype', 'Keenan.ContextSuite.Convert.ShellPrototype',
        'Keenan.ContextSuite.Optimize.ShellPrototype')) {
        if (@(Get-AppxPackage -Name $name -ErrorAction Stop).Count -ne 0) {
            throw 'Development Explorer packages are registered. Remove them explicitly before testing installation; setup will not remove them.'
        }
    }
}

function Install-SuitePackages {
    param($Metadata, [string] $PackageDirectory, [string] $ApplicationDirectory)
    Assert-SuiteInstallAvailable $Metadata
    try {
        foreach ($definition in $Metadata.packages) {
            Add-AppxPackage -Path (Join-Path $PackageDirectory $definition.file) -ExternalLocation $ApplicationDirectory -ErrorAction Stop
        }
        $installed = @(Get-SuitePackages $Metadata)
        foreach ($definition in $Metadata.packages) {
            $matches = @($installed | Where-Object {
                $_.Name -ceq $definition.name -and $_.Publisher -ceq $Metadata.publisher -and
                [version]$_.Version -eq [version]$Metadata.version
            })
            if ($matches.Count -ne 1) { throw 'Installed Explorer package identity did not match the requested version/publisher.' }
        }
    } catch {
        $failure = $_.Exception.Message
        $rollbackFailures = @()
        # Query even the failed Add: Windows may have changed state before it failed.
        foreach ($package in @(Get-SuitePackages $Metadata)) {
            if ($package.Publisher -cne $Metadata.publisher -or [version]$package.Version -ne [version]$Metadata.version) {
                $rollbackFailures += 'Unexpected package identity retained for manual review.'
                continue
            }
            try { Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop }
            catch { $rollbackFailures += $_.Exception.Message }
        }
        if (@(Get-SuitePackages $Metadata).Count -ne 0 -or $rollbackFailures.Count -ne 0) {
            throw "Registration failed; partial registration remains. Keep installed files and repair before uninstalling. $failure $($rollbackFailures -join ' ')"
        }
        throw "Registration failed; new Explorer registrations were rolled back. Installed files may be removed using the uninstaller. $failure"
    }
}

function Uninstall-SuitePackages {
    param($Metadata)
    if ((Get-SuiteMenuMode $Metadata) -eq 'classic') { Uninstall-SuiteClassic $Metadata; return }
    $installed = @(Get-SuitePackages $Metadata)
    # Validate every target before removing any. Never remove another publisher,
    # a newer version, a prototype, or packages for another Windows user.
    foreach ($package in $installed) {
        if ($package.Publisher -cne $Metadata.publisher -or [version]$package.Version -ne [version]$Metadata.version) {
            throw 'Registered identity/version differs from this uninstaller. No packages removed.'
        }
    }
    foreach ($package in $installed) {
        Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
    }
    if (@(Get-SuitePackages $Metadata).Count -ne 0) {
        throw 'Explorer packages remain registered. Application files must be retained; retry uninstall.'
    }
}
