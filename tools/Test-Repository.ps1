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
# Local evidence can exist on a developer machine while being absent from every
# checkout. Only link to files (or their directories) eligible for public source.
$sourcePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$sourceFiles = @(git -C $repositoryRoot -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect public documentation link targets.' }
foreach ($relative in $sourceFiles) {
    $sourcePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relative))
    while ($sourcePath.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        [void] $sourcePaths.Add($sourcePath)
        $sourcePath = Split-Path $sourcePath -Parent
    }
}
[void] $sourcePaths.Add($repositoryRoot)
foreach ($file in $files) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    if ($content -match '(?m)[\t ]+$') { throw "Trailing whitespace: $($file.Name)" }
    foreach ($match in [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')) {
        $link = $match.Groups[1].Value
        if ($link -match '^(https?://|mailto:|#)') { continue }
        $path = [IO.Path]::GetFullPath((Join-Path $file.DirectoryName ($link -split '#')[0])).TrimEnd('\', '/')
        if (-not $sourcePaths.Contains($path)) {
            throw "Link outside public source in $($file.Name): $link. Describe local evidence as a plain path, not a repository link."
        }
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
