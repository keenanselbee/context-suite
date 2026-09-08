[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Check', 'Runtime', 'Desktop', 'VisualCpp', 'Install', 'Uninstall')][string] $Action,
    [Parameter(Mandatory)][string] $InstallDirectory,
    [Parameter(Mandatory)][string] $ResultPath
)
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'ShellRegistration.ps1')
    $metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer.json') -Raw | ConvertFrom-Json
    if (-not [Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -or
        [Environment]::OSVersion.Version.Build -lt 26100) { throw 'Windows 11 x64 build 26100 or newer is required.' }
    if ($Action -in 'Runtime', 'Desktop', 'VisualCpp') {
        $component = if ($Action -eq 'Runtime') { 'All' } else { $Action }
        & (Join-Path $PSScriptRoot 'Test-Prerequisites.ps1') -Component $component | Out-Null
    } elseif ($Action -eq 'Check' -or $Action -eq 'Install') {
        Assert-SuiteInstallAvailable $metadata
        if ($Action -eq 'Check' -and (Test-Path -LiteralPath $InstallDirectory)) {
            throw 'The installation directory already exists. This first-install candidate will not overwrite it.'
        }
        foreach ($definition in $metadata.packages) {
            $path = Join-Path $PSScriptRoot $definition.file
            $signature = Get-AuthenticodeSignature -LiteralPath $path
            if ((Get-FileHash -LiteralPath $path).Hash -ne $definition.sha256 -or
                $signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -cne $metadata.publisher) {
                throw 'Explorer packages must have the expected hashes and trusted matching publisher signatures. Unsigned internal builds cannot be installed.'
            }
        }
        if ($Action -eq 'Install') {
            Install-SuitePackages $metadata $PSScriptRoot (Join-Path $InstallDirectory 'app')
        }
    } else {
        Uninstall-SuitePackages $metadata
    }
    [IO.File]::WriteAllText($ResultPath, 'Completed.', [Text.UTF8Encoding]::new($false))
    exit 0
} catch {
    [IO.File]::WriteAllText($ResultPath, $_.Exception.Message, [Text.UTF8Encoding]::new($false))
    exit 1
}
