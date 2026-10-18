[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory, [switch] $IconOnly)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('ApplicationIcons' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'ApplicationIcons.cs') -ReferencedAssemblies System.Drawing
}
$sourceDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'images'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
[ApplicationIcons]::WriteIcon($sourceDirectory, (Join-Path $OutputDirectory 'ContextSuite.ico'))
if ($IconOnly) { return }

foreach ($tool in 'Analyze', 'Convert', 'Optimize') {
    foreach ($size in 44, 150) {
        $bitmap = [ApplicationIcons]::Render($sourceDirectory, $size)
        try { $bitmap.Save((Join-Path $OutputDirectory "${tool}Square${size}x${size}Logo.png"), [Drawing.Imaging.ImageFormat]::Png) }
        finally { $bitmap.Dispose() }
    }
    # Package taskbar icons need explicit unplated resources, independently of
    # the executable ICO, so Windows does not fill transparent corners.
    foreach ($size in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
        $bitmap = [ApplicationIcons]::Render($sourceDirectory, $size)
        try {
            foreach ($form in '', '_altform-unplated', '_altform-lightunplated') {
                $bitmap.Save((Join-Path $OutputDirectory "${tool}Square44x44Logo.targetsize-${size}${form}.png"), [Drawing.Imaging.ImageFormat]::Png)
            }
        }
        finally { $bitmap.Dispose() }
    }
}
$bitmap = [ApplicationIcons]::Render($sourceDirectory, 50)
try { $bitmap.Save((Join-Path $OutputDirectory 'StoreLogo.png'), [Drawing.Imaging.ImageFormat]::Png) }
finally { $bitmap.Dispose() }
