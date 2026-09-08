[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repository 'packaging\inno\InstallationRecovery.ps1')
$scratch = Join-Path $repository ('.codex-temp\installer-recovery\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$script:passed = 0
$script:realWriter = ${function:Write-RecoveryJson}
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression

# These overrides are scoped to this test script. No native Appx or certificate
# operations occur, even when tests fail. Files are real disposable fixtures.
function Get-AuthenticodeSignature {
    param($LiteralPath)
    [pscustomobject]@{ Status = $(if ($script:badSignature) { 'NotSigned' } else { 'Valid' });
        SignerCertificate = [pscustomobject]@{ Subject = 'CN=Recovery Tests' } }
}
function Get-AppxPackage {
    param($Name, $ErrorAction)
    @($script:registered | Where-Object Name -CEQ $Name)
}
function Test-InjectedFailure {
    param($Point)
    if ($script:failure -eq $Point) { $script:failure = ''; throw "Injected $Point failure" }
}
function Remove-AppxPackage {
    param($Package, $ErrorAction)
    $script:removeCount++
    Test-InjectedFailure "remove-before-$script:removeCount"
    $script:registered = @($script:registered | Where-Object PackageFullName -NE $Package)
    Test-InjectedFailure "remove-after-$script:removeCount"
}
function Add-AppxPackage {
    param($Path, $ExternalLocation, $ErrorAction)
    $script:addCount++
    Test-InjectedFailure "add-before-$script:addCount"
    if ($script:blockRollback -and $ExternalLocation -eq (Join-Path $script:fixture.oldDirectory 'app')) {
        throw 'Injected persistent rollback failure'
    }
    $release = Read-RecoveryJson (Join-Path (Split-Path (Split-Path $Path -Parent) -Parent) 'release.json')
    $name = [IO.Path]::GetFileNameWithoutExtension($Path)
    $script:registered += [pscustomobject]@{ Name = $name; Publisher = $release.publisher;
        Version = $release.version; PackageFullName = "$name-$($release.version)"; ExternalLocation = $ExternalLocation }
    Test-InjectedFailure "add-after-$script:addCount"
}
function Write-RecoveryJson {
    param($Path, $Value)
    $point = if ([IO.Path]::GetFileName($Path) -eq 'active.json') { 'pointer' } else { "journal-$($Value.phase)" }
    Test-InjectedFailure "$point-before"
    & $script:realWriter $Path $Value
    Test-InjectedFailure "$point-after"
}
function Assert-RecoveryTest {
    param([bool] $Condition, [string] $Name)
    if (-not $Condition) { throw "Failed: $Name" }
    $script:passed++
    Write-Output "PASS $Name"
}
function Expect-RecoveryFailure {
    param([scriptblock] $Action, [string] $Pattern)
    try { & $Action | Out-Null } catch {
        if ($_.Exception.Message -notlike "*$Pattern*") { throw }
        return
    }
    throw "Expected failure containing: $Pattern"
}
function New-RecoveryFixture {
    param([string] $NextVersion = '1.1.0.0')
    $script:failure = ''; $script:addCount = 0; $script:removeCount = 0
    $script:badSignature = $false; $script:blockRollback = $false
    $root = Join-Path $scratch ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $releases = @()
    foreach ($version in @('1.0.0.0', $NextVersion)) {
        $id = [guid]::NewGuid().ToString('N')
        $directory = Join-Path $root "releases\$id"
        New-Item -ItemType Directory -Path (Join-Path $directory 'app'), (Join-Path $directory 'packages') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $directory 'app\ContextSuite.Application.exe'), "fixture payload $version")
        $packages = @()
        foreach ($tool in 'Analyze', 'Convert', 'Optimize') {
            $name = "ContextSuite.RecoveryTest.$tool"
            $relative = "packages/$name.msix"
            $zip = [IO.Compression.ZipFile]::Open((Join-Path $directory $relative), [IO.Compression.ZipArchiveMode]::Create)
            try {
                $entry = $zip.CreateEntry('AppxManifest.xml')
                # Fixed timestamp gives byte-identical package fixtures for same-release repair.
                $entry.LastWriteTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $writer = [IO.StreamWriter]::new($entry.Open())
                try { $writer.Write('<Package><Identity Name="' + $name + '" Version="' + $version + '" Publisher="CN=Recovery Tests" ProcessorArchitecture="x64"/><Applications><Application Executable="ContextSuite.Application.exe"/></Applications></Package>') }
                finally { $writer.Dispose() }
            } finally { $zip.Dispose() }
            $packages += [pscustomobject]@{ name = $name; file = $relative; sha256 = (Get-FileHash -LiteralPath (Join-Path $directory $relative)).Hash }
        }
        $files = @(Get-ChildItem -LiteralPath $directory -File -Recurse | ForEach-Object {
            [pscustomobject]@{ path = $_.FullName.Substring($directory.Length + 1).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
        })
        $release = [pscustomobject]@{ schema = 1; id = $id; publisher = 'CN=Recovery Tests'; version = $version; packages = $packages; files = $files }
        Write-RecoveryJson (Join-Path $directory 'release.json') $release
        $releases += $release
    }
    Write-RecoveryJson (Join-Path $root 'active.json') ([pscustomobject]@{ schema = 1; id = $releases[0].id })
    [IO.File]::WriteAllText((Join-Path $root 'user-data-sentinel.json'), 'settings / trial / license must remain untouched')
    $script:registered = @($releases[0].packages | ForEach-Object { [pscustomobject]@{
        Name = $_.name; Publisher = $releases[0].publisher; Version = $releases[0].version;
        PackageFullName = "$($_.name)-$($releases[0].version)"; ExternalLocation = (Join-Path $root "releases\$($releases[0].id)\app")
    } })
    $script:fixture = [pscustomobject]@{ root = $root; old = $releases[0]; next = $releases[1];
        oldDirectory = (Join-Path $root "releases\$($releases[0].id)"); nextDirectory = (Join-Path $root "releases\$($releases[1].id)") }
}
function Assert-PreviousRestored {
    param([string] $Name)
    $pointer = Read-RecoveryJson (Join-Path $script:fixture.root 'active.json')
    Assert-SuiteRecoveryRegistered $script:fixture.old
    $oldPath = Join-Path $script:fixture.oldDirectory 'app'
    Assert-RecoveryTest ($pointer.id -eq $script:fixture.old.id -and
        @($script:registered | Where-Object ExternalLocation -NE $oldPath).Count -eq 0) $Name
}
function Write-PendingFixture {
    param()
    Write-RecoveryJson (Join-Path $script:fixture.root 'recovery.json') ([pscustomobject]@{
        schema = 1; phase = 'pending'; previous = $script:fixture.old.id; next = $script:fixture.next.id; mode = 'upgrade';
        previousHash = (Get-FileHash -LiteralPath (Join-Path $script:fixture.oldDirectory 'release.json')).Hash;
        nextHash = (Get-FileHash -LiteralPath (Join-Path $script:fixture.nextDirectory 'release.json')).Hash
    })
}

