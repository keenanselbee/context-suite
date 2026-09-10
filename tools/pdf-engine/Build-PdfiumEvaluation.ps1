[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreparedDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$prepared = (Resolve-Path -LiteralPath $PreparedDirectory).Path
$allowed = [IO.Path]::GetFullPath((Join-Path $repository '.codex-temp\pdfium-engine')) + '\'
if (-not $prepared.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use repository-local PDFium staging.' }
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'pdfium-evaluation.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $prepared 'upstream.tgz')).Hash -ne $pin.archiveSha256) { throw 'PDFium archive identity changed.' }
$payload = Join-Path $prepared 'unpacked'
$inventory = Get-Content -LiteralPath (Join-Path $prepared 'inventory.json') -Raw | ConvertFrom-Json
foreach ($item in $inventory) {
    $path = [IO.Path]::GetFullPath((Join-Path $payload $item.path))
    if (-not $path.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid PDFium inventory path.' }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $item.sha256) { throw "PDFium evaluation file changed: $($item.path)" }
}
$configuration = Get-Content -LiteralPath (Join-Path $payload 'args.gn') -Raw
if ($configuration -notmatch '(?m)^pdf_enable_v8 = false\s*$' -or $configuration -notmatch '(?m)^pdf_enable_xfa = false\s*$') {
    throw 'Evaluation requires declared V8 and XFA disabled.'
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio x64 C++ tools are required.' }
$cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$build = Join-Path $prepared 'probe-build'
& $cmake -S (Join-Path $PSScriptRoot 'PdfiumProbe') -B $build -G 'Visual Studio 18 2026' -A x64 "-DPDFIUM_PAYLOAD=$payload" '-DCMAKE_SYSTEM_VERSION=10.0.26100.0'
if ($LASTEXITCODE) { throw 'PDFium evaluation configuration failed.' }
& $cmake --build $build --config Release --target ContextSuite.Pdfium.Probe -- /m /p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false /verbosity:minimal
if ($LASTEXITCODE) { throw 'PDFium evaluation probe build failed.' }
$executable = Join-Path $build 'bin\Release\ContextSuite.Pdfium.Probe.exe'
[ordered]@{ executable = $executable; sha256 = (Get-FileHash -LiteralPath $executable).Hash;
    bridgeSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'PdfiumProbe\Probe.cpp')).Hash;
    buildSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'PdfiumProbe\CMakeLists.txt')).Hash;
    pdfiumSha256 = (Get-FileHash -LiteralPath (Join-Path $payload 'bin\pdfium.dll')).Hash;
    visualStudio = $visualStudio; windowsSdk = '10.0.26100.0' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $prepared 'probe-build.json') -Encoding UTF8
Write-Output "Isolated PDFium probe: $executable"
