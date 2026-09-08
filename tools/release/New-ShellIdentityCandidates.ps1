[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Candidate,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]{2,45}$')][string] $IdentityPrefix,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $Publisher,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string] $Version
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (($Version.Split('.') | Where-Object { [int]$_ -gt 65535 }).Count) { throw 'MSIX version components must fit UInt16.' }
& (Join-Path $PSScriptRoot 'Test-ReleaseCandidate.ps1') -Candidate $Candidate
$root = (Resolve-Path -LiteralPath $Candidate).Path
$makeAppx = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe'
if (-not (Test-Path -LiteralPath $makeAppx)) { throw 'Windows SDK 26100 x64 MakeAppx is required.' }
$output = Join-Path $repository ('artifacts\identity-candidates\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$packages = @()
foreach ($tool in 'Analyze', 'Convert', 'Optimize') {
    [xml] $document = Get-Content -LiteralPath (Join-Path $repository "packaging\ContextSuite.$tool.ShellPrototype\AppxManifest.xml") -Raw -Encoding UTF8
    $name = "$IdentityPrefix.$tool"
    $document.Package.Identity.Name = $name
    $document.Package.Identity.Publisher = $Publisher
    $document.Package.Identity.Version = $Version
    $document.Package.Properties.DisplayName = "Context Suite $tool"
    $document.Package.Applications.Application.Executable = 'ContextSuite.Application.exe'
    $folder = Join-Path $output $tool
    New-Item -ItemType Directory -Path $folder | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'app\Assets') -Destination $folder -Recurse
    $document.Save((Join-Path $folder 'AppxManifest.xml'))
    $package = Join-Path $output "$name.msix"
    # External app/DLL paths deliberately live outside this sparse identity package.
    & $makeAppx pack /d $folder /p $package /nv
    if ($LASTEXITCODE -ne 0) { throw "Identity package build failed: $tool" }
    $packages += [ordered]@{ name = $name; file = "$name.msix"; sha256 = (Get-FileHash -LiteralPath $package).Hash }
}
$identity = [ordered]@{
    status = 'unsigned-uninstalled-identity-candidates'; publisher = $Publisher; version = $Version;
    candidateManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $root 'release-manifest.json')).Hash;
    packages = $packages
}
[IO.File]::WriteAllText((Join-Path $output 'identity-candidates.json'), ($identity | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output "Unsigned identity packages: $output. Publisher must match the eventual trusted certificate. Nothing installed or signed."
