# Per-user classic IExplorerCommand registration. Fixed leaf keys only: never
# modify the Windows menu override or recursively delete a shared registry tree.
. (Join-Path $PSScriptRoot 'InstallComponents.ps1')
function Get-SuiteMenuMode {
    param($Release)
    $property = $Release.PSObject.Properties['menuMode']
    if ($null -eq $property) { return 'modern' }
    if ($property.Value -cnotin 'modern', 'classic') { throw 'Invalid context menu mode.' }
    return $property.Value
}

function Get-SuiteClassicDefinitions {
    param($Release)
    $commands = [ordered]@{
        Analyze = '{7F71BFC7-0125-47B6-A163-54EADDB56E8B}'
        Convert = '{C0A78640-7313-4AD7-870C-2F0B838A28C2}'
        Optimize = '{8C4CA2A6-99A2-436A-9021-3D004687FC4C}'
    }
    foreach ($name in @(Get-SuiteReleaseTools $Release)) {
        $owner = "$($Release.publisher)|$($Release.id)|$($Release.applicationDirectory)"
        [pscustomobject]@{ Path = "Software\Classes\CLSID\$($commands[$name])\InprocServer32";
            Values = @{ '' = "$($Release.applicationDirectory)\ContextSuite.Shell.dll"; ThreadingModel = 'Apartment'; ContextSuiteOwner = $owner } }
        [pscustomobject]@{ Path = "Software\Classes\*\shell\ContextSuite.$name";
            Values = @{ MUIVerb = $name; ExplorerCommandHandler = $commands[$name]; MultiSelectModel = 'Player'; ContextSuiteOwner = $owner } }
    }
}

# Small platform boundary so all lifecycle tests run without touching HKCU.
function Read-SuiteClassicKey {
    param([string] $Path)
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Path)
    if ($null -eq $key) { return $null }
    try {
        $values = @{}
        foreach ($name in $key.GetValueNames()) { $values[$name] = $key.GetValue($name) }
        return [pscustomobject]@{ Values = $values; Children = @($key.GetSubKeyNames()) }
    } finally { $key.Dispose() }
}
function Write-SuiteClassicKey {
    param([string] $Path, [hashtable] $Values)
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($Path)
    try {
        # Ownership first allows safe cleanup of interrupted value writes.
        $key.SetValue('ContextSuiteOwner', $Values.ContextSuiteOwner)
        foreach ($name in $Values.Keys) { $key.SetValue($name, $Values[$name], [Microsoft.Win32.RegistryValueKind]::String) }
    } finally { $key.Dispose() }
}
function Remove-SuiteClassicKey {
    param([string] $Path)
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($Path, $false)
}

function Assert-SuiteClassicAvailable {
    foreach ($definition in Get-SuiteClassicDefinitions ([pscustomobject]@{})) {
        if ($null -ne (Read-SuiteClassicKey $definition.Path)) { throw 'Existing classic Context Suite registration requires explicit removal.' }
    }
}
function Assert-SuiteClassicOwnership {
    param($Previous, $Next, [switch] $PreviousOnly)
    $before = @{}; $after = @{}
    foreach ($definition in Get-SuiteClassicDefinitions $Previous) { $before[$definition.Path] = $definition }
    foreach ($definition in Get-SuiteClassicDefinitions $Next) { $after[$definition.Path] = $definition }
    foreach ($path in @(@($before.Keys) + @($after.Keys) | Sort-Object -Unique)) {
        $key = Read-SuiteClassicKey $path
        if ($null -eq $key) { continue }
        $allowed = @($before[$path], $(if (-not $PreviousOnly) { $after[$path] }) | Where-Object { $null -ne $_ })
        # Validate against one complete owner, never mixed values from two releases.
        $matches = @($allowed | Where-Object {
            $definition = $_
            $key.Children.Count -eq 0 -and $key.Values.ContextSuiteOwner -ceq $definition.Values.ContextSuiteOwner -and
            @($key.Values.Keys | Where-Object {
                -not $definition.Values.ContainsKey($_) -or $key.Values[$_] -cne $definition.Values[$_]
            }).Count -eq 0
        })
        if ($matches.Count -eq 0) { throw 'Foreign or modified classic registration retained; manual review required.' }
    }
}
function Assert-SuiteClassicRegistered {
    param($Release)
    Assert-SuiteClassicOwnership $Release $Release
    foreach ($definition in Get-SuiteClassicDefinitions $Release) {
        $key = Read-SuiteClassicKey $definition.Path
        if ($null -eq $key -or $key.Values.Count -ne $definition.Values.Count) { throw 'Classic registration is incomplete.' }
    }
}
function Uninstall-SuiteClassic {
    param($Release)
    Assert-SuiteClassicOwnership $Release $Release
    foreach ($definition in Get-SuiteClassicDefinitions $Release) { Remove-SuiteClassicKey $definition.Path }
    foreach ($definition in Get-SuiteClassicDefinitions $Release) {
        if ($null -ne (Read-SuiteClassicKey $definition.Path)) { throw 'Classic registration remains after uninstall.' }
    }
}
function Set-SuiteClassicRegistered {
    param($Previous, $Next, $Desired)
    Assert-SuiteClassicOwnership $Previous $Next
    foreach ($path in @(@((Get-SuiteClassicDefinitions $Previous).Path) + @((Get-SuiteClassicDefinitions $Next).Path) | Sort-Object -Unique)) {
        Remove-SuiteClassicKey $path
    }
    foreach ($definition in Get-SuiteClassicDefinitions $Desired) {
        Write-SuiteClassicKey $definition.Path $definition.Values
    }
    Assert-SuiteClassicRegistered $Desired
}
