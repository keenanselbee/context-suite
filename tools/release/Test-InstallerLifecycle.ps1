[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
# Reuse the full recovery fault matrix and its scoped fake platform. This script
# is the CI superset, not a second run alongside Test-InstallerRecovery.ps1.
. (Join-Path $PSScriptRoot 'Test-InstallerRecovery.ps1')
. (Join-Path $repository 'packaging\inno\ShellRegistration.ps1')
. (Join-Path $repository 'packaging\inno\InstallerLifecycle.ps1')
$backendCount = $script:passed

function Get-InstallationSnapshot {
    param([string] $Root)
    return @(Get-ChildItem -LiteralPath $Root -Recurse -Force | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($Root.Length); directory = $_.PSIsContainer;
            lastWriteUtc = $_.LastWriteTimeUtc.Ticks; attributes = [int]$_.Attributes;
            hash = $(if (-not $_.PSIsContainer) { (Get-FileHash -LiteralPath $_.FullName).Hash }) }
    }) | ConvertTo-Json -Depth 4 -Compress
}

foreach ($mismatch in 'publisher', 'package-name') {
    foreach ($committedPointer in $false, $true) {
        New-RecoveryFixture
        if ($committedPointer) { Invoke-SuiteRecovery $fixture.root $fixture.next.id | Out-Null }
        Write-PendingFixture
        # Both journal states would mutate files, and rollback can change Appx.
        $incoming = $fixture.next | ConvertTo-Json -Depth 12 | ConvertFrom-Json
        if ($mismatch -eq 'publisher') { $incoming.publisher = 'CN=Other' }
        else { $incoming.packages[0].name = 'ContextSuite.Foreign.Analyze' }
        $beforeFiles = Get-InstallationSnapshot $fixture.root
        $beforePackages = $script:registered | ConvertTo-Json -Depth 4 -Compress
        $beforeCalls = $script:addCount + $script:removeCount
        Expect-RecoveryFailure { Start-SuiteInstallStage $fixture.root $incoming ([guid]::NewGuid().ToString('N')) } 'identity differs'
        Assert-RecoveryTest ((Get-InstallationSnapshot $fixture.root) -ceq $beforeFiles -and
            ($script:registered | ConvertTo-Json -Depth 4 -Compress) -ceq $beforePackages -and
            ($script:addCount + $script:removeCount) -eq $beforeCalls) "Stage rejects $mismatch without any state changes (committed pointer: $committedPointer)"
    }
}

function Copy-StageFixture {
    param([string] $Root, $Release, [string] $Source)
    $id = [guid]::NewGuid().ToString('N')
    $stage = Start-SuiteInstallStage $Root $Release $id
    # Simulate only Inno's file extraction, not its native uninstall file ledger.
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $stage -Recurse
    }
    return $id
}

