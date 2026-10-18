[CmdletBinding()]
param([Parameter(Mandatory)][string] $PackageDirectory, [string] $IndexName)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$root = (Resolve-Path -LiteralPath $PackageDirectory).Path
$manifest = Join-Path $root 'AppxManifest.xml'
if (-not $IndexName -and -not (Test-Path -LiteralPath $manifest)) {
    throw 'A package manifest or explicit resource map name is required before indexing icons.'
}
$makePri = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.26100.0\x64\makepri.exe'
if (-not (Test-Path -LiteralPath $makePri)) { throw 'Windows SDK 26100 x64 MakePRI is required for taskbar assets.' }
$scratch = Join-Path $repository ('.codex-temp\shell-resource-index-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$sourceRoot = Join-Path $scratch 'package'
New-Item -ItemType Directory -Path $sourceRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Assets') -Destination $sourceRoot -Recurse
if (-not $IndexName) { Copy-Item -LiteralPath $manifest -Destination (Join-Path $sourceRoot 'AppxManifest.xml') }
$config = Join-Path $scratch 'priconfig.xml'
$index = Join-Path $root 'resources.pri'
$null = & $makePri createconfig /cf $config /dq en-US /o
if ($LASTEXITCODE -ne 0) { throw 'Taskbar resource configuration failed.' }
$mapArguments = if ($IndexName) { @('/in', $IndexName) } else { @('/mn', $manifest) }
$null = & $makePri new /pr $sourceRoot /cf $config @mapArguments /of $index /o
if ($LASTEXITCODE -ne 0) { throw 'Taskbar resource indexing failed.' }
$dump = Join-Path $scratch 'resources.xml'
$null = & $makePri dump /if $index /of $dump /dt detailed /o
if ($LASTEXITCODE -ne 0) { throw 'Taskbar resource index verification failed.' }
$indexed = Get-Content -LiteralPath $dump -Raw
foreach ($tool in 'Analyze', 'Convert', 'Optimize') {
    foreach ($size in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
        foreach ($form in '', '_altform-unplated', '_altform-lightunplated') {
            $name = "${tool}Square44x44Logo.targetsize-${size}${form}.png"
            if ($indexed -notmatch [regex]::Escape($name)) { throw "Unindexed taskbar asset: $name" }
        }
    }
}
Write-Output 'Indexed and verified 126 taskbar image candidates for Windows resource selection.'
