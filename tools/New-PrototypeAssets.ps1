[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
if (-not ('ContextSuiteIconNativeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ContextSuiteIconNativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);
}
'@
}

$assetsDirectory = Join-Path $OutputDirectory 'Assets'
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null

foreach ($retiredName in @(
    'Inspect.ico',
    'InspectSquare44x44Logo.png',
    'InspectSquare150x150Logo.png'
)) {
    $retiredPath = Join-Path $assetsDirectory $retiredName
    if (Test-Path -LiteralPath $retiredPath) {
        Remove-Item -LiteralPath $retiredPath -Force
    }
}

function New-Logo {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [int] $Size,

        [Parameter(Mandatory)]
        [string] $Color,

        [Parameter(Mandatory)]
        [string] $Letter,

        [string] $IconPath
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $brush = $null
    $font = $null
    $textBrush = $null
    $format = $null
    $iconHandle = [IntPtr]::Zero
    $icon = $null
    $iconStream = $null

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($Color))
        $graphics.FillRectangle($brush, 0, 0, $Size, $Size)

        $font = [System.Drawing.Font]::new(
            'Segoe UI',
            [single] ($Size * 0.50),
            [System.Drawing.FontStyle]::Bold,
            [System.Drawing.GraphicsUnit]::Pixel)
        $textBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
        $format = [System.Drawing.StringFormat]::new()
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $graphics.DrawString(
            $Letter,
            $font,
            $textBrush,
            [System.Drawing.RectangleF]::new(0, 0, $Size, $Size),
            $format)

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        if ($IconPath) {
            $iconHandle = $bitmap.GetHicon()
            $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
            $iconStream = [System.IO.File]::Create($IconPath)
            $icon.Save($iconStream)
        }
    }
    finally {
        if ($iconStream) { $iconStream.Dispose() }
        if ($icon) { $icon.Dispose() }
        if ($iconHandle -ne [IntPtr]::Zero) {
            [ContextSuiteIconNativeMethods]::DestroyIcon($iconHandle) | Out-Null
        }
        if ($format) { $format.Dispose() }
        if ($textBrush) { $textBrush.Dispose() }
        if ($font) { $font.Dispose() }
        if ($brush) { $brush.Dispose() }
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$logos = @(
    @{ Name = 'Analyze'; Color = '#2563EB'; Letter = 'A' },
    @{ Name = 'Convert'; Color = '#7C3AED'; Letter = 'C' },
    @{ Name = 'Optimize'; Color = '#059669'; Letter = 'O' }
)

foreach ($logo in $logos) {
    New-Logo -Path (Join-Path $assetsDirectory "$($logo.Name)Square44x44Logo.png") `
        -Size 44 -Color $logo.Color -Letter $logo.Letter `
        -IconPath (Join-Path $assetsDirectory "$($logo.Name).ico")
    New-Logo -Path (Join-Path $assetsDirectory "$($logo.Name)Square150x150Logo.png") `
        -Size 150 -Color $logo.Color -Letter $logo.Letter
}

New-Logo -Path (Join-Path $assetsDirectory 'StoreLogo.png') `
    -Size 50 -Color '#111827' -Letter 'C'
