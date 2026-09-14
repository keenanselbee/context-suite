[CmdletBinding()]
param([string] $Payload)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
[xml] $properties = Get-Content -LiteralPath (Join-Path $repository 'Version.props') -Raw
$versions = $properties.SelectNodes('/Project/PropertyGroup/ContextSuiteVersion')
if ($versions.Count -ne 1 -or $versions[0].InnerText -notmatch '^(0|[1-9][0-9]*)\.[0-9]\.[0-9]$') {
    throw 'Version.props requires one MAJOR.MINOR.PATCH value with single-digit minor and patch.'
}
$version = $versions[0].InnerText
$windowsVersion = $version + '.0'
foreach ($tool in @('Analyze', 'Convert', 'Optimize')) {
    [xml] $manifest = Get-Content -LiteralPath (Join-Path $repository "packaging\ContextSuite.$tool.ShellPrototype\AppxManifest.xml") -Raw
    if ($manifest.Package.Identity.Version -ne $windowsVersion) { throw "$tool package version differs from Version.props." }
}
if ($Payload) {
    $Payload = (Resolve-Path -LiteralPath $Payload).Path
    foreach ($assembly in @('ContextSuite.Application', 'ContextSuite.Worker', 'ContextSuite.Core', 'ContextSuite.Private', 'ContextSuite.Commercial')) {
        $path = Join-Path $Payload ($assembly + '.dll')
        if ([Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString() -ne $windowsVersion -or
            (Get-Item -LiteralPath $path).VersionInfo.FileVersionRaw.ToString() -ne $windowsVersion) {
            throw "Staged $assembly version differs from Version.props."
        }
    }
}
Write-Output $version
