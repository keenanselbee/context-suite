[CmdletBinding()]
param([ValidateSet('All', 'Desktop', 'VisualCpp')][string] $Component = 'All')
$ErrorActionPreference = 'Stop'
$requirements = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'requirements.json') -Raw | ConvertFrom-Json
if (-not [Environment]::Is64BitProcess -or [Environment]::OSVersion.Platform -ne 'Win32NT' -or
    [Environment]::OSVersion.Version.Build -lt $requirements.minimumWindowsBuild -or
    $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
    throw 'Context Suite requires Windows 11 x64 and 64-bit PowerShell; see requirements.json for the tested minimum build.'
}
if ($Component -ne 'VisualCpp') {
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    $runtimes = if (Test-Path -LiteralPath $dotnet) { @(& $dotnet --list-runtimes) } else { @() }
    if (-not ($runtimes | Where-Object { $_ -match '^Microsoft.WindowsDesktop.App 10\.0\.\d+ ' })) {
        throw 'Install the Microsoft .NET 10 Windows Desktop Runtime (x64), not just the base .NET runtime. See requirements.json.'
    }
}
if ($Component -ne 'Desktop') {
    foreach ($name in 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll') {
        $file = Join-Path ([Environment]::SystemDirectory) $name
        if (-not (Test-Path -LiteralPath $file) -or
            [version](Get-Item -LiteralPath $file).VersionInfo.FileVersionRaw -lt [version]$requirements.minimumVisualCppRuntime) {
            throw "Install/update the Microsoft Visual C++ x64 Redistributable ($($requirements.minimumVisualCppRuntime) or newer). See requirements.json."
        }
    }
}
Write-Output 'Runtime prerequisites found. This is not an install, native-load, conversion or Explorer smoke test.'
