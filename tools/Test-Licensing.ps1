[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repositoryRoot 'proprietary\tests\ContextSuite.Licensing.ContractTests\ContextSuite.Licensing.ContractTests.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw 'The compatible private checkout is required for licensing transport tests.' }
# Synthetic keys and injected HTTP only. Never contacts Polar or activates a slot.
& dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Licensing contracts failed.' }
