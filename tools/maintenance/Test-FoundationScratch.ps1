[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $repositoryRoot 'tools\Test-Foundation.ps1'
$testRoot = Join-Path $repositoryRoot ('.codex-temp\foundation-scratch-guard-' + [guid]::NewGuid().ToString('N'))
$checks = 0
$global:FoundationMockScenario = ''

function Assert-Check {
    param([bool] $Condition, [string] $Name)
    if (-not $Condition) { throw "FAILED: $Name" }
    $script:checks++
    Write-Host "PASS: $Name"
}

function dotnet {
    $values = @($args)
    $global:FoundationMockArguments = $values
    $fixture = $values[-1]
    if ($global:FoundationMockScenario -eq 'integration') { $fixture = $values[-3] }
    New-Item -ItemType Directory -Path (Join-Path $fixture 'nested') -ErrorAction Stop | Out-Null
    Set-Content -LiteralPath (Join-Path $fixture 'nested\sample.txt') -Value 'mock fixture'
    if ($global:FoundationMockScenario -eq 'receipt-failure') {
        New-Item -ItemType Directory -Path (Join-Path (Split-Path $fixture -Parent) 'result.json') | Out-Null
    }
    Write-Output 'Passed 1 mocked foundation contract.'
    $global:LASTEXITCODE = if ($global:FoundationMockScenario -eq 'failure') { 1 } else { 0 }
}

function New-MockRepository {
    param([string] $Name)
    $root = Join-Path $testRoot $Name
    $tools = Join-Path $root 'tools'
    New-Item -ItemType Directory -Path $tools -ErrorAction Stop | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $tools 'Test-Foundation.ps1')
    return $root
}

function Get-MockRun {
    param([string] $Root)
    $runs = @(Get-ChildItem -LiteralPath (Join-Path $Root '.codex-temp\foundation-tests') -Directory)
    Assert-Check ($runs.Count -eq 1) 'one unique run was created'
    return $runs[0].FullName
}

try {
    New-Item -ItemType Directory -Path $testRoot -ErrorAction Stop | Out-Null
    $global:FoundationMockScenario = 'success'
    $root = New-MockRepository 'success'
    & (Join-Path $root 'tools\Test-Foundation.ps1') | Out-Null
    $run = Get-MockRun $root
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $run 'fixtures')) -and
        (Test-Path -LiteralPath (Join-Path $run 'result.json')) -and
        (Test-Path -LiteralPath (Join-Path $run 'foundation.log')) -and
        (Test-Path -LiteralPath (Join-Path $run 'cleanup.txt'))) 'success retains receipt and log, removes fixtures'
    Assert-Check ((Get-Content -LiteralPath (Join-Path $run 'foundation.log') -Raw) -match
        'Passed 1 mocked foundation contract') 'success log keeps the command result'
    $result = Get-Content -LiteralPath (Join-Path $run 'result.json') -Raw | ConvertFrom-Json
    Assert-Check ($result.status -eq 'passed' -and $result.fixtureEntries -eq 2 -and
        $result.fixtureBytes -eq 14) 'success receipt records the fixture inventory'

    $global:FoundationMockScenario = 'failure'
    $root = New-MockRepository 'failure'
    $failed = $false
    try { & (Join-Path $root 'tools\Test-Foundation.ps1') 3>$null | Out-Null } catch { $failed = $true }
    $run = Get-MockRun $root
    Assert-Check ($failed -and (Test-Path -LiteralPath (Join-Path $run 'fixtures\nested\sample.txt')) -and
        (Test-Path -LiteralPath (Join-Path $run 'foundation.log')) -and
        (Test-Path -LiteralPath (Join-Path $run 'failure.txt')) -and
        -not (Test-Path -LiteralPath (Join-Path $run 'result.json'))) 'failed contracts retain fixture, log, and reason'

    $global:FoundationMockScenario = 'receipt-failure'
    $root = New-MockRepository 'receipt-failure'
    $failed = $false
    try { & (Join-Path $root 'tools\Test-Foundation.ps1') 3>$null | Out-Null } catch { $failed = $true }
    $run = Get-MockRun $root
    Assert-Check ($failed -and (Test-Path -LiteralPath (Join-Path $run 'fixtures\nested\sample.txt')) -and
        -not (Test-Path -LiteralPath (Join-Path $run 'cleanup.txt'))) 'receipt failure retains fixtures'

    $global:FoundationMockScenario = 'keep'
    $root = New-MockRepository 'keep'
    & (Join-Path $root 'tools\Test-Foundation.ps1') -KeepScratch | Out-Null
    $run = Get-MockRun $root
    Assert-Check ((Test-Path -LiteralPath (Join-Path $run 'fixtures\nested\sample.txt')) -and
        (Test-Path -LiteralPath (Join-Path $run 'result.json')) -and
        -not (Test-Path -LiteralPath (Join-Path $run 'cleanup.txt'))) 'KeepScratch retains a successful fixture'

    $global:FoundationMockScenario = 'integration'
    $root = New-MockRepository 'integration'
    @'
