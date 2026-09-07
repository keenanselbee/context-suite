[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9-]+$')][string] $RunName = 'prototype-2')

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workspace = Join-Path $repository ".codex-temp\curated-engine\$RunName"
if (-not (Test-Path -LiteralPath (Join-Path $workspace 'preparation.json'))) { throw 'Prepare a fresh isolated workspace first.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio x64 C++ tools are required.' }
$msbuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
$imageRoot = Join-Path $workspace 'native\src\ImageMagick'
Push-Location (Join-Path $imageRoot 'Configure')
try {
    & .\Configure.Release.x64.exe /noWizard /VS2026 /static /Q16 /opencl /noDpc /noHdri /noOpenMP /x64 /linkRuntime /onlyMagick
    if ($LASTEXITCODE -ne 0) { throw 'Native configuration failed.' }
} finally { Pop-Location }
Push-Location $workspace
try {
    # Build only the libraries needed by the native wrapper, not upstream apps/fuzzers.
    foreach ($project in 'CORE_MagickCore', 'CORE_MagickWand', 'CORE_coders') {
        & $msbuild (Join-Path $imageRoot "ProjectFiles\x64\$project\$project.vcxproj") /m:4 /nologo /verbosity:quiet /t:Rebuild /p:Configuration=Release /p:Platform=x64 "/p:SolutionDir=$imageRoot/" "/flp:logfile=$project-build.log;verbosity=normal"
        if ($LASTEXITCODE -ne 0) { throw "ImageMagick build failed: $project" }
    }
    & $msbuild 'native\src\Magick.Native\Magick.Native.vcxproj' /m:4 /nologo /verbosity:quiet /t:Rebuild /p:Configuration=ReleaseQ16 /p:Platform=x64 '/flp:logfile=native-build.log;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Native wrapper build failed.' }
    $binary = Join-Path $workspace 'native\src\Magick.Native\bin\ReleaseQ16\x64\Magick.Native-Q16-x64.dll'
    $result = @{ status = 'built-not-verified'; sha256 = (Get-FileHash $binary).Hash; bytes = (Get-Item $binary).Length; msbuild = $msbuild; msbuildVersion = (Get-Item $msbuild).VersionInfo.FileVersion }
    [IO.File]::WriteAllText((Join-Path $workspace 'build-result.json'), ($result | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    Write-Output "Built isolated candidate: $binary"
} finally { Pop-Location }
