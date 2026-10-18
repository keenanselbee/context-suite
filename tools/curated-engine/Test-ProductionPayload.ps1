[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload, [switch] $AllowAudioCandidate, [switch] $AllowPdfCandidate,
    [switch] $AllowOfficeCandidate, [switch] $AllowHeifCandidate, [switch] $AllowWemCandidate,
    [switch] $AllowImageExpansionCandidate, [switch] $AllowPdfDarkCandidate)
$ErrorActionPreference = 'Stop'
if ($AllowOfficeCandidate -and -not $AllowPdfCandidate) {
    throw 'The Office candidate requires the PDF candidate for independent output validation.'
}
if ($AllowPdfDarkCandidate -and -not $AllowPdfCandidate) {
    throw 'The PDF dark-mode candidate requires the complete PDF candidate.'
}
if ($AllowImageExpansionCandidate -and -not $AllowHeifCandidate) {
    throw 'The AVIF output candidate requires the pinned HEIF decoder for independent validation.'
}
& (Join-Path $PSScriptRoot 'Test-ProductionEngine.ps1') -Payload $Payload
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'dds-engine\Test-DdsEngine.ps1') -Payload $Payload
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'png-engine\Test-PngEngine.ps1') -Payload $Payload -RequireDependencyNotices
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'palette-engine\Test-PaletteEngine.ps1') -Payload $Payload
$allowed = @('Magick.Native-Q16-x64.dll', 'Magick.NET-Q16-x64.dll', 'Magick.NET.Core.dll',
    'Magick.NET.Notice.txt', 'ContextSuite.Engine.json', 'ContextSuite.Shell.dll',
    'THIRD-PARTY-NOTICES.txt', 'ThirdPartyNotices.html', 'DotNet.License.txt', 'DotNet.ThirdPartyNotices.txt', 'payload-inventory.json',
    'ContextSuite.Dds.Native.dll', 'ContextSuite.Dds.Engine.json', 'DirectXTex.License.txt',
    'oxipng.exe', 'ContextSuite.Png.Engine.json', 'Oxipng.License.txt', 'Oxipng.ThirdPartyNotices.txt', 'Oxipng.Rust.Library.Notices.html',
    'ContextSuite.Palette.exe', 'ContextSuite.Palette.Engine.json', 'ExoQuant.License.txt', 'Rust.Library.Notices.html')
