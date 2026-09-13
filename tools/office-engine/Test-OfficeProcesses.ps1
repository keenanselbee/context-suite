[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scratch = Join-Path $repository ('.codex-temp\office-process-' + [guid]::NewGuid().ToString('N'))
& dotnet run --project (Join-Path $PSScriptRoot 'Probe\Office.Evaluation.csproj') -c Release -- --process-contracts $scratch
if ($LASTEXITCODE) { throw 'Office process contracts failed; inspect retained scratch.' }
Write-Output 'Owned evaluation helper processes only; no Office engine, AppContainer profile or access-isolation claim.'
