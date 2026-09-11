[CmdletBinding()]
param([string] $PreparedDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pinPath = Join-Path $PSScriptRoot 'evaluation.json'
$pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
if ($PreparedDirectory) {
    $scratch = (Resolve-Path -LiteralPath $PreparedDirectory).Path
    if (-not $scratch.StartsWith((Join-Path $repository '.codex-temp\office-engine\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Use the isolated office-engine scratch tree.'
    }
} else {
    $scratch = Join-Path $repository ('.codex-temp\office-engine\' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri $pin.url -OutFile (Join-Path $scratch 'upstream.msi') -TimeoutSec 300
}
$archive = Join-Path $scratch 'upstream.msi'
if ((Get-FileHash -LiteralPath $archive).Hash -ne $pin.archiveSha256) { throw 'Office evaluation MSI hash mismatch.' }
Copy-Item -LiteralPath $pinPath -Destination (Join-Path $scratch 'evaluation.json')
& dotnet run --project (Join-Path $PSScriptRoot 'Unpack\Office.Unpack.csproj') -c Release -- --unpack $scratch
if ($LASTEXITCODE) { throw "Office database extraction failed; evidence retained at $scratch" }
Write-Output "Pinned Office evaluation payload: $scratch"
Write-Output 'MSI opened read-only; no installer actions, registration, production staging or PATH changes.'
