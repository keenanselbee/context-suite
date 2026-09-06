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
$files += Get-Item -LiteralPath (Join-Path $repositoryRoot 'README.md'), (Join-Path $repositoryRoot 'AGENTS.md')
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
Write-Output "Public source boundary and $($files.Count) documentation files passed."