$docxSelection = Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'pdf-engine/pdf-docx-engine.json') -Raw | ConvertFrom-Json
foreach ($entry in $docxSelection.files) {
    $allowed += $entry.path
    $file = Join-Path $Payload $entry.path
    if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Missing or changed PDF-to-Word dependency: $($entry.path)"
    }
}
foreach ($name in 'Application', 'Worker') {
    foreach ($extension in 'exe', 'dll', 'pdb', 'deps.json', 'runtimeconfig.json') { $allowed += "ContextSuite.$name.$extension" }
}
foreach ($name in 'Core', 'Private', 'Commercial') { $allowed += "ContextSuite.$name.dll", "ContextSuite.$name.pdb" }
foreach ($name in 'Analyze', 'Convert', 'Optimize') { $allowed += "Assets\$name.ico", "Assets\$($name)Dark.ico", "Assets\$($name)Square44x44Logo.png", "Assets\$($name)Square150x150Logo.png" }
foreach ($name in 'ConvertPng', 'ConvertJpeg', 'ConvertWebp', 'ConvertBmp', 'ConvertTga', 'ConvertDds',
    'ConvertWav', 'ConvertFlac', 'ConvertMp3', 'ConvertM4a', 'ConvertOgg', 'ConvertOpus',
    'ConvertPdf', 'ConvertDocx', 'ConvertWem', 'ConvertIco', 'ConvertAvif', 'Settings') {
    $allowed += "Assets\$name.ico", "Assets\$($name)Dark.ico"
}
$allowed += 'Assets\StoreLogo.png', 'Assets\ContextSuite.ico', 'resources.pri'
$taskbarAssets = @(
    foreach ($tool in 'Analyze', 'Convert', 'Optimize') {
        foreach ($size in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
            foreach ($form in '', '_altform-unplated', '_altform-lightunplated') {
                "Assets\${tool}Square44x44Logo.targetsize-${size}${form}.png"
            }
        }
    }
)
$allowed += $taskbarAssets
$root = [IO.Path]::GetFullPath($Payload).TrimEnd('\')
$applicationVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $root 'ContextSuite.Application.dll')).Version
if ($applicationVersion -ge [version]'1.3.8.0' -and (Test-Path -LiteralPath (Join-Path $root 'ContextSuite.Shell.dll'))) {
    if (-not (Test-Path -LiteralPath (Join-Path $root 'resources.pri') -PathType Leaf)) {
        throw 'Missing external-location taskbar resource index: resources.pri'
    }
}
if ($applicationVersion -ge [version]'1.3.7.0' -and (Test-Path -LiteralPath (Join-Path $root 'ContextSuite.Shell.dll'))) {
    foreach ($asset in $taskbarAssets) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $asset) -PathType Leaf)) { throw "Missing taskbar icon: $asset" }
    }
}
if ($applicationVersion -ge [version]'1.3.6.0' -and (Test-Path -LiteralPath (Join-Path $root 'ContextSuite.Shell.dll'))) {
    $brandIcon = Join-Path $root 'Assets\ContextSuite.ico'
    if (-not (Test-Path -LiteralPath $brandIcon -PathType Leaf) -or (Get-Item -LiteralPath $brandIcon).Length -eq 0) {
        throw 'Missing application branding: Assets\ContextSuite.ico'
    }
}
if ($AllowAudioCandidate) {
    $audioTools = Join-Path (Split-Path $PSScriptRoot -Parent) 'audio-engine'
    & python -B (Join-Path $audioTools 'Stage-AudioPayload.py') --payload $root
    if ($LASTEXITCODE -ne 0) { throw 'Audio candidate payload verification failed.' }
    $engines = & (Join-Path (Split-Path $PSScriptRoot -Parent) 'release\Get-CandidateEngines.ps1') -Payload $root
    $audioRecord = if ($engines.audio -eq 'curated') { 'payload-candidate.json' } else { "$($engines.audio)-payload-candidate.json" }
    $audioSelection = Get-Content -LiteralPath (Join-Path $audioTools $audioRecord) -Raw | ConvertFrom-Json
    $allowed += @($audioSelection.files | ForEach-Object { 'audio-engine\' + $_.path.Replace('/', '\') })
    if (Test-Path -LiteralPath (Join-Path $root 'audio-engine\voice')) {
        $voiceSelection = Get-Content -LiteralPath (Join-Path $audioTools 'voice-provider-candidate.json') -Raw | ConvertFrom-Json
        $allowed += @($voiceSelection.files | ForEach-Object { 'audio-engine\' + $_.path.Replace('/', '\') })
    }
}
if ($AllowPdfCandidate) {
    $pdfTools = Join-Path (Split-Path $PSScriptRoot -Parent) 'pdf-engine'
    & python -B (Join-Path $pdfTools 'Stage-PdfPayload.py') --payload $root
    if ($LASTEXITCODE -ne 0) { throw 'PDF candidate payload verification failed.' }
    $pdfSelection = Get-Content -LiteralPath (Join-Path $pdfTools 'payload-candidate.json') -Raw | ConvertFrom-Json
    $allowed += @($pdfSelection.files | ForEach-Object { $_.path.Replace('/', '\') })
}
if ($AllowPdfDarkCandidate) {
    $darkTools = Join-Path (Split-Path $PSScriptRoot -Parent) 'pdf-dark-engine'
    & python -B (Join-Path $darkTools 'Stage-PdfDarkPayload.py') --payload $root
    if ($LASTEXITCODE -ne 0) { throw 'PDF dark-mode candidate payload verification failed.' }
    $pdfSelection = Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'pdf-engine\payload-candidate.json') -Raw | ConvertFrom-Json
    $allowed += 'pdf-dark\ContextSuite.PdfDark.exe'
    $allowed += @($pdfSelection.files | Where-Object { $_.path -like 'pdf-engine/*' -and $_.path -ne 'pdf-engine/qpdf.exe' } |
        ForEach-Object { 'pdf-dark\' + [IO.Path]::GetFileName($_.path) })
}
if ($AllowOfficeCandidate) {
    $officeTools = Join-Path (Split-Path $PSScriptRoot -Parent) 'office-engine'
    & python -B (Join-Path $officeTools 'Stage-OfficePayload.py') --payload $root
    if ($LASTEXITCODE -ne 0) { throw 'Office candidate payload verification failed.' }
    $officeSource = Get-Content -LiteralPath (Join-Path $officeTools 'source-runtime-candidate.json') -Raw | ConvertFrom-Json
    $pythonMode = if ($officeSource.PSObject.Properties.Name -contains 'pythonRuntime') { $officeSource.pythonRuntime } else { 'bundled' }
    if ($pythonMode -cnotin @('bundled', 'none')) { throw "Unsupported Office Python runtime mode: $pythonMode" }
    $allowed += 'office-engine\ContextSuite.OfficeHost.exe', 'office-engine\runtime-files.txt', 'ContextSuite.OfficeSource.json', 'Office.Dependency.Notices.html'
    if ($pythonMode -ceq 'bundled') {
        $allowed += 'Office.Python.Notices.txt'
    } elseif (Test-Path -LiteralPath (Join-Path $root 'Office.Python.Notices.txt')) {
        throw 'Python-free Office payload contains stale Python notices.'
    }
    $officeSelection = Get-Content -LiteralPath (Join-Path $officeTools 'payload-candidate.json') -Raw | ConvertFrom-Json
    $allowed += @($officeSelection.hostDependencies | ForEach-Object { 'office-engine\' + $_.path })
    $allowed += @(Get-Content -LiteralPath (Join-Path $root 'office-engine\runtime-files.txt') | ForEach-Object {
        'office-engine\runtime\' + ($_ -split "`t", 3)[0].Replace('/', '\')
    })
}
if ($AllowHeifCandidate) {
    $heifTools = Join-Path (Split-Path $PSScriptRoot -Parent) 'heif-engine'
    & python -B (Join-Path $heifTools 'Stage-HeifPayload.py') --payload $root
    if ($LASTEXITCODE -ne 0) { throw 'HEIF candidate payload verification failed.' }
    $heifSelection = Get-Content -LiteralPath (Join-Path $heifTools 'payload-candidate.json') -Raw | ConvertFrom-Json
    $allowed += @($heifSelection.files | ForEach-Object { 'heif\' + $_.path.Replace('/', '\') })
}
if ($AllowWemCandidate) {
    $wemTools = Join-Path (Split-Path $PSScriptRoot -Parent) 'wem-engine'
    & python -B (Join-Path $wemTools 'Stage-WemPayload.py') --payload $root --inventory
    if ($LASTEXITCODE -ne 0) { throw 'WEM decoder candidate payload verification failed.' }
    $wemReceipt = Get-Content -LiteralPath (Join-Path $root 'wem-engine\provenance\build-receipt.json') -Raw | ConvertFrom-Json
    $allowed += 'wem-engine\provenance\build-receipt.json'
    foreach ($entry in $wemReceipt.files.PSObject.Properties.Name) {
        $relative = if ($entry.StartsWith('bin/')) { $entry.Substring(4) } else { $entry }
        $allowed += 'wem-engine\' + $relative.Replace('/', '\')
    }
}
if ($AllowImageExpansionCandidate) {
    $imageTools = Join-Path (Split-Path $PSScriptRoot -Parent) 'image-expansion-engine'
    & python -B (Join-Path $imageTools 'Stage-ImageExpansionPayload.py') --payload $root --inventory
    if ($LASTEXITCODE -ne 0) { throw 'AVIF/SVG candidate payload verification failed.' }
    $imageSelection = Get-Content -LiteralPath (Join-Path $imageTools 'production.json') -Raw | ConvertFrom-Json
    foreach ($engine in 'avif', 'svg') {
        $allowed += @($imageSelection.engines.$engine.files.PSObject.Properties.Name | ForEach-Object { "$engine\" + $_.Replace('/', '\') })
    }
}
# Office has thousands of files. Keep membership lookup linear in payload size.
$allowedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in $allowed) { [void]$allowedSet.Add($name) }
foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
    if (-not $allowedSet.Contains($file.FullName.Substring($root.Length + 1))) { throw "Unreviewed production file: $($file.FullName)" }
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
        if ($library.Value.type -eq 'package' -and $library.Name -notin @('Magick.NET-Q16-x64/14.17.1', 'Magick.NET.Core/14.17.1', 'PdfPig/0.1.16')) {
            throw "Unreviewed packaged dependency: $($library.Name)"
        }
    }
}
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'release\Write-NoticeIndex.ps1') -Payload $root -Verify
Write-Output 'Production file allowlist, package dependencies and notice presence passed (development payload, not release clearance).'
