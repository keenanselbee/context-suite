[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory, [Parameter(Mandatory)][string] $FixtureDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$allowed = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\pdf-engine')) + '\'
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
if (-not $prepared.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or
    -not $fixtures.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use isolated repository PDF evaluation paths.' }
$bin = Join-Path $prepared 'unpacked\qpdf-12.4.1-msvc64\bin'
$evidence = Join-Path $prepared ('adapter-' + [guid]::NewGuid().ToString('N'))
& dotnet run --project (Join-Path $repository 'proprietary\tests\ContextSuite.Pdf.ContractTests\ContextSuite.Pdf.ContractTests.csproj') -c Release -- $bin $fixtures $evidence
if ($LASTEXITCODE -ne 0) { throw 'Private PDF adapter checks failed.' }
