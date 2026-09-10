[CmdletBinding()]
param([Parameter(Mandatory)][string] $Candidate)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$root = (Resolve-Path -LiteralPath $Candidate).Path.TrimEnd('\')
$manifest = Get-Content -LiteralPath (Join-Path $root 'release-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.status -ne 'unsigned-internal-candidate' -or
    $manifest.publicCommit -notmatch '^[a-f0-9]{40}$' -or $manifest.privateCommit -notmatch '^[a-f0-9]{40}$') {
    throw 'Invalid candidate identity.'
}
$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.files) {
    if ($entry.path -notmatch '^(app/|requirements\.json$|Test-Prerequisites\.ps1$)' -or
        $entry.path -match '(^|/)\.\.?(/|$)|[\\:]' -or -not $expected.Add($entry.path)) { throw 'Unsafe or duplicate inventory path.' }
    $file = Join-Path $root $entry.path
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or
        (Get-Item -LiteralPath $file).Length -ne $entry.bytes -or
        (Get-FileHash -LiteralPath $file).Hash -ne $entry.sha256) { throw "Candidate file mismatch: $($entry.path)" }
}
foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -Force) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Candidate must not contain reparse points.' }
    if ($file.PSIsContainer) { continue }
    $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
    if ($relative -ne 'release-manifest.json' -and -not $expected.Contains($relative)) { throw "Uninventoried file: $relative" }
    if ($file.Extension -in '.pdb', '.cs', '.cpp', '.pfx', '.key' -or $file.Name -match 'TestHost|SmokeTests|ContractTests') {
        throw "Development or secret material in candidate: $relative"
    }
}
foreach ($required in 'requirements.json', 'Test-Prerequisites.ps1') {
    if (-not $expected.Contains($required)) { throw "Missing candidate file: $required" }
}
foreach ($name in 'Application', 'Worker') {
    foreach ($extension in 'exe', 'dll', 'deps.json', 'runtimeconfig.json') {
        if (-not $expected.Contains("app/ContextSuite.$name.$extension")) { throw 'Incomplete application/worker payload.' }
    }
}
foreach ($required in 'ContextSuite.Core.dll', 'ContextSuite.Private.dll', 'ContextSuite.Commercial.dll', 'ContextSuite.Shell.dll', 'Magick.NET-Q16-x64.dll', 'Magick.NET.Core.dll') {
    if (-not $expected.Contains("app/$required")) { throw "Missing runtime component: $required" }
}
foreach ($entry in @(@{ name='Application'; recorded=$manifest.applicationDependencies }, @{ name='Worker'; recorded=$manifest.dependencies })) {
    $actual = (Get-Content -LiteralPath (Join-Path $root "app/ContextSuite.$($entry.name).deps.json") -Raw | ConvertFrom-Json).libraries
    $actualLines = @($actual.PSObject.Properties | Sort-Object Name | ForEach-Object { $_.Name + '=' + ($_.Value | ConvertTo-Json -Depth 12 -Compress) })
    $recordedLines = @($entry.recorded.PSObject.Properties | Sort-Object Name | ForEach-Object { $_.Name + '=' + ($_.Value | ConvertTo-Json -Depth 12 -Compress) })
    if (($actualLines -join "`n") -cne ($recordedLines -join "`n")) { throw "Candidate $($entry.name) dependency evidence differs from its payload." }
}
foreach ($tool in 'Analyze', 'Convert', 'Optimize') {
    foreach ($suffix in '.ico', 'Square44x44Logo.png', 'Square150x150Logo.png') {
        if (-not $expected.Contains("app/Assets/$tool$suffix")) { throw "Missing Explorer asset: $tool$suffix" }
    }
}
if (-not $expected.Contains('app/Assets/StoreLogo.png')) { throw 'Missing package logo.' }
& (Join-Path $repository 'tools\curated-engine\Test-ProductionPayload.ps1') -Payload (Join-Path $root 'app')
Write-Output 'Release candidate inventory and source-linked engine guards passed. Hashes are not signatures; installation remains unverified.'
