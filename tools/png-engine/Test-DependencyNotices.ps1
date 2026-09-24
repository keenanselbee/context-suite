[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$root = Join-Path $repository ('.codex-temp\png-notice-tests\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
foreach ($name in 'oxipng.exe', 'Oxipng.License.txt', 'ContextSuite.Png.Engine.json') {
    Copy-Item -LiteralPath (Join-Path $repository ('artifacts\engines\png-win-x64\' + $name)) -Destination $root
}
$assets = Join-Path $repository 'assets\third-party\oxipng'
Get-ChildItem -LiteralPath $assets -File | Copy-Item -Destination $root
$verify = Join-Path $PSScriptRoot 'Test-PngEngine.ps1'
& $verify -Payload $root -RequireDependencyNotices
$passed = 1
foreach ($name in 'Oxipng.ThirdPartyNotices.txt', 'Oxipng.Rust.Library.Notices.html') {
    $path = Join-Path $root $name
    foreach ($mode in 'missing', 'altered') {
        if ($mode -eq 'missing') { [IO.File]::Move($path, $path + '.held') }
        else { [IO.File]::WriteAllText($path, 'altered notice') }
        $refused = $false
        try { & $verify -Payload $root -RequireDependencyNotices | Out-Null } catch { $refused = $true }
        if (-not $refused) { throw "PNG notice check accepted $mode $name" }
        $passed++
        if ($mode -eq 'missing') { [IO.File]::Move($path + '.held', $path) }
        else { Copy-Item -LiteralPath (Join-Path $assets $name) -Destination $path -Force }
        & $verify -Payload $root -RequireDependencyNotices
        $passed++
    }
}
@{ Passed = $true; Checks = $passed; Scope = 'Copied inputs only; original engine files unchanged' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding UTF8
Write-Output "Passed $passed PNG dependency-notice checks. Evidence: $root"
