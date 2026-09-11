[CmdletBinding()]
param([Parameter(Mandatory)][string] $ProductionStage, [Parameter(Mandatory)][string] $PreparedDirectory,
    [Parameter(Mandatory)][string] $FixtureDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = (Resolve-Path -LiteralPath $ProductionStage).Path
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
if (-not $stage.StartsWith((Join-Path $repository 'artifacts\production-staging\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $prepared.StartsWith((Join-Path $repository '.codex-temp\pdf-engine\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $fixtures.StartsWith((Join-Path $repository '.codex-temp\pdfium-engine\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use isolated production staging, prepared qpdf and generated image-PDF fixtures.'
}
$build = Get-Content -LiteralPath (Join-Path $prepared 'image-validator-build.json') -Raw | ConvertFrom-Json
$binary = Join-Path $prepared 'image-validator-build\bin\Release'
$source = Join-Path $repository 'proprietary\src\ContextSuite.ImagePdfValidator.Native'
if ((Get-FileHash -LiteralPath (Join-Path $binary 'ContextSuite.ImagePdfValidator.exe')).Hash -ne $build.sha256 -or
    (Get-FileHash -LiteralPath (Join-Path $binary 'qpdf30.dll')).Hash -ne $build.qpdfSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $source 'Validator.cpp')).Hash -ne $build.bridgeSourceSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $source 'CMakeLists.txt')).Hash -ne $build.buildSourceSha256) {
    throw 'Validator source or binary identity changed; rebuild the isolated host.'
}
$scratch = Join-Path $prepared ('image-pdf-worker-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $scratch 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
Get-ChildItem -LiteralPath $stage -File | Where-Object Name -ne 'payload-inventory.json' | Copy-Item -Destination $payload
$validator = Join-Path $payload 'pdf-validator'
New-Item -ItemType Directory -Path $validator | Out-Null
foreach ($name in @('ContextSuite.ImagePdfValidator.exe', 'qpdf30.dll', 'concrt140.dll', 'msvcp140.dll', 'msvcp140_1.dll',
    'msvcp140_2.dll', 'msvcp140_atomic_wait.dll', 'msvcp140_codecvt_ids.dll', 'vcruntime140.dll', 'vcruntime140_1.dll')) {
    Copy-Item -LiteralPath (Join-Path $binary $name) -Destination $validator
}
Copy-Item -LiteralPath (Join-Path $prepared 'image-validator-build.json') -Destination $validator
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --image-pdf-worker (Join-Path $scratch 'results') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
if ($LASTEXITCODE -ne 0) { throw "Combined PDF workflow failed; evidence retained at $scratch" }
& dotnet run --project (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release -- --image-pdf-direct (Join-Path $scratch 'direct') (Join-Path $payload 'ContextSuite.Worker.exe') $fixtures
if ($LASTEXITCODE -ne 0) { throw "Direct combined PDF workflow failed; evidence retained at $scratch" }
Write-Output "Evaluation-only combined PDF worker: $scratch"
