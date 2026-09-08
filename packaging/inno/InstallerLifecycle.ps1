# Shared by Inno's entry point and repository-only lifecycle tests.
# No platform or filesystem actions occur on dot-source.
function Assert-SuiteLifecycleAdmission {
    param([string] $Root)
    if (-not [IO.Path]::IsPathRooted($Root) -or [IO.Path]::GetFullPath($Root).TrimEnd('\') -eq [IO.Path]::GetPathRoot($Root).TrimEnd('\')) {
        throw 'An absolute installation directory below a filesystem root is required.'
    }
    # No install-time switch can waive the outstanding native acceptance gate.
    if (Test-Path -LiteralPath $Root) {
        throw 'Existing installation detected. Native upgrade/repair acceptance is pending; this internal installer will not change it. Legacy flat-layout and prototype installs require explicit removal.'
    }
    $ancestor = Split-Path $Root -Parent
    while (-not (Test-Path -LiteralPath $ancestor)) { $ancestor = Split-Path $ancestor -Parent }
    Assert-RecoveryPath $ancestor
}

function Assert-SuiteInstallerIdentity {
    param($Expected, $Actual)
    if ($Actual.publisher -cne $Expected.publisher -or
        (($Actual.packages.name | Sort-Object) -join '|') -cne (($Expected.packages.name | Sort-Object) -join '|')) {
        throw 'Installer identity differs from the managed installation.'
    }
}

function Get-SuiteActiveRelease {
    param([string] $Root)
    Assert-RecoveryPath $Root
    if (Test-Path -LiteralPath (Join-Path $Root 'uninstall.json')) { throw 'Uninstall is pending; finish uninstall before launching or updating.' }
    $pointer = Read-RecoveryJson (Join-Path $Root 'active.json')
    if ($pointer.schema -ne 1) { throw 'Invalid active release pointer.' }
    return Read-SuiteRecoveryRelease $Root $pointer.id
}

function Resolve-SuiteLaunch {
    param([string] $Root)
    $journal = $null
    # Do not launch while registration is between releases. Launch does not repair.
    $lockPath = Join-Path $Root 'recovery.lock'
    Assert-RecoveryPath $lockPath
    $lock = [IO.FileStream]::new($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        if (Test-Path -LiteralPath (Join-Path $Root 'recovery.json')) {
            $journal = Read-RecoveryJson (Join-Path $Root 'recovery.json')
            if ($journal.schema -ne 1 -or $journal.phase -notin 'committed', 'rolled-back') {
                throw 'Installation recovery is pending. Run the supported repair installer before launching.'
            }
        }
        $release = Get-SuiteActiveRelease $Root
        if ($null -ne $journal) {
            $expectedId = if ($journal.phase -eq 'committed') { $journal.next } else { $journal.previous }
            if ($release.id -cne $expectedId) { throw 'Journal and launch pointer disagree.' }
        }
        $executable = Join-Path $Root "releases\$($release.id)\app\ContextSuite.Application.exe"
        Assert-RecoveryPath $executable
        $entry = @($release.files | Where-Object path -CEQ 'app/ContextSuite.Application.exe')
        if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath $executable).Hash -ne $entry[0].sha256) {
            throw 'The installed application is damaged. Repair is required.'
        }
        return $executable
    } finally { $lock.Dispose() }
}

function Start-SuiteInstallStage {
    param([string] $Root, $Incoming, [string] $Id)
    if ($Id -cnotmatch '^[a-f0-9]{32}$') { throw 'Invalid stage ID.' }
    if (Test-Path -LiteralPath $Root) {
        Assert-RecoveryPath $Root
        if (-not (Test-Path -LiteralPath (Join-Path $Root 'active.json'))) {
            throw 'Incomplete or legacy installation requires recovery before staging another payload.'
        }
        if (Test-Path -LiteralPath (Join-Path $Root 'recovery.json')) {
            $journal = Read-RecoveryJson (Join-Path $Root 'recovery.json')
            if ($journal.phase -eq 'pending') { Invoke-SuiteRecovery $Root | Out-Null }
        }
        $previous = Get-SuiteActiveRelease $Root
        Assert-SuiteInstallerIdentity $Incoming $previous
        $next = $Incoming | ConvertTo-Json -Depth 12 | ConvertFrom-Json
        $next.id = $Id
        Assert-SuiteRecoveryPair $previous $next | Out-Null
    } else {
        Assert-SuiteLifecycleAdmission $Root
        New-Item -ItemType Directory -Path $Root -Force | Out-Null
        # A failed first install has no active pointer. Keep enough identity
        # information to refuse foreign packages and safely retry unregistration.
        Write-RecoveryJson (Join-Path $Root 'bootstrap.json') ([ordered]@{
            schema = 1; id = $Id; publisher = $Incoming.publisher;
            version = $Incoming.version; packages = $Incoming.packages
        })
    }
    $releases = Join-Path $Root 'releases'
    if (-not (Test-Path -LiteralPath $releases)) { New-Item -ItemType Directory -Path $releases | Out-Null }
    Assert-RecoveryPath $releases
    $stage = Join-Path $releases $Id
    if (Test-Path -LiteralPath $stage) { throw 'Stage already exists; immutable releases are never overwritten.' }
    New-Item -ItemType Directory -Path $stage | Out-Null
    return $stage
}

