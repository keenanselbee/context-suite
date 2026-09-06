[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug', [switch] $Integration)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $repositoryRoot '.codex-temp\foundation-tests'
$project = Join-Path $repositoryRoot 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj'
$arguments = @($scratch)
if ($Integration) {
    & (Join-Path $PSScriptRoot 'Build-Production.ps1') -Configuration $Configuration -SkipShell
    $arguments += Join-Path $repositoryRoot "artifacts\production\$Configuration\ContextSuite.Worker.exe"
    $arguments += Join-Path $repositoryRoot "artifacts\production\$Configuration\ContextSuite.Application.exe"
}
& dotnet run --project $project -c $Configuration -- @arguments
if ($LASTEXITCODE -ne 0) { throw 'Foundation contracts failed.' }
