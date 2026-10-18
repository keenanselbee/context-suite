[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug', [switch] $Integration,
    [switch] $KeepScratch)

$ErrorActionPreference = 'Stop'

function Assert-FoundationAncestors {
    param([string] $Path, [switch] $AllowMissing)

    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $current = [IO.Path]::GetPathRoot($fullPath)
    foreach ($part in $fullPath.Substring($current.Length).Split('\', [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $part
        try { $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop }
        catch [Management.Automation.ItemNotFoundException] {
            if ($AllowMissing) { return }
            throw "Missing foundation path during cleanup: $current"
        }
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing linked or non-directory foundation path: $current"
        }
    }
}

function Assert-FoundationFixture {
    param([string] $RepositoryRoot, [string] $RunRoot, [string] $FixtureRoot)

    $repository = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $run = [IO.Path]::GetFullPath($RunRoot).TrimEnd('\')
    $fixture = [IO.Path]::GetFullPath($FixtureRoot).TrimEnd('\')
    $scratch = Join-Path $repository '.codex-temp\foundation-tests'
    if ($run -ne (Join-Path $scratch ([IO.Path]::GetFileName($run))) -or
        [IO.Path]::GetDirectoryName($run) -ne $scratch -or
        [IO.Path]::GetFileName($run) -notmatch '^[0-9a-f]{32}$' -or
        $fixture -ne (Join-Path $run 'fixtures')) {
        throw "Refusing cleanup outside an exact foundation run: $fixture"
    }

    Assert-FoundationAncestors $fixture
}

function Get-FoundationFixtureInventory {
    param([string] $FixtureRoot)

    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($FixtureRoot)
    $entries = [Collections.Generic.List[object]]::new()
    while ($pending.Count) {
        $directory = $pending.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing linked foundation fixture: $($item.FullName)"
            }
            $relative = $item.FullName.Substring($FixtureRoot.Length + 1)
            if ($item.PSIsContainer) {
                $pending.Push($item.FullName)
                $entries.Add([pscustomobject]@{ Path = $relative; Kind = 'directory' })
            } elseif ($item -is [IO.FileInfo]) {
                $entries.Add([pscustomobject]@{ Path = $relative; Kind = 'file'; Length = $item.Length;
                    LastWriteTicks = $item.LastWriteTimeUtc.Ticks;
                    Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256 -ErrorAction Stop).Hash })
            } else {
                throw "Unexpected foundation fixture entry: $($item.FullName)"
            }
        }
    }
    return @($entries | Sort-Object Path)
}

function Remove-FoundationFixture {
    param([string] $RepositoryRoot, [string] $RunRoot, [string] $FixtureRoot, [object[]] $Inventory)

    Assert-FoundationFixture $RepositoryRoot $RunRoot $FixtureRoot
    $current = @(Get-FoundationFixtureInventory $FixtureRoot | ConvertTo-Json -Depth 3 -Compress)
    $recorded = @($Inventory | ConvertTo-Json -Depth 3 -Compress)
    if (($current -join '') -cne ($recorded -join '')) {
        throw "Foundation fixtures changed after receipt; retained at $FixtureRoot"
    }
    foreach ($entry in @($Inventory | Where-Object Kind -eq 'file')) {
        $path = Join-Path $FixtureRoot $entry.Path
        Assert-FoundationAncestors ([IO.Path]::GetDirectoryName($path))
        $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if ($item -isnot [IO.FileInfo] -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            $item.Length -ne $entry.Length -or $item.LastWriteTimeUtc.Ticks -ne $entry.LastWriteTicks -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash -ne $entry.Sha256) {
            throw "Foundation fixture changed during cleanup: $path"
        }
        Assert-FoundationAncestors ([IO.Path]::GetDirectoryName($path))
        Remove-Item -LiteralPath $path -Force -ErrorAction Stop
    }
    foreach ($entry in @($Inventory | Where-Object Kind -eq 'directory' | Sort-Object { $_.Path.Length } -Descending)) {
        $path = Join-Path $FixtureRoot $entry.Path
        Assert-FoundationAncestors $path
        $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Foundation fixture changed during cleanup: $path"
        }
        Assert-FoundationAncestors $path
        $item.Delete()
    }
    Assert-FoundationAncestors $FixtureRoot
    (Get-Item -LiteralPath $FixtureRoot -Force -ErrorAction Stop).Delete()
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$scratchRoot = Join-Path $repositoryRoot '.codex-temp\foundation-tests'
$runRoot = Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))
$fixtures = Join-Path $runRoot 'fixtures'
$log = Join-Path $runRoot 'foundation.log'
$receipt = Join-Path $runRoot 'result.json'
$project = Join-Path $repositoryRoot 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj'
$started = [DateTime]::UtcNow

