[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$absent = Join-Path $repositoryRoot '.codex-temp\intentionally-absent-private.csproj'
if (Test-Path -LiteralPath $absent) { throw 'The deliberately absent test target unexpectedly exists.' }
foreach ($name in @('ContextSuite.Application', 'ContextSuite.Worker')) {
    $project = Join-Path $repositoryRoot "src\$name\$name.csproj"
    # Override the project location rather than moving or modifying the private checkout.
    $output = & dotnet build $project --nologo "-p:ContextSuitePrivateProject=$absent" 2>&1
    if ($LASTEXITCODE -eq 0 -or ($output -join "`n") -notmatch 'production requires the compatible context-suite-private') {
        throw "Missing-private build did not fail clearly for $name. Output: $output"
    }
}
Write-Output 'Both production projects fail clearly without private implementations.'
