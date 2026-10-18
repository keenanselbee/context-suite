[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
# Reuse legacy lifecycle coverage and its mocked Appx/signature boundary.
. (Join-Path $PSScriptRoot 'Test-InstallerLifecycle.ps1')
$legacyCount = $script:passed
function Read-SuiteClassicKey {
    param([string] $Path)
    return $script:classicKeys[$Path]
}
function Write-SuiteClassicKey {
    param([string] $Path, [hashtable] $Values)
    $script:classicWrites++
    $script:classicKeys[$Path] = [pscustomobject]@{ Values = $Values.Clone(); Children = @() }
    if ($script:classicWrites -eq $script:failClassicWrite) {
        $script:failClassicWrite = 0
        throw 'Injected classic write failure'
    }
}
function Remove-SuiteClassicKey {
    param([string] $Path)
    $script:classicKeys.Remove($Path)
}
function New-SelectedRelease {
    param([string] $Root, [string[]] $Tools, [string] $Version, [string] $Mode)
    $id = [guid]::NewGuid().ToString('N')
    $directory = Join-Path $Root "releases\$id"
    New-Item -ItemType Directory -Path (Join-Path $directory 'app'), (Join-Path $directory 'packages') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $directory 'app\ContextSuite.Application.exe'), "fixture $Version")
    [IO.File]::WriteAllText((Join-Path $directory 'app\ContextSuite.Shell.dll'), 'mock shell')
    Write-RecoveryJson (Join-Path $directory 'app\installation-tools.json') ([ordered]@{ schema = 1; tools = $Tools })
    $packages = @()
    foreach ($tool in $Tools) {
        $name = "ContextSuite.RecoveryTest.$tool"
        $relative = "packages/$name.msix"
        $zip = [IO.Compression.ZipFile]::Open((Join-Path $directory $relative), [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entry = $zip.CreateEntry('AppxManifest.xml')
            $entry.LastWriteTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write('<Package><Identity Name="' + $name + '" Version="' + $Version + '" Publisher="CN=Recovery Tests" ProcessorArchitecture="x64"/><Applications><Application Executable="ContextSuite.Application.exe"/></Applications></Package>') }
            finally { $writer.Dispose() }
        } finally { $zip.Dispose() }
        $packages += [pscustomobject]@{ name = $name; file = $relative; sha256 = (Get-FileHash -LiteralPath (Join-Path $directory $relative)).Hash }
    }
    $files = @(Get-ChildItem -LiteralPath $directory -File -Recurse | ForEach-Object {
        [pscustomobject]@{ path = $_.FullName.Substring($directory.Length + 1).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    $release = [pscustomobject]@{ schema = 2; id = $id; publisher = 'CN=Recovery Tests'; version = $Version;
        packages = $packages; files = $files; tools = $Tools; menuMode = $Mode; applicationDirectory = (Join-Path $directory 'app') }
    Write-RecoveryJson (Join-Path $directory 'release.json') $release
    return $release
}
function New-SelectionFixture {
    param([string[]] $Before, [string[]] $After, [string] $Mode)
    $script:failure = ''; $script:addCount = 0; $script:removeCount = 0; $script:blockRollback = $false; $script:badSignature = $false
    $script:registered = @(); $script:classicKeys = @{}; $script:classicWrites = 0; $script:failClassicWrite = 0
    $root = Join-Path $scratch ('selection-' + [guid]::NewGuid().ToString('N'))
    $old = New-SelectedRelease $root $Before '1.0.0.0' $Mode
    $next = New-SelectedRelease $root $After '1.1.0.0' $Mode
    $script:fixture = [pscustomobject]@{ root = $root; old = $old; next = $next;
        oldDirectory = (Join-Path $root "releases\$($old.id)"); nextDirectory = (Join-Path $root "releases\$($next.id)") }
    $script:catalog = [pscustomobject]@{ publisher = $old.publisher; packages = @('Analyze', 'Convert', 'Optimize' | ForEach-Object { [pscustomobject]@{ name = "ContextSuite.RecoveryTest.$_" } }) }
    Write-RecoveryJson (Join-Path $root 'active.json') ([ordered]@{ schema = 1; id = $old.id })
    Set-SuiteRecoveryRegistered $root $old $old $old
}
$selections = @(
    @('Analyze'), @('Convert'), @('Optimize'), @('Analyze', 'Convert'),
    @('Analyze', 'Optimize'), @('Convert', 'Optimize'), @('Analyze', 'Convert', 'Optimize')
)
foreach ($mode in 'modern', 'classic') {
    for ($index = 0; $index -lt $selections.Count; $index++) {
        $tools = $selections[$index]
        $nextTools = $selections[($index + 3) % $selections.Count]
        $label = "$mode/$($tools -join '+')"
        New-SelectionFixture $tools $nextTools $mode
        $script:registered = @(); $script:classicKeys = @{}
        $root = Join-Path $scratch ('selected-first-' + [guid]::NewGuid().ToString('N'))
        $id = Copy-StageFixture $root $fixture.old $fixture.oldDirectory
        Assert-RecoveryTest ((Complete-SuiteInstallStage $root $fixture.old $id) -eq 'installed') "$label first install"
        $active = Get-SuiteActiveRelease $root
        Assert-SuiteRecoveryRegistered $active
        Assert-RecoveryTest ((@(Get-SuiteReleaseTools $active) -join '|') -ceq ($tools -join '|')) "$label persists exact selection"
        $count = if ($mode -eq 'classic') { $script:classicKeys.Count / 2 } else { $script:registered.Count }
        Assert-RecoveryTest ($count -eq $tools.Count) "$label registers only selected commands"
        Assert-RecoveryTest ((Uninstall-SuiteActiveRelease $root $script:catalog) -eq 'unregistered' -and
            $script:registered.Count -eq 0 -and $script:classicKeys.Count -eq 0) "$label catalog uninstall removes selected commands"

        New-SelectionFixture $tools $nextTools $mode
        Assert-RecoveryTest ((Invoke-SuiteRecovery $fixture.root $fixture.next.id) -eq 'upgrade') "$label selection upgrade"
        Assert-SuiteRecoveryRegistered $fixture.next
        $count = if ($mode -eq 'classic') { $script:classicKeys.Count / 2 } else { $script:registered.Count }
        Assert-RecoveryTest ($count -eq $nextTools.Count) "$label upgrade removes deselected commands"

        New-SelectionFixture $tools $nextTools $mode
        if ($mode -eq 'classic') { $script:failClassicWrite = $script:classicWrites + 1 }
        else { $script:failure = 'add-after-' + ($script:addCount + 1) }
        Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root $fixture.next.id } 'previous registrations restored'
        Assert-SuiteRecoveryRegistered $fixture.old
        $count = if ($mode -eq 'classic') { $script:classicKeys.Count / 2 } else { $script:registered.Count }
        Assert-RecoveryTest ($count -eq $tools.Count -and (Get-SuiteActiveRelease $fixture.root).id -ceq $fixture.old.id) "$label partial upgrade restores old selection"
    }
}
foreach ($mode in 'modern', 'classic') {
    New-SelectionFixture @('Analyze') @('Convert') $mode
    if ($mode -eq 'classic') {
        $definition = @(Get-SuiteClassicDefinitions $fixture.next)[0]
        $values = $definition.Values.Clone(); $values.ContextSuiteOwner = 'foreign-owner'
        $script:classicKeys[$definition.Path] = [pscustomobject]@{ Values = $values; Children = @() }
    } else {
        $script:registered += [pscustomobject]@{ Name = $fixture.next.packages[0].name; Publisher = 'CN=Foreign';
            Version = $fixture.next.version; PackageFullName = 'foreign-package' }
    }
    $before = @($script:registered, $script:classicKeys) | ConvertTo-Json -Depth 8 -Compress
    Expect-RecoveryFailure { Invoke-SuiteRecovery $fixture.root $fixture.next.id } $(if ($mode -eq 'classic') { 'Foreign or modified' } else { 'Unexpected registered' })
    $after = @($script:registered, $script:classicKeys) | ConvertTo-Json -Depth 8 -Compress
    Assert-RecoveryTest ($before -ceq $after -and -not (Test-Path -LiteralPath (Join-Path $fixture.root 'recovery.json'))) "$mode rejects newly selected foreign registration before mutation"
}
New-SelectionFixture @('Analyze') @('Convert') 'modern'
$script:registered = @()
$root = Join-Path $scratch ('selected-bootstrap-' + [guid]::NewGuid().ToString('N'))
$id = [guid]::NewGuid().ToString('N')
Start-SuiteInstallStage $root $fixture.old $id | Out-Null
Assert-RecoveryTest ((Uninstall-SuiteActiveRelease $root $script:catalog) -eq 'unregistered') 'Partial extraction retains selected bootstrap identity for uninstall'
$markerPath = Join-Path $fixture.oldDirectory 'app\installation-tools.json'
Write-RecoveryJson $markerPath ([ordered]@{ schema = 1; tools = @('Convert') })
Expect-RecoveryFailure { Read-SuiteRecoveryRelease $fixture.root $fixture.old.id -VerifyToolSelection } 'tool selection does not match'
Assert-RecoveryTest $true 'Changed installation marker fails before registration'
foreach ($mode in 'modern', 'classic') {
    foreach ($damage in 'missing', 'malformed', 'mismatched') {
        New-SelectionFixture @('Analyze') @('Convert') $mode
        $markerPath = Join-Path $fixture.oldDirectory 'app\installation-tools.json'
        if ($damage -eq 'missing') {
            Assert-RecoveryPath $markerPath
            [IO.File]::Delete($markerPath)
        } elseif ($damage -eq 'malformed') {
            [IO.File]::WriteAllText($markerPath, '{')
        } else {
            Write-RecoveryJson $markerPath ([ordered]@{ schema = 1; tools = @('Convert') })
        }
        [IO.File]::WriteAllText((Join-Path $fixture.root 'recovery.lock'), '')
        $label = "$mode/$damage marker"
        $active = Get-SuiteActiveRelease $fixture.root
        Assert-RecoveryTest ((@(Get-SuiteReleaseTools $active) -join '|') -ceq 'Analyze') "$label retains descriptor ownership"
        $launchRejected = $false
        try { Resolve-SuiteLaunch $fixture.root | Out-Null } catch { $launchRejected = $true }
        Assert-RecoveryTest $launchRejected "$label refuses launch"
        $payloadRejected = $false
        try { Read-SuiteRecoveryRelease $fixture.root $fixture.old.id -VerifyApplication | Out-Null } catch { $payloadRejected = $true }
        Assert-RecoveryTest $payloadRejected "$label refuses full payload verification"
        Assert-SuiteRecoveryRegistered $fixture.old
        Assert-RecoveryTest ((Uninstall-SuiteActiveRelease $fixture.root $script:catalog) -eq 'unregistered' -and
            $script:registered.Count -eq 0 -and $script:classicKeys.Count -eq 0) "$label permits owned uninstall"
    }
    New-SelectionFixture @('Analyze') @('Convert') $mode
    [IO.File]::WriteAllText((Join-Path $fixture.oldDirectory 'app\installation-tools.json'), '{')
    $before = @($script:registered, $script:classicKeys) | ConvertTo-Json -Depth 8 -Compress
    $script:badSignature = $true
    Expect-RecoveryFailure { Uninstall-SuiteActiveRelease $fixture.root $script:catalog } 'trusted package signatures'
    $script:badSignature = $false
    $after = @($script:registered, $script:classicKeys) | ConvertTo-Json -Depth 8 -Compress
    Assert-RecoveryTest ($before -ceq $after -and -not (Test-Path -LiteralPath (Join-Path $fixture.root 'uninstall.json'))) "$mode damaged marker does not bypass signature ownership"

    New-SelectionFixture @('Analyze') @('Convert') $mode
    $script:registered = @(); $script:classicKeys = @{}
    $root = Join-Path $scratch ('damaged-stage-' + [guid]::NewGuid().ToString('N'))
    $id = Copy-StageFixture $root $fixture.old $fixture.oldDirectory
    [IO.File]::WriteAllText((Join-Path $root "releases\$id\app\installation-tools.json"), '{')
    Expect-RecoveryFailure { Complete-SuiteInstallStage $root $fixture.old $id } 'hash mismatch'
    Assert-RecoveryTest ($script:registered.Count -eq 0 -and $script:classicKeys.Count -eq 0 -and
        -not (Test-Path -LiteralPath (Join-Path $root 'active.json'))) "$mode damaged staged marker fails before registration"
}
New-SelectionFixture @('Analyze') @('Convert') 'modern'
$fixture.next.version = $fixture.old.version
Expect-RecoveryFailure { Assert-SuiteRecoveryPair $fixture.old $fixture.next } 'Same-version repair'
Assert-RecoveryTest $true 'Same-version repair refuses changed selection'
$fixture.old.schema = 1
Write-RecoveryJson (Join-Path $fixture.oldDirectory 'release.json') $fixture.old
Expect-RecoveryFailure { Read-SuiteRecoveryRelease $fixture.root $fixture.old.id } 'legacy'
Assert-RecoveryTest $true 'Legacy schema cannot represent a partial selection'
Write-Output "$($script:passed - $legacyCount) selection checks plus $legacyCount legacy checks passed. Appx and registry operations were mocked; native upgrade acceptance remains gated."
Write-Output "Fixture evidence retained at $scratch"
