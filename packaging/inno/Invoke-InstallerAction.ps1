[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Check', 'Runtime', 'Desktop', 'VisualCpp', 'Stage', 'Install', 'Uninstall')][string] $Action,
    [Parameter(Mandatory)][string] $InstallDirectory,
    [Parameter(Mandatory)][string] $ResultPath,
    [string] $ReleaseId,
    [ValidateSet('modern', 'classic')][string] $MenuMode = 'modern'
)
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'ShellRegistration.ps1')
    . (Join-Path $PSScriptRoot 'InstallationRecovery.ps1')
    . (Join-Path $PSScriptRoot 'InstallerLifecycle.ps1')
    $metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer.json') -Raw | ConvertFrom-Json
    $metadata.release | Add-Member -Force NoteProperty menuMode $MenuMode
    if (-not [Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -or
        [Environment]::OSVersion.Version.Build -lt 26100) { throw 'Windows 11 x64 build 26100 or newer is required.' }
    if ($Action -in 'Runtime', 'Desktop', 'VisualCpp') {
        $component = if ($Action -eq 'Runtime') { 'All' } else { $Action }
        & (Join-Path $PSScriptRoot 'Test-Prerequisites.ps1') -Component $component | Out-Null
    } elseif ($Action -in 'Check', 'Stage', 'Install') {
        if ($Action -in 'Check', 'Stage') {
            Assert-SuiteLifecycleAdmission $InstallDirectory
            Assert-SuiteInstallAvailable $metadata
            Assert-SuiteClassicAvailable
        }
        foreach ($definition in $metadata.packages) {
            $path = Join-Path $PSScriptRoot $definition.file
            $signature = Get-AuthenticodeSignature -LiteralPath $path
            if ((Get-FileHash -LiteralPath $path).Hash -ne $definition.sha256 -or
                $signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -cne $metadata.publisher) {
                throw 'Explorer packages must have the expected hashes and trusted matching publisher signatures. Unsigned internal builds cannot be installed.'
            }
        }
        if ($Action -eq 'Stage') {
            Start-SuiteInstallStage $InstallDirectory $metadata.release $ReleaseId | Out-Null
        } elseif ($Action -eq 'Install') {
            # Existing-install updates remain inaccessible through Check/Stage
            # until native lifecycle acceptance opens admission in a reviewed change.
            Complete-SuiteInstallStage $InstallDirectory $metadata.release $ReleaseId | Out-Null
        }
    } else {
        Uninstall-SuiteActiveRelease $InstallDirectory $metadata | Out-Null
    }
    [IO.File]::WriteAllText($ResultPath, 'Completed.', [Text.UTF8Encoding]::new($false))
    exit 0
} catch {
    [IO.File]::WriteAllText($ResultPath, $_.Exception.Message, [Text.UTF8Encoding]::new($false))
    exit 1
}
