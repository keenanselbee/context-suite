[CmdletBinding()]
param([Parameter(Mandatory)][string] $AssetsDirectory)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$checked = 0
$shapes = @{}
foreach ($name in 'Analyze', 'Convert', 'Optimize', 'ConvertPng', 'ConvertJpeg', 'ConvertWebp',
    'ConvertBmp', 'ConvertTga', 'ConvertDds', 'ConvertWav', 'ConvertFlac', 'ConvertMp3',
    'ConvertM4a', 'ConvertOgg', 'ConvertOpus', 'ConvertPdf', 'ConvertDocx', 'ConvertWem', 'ConvertIco', 'ConvertAvif', 'Settings') {
    foreach ($suffix in '', 'Dark') {
        $path = Join-Path $AssetsDirectory "$name$suffix.ico"
        foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128) {
            $icon = [Drawing.Icon]::new($path, [Drawing.Size]::new($size, $size))
            $bitmap = $null
            try {
                if ($icon.Width -ne $size -or $icon.Height -ne $size) {
                    throw "Missing $size-pixel frame: $path"
                }
                # Exercise the Windows icon decoder, not just the source PNG renderer.
                $bitmap = $icon.ToBitmap()
                if ($bitmap.GetPixel(0, 0).A -ne 0 -or $bitmap.GetPixel($size - 1, 0).A -ne 0) {
                    throw "Icon transparency was lost: $path ($size pixels)"
                }
                $solidPixels = 0
                $alpha = [byte[]]::new($size * $size)
                for ($y = 0; $y -lt $size; $y++) {
                    for ($x = 0; $x -lt $size; $x++) {
                        $pixel = $bitmap.GetPixel($x, $y)
                        $alpha[$y * $size + $x] = $pixel.A
                        if ($pixel.A -lt 250) { continue }
                        $expected = if ($suffix) { 245 } else { 36 }
                        if ([Math]::Abs($pixel.R - $expected) -gt 24 -or
                            [Math]::Abs($pixel.G - $expected) -gt 24 -or
                            [Math]::Abs($pixel.B - $expected) -gt 24) {
                            throw "Icon color was corrupted: $path ($size pixels)"
                        }
                        $solidPixels++
                    }
                }
                if ($solidPixels -lt 10) { throw "Icon is empty or illegible: $path ($size pixels)" }
                $hasher = [Security.Cryptography.SHA256]::Create()
                try { $fingerprint = [BitConverter]::ToString($hasher.ComputeHash($alpha)) }
                finally { $hasher.Dispose() }
                $key = "$suffix/$size/$fingerprint"
                if ($shapes.ContainsKey($key)) {
                    throw "Duplicate $size-pixel silhouette: $name and $($shapes[$key]) ($suffix)."
                }
                $shapes[$key] = $name
                $checked++
            }
            finally {
                if ($bitmap) { $bitmap.Dispose() }
                $icon.Dispose()
            }
        }
    }
}
Write-Output "Validated $checked Windows icon frames: size, transparency, monochrome colors and distinct silhouettes."

$brandIcon = Join-Path $AssetsDirectory 'ContextSuite.ico'
if (-not ('ContextSuiteBrandIconNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ContextSuiteBrandIconNative
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr icon);
}
'@
}
$brandIcon = (Resolve-Path -LiteralPath $brandIcon).Path
$iconBytes = [IO.File]::ReadAllBytes($brandIcon)
$frameSizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
if ($iconBytes.Length -lt 166 -or [BitConverter]::ToUInt16($iconBytes, 2) -ne 1 -or
    [BitConverter]::ToUInt16($iconBytes, 4) -ne $frameSizes.Count) { throw 'Invalid application ICO directory.' }
