# Canonical tool selection shared by packaging and installation. Legacy releases
# have all three tools; only schema 2 release inventories support subsets.
function Get-SuiteTools {
    param([object[]] $Tools)
    $known = @('Analyze', 'Convert', 'Optimize')
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($null -eq $Tools -or $Tools.Count -eq 0) { throw 'Select at least one Context Suite tool.' }
    foreach ($tool in $Tools) {
        if ($tool -isnot [string] -or $known -cnotcontains $tool -or -not $seen.Add($tool)) {
            throw 'Tool selection must contain unique Analyze, Convert, or Optimize names.'
        }
    }
    $known | Where-Object { $seen.Contains($_) }
}

function Get-SuiteReleaseTools {
    param($Release)
    if ($null -eq $Release) { throw 'Release metadata is required.' }
    $hasSchema = if ($Release -is [Collections.IDictionary]) { $Release.Contains('schema') } else { $null -ne $Release.PSObject.Properties['schema'] }
    if ($hasSchema -and $Release.schema -notin 1, 2) {
        throw 'Unsupported release selection schema.'
    }
    $hasTools = if ($Release -is [Collections.IDictionary]) { $Release.Contains('tools') } else { $null -ne $Release.PSObject.Properties['tools'] }
    if ($Release.schema -eq 2 -and -not $hasTools) { throw 'Selected releases require an explicit tool selection.' }
    $tools = if ($hasTools) { @(Get-SuiteTools $Release.tools) } else { @('Analyze', 'Convert', 'Optimize') }
    if ($Release.schema -eq 1 -and ($tools -join ',') -cne 'Analyze,Convert,Optimize') {
        throw 'Legacy releases must contain all three tools.'
    }
    $tools
}

function Get-SuitePayloadComponent {
    param([Parameter(Mandatory)][string] $Path)
    if ($Path -cnotmatch '^app/[A-Za-z0-9_. /-]+$' -or $Path -match '(^|/)\.\.?(/|$)|[. ](/|$)' -or $Path.Contains('//')) {
        throw "Unsafe application payload path: $Path"
    }
    $parts = $Path.Split('/')
    if ($parts.Count -eq 2) { return 'common' }
    switch -CaseSensitive ($parts[1]) {
        { $_ -cin @('Assets', 'audio-engine', 'pdf-engine') } { return 'common' }
        { $_ -cin @('office-engine', 'pdf-renderer', 'pdf-validator', 'pdf-dark', 'avif', 'heif', 'svg', 'wem-engine') } { return 'convert' }
        default { throw "Unclassified application payload directory: $($parts[1])" }
    }
}

function Read-SuiteInstallerSelection {
    param($Metadata, [Parameter(Mandatory)][string] $Directory, [object[]] $Tools)
    $selected = @(Get-SuiteTools $Tools)
    $key = ($selected -join '-').ToLowerInvariant()
    $entries = @($Metadata.selections | Where-Object { $_.key -ceq $key })
    if ($entries.Count -ne 1 -or $entries[0].file -cne "release-$key.json" -or
        (@(Get-SuiteTools $entries[0].tools) -join ',') -cne ($selected -join ',')) {
        throw 'Installer selection is missing or inconsistent.'
    }
    $path = Join-Path $Directory $entries[0].file
    if ((Get-FileHash -LiteralPath $path).Hash -cne $entries[0].sha256) { throw 'Selected release descriptor hash mismatch.' }
    $release = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($release.schema -ne 2 -or (@(Get-SuiteReleaseTools $release) -join ',') -cne ($selected -join ',') -or
        $release.publisher -cne $Metadata.publisher -or $release.version -cne $Metadata.version) {
        throw 'Selected release descriptor differs from installer metadata.'
    }
    $release
}