New-RecoveryFixture
$outcome = Invoke-SuiteRecovery $fixture.root $fixture.next.id
Assert-RecoveryTest ($outcome -eq 'upgrade' -and (Read-RecoveryJson (Join-Path $fixture.root 'active.json')).id -eq $fixture.next.id) 'Upgrade commits the verified new release'
Assert-SuiteRecoveryRegistered $fixture.next
Assert-RecoveryTest (@($script:registered | Where-Object ExternalLocation -NE (Join-Path $fixture.nextDirectory 'app')).Count -eq 0) 'All three registrations target the new immutable payload'
Assert-RecoveryTest ((Get-Content -LiteralPath (Join-Path $fixture.root 'user-data-sentinel.json') -Raw) -ceq 'settings / trial / license must remain untouched') 'User-data sentinel unchanged'
Assert-RecoveryTest ((Test-Path -LiteralPath $fixture.oldDirectory) -and (Test-Path -LiteralPath $fixture.nextDirectory)) 'Both payloads retained after commit'
$before = $script:addCount + $script:removeCount
Assert-RecoveryTest ((Invoke-SuiteRecovery $fixture.root) -eq 'committed' -and $before -eq ($script:addCount + $script:removeCount)) 'Committed recovery retry does not re-register'

New-RecoveryFixture '1.0.0.0'
$script:registered = @($script:registered | Select-Object -First 1)
# Damaged application file can be repaired from a different verified copy.
[IO.File]::WriteAllText((Join-Path $fixture.oldDirectory 'app\ContextSuite.Application.exe'), 'damaged')
Assert-RecoveryTest ((Invoke-SuiteRecovery $fixture.root $fixture.next.id) -eq 'repair') 'Same-release repair restores partial registration and selects a clean payload'

