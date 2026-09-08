$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'InstallationRecovery.ps1')
    . (Join-Path $PSScriptRoot 'InstallerLifecycle.ps1')
    $executable = Resolve-SuiteLaunch (Split-Path $PSScriptRoot -Parent)
    # This is a user-requested interactive launch, not a background helper.
    Start-Process -FilePath $executable -WorkingDirectory (Split-Path $executable -Parent)
} catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show($_.Exception.Message, 'Context Suite - installation requires attention') | Out-Null
    exit 1
}
