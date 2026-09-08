[CmdletBinding()]
param([Parameter(Mandatory)][string] $VisualCppRedistributable)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pins = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-inputs.json') -Raw | ConvertFrom-Json
$output = Join-Path $repository 'artifacts\installer-inputs'
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($input in @($pins.inno, $pins.desktopRuntime, $pins.visualCppRuntime)) {
    $path = Join-Path $output $input.file
    if (-not (Test-Path -LiteralPath $path)) {
        if ($input -eq $pins.visualCppRuntime) {
            Copy-Item -LiteralPath $VisualCppRedistributable -Destination $path
        } else {
            Invoke-WebRequest -UseBasicParsing -Uri $input.url -OutFile $path
        }
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm $input.algorithm).Hash -ne $input.hash) {
        throw "Pinned input mismatch: $path. Do not update the pin to bypass review."
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -ne $input.signer) {
        throw "Input signature/publisher mismatch: $path"
    }
}
# Official portable mode: no shortcuts, associations, uninstall entry or PATH changes.
# This provisions the compiler only. Never execute either runtime redistributable here.
$compiler = Join-Path $output ('inno-' + $pins.inno.version)
if (-not (Test-Path -LiteralPath (Join-Path $compiler 'ISCC.exe'))) {
    $process = Start-Process -FilePath (Join-Path $output $pins.inno.file) -ArgumentList @(
        '/PORTABLE=1', '/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-',
        ('/DIR="' + $compiler + '"'), ('/LOG="' + (Join-Path $output 'compiler-provision.log') + '"')
    ) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Portable compiler provisioning failed: $($process.ExitCode)" }
}
$compilerFile = Join-Path $compiler 'ISCC.exe'
if ((Get-FileHash -LiteralPath $compilerFile).Hash -ne $pins.inno.compilerSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $compiler 'ISCmplr.dll')).Hash -ne $pins.inno.compilerEngineSha256 -or
    (Get-AuthenticodeSignature -LiteralPath $compilerFile).Status -ne 'Valid') {
    throw 'Pinned compiler version/signature mismatch.'
}
Write-Output "Verified offline inputs and portable compiler: $output. No application or runtimes installed."
