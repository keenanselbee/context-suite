[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $repository "artifacts\production\$Configuration"
$root = Join-Path $repository ('.codex-temp\production-packaging-' + [guid]::NewGuid().ToString('N'))
foreach ($case in 'valid', 'unexpected-file', 'empty-dotnet-notice', 'modified-acknowledgment', 'stale-selection', 'unexpected-package', 'dds-native', 'dds-identity', 'dds-notice', 'png-native', 'png-identity', 'png-notice', 'palette-native', 'palette-identity', 'palette-notice', 'rust-notice') {
    $payload = Join-Path $root $case
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $payload
    switch ($case) {
        'palette-native' { [IO.File]::WriteAllText((Join-Path $payload 'ContextSuite.Palette.exe'), 'corrupt') }
        'palette-identity' { [IO.File]::WriteAllText((Join-Path $payload 'ContextSuite.Palette.Engine.json'), '{}') }
        'palette-notice' { [IO.File]::WriteAllText((Join-Path $payload 'ExoQuant.License.txt'), 'incomplete') }
        'rust-notice' { [IO.File]::WriteAllText((Join-Path $payload 'Rust.Library.Notices.html'), 'incomplete') }
        'png-native' { [IO.File]::WriteAllText((Join-Path $payload 'oxipng.exe'), 'corrupt') }
        'png-identity' { [IO.File]::WriteAllText((Join-Path $payload 'ContextSuite.Png.Engine.json'), '{}') }
        'png-notice' { [IO.File]::WriteAllText((Join-Path $payload 'Oxipng.License.txt'), 'incomplete') }
        'dds-native' { [IO.File]::WriteAllText((Join-Path $payload 'ContextSuite.Dds.Native.dll'), 'corrupt') }
        'dds-identity' { [IO.File]::WriteAllText((Join-Path $payload 'ContextSuite.Dds.Engine.json'), '{}') }
        'dds-notice' { [IO.File]::WriteAllText((Join-Path $payload 'DirectXTex.License.txt'), 'incomplete') }
        'unexpected-file' { [IO.File]::WriteAllText((Join-Path $payload 'unreviewed-codec.dll'), 'fixture') }
        'empty-dotnet-notice' { [IO.File]::WriteAllText((Join-Path $payload 'DotNet.License.txt'), '') }
        'modified-acknowledgment' { [IO.File]::WriteAllText((Join-Path $payload 'THIRD-PARTY-NOTICES.txt'), 'incomplete') }
        'stale-selection' { [IO.File]::WriteAllText((Join-Path $payload 'ContextSuite.Engine.json'), '{}') }
        'unexpected-package' {
            $path = Join-Path $payload 'ContextSuite.Worker.deps.json'
            $json = Get-Content $path -Raw | ConvertFrom-Json
            $json.libraries | Add-Member -NotePropertyName 'Unreviewed/1.0.0' -NotePropertyValue @{ type = 'package' }
            [IO.File]::WriteAllText($path, ($json | ConvertTo-Json -Depth 30))
        }
    }
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Test-ProductionPayload.ps1') -Payload $payload | Out-Null }
    catch { $rejected = $true }
    if ($rejected -eq ($case -eq 'valid')) { throw "Packaging contract failed: $case" }
    Write-Output "PASS: production packaging $case"
}
[IO.File]::WriteAllText((Join-Path $root 'result.txt'), 'Passed 16 production packaging contracts.')
Write-Output "Passed 16 production packaging contracts. Evidence: $root"
