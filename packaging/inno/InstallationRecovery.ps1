# Versioned upgrade/repair backend. Dot-sourcing defines functions only.
# Inno lifecycle orchestration and repository-only tests share this module.
function Assert-RecoveryPath {
    param([string] $Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Recovery paths must not traverse reparse points.' }
        $item = if ($item.PSIsContainer) { $item.Parent } else { $item.Directory }
    }
}

function Read-RecoveryJson {
    param([string] $Path)
    Assert-RecoveryPath $Path
    if ((Get-Item -LiteralPath $Path).Length -gt 1048576) { throw 'Recovery metadata exceeds the size limit.' }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Write-RecoveryJson {
    param([string] $Path, $Value)
    Assert-RecoveryPath (Split-Path $Path -Parent)
    if (Test-Path -LiteralPath $Path) { Assert-RecoveryPath $Path }
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 12))
    $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $Path) }
}

function Read-SuiteRecoveryRelease {
    param([string] $Root, [string] $Id, [switch] $VerifyApplication)
    if ($Id -cnotmatch '^[a-f0-9]{32}$') { throw 'Invalid release ID.' }
    $directory = Join-Path $Root "releases\$Id"
    $release = Read-RecoveryJson (Join-Path $directory 'release.json')
    if ($release.schema -ne 1 -or $release.id -cne $Id -or
        $release.version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
        [string]::IsNullOrWhiteSpace($release.publisher) -or @($release.packages).Count -ne 3 -or
        @($release.files).Count -lt 4 -or @($release.files).Count -gt 4096) { throw 'Invalid recovery release descriptor.' }
    if (($release.version.Split('.') | Where-Object { [int]$_ -gt 65535 }).Count) { throw 'Invalid recovery version.' }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $prefixes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($package in $release.packages) {
        if ($package.name -cnotmatch '^([A-Za-z0-9][A-Za-z0-9.-]{2,45})\.(Analyze|Convert|Optimize)$') { throw 'Invalid recovery package name.' }
        $prefixes.Add($Matches[1]) | Out-Null
        if (-not $names.Add($Matches[2]) -or $package.file -cne "packages/$($package.name).msix" -or
            $package.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid or duplicate recovery package.' }
    }
    if ($prefixes.Count -ne 1) { throw 'Recovery packages must share an identity prefix.' }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $release.files) {
        if ($file.path -notmatch '^(app|packages)/[a-zA-Z0-9_. /-]+$' -or
            $file.path -match '(^|/)\.\.?(/|$)|[. ](/|$)|//' -or -not $paths.Add($file.path) -or
            $file.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Unsafe or duplicate recovery inventory path.' }
        if ($VerifyApplication -or $file.path.StartsWith('packages/')) {
            $path = Join-Path $directory $file.path
            Assert-RecoveryPath $path
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw 'Recovery payload hash mismatch.' }
        }
    }
    if (-not $paths.Contains('app/ContextSuite.Application.exe')) { throw 'Recovery application is missing from inventory.' }
    foreach ($package in $release.packages) {
        $entry = @($release.files | Where-Object path -CEQ $package.file)
        if ($entry.Count -ne 1 -or $entry[0].sha256 -ne $package.sha256) { throw 'Recovery package inventory mismatch.' }
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $directory $package.file)
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -cne $release.publisher) {
            throw 'Recovery requires trusted package signatures matching the publisher.'
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $directory $package.file))
        try {
            $entries = @($zip.Entries | Where-Object FullName -CEQ 'AppxManifest.xml')
            if ($entries.Count -ne 1 -or $entries[0].Length -gt 65536) { throw 'Invalid recovery package manifest.' }
            $reader = [IO.StreamReader]::new($entries[0].Open())
            try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($manifest.Package.Identity.Name -cne $package.name -or
                $manifest.Package.Identity.Version -cne $release.version -or
                $manifest.Package.Identity.Publisher -cne $release.publisher -or
                $manifest.Package.Identity.ProcessorArchitecture -cne 'x64' -or
                $manifest.Package.Applications.Application.Executable -cne 'ContextSuite.Application.exe') {
                throw 'Recovery package manifest does not match descriptor.'
            }
        } finally { $zip.Dispose() }
    }
    if ($VerifyApplication) {
        foreach ($item in Get-ChildItem -LiteralPath $directory -Recurse -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Recovery payload contains a reparse point.' }
            if ($item.PSIsContainer) { continue }
            $relative = $item.FullName.Substring($directory.Length + 1).Replace('\', '/')
            if ($relative -cne 'release.json' -and -not $paths.Contains($relative)) { throw 'Uninventoried recovery payload file.' }
        }
    }
    return $release
}

