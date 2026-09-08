[CmdletBinding()]
param([string] $Candidate, [string] $IdentityDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repository 'packaging\inno\ShellRegistration.ps1')
$script:passed = 0
$metadata = [pscustomobject]@{
    publisher = 'CN=Test'; version = '0.1.0.0'; packages = @('Analyze', 'Convert', 'Optimize' | ForEach-Object {
        [pscustomobject]@{ name = "ContextSuite.Test.$_"; file = "ContextSuite.Test.$_.msix" }
    })
}
function Reset-TestState {
    $script:registered = @()
    $script:addCount = 0
    $script:removeCount = 0
    $script:failAdd = 0
    $script:failRemove = $false
    $script:addBeforeFailure = $false
    $script:wrongPublisher = $false
}
function Get-AppxPackage {
    param($Name, $ErrorAction)
    return @($script:registered | Where-Object Name -EQ $Name)
}
function Add-AppxPackage {
    param($Path, $ExternalLocation, $ErrorAction)
    $script:addCount++
    if ($script:addCount -eq $script:failAdd -and -not $script:addBeforeFailure) { throw 'Injected Add failure.' }
    $name = [IO.Path]::GetFileNameWithoutExtension($Path)
    $script:registered += [pscustomobject]@{
        Name = $name; Publisher = $(if ($script:wrongPublisher) { 'CN=Other' } else { $metadata.publisher });
        Version = $metadata.version; PackageFullName = "$name-full"
    }
    if ($script:addCount -eq $script:failAdd) { throw 'Injected failure after registration.' }
}
function Remove-AppxPackage {
    param($Package, $ErrorAction)
    $script:removeCount++
    if ($script:failRemove) { throw 'Injected removal failure.' }
    $script:registered = @($script:registered | Where-Object PackageFullName -NE $Package)
}
function Assert-Test {
    param([bool] $Condition, [string] $Name)
    if (-not $Condition) { throw "Contract failed: $Name" }
    $script:passed++
    Write-Output "PASS $Name"
}
function Expect-Failure {
    param([scriptblock] $Action, [string] $Pattern)
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Pattern*") { throw }
        return
    }
    throw 'Expected operation to fail.'
}
Reset-TestState
Install-SuitePackages $metadata 'C:\Test Packages' 'C:\Test App'
Assert-Test ($script:registered.Count -eq 3) 'All three peer identities register'
Uninstall-SuitePackages $metadata
Assert-Test ($script:registered.Count -eq 0) 'Uninstall removes all owned identities'
Uninstall-SuitePackages $metadata
Assert-Test ($script:removeCount -eq 3) 'Uninstall is idempotent when packages are absent'
foreach ($failurePoint in 1, 2, 3) {
    Reset-TestState
    $script:failAdd = $failurePoint
    Expect-Failure { Install-SuitePackages $metadata 'C:\Test' 'C:\Test' } 'rolled back'
    Assert-Test ($script:registered.Count -eq 0) "Registration failure $failurePoint compensates earlier additions"
}
Reset-TestState
$script:failAdd = 2
$script:addBeforeFailure = $true
Expect-Failure { Install-SuitePackages $metadata 'C:\Test' 'C:\Test' } 'rolled back'
Assert-Test ($script:registered.Count -eq 0) 'Failure after platform mutation also rolls back'
Reset-TestState
$script:failAdd = 2
$script:failRemove = $true
Expect-Failure { Install-SuitePackages $metadata 'C:\Test' 'C:\Test' } 'partial registration remains'
Assert-Test ($script:registered.Count -eq 1) 'Rollback failure reports retained registration instead of success'
Reset-TestState
Add-AppxPackage -Path 'ContextSuite.Test.Analyze.msix'
Expect-Failure { Install-SuitePackages $metadata 'C:\Test' 'C:\Test' } 'existing or partial'
Assert-Test ($script:addCount -eq 1 -and $script:removeCount -eq 0) 'Existing/partial installation is untouched'
Reset-TestState
Add-AppxPackage -Path 'Keenan.ContextSuite.Analyze.ShellPrototype.msix'
Expect-Failure { Assert-SuiteInstallAvailable $metadata } 'Development Explorer packages'
Assert-Test ($script:removeCount -eq 0) 'Prototype identities are never silently removed'
Reset-TestState
$script:wrongPublisher = $true
Add-AppxPackage -Path 'ContextSuite.Test.Analyze.msix'
Expect-Failure { Uninstall-SuitePackages $metadata } 'differs from this uninstaller'
Assert-Test ($script:removeCount -eq 0) 'Uninstall refuses a foreign publisher'
Reset-TestState
Add-AppxPackage -Path 'ContextSuite.Test.Analyze.msix'
$script:registered[0].Version = '0.2.0.0'
Expect-Failure { Uninstall-SuitePackages $metadata } 'differs from this uninstaller'
Assert-Test ($script:removeCount -eq 0) 'Uninstall refuses a newer package'
Reset-TestState
Install-SuitePackages $metadata 'C:\Test' 'C:\Test'
$script:failRemove = $true
Expect-Failure { Uninstall-SuitePackages $metadata } 'Injected removal failure'
Assert-Test ($script:registered.Count -eq 3) 'Removal failure propagates so installer retains files'
$scriptSource = Get-Content -LiteralPath (Join-Path $repository 'packaging\inno\ContextSuite.iss') -Raw
Assert-Test ($scriptSource -match 'PrivilegesRequired=lowest' -and $scriptSource -match 'ArchitecturesAllowed=x64os') 'Per-user native x64 installation'
Assert-Test ($scriptSource -match 'WizardStyle=modern dynamic') 'Windows appearance followed'
Assert-Test ($scriptSource -match 'CloseApplications=no' -and $scriptSource -match 'RestartApplications=no') 'Installer does not close or restart applications'
Assert-Test ($scriptSource -notmatch '\[UninstallDelete\]|\[InstallDelete\]|\[Registry\]') 'No broad filesystem/registry cleanup or user-data removal'
Assert-Test ($scriptSource -match 'if RegistrationFailed then Result := 20' -and $scriptSource -match 'Installation incomplete') 'Registration failure is visible and nonzero'
Assert-Test ($scriptSource -match "not RunAction\('Desktop'" -and $scriptSource -match "not RunAction\('VisualCpp'") 'Only missing runtime components installed'
Assert-Test ($scriptSource -notmatch '(?i)https?://|DownloadTemporary|download;') 'Setup contains no dependency downloader'
if ($Candidate -or $IdentityDirectory) {
    if (-not $Candidate -or -not $IdentityDirectory) { throw 'Supply both Candidate and IdentityDirectory for real input rejection tests.' }
    $scratch = Join-Path $repository ('.codex-temp\installer-contracts\' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $IdentityDirectory 'identity-candidates.json') -Destination $scratch
    $original = Get-Content -LiteralPath (Join-Path $scratch 'identity-candidates.json') -Raw
    $realMetadata = $original | ConvertFrom-Json
    foreach ($package in $realMetadata.packages) {
        Copy-Item -LiteralPath (Join-Path $IdentityDirectory $package.file) -Destination $scratch
    }
    $builder = Join-Path $PSScriptRoot 'Build-InnoInstaller.ps1'
    Expect-Failure { & $builder -Candidate $Candidate -IdentityDirectory $scratch | Out-Null } 'Trusted signed identities required'
    Assert-Test $true 'Unsigned identities rejected by default builder'
    foreach ($case in 'candidate', 'path', 'hash', 'version') {
        $altered = $original | ConvertFrom-Json
        $pattern = switch ($case) {
            'candidate' { $altered.candidateManifestSha256 = '0' * 64; 'does not match' }
            'path' { $altered.packages[0].file = '..\outside.msix'; 'Unsafe or duplicate' }
            'hash' { $altered.packages[0].sha256 = '0' * 64; 'hash mismatch' }
            'version' { $altered.version = '0.2.0.0'; 'manifest differs' }
        }
        [IO.File]::WriteAllText((Join-Path $scratch 'identity-candidates.json'), ($altered | ConvertTo-Json -Depth 6))
        Expect-Failure { & $builder -Candidate $Candidate -IdentityDirectory $scratch -UnsignedInternal | Out-Null } $pattern
        Assert-Test $true "Invalid $case input rejected before compilation"
    }
    Write-Output "Retained temporary rejection fixtures: $scratch"
}
Write-Output "$script:passed installer contracts passed with mocked Appx operations. No package registration or installation performed."
