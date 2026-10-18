. (Join-Path $PSScriptRoot '..\..\packaging\inno\InstallComponents.ps1')

function New-SuiteInstallerSelections {
    param($Release, [string] $ReleaseDirectory, [string] $InstallationDirectory, [string] $IncludePath)
    if ($Release.schema -ne 1 -or (@(Get-SuiteReleaseTools $Release) -join ',') -cne 'Analyze,Convert,Optimize') {
        throw 'Component projection requires the verified complete release catalog.'
    }
    $utf8 = [Text.UTF8Encoding]::new($false)
    $files = [Collections.Generic.List[object]]::new()
    $lines = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $Release.files) {
        $path = [string]$entry.path
        if (-not $seen.Add($path) -or $path -match '[";{}\r\n]' -or $path -ceq 'app/installation-tools.json') {
            throw "Unsafe, duplicate, or reserved component path: $path"
        }
        if ($path.StartsWith('app/', [StringComparison]::Ordinal)) {
            $component = Get-SuitePayloadComponent $path
        } else {
            $package = @($Release.packages | Where-Object { $_.file -ceq $path })
            if ($package.Count -ne 1 -or $package[0].name -cnotmatch '\.(Analyze|Convert|Optimize)$' -or
                $path -cnotmatch '^packages/[A-Za-z0-9.-]+\.msix$') { throw "Unexpected release file: $path" }
            $component = ($package[0].name.Split('.')[-1]).ToLowerInvariant()
        }
        $source = Join-Path $ReleaseDirectory $path
        if ((Get-FileHash -LiteralPath $source).Hash -cne $entry.sha256) { throw "Component source hash mismatch: $path" }
        $files.Add([pscustomobject]@{ path = $path; sha256 = $entry.sha256; bytes = (Get-Item -LiteralPath $source).Length; component = $component })
        $relative = $path.Replace('/', '\')
        $parent = $relative.Substring(0, $relative.LastIndexOf('\'))
        $line = 'Source: "payload\release\' + $relative + '"; DestDir: "{app}\releases\{code:GetReleaseId}\' + $parent + '"; Flags: ignoreversion'
        if ($component -ne 'common') { $line += '; Components: ' + $component }
        $lines.Add($line)
    }
    $selections = @()
    for ($mask = 1; $mask -le 7; $mask++) {
        $tools = @(for ($index = 0; $index -lt 3; $index++) { if ($mask -band (1 -shl $index)) { @('Analyze', 'Convert', 'Optimize')[$index] } })
        $key = ($tools -join '-').ToLowerInvariant()
        $markerName = "tools-$key.json"
        $markerPath = Join-Path $InstallationDirectory $markerName
        $descriptorName = "release-$key.json"
        $descriptorPath = Join-Path $InstallationDirectory $descriptorName
        foreach ($output in @($markerPath, $descriptorPath, $IncludePath)) {
            if (Test-Path -LiteralPath $output) { throw "Component output already exists: $output" }
        }
        [IO.File]::WriteAllText($markerPath, ([ordered]@{ schema = 1; tools = $tools } | ConvertTo-Json -Compress), $utf8)
        $selectedFiles = @($files | Where-Object { $_.component -eq 'common' -or $tools -icontains $_.component } | ForEach-Object {
            [pscustomobject]@{ path = $_.path; sha256 = $_.sha256; bytes = $_.bytes }
        })
        $selectedFiles += [pscustomobject]@{ path = 'app/installation-tools.json'; sha256 = (Get-FileHash -LiteralPath $markerPath).Hash; bytes = (Get-Item -LiteralPath $markerPath).Length }
        $selectedRelease = [ordered]@{
            schema = 2; id = $Release.id; publisher = $Release.publisher; version = $Release.version; tools = $tools;
            packages = @($Release.packages | Where-Object { $tools -ccontains $_.name.Split('.')[-1] }); files = $selectedFiles
        }
        if (@($selectedRelease.packages).Count -ne $tools.Count) { throw 'Selected packages do not match tools.' }
        [IO.File]::WriteAllText($descriptorPath, ($selectedRelease | ConvertTo-Json -Depth 12), $utf8)
        $selections += [ordered]@{ key = $key; tools = $tools; file = $descriptorName;
            sha256 = (Get-FileHash -LiteralPath $descriptorPath).Hash;
            payloadBytes = [long](($selectedFiles | Measure-Object -Property bytes -Sum).Sum) }
        $check = '; Check: SelectionIs(''' + $key + ''')'
        $lines.Add('Source: "payload\installation\' + $descriptorName + '"; DestDir: "{app}\releases\{code:GetReleaseId}"; DestName: "release.json"; Flags: ignoreversion' + $check)
        $lines.Add('Source: "payload\installation\' + $markerName + '"; DestDir: "{app}\releases\{code:GetReleaseId}\app"; DestName: "installation-tools.json"; Flags: ignoreversion' + $check)
    }
    [IO.File]::WriteAllText($IncludePath, ($lines -join "`r`n"), $utf8)
    return $selections
}
