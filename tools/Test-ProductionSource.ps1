[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
# The preserved private checkout and reference material are never build inputs.
$projects = @(Get-ChildItem -LiteralPath (Join-Path $repository 'src') -Filter '*.csproj' -Recurse)
$projects += @(Get-ChildItem -LiteralPath (Join-Path $repository 'tests') -Filter '*.csproj' -Recurse)
foreach ($project in $projects) {
    [xml] $xml = Get-Content -LiteralPath $project.FullName -Raw
    foreach ($node in $xml.SelectNodes('//*[@Include or @Project]')) {
        $value = if ($node.HasAttribute('Include')) { $node.GetAttribute('Include') } else { $node.GetAttribute('Project') }
        if ($value -match 'proprietary[\\/]|z backup[\\/]|reference[\\/]|ContextSuite.Commercial|Require-Private') {
            throw "Production source boundary violation in $($project.Name): $value"
        }
        if ($node.Name -eq 'ProjectReference' -and -not (Test-Path -LiteralPath (Join-Path $project.DirectoryName $value) -PathType Leaf)) {
            throw "Missing public project reference in $($project.Name): $value"
        }
    }
}
[xml] $solution = Get-Content -LiteralPath (Join-Path $repository 'ContextSuite.Production.slnx') -Raw
foreach ($node in $solution.SelectNodes('/Solution/Project')) {
    if ($node.Path -match 'proprietary|z backup[\\/]|Commercial' -or -not (Test-Path -LiteralPath (Join-Path $repository $node.Path) -PathType Leaf)) {
        throw "Production solution requires unavailable or commercial source: $($node.Path)"
    }
}
$worker = [xml](Get-Content -LiteralPath (Join-Path $repository 'src/ContextSuite.Worker/ContextSuite.Worker.csproj') -Raw)
if (-not @($worker.Project.ItemGroup.ProjectReference | Where-Object Include -match 'ContextSuite.Private').Count) {
    throw 'The production worker must reference the public media implementation.'
}
$app = [xml](Get-Content -LiteralPath (Join-Path $repository 'src/ContextSuite.Application/ContextSuite.Application.csproj') -Raw)
if (@($app.Project.ItemGroup.ProjectReference | Where-Object Include -match 'ContextSuite.Private').Count) {
    throw 'The WPF application must not load the media implementation.'
}
Write-Output "Complete public production source and $($projects.Count) project references passed. Full compilation requires the pinned native engines."
