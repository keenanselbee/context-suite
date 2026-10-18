[CmdletBinding()]
param([Parameter(Mandatory)][string] $Payload, [switch] $Verify)

$ErrorActionPreference = 'Stop'
$toolsRoot = Split-Path $PSScriptRoot -Parent
$root = (Get-Item -LiteralPath $Payload).FullName
$groups = [ordered]@{
    'Application and image tools' = @('THIRD-PARTY-NOTICES.txt', 'DotNet.License.txt', 'DotNet.ThirdPartyNotices.txt',
        'Magick.NET.Notice.txt', 'DirectXTex.License.txt', 'Oxipng.License.txt', 'Oxipng.ThirdPartyNotices.txt',
        'Oxipng.Rust.Library.Notices.html', 'ExoQuant.License.txt', 'Rust.Library.Notices.html')
}
if (Test-Path -LiteralPath (Join-Path $root 'UglyToad.PdfPig.dll')) {
    $groups['PDF-to-Word'] = @('PdfPig.License.txt', 'PdfPig.Notices.txt')
}
foreach ($engine in @(
    @{ Directory = 'audio-engine'; Record = 'audio-engine/payload-candidate.json'; Title = 'Audio'; Prefix = 'audio-engine/' },
    @{ Directory = 'audio-engine/voice'; Record = 'audio-engine/voice-provider-candidate.json'; Title = 'Windows Voice audio'; Prefix = 'audio-engine/' },
    @{ Directory = 'pdf-engine'; Record = 'pdf-engine/payload-candidate.json'; Title = 'PDF tools'; Prefix = '' },
    @{ Directory = 'heif'; Record = 'heif-engine/payload-candidate.json'; Title = 'HEIC and AVIF images'; Prefix = 'heif/' }
)) {
    if (Test-Path -LiteralPath (Join-Path $root $engine.Directory)) {
        $record = Get-Content -LiteralPath (Join-Path $toolsRoot $engine.Record) -Raw | ConvertFrom-Json
        $groups[$engine.Title] = @($record.files | Where-Object {
            $_.path -match '(?i)(\.(txt|md|html|ijg|patch)$|(^|/)LICENSE$)'
        } | ForEach-Object { $engine.Prefix + $_.path } | Sort-Object)
        if ($engine.Directory -eq 'audio-engine') {
            # Supported profiles currently ship the same notice paths, with
            # different contents. Their owning payload verifier checks the hashes.
            $expanded = Get-Content -LiteralPath (Join-Path $toolsRoot 'audio-engine/expanded-payload-candidate.json') -Raw | ConvertFrom-Json
            $expandedPaths = @($expanded.files | Where-Object { $_.path -match '(?i)(\.(txt|md|html|ijg|patch)$|(^|/)LICENSE$)' } |
                ForEach-Object { $engine.Prefix + $_.path } | Sort-Object)
            if (Compare-Object $groups[$engine.Title] $expandedPaths) { throw 'Audio profile notice paths differ; update index selection before packaging.' }
            $videoRecord = Join-Path $toolsRoot 'audio-engine/video-payload-candidate.json'
            if (Test-Path -LiteralPath $videoRecord) {
                $video = Get-Content -LiteralPath $videoRecord -Raw | ConvertFrom-Json
                $videoPaths = @($video.files | Where-Object { $_.path -match '(?i)(\.(txt|md|html|ijg|patch)$|(^|/)LICENSE$)' } |
                    ForEach-Object { $engine.Prefix + $_.path } | Sort-Object)
                if (Compare-Object $groups[$engine.Title] $videoPaths) { throw 'Video audio notice paths differ; update index selection before packaging.' }
            }
        }
        if ($groups[$engine.Title].Count -eq 0) { throw "No notices selected for $($engine.Title)." }
    }
}
if (Test-Path -LiteralPath (Join-Path $root 'office-engine')) {
    $officeSourcePath = Join-Path $toolsRoot 'office-engine/source-runtime-candidate.json'
    $packagedSourcePath = Join-Path $root 'ContextSuite.OfficeSource.json'
    if (-not (Test-Path -LiteralPath $packagedSourcePath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $officeSourcePath -Algorithm SHA256).Hash -cne
        (Get-FileHash -LiteralPath $packagedSourcePath -Algorithm SHA256).Hash) {
        throw 'Packaged Office source provenance record is missing or differs from the selected record.'
    }
    $officeSource = Get-Content -LiteralPath $officeSourcePath -Raw | ConvertFrom-Json
    $pythonMode = if ($officeSource.PSObject.Properties.Name -contains 'pythonRuntime') { $officeSource.pythonRuntime } else { 'bundled' }
    if ($pythonMode -cnotin @('bundled', 'none')) { throw "Unsupported Office Python runtime mode: $pythonMode" }
    $groups['Office documents'] = @('office-engine/runtime/LICENSE.html', 'office-engine/runtime/NOTICE',
        'office-engine/runtime/CREDITS.fodt', 'office-engine/runtime/readmes/readme_en-US.txt')
    if ($pythonMode -ceq 'bundled') {
        $groups['Office documents'] += 'Office.Python.Notices.txt'
    } elseif (Test-Path -LiteralPath (Join-Path $root 'Office.Python.Notices.txt')) {
        throw 'Python-free Office payload contains stale Python notices.'
    }
    $groups['Office documents'] += 'Office.Dependency.Notices.html'
}
if (Test-Path -LiteralPath (Join-Path $root 'pdf-dark')) {
    $groups['PDF dark mode'] = @('pdf-dark/LICENSE.txt', 'pdf-dark/NOTICE.md')
}
if (Test-Path -LiteralPath (Join-Path $root 'wem-engine')) {
    $groups['WEM audio'] = @(
        'wem-engine/provenance/notices/vgmstream-COPYING.txt',
        'wem-engine/provenance/notices/GNU-LGPL-2.1.txt',
        'wem-engine/provenance/notices/Xiph-libvorbis-1.3.7.txt',
        'wem-engine/provenance/notices/Xiph-libogg-1.3.5.txt',
        'wem-engine/provenance/notices/linked-embedded-comments.txt',
        'wem-engine/provenance/recipe/README.md',
        'wem-engine/provenance/source/vgmstream-71e2361042531fe767fb98300cf8c1ee95e539a0-wem-only-source.zip'
    )
}
if ((Test-Path -LiteralPath (Join-Path $root 'avif')) -or (Test-Path -LiteralPath (Join-Path $root 'svg'))) {
    if (-not (Test-Path -LiteralPath (Join-Path $root 'avif')) -or -not (Test-Path -LiteralPath (Join-Path $root 'svg'))) {
        throw 'AVIF and SVG distribution inputs must be packaged together.'
    }
    $selection = Get-Content -LiteralPath (Join-Path $toolsRoot 'image-expansion-engine/production.json') -Raw | ConvertFrom-Json
    foreach ($engine in 'avif', 'svg') {
        $title = if ($engine -eq 'avif') { 'AVIF encoder' } else { 'SVG renderer' }
        $groups[$title] = @($selection.engines.$engine.files.PSObject.Properties.Name | Where-Object {
            $_.StartsWith('provenance/notices/') -or $_.StartsWith('provenance/licenses/') -or
            $_.StartsWith('provenance/recipe/') -or $_.StartsWith('provenance/source/') -or
            $_ -eq 'provenance/dependency-inventory.json'
        } | ForEach-Object { "$engine/$_" } | Sort-Object)
        if ($groups[$title].Count -eq 0) { throw "No notices or source materials selected for $title." }
    }
}
$lines = [Collections.Generic.List[string]]::new()
$lines.Add(@'
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
<title>Context Suite - Third-party notices</title>
<style>
:root { color-scheme: light dark; font: 1rem/1.6 system-ui, sans-serif; }
body { max-width: 60rem; margin: 2rem auto; padding: 0 1.5rem; }
a { overflow-wrap: anywhere; }
a:focus-visible { outline: 3px solid currentColor; outline-offset: 3px; }
li { margin-block: .4rem; }
</style>
</head>
<body>
<main>
<h1>Third-party notices</h1>
<p>Context Suite includes software from other projects. The links below open the license, copyright and credit files included with this copy of the application.</p>
'@)
$links = 0
foreach ($title in $groups.Keys) {
    $lines.Add('<section><h2>' + [Net.WebUtility]::HtmlEncode($title) + '</h2><ul>')
    foreach ($relative in $groups[$title]) {
        # Only link files inside this payload, never a URI or a traversal path.
        if ($relative -match '[:\\?#]' -or $relative.StartsWith('/') -or ($relative.Split('/') | Where-Object { $_ -in @('', '.', '..') })) {
            throw "Invalid notice path: $relative"
        }
        $file = Get-Item -LiteralPath (Join-Path $root $relative) -ErrorAction Stop
        if ($file.PSIsContainer -or $file.Length -eq 0) { throw "Missing/empty notice: $relative" }
        $href = ($relative.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
        $label = [Net.WebUtility]::HtmlEncode($relative)
        $lines.Add('<li><a href="' + $href + '">' + $label + '</a></li>')
        $links++
    }
    $lines.Add('</ul></section>')
}
$lines.Add('</main></body></html>')
# Stable bytes allow the packaging check to reject a stale or edited index.
$content = ($lines -join "`n").Replace("`r`n", "`n") + "`n"
$index = Join-Path $root 'ThirdPartyNotices.html'
if ($Verify) {
    if (-not (Test-Path -LiteralPath $index) -or [IO.File]::ReadAllText($index) -cne $content) {
        throw 'The bundled third-party notice index is missing or stale.'
    }
} else {
    [IO.File]::WriteAllText($index, $content, [Text.UTF8Encoding]::new($false))
}
Write-Output "Third-party notice index: $links local links verified."