foreach ($point in @('add-before-1','add-before-2','add-before-3','add-after-1','add-after-2','add-after-3',
    'remove-before-1','remove-before-2','remove-before-3','remove-after-1','remove-after-2','remove-after-3','pointer-before')) {
    New-RecoveryFixture
    $script:failure = $point
    Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root $fixture.next.id } 'previous registrations restored'
    Assert-PreviousRestored "Failure $point restores old pointer and registrations"
}
foreach ($point in 'pointer-after', 'journal-committed-before', 'journal-committed-after') {
    New-RecoveryFixture
    $script:failure = $point
    Assert-RecoveryTest ((Invoke-SuiteRecovery $fixture.root $fixture.next.id) -eq 'upgrade') "Failure $point recognizes already committed update"
    Assert-SuiteRecoveryRegistered $fixture.next
}
foreach ($point in 'journal-pending-before', 'journal-pending-after') {
    New-RecoveryFixture
    $script:failure = $point
    Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root $fixture.next.id } 'Injected'
    Assert-RecoveryTest ($script:addCount -eq 0 -and $script:removeCount -eq 0) "Failure $point changes no registration"
}
New-RecoveryFixture
$script:failure = 'add-after-2'; $script:blockRollback = $true
Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root $fixture.next.id } 'recovery required'
Assert-RecoveryTest ((Read-RecoveryJson (Join-Path $fixture.root 'recovery.json')).phase -eq 'pending') 'Failed rollback preserves pending journal'
$script:blockRollback = $false
Assert-RecoveryTest ((Invoke-SuiteRecovery $fixture.root) -eq 'rolled-back') 'Retry recovers failed rollback'
Assert-PreviousRestored 'Retry restores all previous registrations'
$before = $script:addCount + $script:removeCount
Invoke-SuiteRecovery $fixture.root | Out-Null
Assert-RecoveryTest ($before -eq ($script:addCount + $script:removeCount)) 'Completed rollback retry is idempotent'

New-RecoveryFixture
Write-PendingFixture
$script:registered = @($script:registered | Select-Object -First 1)
Assert-RecoveryTest ((Invoke-SuiteRecovery $fixture.root) -eq 'rolled-back') 'Fresh invocation recovers interruption during removal'
New-RecoveryFixture
Write-PendingFixture
$script:registered = @()
Add-AppxPackage -Path (Join-Path $fixture.nextDirectory $fixture.next.packages[0].file) -ExternalLocation (Join-Path $fixture.nextDirectory 'app')
Assert-RecoveryTest ((Invoke-SuiteRecovery $fixture.root) -eq 'rolled-back') 'Fresh invocation recovers interruption during addition'

