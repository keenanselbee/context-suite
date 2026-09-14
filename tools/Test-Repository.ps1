[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$trackedPrivate = @(git -C $repositoryRoot ls-files -- proprietary reference)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect repository source boundaries.' }
if ($trackedPrivate.Count) { throw 'Private or reference files must not be tracked in the public repository.' }
git -C $repositoryRoot diff --check
if ($LASTEXITCODE -ne 0) { throw 'Git whitespace validation failed.' }
$files = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs') -Filter '*.md' -Recurse)
$files += Get-Item -LiteralPath (Join-Path $repositoryRoot 'README.md'), (Join-Path $repositoryRoot 'AGENTS.md'), (Join-Path $repositoryRoot 'CHANGELOG.md')
foreach ($file in $files) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    if ($content -match '(?m)[\t ]+$') { throw "Trailing whitespace: $($file.Name)" }
    foreach ($match in [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')) {
        $link = $match.Groups[1].Value
        if ($link -match '^(https?://|mailto:|#)') { continue }
        $path = Join-Path $file.DirectoryName ($link -split '#')[0]
        if (-not (Test-Path -LiteralPath $path)) { throw "Broken link in $($file.Name): $link" }
    }
}
[xml] $applicationXaml = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\ContextSuite.Application\App.xaml') -Raw
if ($applicationXaml.DocumentElement.GetAttribute('ThemeMode') -ne 'System') {
    throw 'Application appearance must follow the Windows app theme.'
}
foreach ($window in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src\ContextSuite.Application') -Filter '*Window.xaml' -Recurse) {
    [xml] $windowXaml = Get-Content -LiteralPath $window.FullName -Raw
    if ($windowXaml.DocumentElement.HasAttribute('ThemeMode')) {
        throw "Window must inherit the application theme: $($window.Name)"
    }
}
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Filter '*.cs' -Recurse) {
    if ((Get-Content -LiteralPath $source.FullName -Raw) -match 'CONTEXTSUITE_TEST_(ROOT|WORKER)') {
        throw "Test-host environment inputs must not enter shipping source: $($source.Name)"
    }
}
Write-Output "Public source boundary, system-theme policy, and $($files.Count) documentation files passed."