function Assert-SuiteRecoveryPair {
    param($Previous, $Next)
    if ($Previous.id -ceq $Next.id -or $Previous.publisher -cne $Next.publisher -or
        (($Previous.packages.name | Sort-Object) -join '|') -cne (($Next.packages.name | Sort-Object) -join '|')) {
        throw 'Recovery requires separate payloads with stable package identities and publisher.'
    }
    if ([version]$Next.version -lt [version]$Previous.version) { throw 'Installer downgrades are not allowed.' }
    if ([version]$Next.version -eq [version]$Previous.version) {
        $before = ($Previous.files | Sort-Object path | ForEach-Object { "$($_.path)=$($_.sha256.ToUpperInvariant())" }) -join '|'
        $after = ($Next.files | Sort-Object path | ForEach-Object { "$($_.path)=$($_.sha256.ToUpperInvariant())" }) -join '|'
        if ($before -cne $after) { throw 'Same-version repair must use the exact original release inventory.' }
        return 'repair'
    }
    return 'upgrade'
}

function Get-SuiteRecoveryPackages {
    param($Release)
    $packages = @()
    foreach ($definition in $Release.packages) { $packages += @(Get-AppxPackage -Name $definition.name -ErrorAction Stop) }
    return $packages
}

function Assert-SuiteRecoveryOwnership {
    param($Previous, $Next)
    foreach ($package in @(Get-SuiteRecoveryPackages $Previous)) {
        if ($package.Name -cnotin @($Previous.packages.name) -or $package.Publisher -cne $Previous.publisher -or
            ([version]$package.Version -ne [version]$Previous.version -and [version]$package.Version -ne [version]$Next.version)) {
            throw 'Unexpected registered package owner/version; recovery will not remove it.'
        }
    }
}

function Assert-SuiteRecoveryRegistered {
    param($Release)
    $installed = @(Get-SuiteRecoveryPackages $Release)
    if ($installed.Count -ne 3) { throw 'Recovery registration is incomplete.' }
    foreach ($definition in $Release.packages) {
        if (@($installed | Where-Object { $_.Name -ceq $definition.name -and $_.Publisher -ceq $Release.publisher -and
            [version]$_.Version -eq [version]$Release.version }).Count -ne 1) { throw 'Recovery registration verification failed.' }
    }
}

function Set-SuiteRecoveryRegistered {
    param([string] $Root, $Previous, $Next, $Desired)
    Assert-SuiteRecoveryOwnership $Previous $Next
    foreach ($package in @(Get-SuiteRecoveryPackages $Previous)) {
        Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
    }
    $directory = Join-Path $Root "releases\$($Desired.id)"
    foreach ($definition in $Desired.packages) {
        Add-AppxPackage -Path (Join-Path $directory $definition.file) -ExternalLocation (Join-Path $directory 'app') -ErrorAction Stop
    }
    Assert-SuiteRecoveryRegistered $Desired
}

