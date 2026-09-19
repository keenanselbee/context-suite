[CmdletBinding()]
param([Parameter(Mandatory)][guid] $StagingId, [switch] $PlanOnly)

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence = Join-Path $repository ('.codex-temp\office-isolation\' + $StagingId.ToString('N'))
$probe = Join-Path $evidence 'case\allowed\probe.exe'
$buildPath = Join-Path $evidence 'build.json'
$profilePath = Join-Path $evidence 'case\profile-name.txt'
$observationPath = Join-Path $evidence 'case\isolated.json'
foreach ($path in @($probe, $buildPath, $profilePath, $observationPath)) {
    $item = Get-Item -LiteralPath $path
    if ($item.PSIsContainer) { throw 'Expected an existing isolation evidence file.' }
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse paths are not permitted for this diagnostic.' }
        $item = if ($item -is [IO.DirectoryInfo]) { $item.Parent } else { $item.Directory }
    }
}
$observation = Get-Content -LiteralPath $observationPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($observation.appContainer -ne 1 -or $observation.capabilityCount -ne 0 -or
    ($observation.networkProcessId -isnot [long] -and $observation.networkProcessId -isnot [int]) -or
    $observation.networkProcessId -le 0 -or $observation.networkProcessId -gt [uint32]::MaxValue) {
    throw 'Use a current isolated network observation with an explicit process identity.'
}
foreach ($name in 'networkRemotePort', 'networkRemotePort6', 'networkLocalPort', 'networkLocalPort6') {
    $value = $observation.$name
    if (($value -isnot [int] -and $value -isnot [long]) -or $value -lt 1 -or $value -gt 65535) {
        throw 'Use an observation with complete IPv4/IPv6 connection ports.'
    }
}
foreach ($name in 'networkStartedFileTime', 'networkEndedFileTime') {
    if ($observation.$name -isnot [long]) { throw 'Use an observation with exact UTC file-time bounds.' }
}
$started = [DateTime]::FromFileTimeUtc($observation.networkStartedFileTime)
$ended = [DateTime]::FromFileTimeUtc($observation.networkEndedFileTime)
if ($ended -lt $started -or ($ended - $started).TotalSeconds -gt 30) {
    throw 'Invalid bounded network observation interval.'
}
$build = Get-Content -LiteralPath $buildPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash -ne $build.sha256) {
    throw 'The case probe no longer matches its retained build identity.'
}
$profile = (Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8).Trim()
if ($profile -cne ('ContextSuite.Office.Evaluation.' + $StagingId.ToString('N'))) {
    throw 'The profile name does not match this generated case.'
}
$output = Join-Path $evidence ('network-diagnostics-' + [guid]::NewGuid().ToString('N'))
$netsh = Join-Path ([Environment]::GetFolderPath('System')) 'netsh.exe'
$queries = foreach ($family in @(
    @{ name='ipv4'; address='127.0.0.1'; remotePort=$observation.networkRemotePort; localPort=$observation.networkLocalPort },
    @{ name='ipv6'; address='::1'; remotePort=$observation.networkRemotePort6; localPort=$observation.networkLocalPort6 })) {
    foreach ($kind in @('netevents', 'filters')) {
        $arguments = @('wfp', 'show', $kind, ('file=' + (Join-Path $output ($family.name + '-' + $kind + '.xml'))),
            'protocol=6', ('remoteaddr=' + $family.address), ('remoteport=' + $family.remotePort),
            ('localport=' + $family.localPort), ('appid=' + $probe))
        if ($kind -eq 'netevents') { $arguments += 'timewindow=600' }
        else { $arguments += 'verbose=ON' }
        [ordered]@{ family=$family.name; kind=$kind; arguments=$arguments }
    }
}
$plan = [ordered]@{ evidence=$evidence; probe=$probe; probeSha256=$build.sha256; profile=$profile;
    processId=$observation.networkProcessId; observationSha256=(Get-FileHash -LiteralPath $observationPath).Hash;
    startedUtc=$started.ToString('o'); endedUtc=$ended.ToString('o');
    executable=$netsh; output=$output; queries=@($queries) }
if ($PlanOnly) { $plan | ConvertTo-Json -Depth 6; return }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This read-only Windows Filtering Platform query requires an elevated PowerShell window. No collection or firewall settings were changed.'
}
$age = ([DateTime]::UtcNow - $started).TotalSeconds
if ($age -lt 0 -or $age -gt 540) {
    throw 'Run a fresh non-elevated isolation probe first, then query within nine minutes; old events are outside this bounded collection window.'
}
if (Test-Path -LiteralPath $output) { throw 'Use a new diagnostic output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$plan | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'plan.json') -Encoding UTF8
$results = @()
foreach ($query in $queries) {
    $arguments = $query.arguments
    $message = & $netsh @arguments 2>&1
    $code = $LASTEXITCODE
    $message | Set-Content -LiteralPath (Join-Path $output ($query.family + '-' + $query.kind + '.log')) -Encoding UTF8
    $results += [ordered]@{ family=$query.family; kind=$query.kind; exitCode=$code }
    $results | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding UTF8
}
Write-Output "Read-only network evidence: $output"
if (@($results | Where-Object { $_.exitCode -ne 0 }).Count) { throw 'One or more read-only queries failed; retain their logs.' }
Write-Output 'Queries completed. Inspect matching drop events and filters; empty results are not isolation evidence.'
