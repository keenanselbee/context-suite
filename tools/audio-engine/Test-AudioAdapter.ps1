[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory, [Parameter(Mandatory)][string] $FixtureDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$root = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\audio-engine')) + '\'
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
if (-not $prepared.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
    -not $fixtures.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use isolated repository audio evaluation paths.' }
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evaluation.json') -Raw | ConvertFrom-Json
$bin = Join-Path $prepared ('unpacked\' + $pin.archiveDirectory + '\bin')
$evidence = Join-Path $prepared ('adapter-' + [guid]::NewGuid().ToString('N'))
$project = Join-Path $repository 'proprietary\tests\ContextSuite.Audio.ContractTests\ContextSuite.Audio.ContractTests.csproj'
& dotnet run --project $project -c Release -- $bin $fixtures $evidence
if ($LASTEXITCODE -ne 0) { throw 'Private audio adapter checks failed.' }
