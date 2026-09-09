[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
# Superset of existing lifecycle contracts. All Windows registration APIs below
# are mocks; fixture files stay in .codex-temp. Never writes the host registry.
. (Join-Path $PSScriptRoot 'Test-InstallerLifecycle.ps1')
$baseline = $script:passed
function Read-SuiteClassicKey {
    param([string] $Path)
    return $script:classicKeys[$Path]
}
function Write-SuiteClassicKey {
    param([string] $Path, [hashtable] $Values)
    $script:classicWrites++
    $script:classicKeys[$Path] = [pscustomobject]@{ Values = $Values.Clone(); Children = @() }
    if ($script:classicWrites -eq $script:failClassicWrite) { throw 'Injected classic write failure' }
}
function Remove-SuiteClassicKey {
    param([string] $Path)
    $script:classicKeys.Remove($Path)
}

foreach ($fault in 0, 1, 2, 3, 4, 5, 6) {
    New-RecoveryFixture
    $script:registered = @(); $script:classicKeys = @{}; $script:classicWrites = 0; $script:failClassicWrite = $fault
    $incoming = $fixture.old
    $incoming | Add-Member NoteProperty menuMode 'classic'
    $dll = Join-Path $fixture.oldDirectory 'app\ContextSuite.Shell.dll'
    [IO.File]::WriteAllText($dll, 'disposable mock DLL, never loaded')
    $incoming.files += [pscustomobject]@{ path = 'app/ContextSuite.Shell.dll'; sha256 = (Get-FileHash $dll).Hash }
    Write-RecoveryJson (Join-Path $fixture.oldDirectory 'release.json') $incoming
    $root = Join-Path $scratch ('classic-' + [guid]::NewGuid().ToString('N'))
    $id = Copy-StageFixture $root $incoming $fixture.oldDirectory
    Assert-SuiteClassicAvailable
    if ($fault) {
        Expect-RecoveryFailure { Complete-SuiteInstallStage $root $incoming $id } 'Injected classic'
        Assert-RecoveryTest ($script:classicKeys.Count -eq 0 -and -not (Test-Path (Join-Path $root 'active.json'))) "Classic write failure $fault rolls back without activating payload"
        $script:failClassicWrite = 0
    }
    Assert-RecoveryTest ((Complete-SuiteInstallStage $root $incoming $id) -eq 'installed') "Classic installation/retry succeeds ($fault)"
    $active = Get-SuiteActiveRelease $root
    Assert-SuiteRecoveryRegistered $active
    Assert-RecoveryTest ($script:classicKeys.Count -eq 6 -and $script:addCount -eq 0 -and $script:registered.Count -eq 0 -and
        (Get-SuiteMenuMode $active) -eq 'classic') 'Classic mode is persisted and registers no modern packages'
    Expect-RecoveryFailure { Assert-SuiteClassicAvailable } 'Existing classic'
    if ($fault -eq 0) {
        $definition = @(Get-SuiteClassicDefinitions $active)[0]
        $saved = $script:classicKeys[$definition.Path].Values['']
        $script:classicKeys[$definition.Path].Values[''] = 'foreign.dll'
        Expect-RecoveryFailure { Uninstall-SuiteActiveRelease $root $incoming } 'Modified classic'
        Assert-RecoveryTest ($script:classicKeys.Count -eq 6) 'Foreign classic values stop uninstall before any deletion'
        $script:classicKeys[$definition.Path].Values[''] = $saved
    }
    Assert-RecoveryTest ((Uninstall-SuiteActiveRelease $root $incoming) -eq 'unregistered' -and $script:classicKeys.Count -eq 0) 'Classic uninstall uses persisted mode and removes only owned leaf keys'
    Uninstall-SuiteActiveRelease $root $incoming | Out-Null
}
Expect-RecoveryFailure { Get-SuiteMenuMode ([pscustomobject]@{ menuMode = 'unexpected' }) } 'Invalid context menu'
Assert-RecoveryTest ((Get-SuiteMenuMode ([pscustomobject]@{})) -eq 'modern') 'Older descriptors retain modern behavior'
$iss = Get-Content (Join-Path $repository 'packaging/inno/ContextSuite.iss') -Raw
Assert-RecoveryTest ($iss.Contains("GetPreviousData('MenuMode', '')") -and $iss.Contains("PreviousMode <> 'modern'") -and
    $iss.Contains('RegQueryStringValue(HKCU64') -and $iss.Contains("if OverrideValue = ''") -and
    $iss.Contains("MenuPage.SelectedValueIndex := 0") -and -not $iss.Contains('RegWrite')) 'Installer prioritizes remembered choice, reads classic override, defaults modern, never changes global preference'
Write-Output "$($script:passed - $baseline) classic checks passed; native Explorer acceptance remains separate."
