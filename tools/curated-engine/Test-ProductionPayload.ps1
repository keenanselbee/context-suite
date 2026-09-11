[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload, [switch] $AllowAudioCandidate)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-ProductionEngine.ps1') -Payload $Payload
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'dds-engine\Test-DdsEngine.ps1') -Payload $Payload
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'png-engine\Test-PngEngine.ps1') -Payload $Payload
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'palette-engine\Test-PaletteEngine.ps1') -Payload $Payload
$allowed = @('Magick.Native-Q16-x64.dll', 'Magick.NET-Q16-x64.dll', 'Magick.NET.Core.dll',
    'Magick.NET.Notice.txt', 'ContextSuite.Engine.json', 'ContextSuite.Shell.dll',
    'THIRD-PARTY-NOTICES.txt', 'DotNet.License.txt', 'DotNet.ThirdPartyNotices.txt', 'payload-inventory.json',
    'ContextSuite.Dds.Native.dll', 'ContextSuite.Dds.Engine.json', 'DirectXTex.License.txt',
    'oxipng.exe', 'ContextSuite.Png.Engine.json', 'Oxipng.License.txt',
    'ContextSuite.Palette.exe', 'ContextSuite.Palette.Engine.json', 'ExoQuant.License.txt', 'Rust.Library.Notices.html')
foreach ($name in 'Application', 'Worker') {
    foreach ($extension in 'exe', 'dll', 'pdb', 'deps.json', 'runtimeconfig.json') { $allowed += "ContextSuite.$name.$extension" }
}
foreach ($name in 'Core', 'Private', 'Commercial') { $allowed += "ContextSuite.$name.dll", "ContextSuite.$name.pdb" }
foreach ($name in 'Analyze', 'Convert', 'Optimize') { $allowed += "Assets\$name.ico", "Assets\$($name)Square44x44Logo.png", "Assets\$($name)Square150x150Logo.png" }
$allowed += 'Assets\StoreLogo.png'
$root = [IO.Path]::GetFullPath($Payload).TrimEnd('\')
if ($AllowAudioCandidate) {
    $audioTools = Join-Path (Split-Path $PSScriptRoot -Parent) 'audio-engine'
    & python -B (Join-Path $audioTools 'Stage-AudioPayload.py') --payload $root
    if ($LASTEXITCODE -ne 0) { throw 'Audio candidate payload verification failed.' }
    $audioSelection = Get-Content -LiteralPath (Join-Path $audioTools 'payload-candidate.json') -Raw | ConvertFrom-Json
    $allowed += @($audioSelection.files | ForEach-Object { 'audio-engine\' + $_.path.Replace('/', '\') })
}
foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
    if ($file.FullName.Substring($root.Length + 1) -notin $allowed) { throw "Unreviewed production file: $($file.FullName)" }
}
foreach ($required in 'THIRD-PARTY-NOTICES.txt', 'DotNet.License.txt', 'DotNet.ThirdPartyNotices.txt', 'ContextSuite.Engine.json', 'ContextSuite.Commercial.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $root $required)) -or (Get-Item -LiteralPath (Join-Path $root $required)).Length -eq 0) { throw "Missing/empty production notice/identity: $required" }
}
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if ((Get-FileHash (Join-Path $repository 'assets\THIRD-PARTY-NOTICES.txt')).Hash -ne
    (Get-FileHash (Join-Path $root 'THIRD-PARTY-NOTICES.txt')).Hash) { throw 'Product third-party acknowledgment is stale.' }
$selection = Join-Path $PSScriptRoot 'production.json'
if ((Get-FileHash $selection).Hash -ne (Get-FileHash (Join-Path $root 'ContextSuite.Engine.json')).Hash) { throw 'Packaged engine identity is stale.' }
foreach ($name in 'Application', 'Worker') {
    $dependencies = Get-Content (Join-Path $root "ContextSuite.$name.deps.json") -Raw | ConvertFrom-Json
    foreach ($library in $dependencies.libraries.PSObject.Properties) {
        if ($library.Value.type -eq 'package' -and $library.Name -notin @('Magick.NET-Q16-x64/14.17.1', 'Magick.NET.Core/14.17.1')) {
            throw "Unreviewed packaged dependency: $($library.Name)"
        }
    }
}
Write-Output 'Production file allowlist, package dependencies and notice presence passed (development payload, not release clearance).'
