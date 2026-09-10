[CmdletBinding()]
param([switch] $Launch, [ValidateSet('A', 'B')][string] $Profile = 'A')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repository 'proprietary\tests\ContextSuite.Licensing.SandboxHost\ContextSuite.Licensing.SandboxHost.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw 'Compatible private sandbox test sources are required.' }
& dotnet build $project -c Debug --nologo
if ($LASTEXITCODE -ne 0) { throw 'Sandbox licensing host build failed.' }
if (-not $Launch) {
    Write-Output 'Sandbox host compiled only. No window, provider request or activation was made. -Launch is explicit live-sandbox opt-in.'
    return
}
$directory = Join-Path $repository ".codex-temp\licensing-sandbox\$Profile"
$executable = Join-Path $repository 'artifacts\managed\bin\ContextSuite.Licensing.SandboxHost\Debug\net10.0-windows\ContextSuite.Licensing.SandboxHost.exe'
Write-Output "Opening sandbox profile $Profile. Enter a SANDBOX key in the masked window only; never supply keys as command arguments."
Write-Output 'Actions in this window contact Polar sandbox and can allocate/deactivate a sandbox slot. Production licensing and media are not used.'
# Visible UI is explicitly requested by -Launch. The current user's production
# trial/license files and installed app remain separate from these test profiles.
Start-Process -FilePath $executable -ArgumentList @('--state-directory', ('"' + $directory + '"')) -WindowStyle Normal -Wait