New-RecoveryFixture
$script:registered = @()
$root = Join-Path $scratch ('fresh-' + [guid]::NewGuid().ToString('N'))
Assert-SuiteLifecycleAdmission $root
$id = Copy-StageFixture $root $fixture.old $fixture.oldDirectory
Assert-RecoveryTest (-not (Test-Path -LiteralPath (Join-Path $root 'active.json'))) 'Staging does not publish the active pointer'
Assert-RecoveryTest ((Complete-SuiteInstallStage $root $fixture.old $id) -eq 'installed') 'First install commits a verified versioned payload'
$launch = Resolve-SuiteLaunch $root
Assert-RecoveryTest ($launch -eq (Join-Path $root "releases\$id\app\ContextSuite.Application.exe")) 'Launch resolves the active payload'
Expect-RecoveryFailure { Assert-SuiteLifecycleAdmission $root } 'Native upgrade/repair acceptance is pending'
Assert-RecoveryTest $true 'Shipping admission remains closed for existing installations'
Expect-RecoveryFailure { Start-SuiteInstallStage $root $fixture.old $id } 'separate payloads'
Assert-RecoveryTest $true 'Staging refuses to overwrite the active release'
$nextId = Copy-StageFixture $root $fixture.next $fixture.nextDirectory
Assert-RecoveryTest ((Resolve-SuiteLaunch $root) -eq $launch) 'Staged upgrade leaves launch on the prior release'
Assert-RecoveryTest ((Complete-SuiteInstallStage $root $fixture.next $nextId) -eq 'upgrade') 'Lifecycle coordinator upgrades via recovery journal'
Assert-RecoveryTest ((Resolve-SuiteLaunch $root) -like "*\$nextId\app\ContextSuite.Application.exe") 'Launch follows the committed upgrade'
# A finalized successful upgrade must not prevent repairing later damage.
[IO.File]::WriteAllText((Join-Path $root "releases\$nextId\app\ContextSuite.Application.exe"), 'damaged')
Expect-RecoveryFailure { Resolve-SuiteLaunch $root } 'damaged'
Assert-RecoveryTest $true 'Damaged application is not launched'
$repairId = Copy-StageFixture $root $fixture.next $fixture.nextDirectory
Assert-RecoveryTest ((Complete-SuiteInstallStage $root $fixture.next $repairId) -eq 'repair') 'Repair works after a finalized upgrade and later app damage'
Assert-RecoveryTest ((Resolve-SuiteLaunch $root) -like "*\$repairId\app\ContextSuite.Application.exe") 'Repair selects a separate healthy payload'
$script:registered = @($script:registered | Select-Object -Skip 1)
$partialRepairId = Copy-StageFixture $root $fixture.next $fixture.nextDirectory
Assert-RecoveryTest ((Complete-SuiteInstallStage $root $fixture.next $partialRepairId) -eq 'repair') 'Repair restores missing registration after a finalized prior repair'
$repairId = $partialRepairId
$script:failure = 'add-after-' + ($script:addCount + 2)
$failedId = Copy-StageFixture $root $fixture.next $fixture.nextDirectory
Expect-RecoveryFailure { Complete-SuiteInstallStage $root $fixture.next $failedId } 'previous registrations restored'
Assert-RecoveryTest ((Resolve-SuiteLaunch $root) -like "*\$repairId\app\ContextSuite.Application.exe") 'Failed repair retains the committed launch target'
$script:failure = 'remove-before-' + ($script:removeCount + 1)
Expect-RecoveryFailure { Uninstall-SuiteActiveRelease $root $fixture.next } 'Injected'
Expect-RecoveryFailure { Resolve-SuiteLaunch $root } 'Uninstall is pending'
Assert-RecoveryTest (Test-Path -LiteralPath (Join-Path $root "releases\$repairId")) 'Failed uninstall retains payloads and blocks launch'
Assert-RecoveryTest ((Uninstall-SuiteActiveRelease $root $fixture.next) -eq 'unregistered' -and $script:registered.Count -eq 0) 'Uninstall retries its journal without re-registering packages'
Assert-RecoveryTest ((Uninstall-SuiteActiveRelease $root $fixture.next) -eq 'unregistered') 'Unregistration is idempotent before native file cleanup'

foreach ($point in 'add-before-1', 'add-after-2', 'pointer-before', 'pointer-after') {
    New-RecoveryFixture
    $script:registered = @()
    $root = Join-Path $scratch ('first-failure-' + [guid]::NewGuid().ToString('N'))
    $id = Copy-StageFixture $root $fixture.old $fixture.oldDirectory
    $script:failure = $point
    if ($point -eq 'pointer-after') {
        Assert-RecoveryTest ((Complete-SuiteInstallStage $root $fixture.old $id) -eq 'installed') 'First pointer commit is recognized after an error'
    } else {
        Expect-RecoveryFailure { Complete-SuiteInstallStage $root $fixture.old $id } 'First installation failed'
        Assert-RecoveryTest ($script:registered.Count -eq 0 -and -not (Test-Path -LiteralPath (Join-Path $root 'active.json'))) "First-install failure $point compensates registrations"
        Assert-RecoveryTest ((Complete-SuiteInstallStage $root $fixture.old $id) -eq 'installed') "First-install retry after $point succeeds"
    }
}

New-RecoveryFixture
$script:registered = @()
$root = Join-Path $scratch ('partial-copy-' + [guid]::NewGuid().ToString('N'))
$id = [guid]::NewGuid().ToString('N')
Start-SuiteInstallStage $root $fixture.old $id | Out-Null
Expect-RecoveryFailure { Complete-SuiteInstallStage $root $fixture.old $id } 'does not exist'
Assert-RecoveryTest ($script:registered.Count -eq 0) 'Incomplete file extraction cannot register a release'
Assert-RecoveryTest ((Uninstall-SuiteActiveRelease $root $fixture.old) -eq 'unregistered') 'Bootstrap supports safe cleanup after incomplete extraction'

New-RecoveryFixture
$foreign = $fixture.next | ConvertTo-Json -Depth 12 | ConvertFrom-Json
$foreign.publisher = 'CN=Other'
Expect-RecoveryFailure { Uninstall-SuiteActiveRelease $fixture.root $foreign } 'identity differs'
Assert-RecoveryTest ($script:removeCount -eq 0) 'Foreign installer cannot unregister the active release'
$legacy = Join-Path $scratch 'legacy'
New-Item -ItemType Directory -Path $legacy | Out-Null
Expect-RecoveryFailure { Start-SuiteInstallStage $legacy $fixture.next ([guid]::NewGuid().ToString('N')) } 'legacy'
Assert-RecoveryTest $true 'Legacy flat layout is not adopted or migrated'

