[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9-]+$')][string] $RunName = 'prototype-2')

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workspace = Join-Path $repository ".codex-temp\curated-engine\$RunName"
$root = Join-Path $workspace ('guard-tests-' + [guid]::NewGuid().ToString('N'))
$nativeRelative = 'native\src\Magick.Native\bin\ReleaseQ16\x64'
$configRelative = 'native\src\ImageMagick\ImageMagick\MagickCore\magick-baseconfig.h'
$verifier = Join-Path $PSScriptRoot 'Test-CuratedPayload.ps1'
& (Join-Path $PSScriptRoot 'New-CuratedNotices.ps1') -Workspace $workspace
$passed = 0
$package = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages\magick.net-q16-x64\14.17.1'
$stock = Join-Path $package 'runtimes\win-x64\native\Magick.Native-Q16-x64.dll'
if ((Get-FileHash -LiteralPath $stock).Hash -ne '14A0992B54E236E37603DA18AE7B9936E3B3F490EADCC6D99BD13CCC1470B2C6') { throw 'Pinned stock negative-test fixture is missing or changed.' }
foreach ($case in 'valid', 'stock', 'nested-stock', 'extra-library', 'external-library', 'extra-delegate', 'missing-notice', 'stock-notice', 'modified-binary') {
    $caseRoot = Join-Path $root $case
    $payload = Join-Path $caseRoot 'payload'
    $native = Join-Path $caseRoot $nativeRelative
    New-Item -ItemType Directory -Path $payload, $native, (Split-Path (Join-Path $caseRoot $configRelative)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $workspace 'build-result.json'), (Join-Path $workspace 'Magick.NET.Notice.txt') -Destination $caseRoot
    Copy-Item -LiteralPath (Join-Path $workspace $configRelative) -Destination (Join-Path $caseRoot $configRelative)
    foreach ($file in 'Magick.Native-Q16-x64.dll', 'Magick.Native-Q16-x64.map') {
        Copy-Item -LiteralPath (Join-Path (Join-Path $workspace $nativeRelative) $file) -Destination $native
    }
    $binary = Join-Path $payload 'Magick.Native-Q16-x64.dll'
    Copy-Item -LiteralPath (Join-Path $native 'Magick.Native-Q16-x64.dll') -Destination $binary
    if ($case -ne 'missing-notice') { Copy-Item -LiteralPath (Join-Path $workspace 'Magick.NET.Notice.txt') -Destination $payload }
    switch ($case) {
        'stock' { Copy-Item -LiteralPath $stock -Destination $binary -Force }
        'nested-stock' {
            $nested = Join-Path $payload 'runtimes\win-x64\native'
            New-Item -ItemType Directory -Path $nested -Force | Out-Null
            Copy-Item -LiteralPath $stock -Destination $nested
        }
        'extra-library' { [IO.File]::AppendAllText((Join-Path $native 'Magick.Native-Q16-x64.map'), "`nCORE_RL_heif_:example.obj`n") }
        'external-library' { [IO.File]::AppendAllText((Join-Path $native 'Magick.Native-Q16-x64.map'), "`n 0001:00000000 external_symbol 0000000180001000 f externalcodec:example.obj`n") }
        'extra-delegate' { [IO.File]::AppendAllText((Join-Path $caseRoot $configRelative), "`n#define MAGICKCORE_HEIC_DELEGATE`n") }
        'stock-notice' { Copy-Item -LiteralPath (Join-Path $package 'Notice.txt') -Destination (Join-Path $payload 'Magick.NET.Notice.txt') -Force }
        'modified-binary' {
            $bytes = [IO.File]::ReadAllBytes($binary)
            $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 1
            [IO.File]::WriteAllBytes($binary, $bytes)
        }
    }
    $rejected = $false
    try { & $verifier -Workspace $caseRoot -Payload $payload | Out-Null }
    catch { $rejected = $true }
    if ($rejected -eq ($case -eq 'valid')) { throw "Guard contract failed: $case" }
    $passed++
    Write-Output "PASS: curated guard $case"
    if ($case -in @('valid', 'stock', 'nested-stock', 'missing-notice', 'stock-notice', 'modified-binary')) {
        $productionRejected = $false
        try { & (Join-Path $PSScriptRoot 'Test-ProductionEngine.ps1') -Payload $payload | Out-Null }
        catch { $productionRejected = $true }
        if ($productionRejected -eq ($case -eq 'valid')) { throw "Production guard contract failed: $case" }
        $passed++
        Write-Output "PASS: production guard $case"
    }
}
[IO.File]::WriteAllText((Join-Path $root 'result.txt'), "Passed $passed curated packaging guard contracts.")
Write-Output "Passed $passed curated packaging guard contracts. Evidence: $root"
