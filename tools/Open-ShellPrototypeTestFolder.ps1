[CmdletBinding()]
param([switch] $Production)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-InstalledShellPrototype.ps1')

$testDirectory = Join-Path $repositoryRoot '.codex-temp\shell-ui-test'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null

foreach ($name in @('alpha.png', 'beta.jpg', 'gamma.webp')) {
    $path = Join-Path $testDirectory $name
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -ItemType File -Path $path | Out-Null
    }
}

Start-Process explorer.exe -ArgumentList ('"{0}"' -f $testDirectory)

Write-Output ''
Write-Output 'Select all three files and right-click the selection.'
Write-Output 'Confirm Analyze is topmost, has an icon, and has no submenu arrow.'
if ($Production) {
    Write-Output 'Invoke Analyze directly; the WPF window must list all three files under one batch number.'
}
else {
    Write-Output 'Invoke Analyze directly; its host dialog must report Selected files: 3.'
}
Write-Output 'Confirm Convert and Optimize have icons and isolated submenu arrows.'
if ($Production) {
    Write-Output 'Leave the window open and invoke Convert and Optimize; both must add three-file batches to that same window.'
    Write-Output 'Unsupported/not implemented is expected. Check Tab navigation and resize, then close the app.'
}
else {
    Write-Output 'Invoke each submenu action; both host dialogs must report Selected files: 3.'
}