foreach ($scenario in 'downgrade','same-version-changed','foreign-publisher','foreign-version','bad-signature','bad-hash','extra-file','bad-id','bad-pointer','bad-inventory','duplicate-inventory','bad-json','missing-old-package','wrong-manifest') {
    New-RecoveryFixture $(if ($scenario -eq 'downgrade') { '0.9.0.0' } elseif ($scenario -eq 'same-version-changed') { '1.0.0.0' } else { '1.1.0.0' })
    $target = $fixture.next.id
    $pattern = switch ($scenario) {
        'downgrade' { 'downgrades' }
        'same-version-changed' {
            $file = Join-Path $fixture.nextDirectory 'app\ContextSuite.Application.exe'
            [IO.File]::WriteAllText($file, 'different release using same version')
            ($fixture.next.files | Where-Object path -EQ 'app/ContextSuite.Application.exe').sha256 = (Get-FileHash -LiteralPath $file).Hash
            Write-RecoveryJson (Join-Path $fixture.nextDirectory 'release.json') $fixture.next
            'exact original'
        }
        'foreign-publisher' { $script:registered[0].Publisher = 'CN=Other'; 'Unexpected registered' }
        'foreign-version' { $script:registered[0].Version = '9.0.0.0'; 'Unexpected registered' }
        'bad-signature' { $script:badSignature = $true; 'trusted package signatures' }
        'bad-hash' { [IO.File]::WriteAllText((Join-Path $fixture.nextDirectory 'app\ContextSuite.Application.exe'), 'bad'); 'hash mismatch' }
        'extra-file' { [IO.File]::WriteAllText((Join-Path $fixture.nextDirectory 'app\extra.dll'), 'bad'); 'Uninventoried' }
        'bad-id' { $target = '..\outside'; 'Invalid release ID' }
        'bad-pointer' { Write-RecoveryJson (Join-Path $fixture.root 'active.json') @{schema=1;id='..\outside'}; 'Invalid release ID' }
        'bad-inventory' { $fixture.next.files[0].path = 'app/../../outside'; Write-RecoveryJson (Join-Path $fixture.nextDirectory 'release.json') $fixture.next; 'Unsafe or duplicate' }
        'duplicate-inventory' { $fixture.next.files += $fixture.next.files[0]; Write-RecoveryJson (Join-Path $fixture.nextDirectory 'release.json') $fixture.next; 'Unsafe or duplicate' }
        'bad-json' { [IO.File]::WriteAllText((Join-Path $fixture.root 'active.json'), '{broken'); 'Invalid' }
        'missing-old-package' { [IO.File]::WriteAllText((Join-Path $fixture.oldDirectory $fixture.old.packages[0].file), 'corrupted rollback input'); 'hash mismatch' }
        'wrong-manifest' {
            $fixture.next.version = '1.2.0.0'
            Write-RecoveryJson (Join-Path $fixture.nextDirectory 'release.json') $fixture.next
            'manifest does not match'
        }
    }
    Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root $target } $pattern
    Assert-RecoveryTest ($script:addCount -eq 0 -and $script:removeCount -eq 0) "Reject $scenario without registration changes"
}
New-RecoveryFixture
$held = [IO.FileStream]::new((Join-Path $fixture.root 'recovery.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root $fixture.next.id } 'being used'
    Assert-RecoveryTest ($script:addCount -eq 0) 'Concurrent recovery rejected by exclusive lock'
} finally { $held.Dispose() }
New-RecoveryFixture
Assert-RecoveryTest ((Invoke-SuiteRecovery $fixture.root) -eq 'no-pending-recovery') 'No journal is a safe no-op'
New-RecoveryFixture
Write-PendingFixture
$descriptor = Read-RecoveryJson (Join-Path $fixture.nextDirectory 'release.json')
$descriptor | Add-Member -NotePropertyName unexpected -NotePropertyValue 'changed after preflight'
Write-RecoveryJson (Join-Path $fixture.nextDirectory 'release.json') $descriptor
Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root } 'changed since journal'
Assert-RecoveryTest ($script:removeCount -eq 0) 'Pending recovery refuses changed descriptor'
New-RecoveryFixture
Write-PendingFixture
Write-RecoveryJson (Join-Path $fixture.root 'active.json') @{schema=1;id=([guid]::NewGuid().ToString('N'))}
Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root } 'differs from journal'
Assert-RecoveryTest ($script:removeCount -eq 0) 'Pending recovery refuses unrelated active pointer'
New-RecoveryFixture
Write-PendingFixture
$journal = Read-RecoveryJson (Join-Path $fixture.root 'recovery.json')
$journal.phase = 'unknown'
Write-RecoveryJson (Join-Path $fixture.root 'recovery.json') $journal
Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root } 'Invalid recovery journal'
Assert-RecoveryTest ($script:removeCount -eq 0) 'Unknown journal phase fails closed'
foreach ($phase in 'committed', 'rolled-back') {
    New-RecoveryFixture
    Write-PendingFixture
    $journal = Read-RecoveryJson (Join-Path $fixture.root 'recovery.json')
    $journal.phase = $phase
    Write-RecoveryJson (Join-Path $fixture.root 'recovery.json') $journal
    if ($phase -eq 'rolled-back') {
        Write-RecoveryJson (Join-Path $fixture.root 'active.json') @{ schema=1; id=$fixture.next.id }
    }
    Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root } 'Journal and active release disagree'
    Assert-RecoveryTest ($script:removeCount -eq 0) "Inconsistent $phase journal cannot mutate registration"
}
New-RecoveryFixture
$link = Join-Path $scratch ('junction-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Junction -Path $link -Target $fixture.root | Out-Null
Expect-RecoveryFailure { Invoke-SuiteRecovery $link $fixture.next.id } 'reparse points'
Assert-RecoveryTest ($script:removeCount -eq 0) 'Reparse-root path rejected before lock or registration'

Write-Output "$script:passed recovery checks passed. Native Appx and certificate verification were mocked. Evidence: $scratch"