for ($index = 0; $index -lt $frameSizes.Count; $index++) {
    $entry = 6 + 16 * $index
    $dimension = if ($frameSizes[$index] -eq 256) { 0 } else { $frameSizes[$index] }
    if ($iconBytes[$entry] -ne $dimension -or $iconBytes[$entry + 1] -ne $dimension) {
        throw "Missing encoded application icon frame: $($frameSizes[$index])"
    }
}
foreach ($size in 16, 20, 24, 32, 40, 48, 64, 96, 128, 256) {
    # The managed Icon constructor skips PNG-compressed 256-pixel frames.
    # Exercise the Windows loader used by Explorer, including that largest frame.
    $handle = [ContextSuiteBrandIconNative]::LoadImage([IntPtr]::Zero, $brandIcon, 1, $size, $size, 16)
    if ($handle -eq [IntPtr]::Zero) { throw [ComponentModel.Win32Exception]::new() }
    $icon = [Drawing.Icon]::FromHandle($handle)
    $bitmap = $null
    try {
        if ($icon.Width -ne $size -or $icon.Height -ne $size) { throw "Missing application icon frame: $size" }
        $bitmap = $icon.ToBitmap()
        if ($bitmap.GetPixel(0, 0).A -ne 0) { throw "Application icon lost corner transparency: $size" }
        $hole = $bitmap.GetPixel([int]($size / 2), [int]($size * 0.62))
        $outside = $bitmap.GetPixel([int]($size * 0.1), [int]($size * 0.2))
        if ($hole.A -gt 1 -or $outside.A -gt 1) {
            throw "Application triangle acquired an opaque background or center: $size"
        }
        $white = $bitmap.GetPixel([int]($size / 2), [int]($size * 0.3))
        if ($white.A -lt 240 -or $white.R -lt 200 -or $white.G -lt 200 -or $white.B -lt 200) {
            throw "Application icon lost its white triangle: $size"
        }
    }
    finally {
        if ($bitmap) { $bitmap.Dispose() }
        $icon.Dispose()
        [ContextSuiteBrandIconNative]::DestroyIcon($handle) | Out-Null
    }
}
foreach ($tool in 'Analyze', 'Convert', 'Optimize') {
    foreach ($size in 44, 150) {
        $bitmap = [Drawing.Bitmap]::new((Join-Path $AssetsDirectory "${tool}Square${size}x${size}Logo.png"))
        try {
            if ($bitmap.Width -ne $size -or $bitmap.Height -ne $size -or $bitmap.GetPixel(0, 0).A -ne 0) {
                throw "Invalid application package logo: $tool ($size)"
            }
        }
        finally { $bitmap.Dispose() }
    }
}
Write-Output 'Validated ten application icon frames and six transparent package logos.'
$taskbarChecked = 0
foreach ($tool in 'Analyze', 'Convert', 'Optimize') {
    foreach ($size in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
        foreach ($form in '', '_altform-unplated', '_altform-lightunplated') {
            $path = Join-Path $AssetsDirectory "${tool}Square44x44Logo.targetsize-${size}${form}.png"
            $bitmap = [Drawing.Bitmap]::new($path)
            try {
                if ($bitmap.Width -ne $size -or $bitmap.Height -ne $size -or
                    $bitmap.GetPixel(0, 0).A -gt 1 -or $bitmap.GetPixel($size - 1, 0).A -gt 1) {
                    throw "Taskbar icon size or transparency changed: $path"
                }
                # The lower tips can reach bottom corner pixels when downsized.
                # Verify clear space above the sides and inside the opening.
                $hole = $bitmap.GetPixel([int]($size / 2), [int]($size * 0.62))
                $outside = $bitmap.GetPixel([int]($size * 0.1), [int]($size * 0.2))
                $white = $bitmap.GetPixel([int]($size / 2), [int]($size * 0.3))
                if ($hole.A -gt 1 -or $outside.A -gt 1 -or
                    $white.A -lt 240 -or $white.R -lt 200 -or $white.G -lt 200 -or $white.B -lt 200) {
                    throw "Taskbar icon artwork changed: $path"
                }
                $taskbarChecked++
            }
            finally { $bitmap.Dispose() }
        }
    }
}
Write-Output "Validated $taskbarChecked default, dark and light taskbar PNGs with transparent corners."