param([string] $Configuration, [switch] $SkipShell)
if ($Configuration -ne 'Release' -or -not $SkipShell) { throw 'Integration build arguments changed.' }
Write-Output 'Mock production build passed.'
$global:LASTEXITCODE = 0
'@ | Set-Content -LiteralPath (Join-Path $root 'tools\Build-Production.ps1')
    & (Join-Path $root 'tools\Test-Foundation.ps1') -Configuration Release -Integration | Out-Null
    $run = Get-MockRun $root
    $result = Get-Content -LiteralPath (Join-Path $run 'result.json') -Raw | ConvertFrom-Json
    Assert-Check ($result.integration -and -not (Test-Path -LiteralPath (Join-Path $run 'fixtures')) -and
        $global:FoundationMockArguments.Count -eq 8 -and
        $global:FoundationMockArguments[-2] -eq (Join-Path $root 'artifacts\production\Release\ContextSuite.Worker.exe') -and
        $global:FoundationMockArguments[-1] -eq (Join-Path $root 'artifacts\production\Release\ContextSuite.Application.exe')) 'Integration keeps production build and worker arguments'

    foreach ($linkName in @('scratch', 'foundation-tests')) {
        $root = New-MockRepository ('linked-' + $linkName)
        $target = Join-Path $testRoot ('target-' + $linkName)
        New-Item -ItemType Directory -Path $target -ErrorAction Stop | Out-Null
        if ($linkName -eq 'foundation-tests') {
            New-Item -ItemType Directory -Path (Join-Path $root '.codex-temp') -ErrorAction Stop | Out-Null
            $link = Join-Path $root '.codex-temp\foundation-tests'
        } else { $link = Join-Path $root '.codex-temp' }
        New-Item -ItemType Junction -Path $link -Target $target -ErrorAction Stop | Out-Null
        $global:FoundationMockArguments = @()
        $rejected = $false
        try { & (Join-Path $root 'tools\Test-Foundation.ps1') 3>$null | Out-Null } catch { $rejected = $true }
        Assert-Check ($rejected -and $global:FoundationMockArguments.Count -eq 0 -and
            @(Get-ChildItem -LiteralPath $target -Force).Count -eq 0) "linked $linkName ancestor rejected before test creation"
        (Get-Item -LiteralPath $link -Force).Delete()
    }

    # Load only the runner's functions; never execute its real dotnet or build path.
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref] $tokens, [ref] $errors)
    Assert-Check ($errors.Count -eq 0) 'foundation runner parses'
    foreach ($function in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
        . ([scriptblock]::Create($function.Extent.Text))
    }
    $root = New-MockRepository 'guards'
    $scratch = Join-Path $root '.codex-temp\foundation-tests'
    $run = Join-Path $scratch ([guid]::NewGuid().ToString('N'))
    $fixture = Join-Path $run 'fixtures'
    New-Item -ItemType Directory -Path $fixture -ErrorAction Stop | Out-Null
    $sibling = Join-Path $scratch ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $sibling -ErrorAction Stop | Out-Null
    Set-Content -LiteralPath (Join-Path $sibling 'history.txt') -Value 'keep'
    Set-Content -LiteralPath (Join-Path $fixture 'a.txt') -Value 'a'
    $inventory = @(Get-FoundationFixtureInventory $fixture)
    $outside = Join-Path $testRoot 'outside.txt'
    Set-Content -LiteralPath $outside -Value 'safe'
    $rejected = $false
    try { Remove-FoundationFixture $root $run $testRoot $inventory } catch { $rejected = $true }
    Assert-Check ($rejected -and (Test-Path -LiteralPath $outside)) 'outside path rejected'
    $originalTime = (Get-Item -LiteralPath (Join-Path $fixture 'a.txt')).LastWriteTimeUtc
    Set-Content -LiteralPath (Join-Path $fixture 'a.txt') -Value 'b'
    [IO.File]::SetLastWriteTimeUtc((Join-Path $fixture 'a.txt'), $originalTime)
    $rejected = $false
    try { Remove-FoundationFixture $root $run $fixture $inventory } catch { $rejected = $true }
    Assert-Check ($rejected -and (Test-Path -LiteralPath (Join-Path $fixture 'a.txt'))) 'same-name replacement rejected'
    Set-Content -LiteralPath (Join-Path $fixture 'a.txt') -Value 'a'
    [IO.File]::SetLastWriteTimeUtc((Join-Path $fixture 'a.txt'), $originalTime)
    Set-Content -LiteralPath (Join-Path $fixture 'added.txt') -Value 'unexpected'
    $rejected = $false
    try { Remove-FoundationFixture $root $run $fixture $inventory } catch { $rejected = $true }
    Assert-Check ($rejected -and (Test-Path -LiteralPath (Join-Path $fixture 'a.txt'))) 'added fixture prevents cleanup'
    Remove-Item -LiteralPath (Join-Path $fixture 'added.txt')
    $linked = Join-Path $fixture 'linked'
    New-Item -ItemType Junction -Path $linked -Target $testRoot -ErrorAction Stop | Out-Null
    $rejected = $false
    try { Remove-FoundationFixture $root $run $fixture $inventory } catch { $rejected = $true }
    Assert-Check ($rejected -and (Test-Path -LiteralPath $outside)) 'linked fixture rejected'
    (Get-Item -LiteralPath $linked -Force).Delete()
    Remove-FoundationFixture $root $run $fixture $inventory
    Assert-Check (-not (Test-Path -LiteralPath $fixture) -and (Test-Path -LiteralPath $outside) -and
        (Test-Path -LiteralPath (Join-Path $sibling 'history.txt'))) 'exact fixture cleanup preserves siblings'

    # A newly added file after inventory validation can stop a directory delete.
    $fixture = Join-Path $run 'fixtures'
    New-Item -ItemType Directory -Path $fixture -ErrorAction Stop | Out-Null
    Set-Content -LiteralPath (Join-Path $fixture 'first.txt') -Value 'first'
    $inventory = @(Get-FoundationFixtureInventory $fixture)
    function Remove-Item {
        param([string] $LiteralPath, [switch] $Force, [string] $ErrorAction)
        if ($LiteralPath -like '*first.txt') {
            Microsoft.PowerShell.Management\Remove-Item -LiteralPath $LiteralPath -Force
            Set-Content -LiteralPath (Join-Path $fixture 'new.txt') -Value 'late'
        } else { Microsoft.PowerShell.Management\Remove-Item -LiteralPath $LiteralPath -Force }
    }
    $rejected = $false
    try { Remove-FoundationFixture $root $run $fixture $inventory } catch { $rejected = $true }
    Remove-Item Function:\Remove-Item
    Assert-Check ($rejected -and (Test-Path -LiteralPath (Join-Path $fixture 'new.txt'))) 'partial cleanup preserves late content'

    $run = Join-Path $scratch ([guid]::NewGuid().ToString('N'))
    $fixture = Join-Path $run 'fixtures'
    $script:swapDirectory = Join-Path $fixture 'inside'
    $script:savedDirectory = Join-Path $fixture 'saved'
    $script:swapFilePath = Join-Path $script:swapDirectory 'sample.txt'
    $script:swapTarget = Join-Path $testRoot 'swap-target'
    New-Item -ItemType Directory -Path $script:swapDirectory -ErrorAction Stop | Out-Null
    New-Item -ItemType Directory -Path $script:swapTarget -ErrorAction Stop | Out-Null
    Set-Content -LiteralPath $script:swapFilePath -Value 'safe'
    Copy-Item -LiteralPath $script:swapFilePath -Destination (Join-Path $script:swapTarget 'sample.txt')
    [IO.File]::SetLastWriteTimeUtc((Join-Path $script:swapTarget 'sample.txt'),
        (Get-Item -LiteralPath $script:swapFilePath).LastWriteTimeUtc)
    $inventory = @(Get-FoundationFixtureInventory $fixture)
    $script:didSwap = $false
    function Get-Item {
        param([string] $LiteralPath, [switch] $Force, [string] $ErrorAction)
        if ($LiteralPath -eq $script:swapFilePath -and -not $script:didSwap) {
            Move-Item -LiteralPath $script:swapDirectory -Destination $script:savedDirectory
            New-Item -ItemType Junction -Path $script:swapDirectory -Target $script:swapTarget | Out-Null
            $script:didSwap = $true
        }
        Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
    }
    $rejected = $false
    try { Remove-FoundationFixture $root $run $fixture $inventory } catch { $rejected = $true }
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath Function:\Get-Item
    Assert-Check ($rejected -and $script:didSwap -and
        (Test-Path -LiteralPath (Join-Path $script:swapTarget 'sample.txt')) -and
        (Test-Path -LiteralPath (Join-Path $script:savedDirectory 'sample.txt'))) 'swapped parent link rejects deletion and retains both files'
    (Get-Item -LiteralPath $script:swapDirectory -Force).Delete()

    Write-Output "Passed $checks foundation scratch guard checks."
    # This entire guard fixture was created by this invocation and has no consumers.
    if ([IO.Path]::GetDirectoryName($testRoot) -ne (Join-Path $repositoryRoot '.codex-temp') -or
        [IO.Path]::GetFileName($testRoot) -notmatch '^foundation-scratch-guard-[0-9a-f]{32}$') {
        throw "Refusing guard fixture cleanup: $testRoot"
    }
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction Stop
} catch {
    Write-Warning "Foundation guard fixtures retained at $testRoot"
    throw
} finally {
    $global:FoundationMockScenario = $null
    $global:FoundationMockArguments = $null
}
