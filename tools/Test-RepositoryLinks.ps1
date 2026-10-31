[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$run = Join-Path $repository ('.codex-temp\repository-link-tests\' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $run 'fixture'
$utf8 = [Text.UTF8Encoding]::new($false)
$passed = 0
New-Item -ItemType Directory -Path (Join-Path $fixture 'docs'), (Join-Path $fixture 'tools'),
    (Join-Path $fixture 'src\ContextSuite.Application'), (Join-Path $fixture '.codex-temp') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-Repository.ps1') -Destination (Join-Path $fixture 'tools')
git -C $fixture init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize documentation fixture.' }
[IO.File]::WriteAllText((Join-Path $fixture '.gitignore'), ".codex-temp/`n", $utf8)
[IO.File]::WriteAllText((Join-Path $fixture 'src\ContextSuite.Application\App.xaml'), '<Application ThemeMode="System" />', $utf8)
foreach ($name in 'README.md', 'AGENTS.md', 'CHANGELOG.md', 'docs\target.md') {
    [IO.File]::WriteAllText((Join-Path $fixture $name), "Fixture`n", $utf8)
}
$localEvidence = Join-Path $fixture '.codex-temp\evidence.txt'
[IO.File]::WriteAllText($localEvidence, "Local evidence`n", $utf8)
[IO.File]::WriteAllText((Join-Path $run 'outside.txt'), "Outside checkout`n", $utf8)
$cases = @(
    @{ Name = 'New public file'; Link = 'target.md'; Rejected = $false },
    @{ Name = 'Public directory'; Link = '../src/'; Rejected = $false },
    @{ Name = 'Existing ignored evidence'; Link = '../.codex-temp/evidence.txt'; Rejected = $true },
    @{ Name = 'Missing ignored evidence'; Link = '../.codex-temp/missing.txt'; Rejected = $true },
    @{ Name = 'Missing public file'; Link = 'missing.md'; Rejected = $true },
    @{ Name = 'Outside checkout'; Link = '../../outside.txt'; Rejected = $true }
)
foreach ($case in $cases) {
    [IO.File]::WriteAllText((Join-Path $fixture 'docs\links.md'), "[Target]($($case.Link))`n", $utf8)
    $rejected = $false
    try { & (Join-Path $fixture 'tools\Test-Repository.ps1') | Out-Null }
    catch {
        if ($_.Exception.Message -notlike 'Link outside public source*') { throw }
        $rejected = $true
    }
    if ($rejected -ne $case.Rejected) { throw "Unexpected documentation result: $($case.Name)" }
    $passed++
}
$result = @{ passed = $passed; cases = @($cases | ForEach-Object { $_.Name }); fixtureCleanup = 'pending' }
$receipt = Join-Path $run 'result.json'
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receipt -Encoding UTF8
# Only this successful run's fixtures are disposable. Retain failures for diagnosis.
foreach ($target in @($fixture, (Join-Path $run 'outside.txt'))) {
    $resolved = (Resolve-Path -LiteralPath $target).ProviderPath
    if (-not $resolved.StartsWith($run + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture escaped its run.' }
    $entries = @(Get-Item -LiteralPath $resolved -Force)
    if (Test-Path -LiteralPath $resolved -PathType Container) { $entries += @(Get-ChildItem -LiteralPath $resolved -Recurse -Force) }
    if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Linked fixture refused.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$result.fixtureCleanup = 'passed'
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receipt -Encoding UTF8
Write-Output "Passed $passed documentation link contracts. Receipt: $receipt"