function Complete-SuiteRecoveryJournal {
    param([string] $Root, $Journal, [switch] $RepairPreflight)
    # Caller owns recovery.lock. Re-read/validate immutable inputs on every retry.
    if ($Journal.schema -ne 1 -or $Journal.phase -notin 'pending', 'committed', 'rolled-back') { throw 'Invalid recovery journal.' }
    $previous = Read-SuiteRecoveryRelease $Root $Journal.previous
    $next = Read-SuiteRecoveryRelease $Root $Journal.next
    foreach ($side in 'previous', 'next') {
        $hash = (Get-FileHash -LiteralPath (Join-Path $Root "releases\$($Journal.$side)\release.json")).Hash
        if ($Journal.($side + 'Hash') -ne $hash) { throw 'Recovery descriptor changed since journal creation.' }
    }
    Assert-SuiteRecoveryPair $previous $next | Out-Null
    $active = Read-RecoveryJson (Join-Path $Root 'active.json')
    if ($active.schema -ne 1 -or $active.id -cnotin @($previous.id, $next.id)) { throw 'Active release differs from journal; manual review required.' }
    if (($Journal.phase -eq 'committed' -and $active.id -cne $next.id) -or
        ($Journal.phase -eq 'rolled-back' -and $active.id -cne $previous.id)) { throw 'Journal and active release disagree.' }
    if ($active.id -ceq $next.id) {
        # An already finalized transaction may later need repair for damaged app
        # files. Pending commit recovery still requires the complete next payload.
        if (-not $RepairPreflight -or $Journal.phase -ne 'committed') {
            Read-SuiteRecoveryRelease $Root $next.id -VerifyApplication | Out-Null
        }
        if ($RepairPreflight -and $Journal.phase -eq 'committed') { Assert-SuiteRecoveryOwnership $next $next }
        else { Assert-SuiteRecoveryRegistered $next }
        $Journal.phase = 'committed'
    } else {
        # Once rolled back, retry is read-only. Pending restores the previous
        # registrations even if an earlier platform call partially succeeded.
        if ($Journal.phase -ne 'rolled-back') { Set-SuiteRecoveryRegistered $Root $previous $next $previous }
        elseif ($RepairPreflight) { Assert-SuiteRecoveryOwnership $previous $previous }
        else { Assert-SuiteRecoveryRegistered $previous }
        $Journal.phase = 'rolled-back'
    }
    Write-RecoveryJson (Join-Path $Root 'recovery.json') $Journal
    return $Journal.phase
}

function Invoke-SuiteRecovery {
    param([Parameter(Mandatory)][string] $Root, [string] $NextReleaseId)
    $Root = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path.TrimEnd('\')
    Assert-RecoveryPath $Root
    $lockPath = Join-Path $Root 'recovery.lock'
    if (Test-Path -LiteralPath $lockPath) { Assert-RecoveryPath $lockPath }
    $lock = [IO.FileStream]::new($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $journalPath = Join-Path $Root 'recovery.json'
        if (Test-Path -LiteralPath $journalPath) {
            $journal = Read-RecoveryJson $journalPath
            if (-not $NextReleaseId -or $journal.phase -eq 'pending') {
                $result = Complete-SuiteRecoveryJournal $Root $journal
                if (-not $NextReleaseId) { return $result }
            } else {
                # Validate terminal metadata before replacing it with a new transaction.
                Complete-SuiteRecoveryJournal $Root $journal -RepairPreflight | Out-Null
            }
        }
        if (-not $NextReleaseId) { return 'no-pending-recovery' }
        $active = Read-RecoveryJson (Join-Path $Root 'active.json')
        if ($active.schema -ne 1) { throw 'Invalid active release pointer.' }
        $previous = Read-SuiteRecoveryRelease $Root $active.id
        $next = Read-SuiteRecoveryRelease $Root $NextReleaseId -VerifyApplication
        $mode = Assert-SuiteRecoveryPair $previous $next
        # A new transaction must start from the previous version, never silently
        # adopt an unrelated target-version registration found without a journal.
        Assert-SuiteRecoveryOwnership $previous $previous
        $journal = [pscustomobject]@{
            schema = 1; phase = 'pending'; previous = $previous.id; next = $next.id; mode = $mode;
            previousHash = (Get-FileHash -LiteralPath (Join-Path $Root "releases\$($previous.id)\release.json")).Hash;
            nextHash = (Get-FileHash -LiteralPath (Join-Path $Root "releases\$($next.id)\release.json")).Hash
        }
        Write-RecoveryJson $journalPath $journal
        try {
            Set-SuiteRecoveryRegistered $Root $previous $next $next
            Write-RecoveryJson (Join-Path $Root 'active.json') ([ordered]@{ schema = 1; id = $next.id })
            $journal.phase = 'committed'
            Write-RecoveryJson $journalPath $journal
            return $mode
        } catch {
            $failure = $_.Exception.Message
            try { $outcome = Complete-SuiteRecoveryJournal $Root (Read-RecoveryJson $journalPath) }
            catch { throw "Update failed; recovery required. Both payloads and the journal were retained. $failure Recovery: $($_.Exception.Message)" }
            if ($outcome -eq 'committed') { return $mode }
            throw "Update failed; previous registrations restored. Both payloads retained. $failure"
        }
    } finally { $lock.Dispose() }
}