try {
    Assert-FoundationAncestors $scratchRoot -AllowMissing
    New-Item -ItemType Directory -Path $runRoot -ErrorAction Stop | Out-Null
    Assert-FoundationAncestors $runRoot
    New-Item -ItemType Directory -Path $fixtures -ErrorAction Stop | Out-Null
    Assert-FoundationFixture $repositoryRoot $runRoot $fixtures
    $arguments = @($fixtures)
    if ($Integration) {
        & (Join-Path $PSScriptRoot 'Build-Production.ps1') -Configuration $Configuration -SkipShell |
            Tee-Object -FilePath $log -ErrorAction Stop
        if ($LASTEXITCODE -ne 0) { throw 'Foundation production build failed.' }
        $arguments += Join-Path $repositoryRoot "artifacts\production\$Configuration\ContextSuite.Worker.exe"
        $arguments += Join-Path $repositoryRoot "artifacts\production\$Configuration\ContextSuite.Application.exe"
    }
    $previousErrorAction = $ErrorActionPreference
    try {
        # Windows PowerShell promotes native stderr to an error record when redirected.
        $ErrorActionPreference = 'Continue'
        & dotnet run --project $project -c $Configuration -- @arguments 2>&1 |
            Tee-Object -FilePath $log -Append:$Integration -ErrorAction Stop
        $testExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($testExitCode -ne 0) { throw "Foundation contracts failed (exit $testExitCode)." }

    $inventory = @(Get-FoundationFixtureInventory $fixtures)
    $result = [ordered]@{
        status = 'passed'
        configuration = $Configuration
        integration = [bool] $Integration
        startedAtUtc = $started.ToString('o')
        completedAtUtc = [DateTime]::UtcNow.ToString('o')
        command = "tools/Test-Foundation.ps1 -Configuration $Configuration" + $(if ($Integration) { ' -Integration' } else { '' })
        fixtureEntries = $inventory.Count
        fixtureBytes = [long] (@($inventory | Where-Object Kind -eq 'file' | Measure-Object Length -Sum)[0].Sum)
        fixtureDisposition = $(if ($KeepScratch) { 'retained by KeepScratch' } else { 'cleanup requested' })
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding UTF8 -ErrorAction Stop
    if (-not $KeepScratch) {
        Remove-FoundationFixture $repositoryRoot $runRoot $fixtures $inventory
        'Fixtures removed after successful contracts and receipt.' |
            Set-Content -LiteralPath (Join-Path $runRoot 'cleanup.txt') -Encoding UTF8
    }
    if (-not $KeepScratch) {
        try {
            $logTail = @(Get-Content -LiteralPath $log -Tail 60 -ErrorAction Stop)
            $logTail | Set-Content -LiteralPath $log -Encoding UTF8 -ErrorAction Stop
        } catch {
            Write-Warning "Could not compact successful foundation log at $log`: $($_.Exception.Message)"
        }
    }
    Write-Output "Foundation evidence: $runRoot"
} catch {
    $originalError = $_
    $reason = $_.Exception.Message
    $hasSafeRun = $false
    try {
        Assert-FoundationAncestors $runRoot
        $hasSafeRun = $true
    } catch { }
    if (-not $hasSafeRun) {
        Write-Warning "Foundation run stopped before a safe scratch directory was available: $reason"
        throw $originalError
    }
    try {
        @("Foundation run retained: $reason", "Reproduce: tools/Test-Foundation.ps1 -Configuration $Configuration" +
            $(if ($Integration) { ' -Integration' } else { '' }), "Evidence: $runRoot") |
            Set-Content -LiteralPath (Join-Path $runRoot 'failure.txt') -Encoding UTF8 -ErrorAction Stop
    } catch {
        Write-Warning "Could not write foundation failure note: $($_.Exception.Message)"
    }
    Write-Warning "Foundation run retained for diagnosis at $runRoot"
    throw $originalError
}
