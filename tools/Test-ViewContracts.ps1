[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Application.TestHost\ContextSuite.Application.TestHost.csproj') -c $Configuration -- --view-contracts
if ($LASTEXITCODE -ne 0) { throw 'Hidden view contracts failed.' }
