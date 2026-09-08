[CmdletBinding()]
param([switch] $SkipNativeBuild)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $SkipNativeBuild) { & (Join-Path $PSScriptRoot 'dds-engine\Build-DdsEngine.ps1') }
$project = Join-Path $repository 'proprietary\tests\ContextSuite.Dds.ContractTests\ContextSuite.Dds.ContractTests.csproj'
$scratch = Join-Path $repository ('.codex-temp\dds-tests\codec-' + [guid]::NewGuid().ToString('N'))
& dotnet run --project $project -c Release -- $scratch
if ($LASTEXITCODE) { throw 'DDS codec contracts failed.' }
Write-Output "DDS codec evidence: $scratch"
