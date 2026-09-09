# Per-user classic IExplorerCommand registration. Fixed leaf keys only: never
# modify the Windows menu override or recursively delete a shared registry tree.
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
    foreach ($name in $commands.Keys) {
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
    param($Previous, $Next)
    $before = @(Get-SuiteClassicDefinitions $Previous)
    $after = @(Get-SuiteClassicDefinitions $Next)
    for ($i = 0; $i -lt $before.Count; $i++) {
        $key = Read-SuiteClassicKey $before[$i].Path
        if ($null -eq $key) { continue }
        if ($key.Children.Count -ne 0 -or $key.Values.ContextSuiteOwner -cnotin @($before[$i].Values.ContextSuiteOwner, $after[$i].Values.ContextSuiteOwner) -or
            @($key.Values.Keys | Where-Object { -not $before[$i].Values.ContainsKey($_) }).Count -ne 0) {
            throw 'Foreign or modified classic registration retained; manual review required.'
        }
        # Known values may be absent after interruption, but must never be foreign.
        foreach ($name in $key.Values.Keys) {
            if ($key.Values[$name] -cnotin @($before[$i].Values[$name], $after[$i].Values[$name])) {
                throw 'Modified classic registration retained; manual review required.'
            }
        }
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
    Assert-SuiteClassicAvailable
}
function Set-SuiteClassicRegistered {
    param($Previous, $Next, $Desired)
    Assert-SuiteClassicOwnership $Previous $Next
    foreach ($definition in Get-SuiteClassicDefinitions $Desired) {
        Remove-SuiteClassicKey $definition.Path
        Write-SuiteClassicKey $definition.Path $definition.Values
    }
    Assert-SuiteClassicRegistered $Desired
}
