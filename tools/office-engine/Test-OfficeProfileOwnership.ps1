[CmdletBinding()]
param([switch] $CreateDisposableProfile)
$ErrorActionPreference = 'Stop'
if (-not $CreateDisposableProfile) {
    throw 'This opt-in test creates and removes disposable Windows AppContainer profile metadata. Supply -CreateDisposableProfile only with authorization.'
}
& python (Join-Path $PSScriptRoot 'Test-OfficeProfileOwnership.py') --create-disposable-profile
if ($LASTEXITCODE) { throw 'Office profile ownership verification failed; inspect the retained profile name and evidence before retrying.' }
