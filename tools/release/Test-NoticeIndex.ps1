[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$root = Join-Path $repository ('.codex-temp\notice-index\' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $root 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
$generator = Join-Path $PSScriptRoot 'Write-NoticeIndex.ps1'
$checks = [Collections.Generic.List[string]]::new()
function Check([bool] $Condition, [string] $Name) {
    if (-not $Condition) { throw $Name }
    $checks.Add($Name)
}
function Expect-Refusal([scriptblock] $Action, [string] $Name) {
    $refused = $false
    try { & $Action | Out-Null } catch { $refused = $true }
    Check $refused $Name
}
function Add-Notice([string] $Relative) {
    $path = Join-Path $payload $Relative
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($path, 'Disposable notice fixture: ' + $Relative)
}
foreach ($name in 'THIRD-PARTY-NOTICES.txt', 'DotNet.License.txt', 'DotNet.ThirdPartyNotices.txt',
    'Magick.NET.Notice.txt', 'DirectXTex.License.txt', 'Oxipng.License.txt', 'Oxipng.ThirdPartyNotices.txt',
    'Oxipng.Rust.Library.Notices.html', 'ExoQuant.License.txt', 'Rust.Library.Notices.html') { Add-Notice $name }
& $generator -Payload $payload
& $generator -Payload $payload -Verify
$index = Join-Path $payload 'ThirdPartyNotices.html'
$base = [IO.File]::ReadAllText($index)
Check (([regex]::Matches($base, '<li><a href=')).Count -eq 10 -and -not $base.Contains('<h2>Audio</h2>')) 'Base build links only its ten bundled notices'
foreach ($selection in @(
    @('audio-engine/payload-candidate.json', 'audio-engine/'),
    @('audio-engine/voice-provider-candidate.json', 'audio-engine/'),
    @('pdf-engine/payload-candidate.json', ''),
    @('heif-engine/payload-candidate.json', 'heif/')
)) {
    $record = Get-Content -LiteralPath (Join-Path $repository ('tools/' + $selection[0])) -Raw | ConvertFrom-Json
    foreach ($file in $record.files | Where-Object { $_.path -match '(?i)(\.(txt|md|html|ijg|patch)$|(^|/)LICENSE$)' }) {
        Add-Notice ($selection[1] + $file.path)
    }
}
foreach ($file in 'LICENSE.html', 'NOTICE', 'CREDITS.fodt', 'readmes/readme_en-US.txt') { Add-Notice ('office-engine/runtime/' + $file) }
Add-Notice 'Office.Python.Notices.txt'
Add-Notice 'Office.Dependency.Notices.html'
Copy-Item -LiteralPath (Join-Path $repository 'tools/office-engine/source-runtime-candidate.json') -Destination (Join-Path $payload 'ContextSuite.OfficeSource.json')
Expect-Refusal { & $generator -Payload $payload -Verify } 'Stale base index rejects newly added components'
& $generator -Payload $payload
& $generator -Payload $payload -Verify
$expanded = [IO.File]::ReadAllText($index)
$links = [regex]::Matches($expanded, '<a href="([^"]+)">')
Check ($links.Count -eq 63) 'Expanded build exposes all 63 selected notices and change records'
$officePythonLink = $expanded.IndexOf('href="Office.Python.Notices.txt"', [StringComparison]::Ordinal)
$officeComponentLink = $expanded.IndexOf('href="Office.Dependency.Notices.html"', [StringComparison]::Ordinal)
Check ($officePythonLink -ge 0 -and $officeComponentLink -gt $officePythonLink) 'Bundled Office index preserves Python-before-component notice order'
foreach ($link in $links) {
    $relative = [Uri]::UnescapeDataString($link.Groups[1].Value)
    Check (-not [IO.Path]::IsPathRooted($relative) -and $relative -notmatch '[:\\?#]|(^|/)\.\.(/|$)' -and
        (Test-Path -LiteralPath (Join-Path $payload $relative) -PathType Leaf)) ('Local target exists: ' + $relative)
}
Check ($expanded -notmatch '<script|<iframe|<img|https?://' -and $expanded.Contains('<html lang="en">')) 'Index needs no remote assets or scripts and identifies its language'
[IO.File]::WriteAllText($index, $expanded + 'tampered')
Expect-Refusal { & $generator -Payload $payload -Verify } 'Edited index is rejected'
[IO.File]::WriteAllText($index, $expanded)
$missing = Join-Path $payload 'heif/licenses/AOM.PATENTS.txt'
[IO.File]::Move($missing, $missing + '.held')
Expect-Refusal { & $generator -Payload $payload } 'Missing component notice prevents generation'
Check ([IO.File]::ReadAllText($index) -ceq $expanded) 'Failed generation preserves the preceding index'
[IO.File]::Move($missing + '.held', $missing)
[IO.File]::WriteAllText($missing, '')
Expect-Refusal { & $generator -Payload $payload -Verify } 'Empty component notice is rejected'
Add-Notice 'heif/licenses/AOM.PATENTS.txt'
& $generator -Payload $payload -Verify
$modeRoot = Join-Path $root 'office-modes'
$modePayload = Join-Path $modeRoot 'payload'
$modeTools = Join-Path $modeRoot 'tools'
$modeGenerator = Join-Path $modeTools 'release/Write-NoticeIndex.ps1'
$modeSource = Join-Path $modeTools 'office-engine/source-runtime-candidate.json'
New-Item -ItemType Directory -Path $modePayload, (Split-Path $modeGenerator -Parent), (Split-Path $modeSource -Parent) -Force | Out-Null
Copy-Item -LiteralPath $generator -Destination $modeGenerator
$sourceRecord = Get-Content -LiteralPath (Join-Path $repository 'tools/office-engine/source-runtime-candidate.json') -Raw | ConvertFrom-Json
function Set-OfficeMode([object] $Value, [bool] $Present = $true) {
    $record = $sourceRecord | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    if ($Present) { $record | Add-Member -NotePropertyName pythonRuntime -NotePropertyValue $Value -Force }
    else { $record.PSObject.Properties.Remove('pythonRuntime') }
    $record | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $modeSource -Encoding UTF8
    Copy-Item -LiteralPath $modeSource -Destination (Join-Path $modePayload 'ContextSuite.OfficeSource.json') -Force
}
foreach ($name in 'THIRD-PARTY-NOTICES.txt', 'DotNet.License.txt', 'DotNet.ThirdPartyNotices.txt',
    'Magick.NET.Notice.txt', 'DirectXTex.License.txt', 'Oxipng.License.txt', 'Oxipng.ThirdPartyNotices.txt',
    'Oxipng.Rust.Library.Notices.html', 'ExoQuant.License.txt', 'Rust.Library.Notices.html',
    'office-engine/runtime/LICENSE.html', 'office-engine/runtime/NOTICE', 'office-engine/runtime/CREDITS.fodt',
    'office-engine/runtime/readmes/readme_en-US.txt', 'Office.Dependency.Notices.html') {
    $path = Join-Path $modePayload $name
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($path, 'Disposable notice fixture: ' + $name)
}
$pythonNotice = Join-Path $modePayload 'Office.Python.Notices.txt'
Set-OfficeMode $null $false
Expect-Refusal { & $modeGenerator -Payload $modePayload } 'Legacy bundled mode requires Python notice'
[IO.File]::WriteAllText($pythonNotice, 'Disposable Python notice')
& $modeGenerator -Payload $modePayload
Check (([regex]::Matches([IO.File]::ReadAllText((Join-Path $modePayload 'ThirdPartyNotices.html')), '<li><a href=')).Count -eq 16) 'Legacy bundled mode indexes Python and component notices'
Set-OfficeMode 'bundled'
& $modeGenerator -Payload $modePayload -Verify
Check $true 'Explicit bundled mode matches legacy index'
Set-OfficeMode 'none'
Expect-Refusal { & $modeGenerator -Payload $modePayload } 'Python-free mode rejects stale Python notice'
Remove-Item -LiteralPath $pythonNotice
& $modeGenerator -Payload $modePayload
& $modeGenerator -Payload $modePayload -Verify
$pythonFreeIndex = [IO.File]::ReadAllText((Join-Path $modePayload 'ThirdPartyNotices.html'))
Check (([regex]::Matches($pythonFreeIndex, '<li><a href=')).Count -eq 15 -and
    -not $pythonFreeIndex.Contains('Office.Python.Notices.txt') -and
    $pythonFreeIndex.Contains('Office.Dependency.Notices.html')) 'Python-free mode indexes only required Office notices'
foreach ($invalid in @('unknown', $null)) {
    Set-OfficeMode $invalid
    Expect-Refusal { & $modeGenerator -Payload $modePayload -Verify } 'Unknown or null Python mode is refused'
}
Set-OfficeMode 'none'
Set-Content -LiteralPath (Join-Path $modePayload 'ContextSuite.OfficeSource.json') -Value '{}' -Encoding UTF8
Expect-Refusal { & $modeGenerator -Payload $modePayload -Verify } 'Mismatched packaged Office source record is refused'
Copy-Item -LiteralPath $modeSource -Destination (Join-Path $modePayload 'ContextSuite.OfficeSource.json') -Force
Remove-Item -LiteralPath (Join-Path $modePayload 'Office.Dependency.Notices.html')
Expect-Refusal { & $modeGenerator -Payload $modePayload -Verify } 'Python-free mode requires component notice'
@{ Passed = $true; Checks = $checks; Payload = $payload; Scope = 'Synthetic notices; no engine execution, installation or commercial clearance' } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding UTF8
Write-Output "Passed $($checks.Count) notice-index checks. Evidence: $root"