function Complete-SuiteInstallStage {
    param([string] $Root, $Incoming, [string] $Id)
    if ($Id -cnotmatch '^[a-f0-9]{32}$') { throw 'Invalid stage ID.' }
    $directory = Join-Path $Root "releases\$Id"
    $descriptorPath = Join-Path $directory 'release.json'
    $descriptor = Read-RecoveryJson $descriptorPath
    Assert-SuiteInstallerIdentity $Incoming $descriptor
    if ($descriptor.version -cne $Incoming.version -or
        (($descriptor.files | Sort-Object path | ConvertTo-Json -Depth 4 -Compress) -cne
         ($Incoming.files | Sort-Object path | ConvertTo-Json -Depth 4 -Compress))) {
        throw 'Staged inventory differs from the installer template.'
    }
    # Inno copies the builder's template. Bind it to this unique staging run.
    $descriptor.id = $Id
    Write-RecoveryJson $descriptorPath $descriptor
    $next = Read-SuiteRecoveryRelease $Root $Id -VerifyApplication
    if (Test-Path -LiteralPath (Join-Path $Root 'active.json')) {
        return Invoke-SuiteRecovery $Root $Id
    }
    $bootstrap = Read-RecoveryJson (Join-Path $Root 'bootstrap.json')
    if ($bootstrap.schema -ne 1 -or $bootstrap.id -cne $Id -or $bootstrap.version -cne $next.version) {
        throw 'First-install journal does not match the staged release.'
    }
    Assert-SuiteInstallerIdentity $bootstrap $next
    $lockPath = Join-Path $Root 'recovery.lock'
    if (Test-Path -LiteralPath $lockPath) { Assert-RecoveryPath $lockPath }
    $lock = [IO.FileStream]::new($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        # Retrying an interrupted first registration may find only some roots.
        Assert-SuiteRecoveryOwnership $next $next
        try {
            Set-SuiteRecoveryRegistered $Root $next $next $next
            Write-RecoveryJson (Join-Path $Root 'active.json') ([ordered]@{ schema = 1; id = $Id })
        } catch {
            $failure = $_.Exception.Message
            # If the durable pointer committed, do not undo the successful switch.
            if (Test-Path -LiteralPath (Join-Path $Root 'active.json')) {
                $pointer = Read-RecoveryJson (Join-Path $Root 'active.json')
                if ($pointer.schema -eq 1 -and $pointer.id -ceq $Id) {
                    Assert-SuiteRecoveryRegistered $next
                    return 'installed'
                }
            }
            Uninstall-SuitePackages $next
            throw "First installation failed; registrations removed and payload retained for recovery. $failure"
        }
        return 'installed'
    } finally { $lock.Dispose() }
}

function Uninstall-SuiteActiveRelease {
    param([string] $Root, $Expected)
    Assert-RecoveryPath $Root
    # Establish ownership before even attempting pending-update compensation.
    if (Test-Path -LiteralPath (Join-Path $Root 'uninstall.json')) {
        $owner = Read-RecoveryJson (Join-Path $Root 'uninstall.json')
    } elseif (Test-Path -LiteralPath (Join-Path $Root 'active.json')) {
        $owner = Get-SuiteActiveRelease $Root
    } else {
        $owner = Read-RecoveryJson (Join-Path $Root 'bootstrap.json')
    }
    Assert-SuiteInstallerIdentity $Expected $owner
    if (-not (Test-Path -LiteralPath (Join-Path $Root 'uninstall.json')) -and
        (Test-Path -LiteralPath (Join-Path $Root 'recovery.json'))) {
        $journal = Read-RecoveryJson (Join-Path $Root 'recovery.json')
        if ($journal.phase -eq 'pending') { Invoke-SuiteRecovery $Root | Out-Null }
    }
    $lockPath = Join-Path $Root 'recovery.lock'
    if (Test-Path -LiteralPath $lockPath) { Assert-RecoveryPath $lockPath }
    $lock = [IO.FileStream]::new($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { return Remove-SuiteActiveRegistrations $Root $Expected }
    finally { $lock.Dispose() }
}

function Remove-SuiteActiveRegistrations {
    param([string] $Root, $Expected)
    # Caller owns recovery.lock. The marker prevents launch between successful
    # unregistration and Inno's later file cleanup, after this helper exits.
    # Validate every existing path before allowing Inno's own file ledger to run.
    foreach ($item in Get-ChildItem -LiteralPath $Root -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Uninstall refuses reparse points.' }
    }
    $markerPath = Join-Path $Root 'uninstall.json'
    if (Test-Path -LiteralPath $markerPath) {
        $release = Read-RecoveryJson $markerPath
        if ($release.schema -ne 1) { throw 'Invalid uninstall journal.' }
    } else {
        if (Test-Path -LiteralPath (Join-Path $Root 'active.json')) {
            $release = Get-SuiteActiveRelease $Root
        } else {
            $release = Read-RecoveryJson (Join-Path $Root 'bootstrap.json')
            if ($release.schema -ne 1) { throw 'Invalid first-install journal.' }
        }
        Assert-SuiteInstallerIdentity $Expected $release
        Write-RecoveryJson $markerPath $release
    }
    Assert-SuiteInstallerIdentity $Expected $release
    if ($release.version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid uninstall version.' }
    # Use the active release's version, never the latest attempted installer's.
    Uninstall-SuitePackages $release
    return 'unregistered'
}