New-RecoveryFixture
Invoke-SuiteRecovery $fixture.root $fixture.next.id | Out-Null
$lock = [IO.FileStream]::new((Join-Path $fixture.root 'recovery.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    Expect-RecoveryFailure { Resolve-SuiteLaunch $fixture.root } 'being used'
    Expect-RecoveryFailure { Uninstall-SuiteActiveRelease $fixture.root $fixture.next } 'being used'
    Assert-RecoveryTest $true 'Launch and uninstall respect the exclusive recovery lock'
} finally { $lock.Dispose() }
$journal = Read-RecoveryJson (Join-Path $fixture.root 'recovery.json')
$journal.phase = 'pending'
Write-RecoveryJson (Join-Path $fixture.root 'recovery.json') $journal
Expect-RecoveryFailure { Resolve-SuiteLaunch $fixture.root } 'recovery is pending'
Assert-RecoveryTest $true 'Launch cannot use a pending transaction'
$foreign = $fixture.old | ConvertTo-Json -Depth 12 | ConvertFrom-Json
$foreign.publisher = 'CN=Other'
$changesBefore = $script:addCount + $script:removeCount
Expect-RecoveryFailure { Uninstall-SuiteActiveRelease $fixture.root $foreign } 'identity differs'
Assert-RecoveryTest (($script:addCount + $script:removeCount) -eq $changesBefore) 'Foreign uninstall cannot initiate pending recovery'
Uninstall-SuiteActiveRelease $fixture.root $fixture.old | Out-Null
Assert-RecoveryTest ($script:registered.Count -eq 0) 'Older installer identity uninstalls the actual active newer version after recovery'
Assert-RecoveryTest ((Get-Content -LiteralPath (Join-Path $fixture.root 'user-data-sentinel.json') -Raw) -eq 'settings / trial / license must remain untouched') 'Lifecycle preserves user data through recovery and uninstall'

New-RecoveryFixture
$script:registered = @()
$root = Join-Path $scratch ('corrupt-stage-' + [guid]::NewGuid().ToString('N'))
$id = Copy-StageFixture $root $fixture.old $fixture.oldDirectory
[IO.File]::WriteAllText((Join-Path $root "releases\$id\app\ContextSuite.Application.exe"), 'bad copy')
Expect-RecoveryFailure { Complete-SuiteInstallStage $root $fixture.old $id } 'hash mismatch'
Assert-RecoveryTest ($script:addCount -eq 0) 'Corrupted extraction is rejected before any registration'
$bootstrap = Read-RecoveryJson (Join-Path $root 'bootstrap.json')
$bootstrap.publisher = 'CN=Foreign'
Write-RecoveryJson (Join-Path $root 'bootstrap.json') $bootstrap
Expect-RecoveryFailure { Uninstall-SuiteActiveRelease $root $fixture.old } 'identity differs'
Assert-RecoveryTest ($script:removeCount -eq 0) 'Foreign first-install journal cannot authorize cleanup'

$inno = Get-Content -LiteralPath (Join-Path $repository 'packaging\inno\ContextSuite.iss') -Raw
$entry = Get-Content -LiteralPath (Join-Path $repository 'packaging\inno\Invoke-InstallerAction.ps1') -Raw
$builder = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Build-InnoInstaller.ps1') -Raw
Assert-RecoveryTest ($inno.Contains('releases\{code:GetReleaseId}') -and $inno.Contains('Launch-Active.ps1')) 'Inno wires versioned extraction and stable launch shortcut'
Assert-RecoveryTest ($entry.Contains('Complete-SuiteInstallStage') -and $entry.Contains('Uninstall-SuiteActiveRelease') -and $entry.Contains('Assert-SuiteLifecycleAdmission')) 'Shipping dispatcher wires lifecycle and acceptance gate'
Assert-RecoveryTest ($builder.Contains("'InstallerLifecycle.ps1'") -and $builder.Contains("'InstallationRecovery.ps1'") -and $builder.Contains('nativeUpgradeAdmission = $false')) 'Builder includes lifecycle helpers and records closed native gate'
Assert-RecoveryTest ($inno.Contains('function InitializeUninstall') -and $inno.Contains('WaitForSingleObject(LifecycleMutex, 0)') -and $inno.Contains('procedure DeinitializeUninstall')) 'Setup and uninstall share an atomic process-lifetime mutex'
Write-Output "$($script:passed - $backendCount) lifecycle integration checks plus $backendCount recovery checks passed. Native Inno file cleanup and Windows Appx behavior are not simulated acceptance."
