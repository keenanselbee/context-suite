[CmdletBinding()]
param([Parameter(Mandatory)][string] $SourceDirectory)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build-AudioDependency.ps1') -SourceDirectory $SourceDirectory -Dependency Opus
