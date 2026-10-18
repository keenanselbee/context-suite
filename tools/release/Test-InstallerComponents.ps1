[CmdletBinding()]
param([switch] $Compile)
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $PSScriptRoot 'New-InstallerSelections.ps1')
$run = Join-Path $repository ('.codex-temp\installer-components\' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $run 'fixture'
$releaseDirectory = Join-Path $fixture 'payload\release'
$installation = Join-Path $fixture 'payload\installation'
$include = Join-Path $fixture 'ComponentFiles.iss'
$utf8 = [Text.UTF8Encoding]::new($false)
$script:componentChecks = 0
$success = $false
New-Item -ItemType Directory -Path $releaseDirectory, $installation -Force | Out-Null

function Assert-ComponentTest {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "Failed: $Message" }
    $script:componentChecks++
    Write-Output "PASS: $Message"
}

function Assert-ComponentRefusal {
    param([scriptblock] $Action, [string] $Message)
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-ComponentTest $rejected $Message
}

function Copy-ComponentObject {
    param($Value)
    $Value | ConvertTo-Json -Depth 16 | ConvertFrom-Json
}

try {
    $commonPaths = @('app/ContextSuite.Application.exe', 'app/ContextSuite.Worker.exe', 'app/ContextSuite.Core.dll',
        'app/Assets/Analyze.png', 'app/audio-engine/ffmpeg.exe', 'app/audio-engine/ffprobe.exe', 'app/pdf-engine/qpdf.exe')
    $convertPaths = @('app/office-engine/program/soffice.exe', 'app/pdf-renderer/pdfium.dll',
        'app/pdf-validator/mutool.exe', 'app/pdf-dark/ContextSuite.PdfDark.exe', 'app/avif/avifenc.exe',
        'app/heif/heif-convert.exe', 'app/svg/resvg.dll', 'app/wem-engine/vgmstream-cli.exe')
    $packagePaths = @('packages/Fixture.Analyze.msix', 'packages/Fixture.Convert.msix', 'packages/Fixture.Optimize.msix')
    $files = @()
    foreach ($path in @($commonPaths + $convertPaths + $packagePaths)) {
        $full = Join-Path $releaseDirectory $path
        New-Item -ItemType Directory -Path (Split-Path $full -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($full, "Compile-only inert fixture: $path", $utf8)
        $files += [pscustomobject]@{ path = $path; sha256 = (Get-FileHash -LiteralPath $full).Hash }
    }
    # Match the builder's dictionary representation as well as JSON-loaded objects below.
    $release = [ordered]@{
        schema = 1; id = '11111111111111111111111111111111'; version = '0.0.0.0'; publisher = 'CN=ComponentTest'; files = $files;
        packages = @($packagePaths | ForEach-Object {
            [pscustomobject]@{ name = [IO.Path]::GetFileNameWithoutExtension($_); file = $_;
                sha256 = (Get-FileHash -LiteralPath (Join-Path $releaseDirectory $_)).Hash }
        })
    }
    $selections = @(New-SuiteInstallerSelections $release $releaseDirectory $installation $include)
    $metadata = [pscustomobject]@{ publisher = $release.publisher; version = $release.version; selections = $selections }
    $includeText = [IO.File]::ReadAllText($include)
    Assert-ComponentTest ($selections.Count -eq 7 -and @($selections.key | Select-Object -Unique).Count -eq 7) 'Exactly seven unique nonempty selections are projected'
    Assert-ComponentTest (@(Get-SuiteTools @('Optimize', 'Analyze', 'Convert')) -join ',' -ceq 'Analyze,Convert,Optimize') 'Tool order is canonical regardless of caller order'
    Assert-ComponentTest ((@(Get-SuiteReleaseTools ([ordered]@{ schema = 2; tools = @('Optimize') })) -join ',') -ceq 'Optimize') 'Schema and selection are honored for builder dictionaries'

    for ($mask = 1; $mask -le 7; $mask++) {
        $tools = @(for ($index = 0; $index -lt 3; $index++) { if ($mask -band (1 -shl $index)) { @('Analyze', 'Convert', 'Optimize')[$index] } })
        $key = ($tools -join '-').ToLowerInvariant()
        $selection = @($selections | Where-Object key -CEQ $key)[0]
        $projected = Read-SuiteInstallerSelection $metadata $installation $tools
        $markerPath = Join-Path $installation "tools-$key.json"
        $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        $expected = @($commonPaths)
        if ($tools -ccontains 'Convert') { $expected += $convertPaths }
        $expected += @($tools | ForEach-Object { "packages/Fixture.$_.msix" })
        $expected += 'app/installation-tools.json'
        Assert-ComponentTest ($projected.schema -eq 2 -and $marker.schema -eq 1 -and
            ($projected.tools -join ',') -ceq ($tools -join ',') -and ($marker.tools -join ',') -ceq ($tools -join ',')) "$key descriptor and marker agree on exact tools"
        Assert-ComponentTest ((@($projected.files.path | Sort-Object) -join '|') -ceq (@($expected | Sort-Object) -join '|')) "$key contains exactly its shared and required files"
        Assert-ComponentTest ((@($projected.packages.name | Sort-Object) -join '|') -ceq (@($tools | ForEach-Object { "Fixture.$_" } | Sort-Object) -join '|')) "$key contains exactly its selected Explorer packages"

        $projectionRoot = Join-Path $fixture "selection-$key"
        $totalBytes = [long]0
        foreach ($entry in $projected.files) {
            $source = if ($entry.path -ceq 'app/installation-tools.json') { $markerPath } else { Join-Path $releaseDirectory $entry.path }
            $destination = Join-Path $projectionRoot $entry.path
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $destination
            if ((Get-FileHash -LiteralPath $destination).Hash -cne $entry.sha256 -or (Get-Item -LiteralPath $destination).Length -ne $entry.bytes) {
                throw "Projected bytes differ: $key / $($entry.path)"
            }
            $totalBytes += $entry.bytes
        }
        Assert-ComponentTest ($totalBytes -eq $selection.payloadBytes -and
            @(Get-ChildItem -LiteralPath $projectionRoot -File -Recurse).Count -eq $expected.Count) "$key projected files preserve hashes, exact inventory, and measured size"

        $includedSources = @()
        foreach ($line in ($includeText -split '\r?\n')) {
            if ($line -match '^Source: "payload\\release\\([^"]+)";.*?(?:; Components: ([a-z]+))?$') {
                $relative = $Matches[1].Replace('\', '/')
                $component = $Matches[2]
                if (-not $component -or $tools -icontains $component) { $includedSources += $relative }
            }
        }
        Assert-ComponentTest ((@($includedSources | Sort-Object) -join '|') -ceq (@($expected | Where-Object { $_ -cne 'app/installation-tools.json' } | Sort-Object) -join '|')) "$key generated Inno file conditions match the descriptor"
        $keyLines = @($includeText -split '\r?\n' | Where-Object { $_.Contains("SelectionIs('$key')") })
        Assert-ComponentTest ($keyLines.Count -eq 2 -and $keyLines[0].Contains("release-$key.json") -and $keyLines[1].Contains("tools-$key.json")) "$key selects one release descriptor and one marker in Inno"
    }

    foreach ($tools in @(@(), @('Analyze', 'Analyze'), @('analyze'), @('Analyze', 'Other'), @(''), @(1))) {
        Assert-ComponentRefusal { Get-SuiteTools $tools } 'Empty, repeated, unknown, noncanonical, or nonstring tools are refused'
    }
    foreach ($invalid in @(
        [pscustomobject]@{ schema = 2 }, [pscustomobject]@{ schema = 1; tools = @('Analyze') },
        [pscustomobject]@{ schema = 3; tools = @('Analyze') })) {
        Assert-ComponentRefusal { Get-SuiteReleaseTools $invalid } 'Invalid release schema or selection is refused'
    }
    Assert-ComponentTest ((@(Get-SuiteReleaseTools ([pscustomobject]@{})) -join ',') -ceq 'Analyze,Convert,Optimize') 'Legacy installer metadata without a schema retains all tools'
    foreach ($path in @('app/../outside.exe', 'app/Assets/../../outside.exe', 'app//file.exe', 'app\file.exe',
        'app/C:/file.exe', 'other/file.exe', 'app/new-engine/tool.exe')) {
        Assert-ComponentRefusal { Get-SuitePayloadComponent $path } "Unsafe or unclassified payload path is refused: $path"
    }
    foreach ($change in @('hash', 'path', 'reserved', 'duplicate', 'package')) {
        $invalid = Copy-ComponentObject $release
        switch ($change) {
            'hash' { $invalid.files[0].sha256 = '0' * 64 }
            'path' { $invalid.files[0].path = 'app/../outside.exe' }
            'reserved' { $invalid.files[0].path = 'app/installation-tools.json' }
            'duplicate' { $invalid.files += $invalid.files[0] }
            'package' { $invalid.packages[0].name = 'Fixture.Other' }
        }
        $badOutput = Join-Path $fixture "refused-$change"
        New-Item -ItemType Directory -Path $badOutput | Out-Null
        Assert-ComponentRefusal { New-SuiteInstallerSelections $invalid $releaseDirectory $badOutput (Join-Path $badOutput 'include.iss') } "Projection refuses $change defects"
    }
    $beforeInclude = (Get-FileHash -LiteralPath $include).Hash
    Assert-ComponentRefusal { New-SuiteInstallerSelections $release $releaseDirectory $installation $include } 'Projection is create-only'
    Assert-ComponentTest ((Get-FileHash -LiteralPath $include).Hash -ceq $beforeInclude) 'Rejected projection preserves existing output'
    $tamperedSource = Join-Path $releaseDirectory $commonPaths[0]
    $originalSource = [IO.File]::ReadAllBytes($tamperedSource)
    [IO.File]::AppendAllText($tamperedSource, 'changed after inventory', $utf8)
    $tamperOutput = Join-Path $fixture 'refused-source-tamper'
    New-Item -ItemType Directory -Path $tamperOutput | Out-Null
    Assert-ComponentRefusal { New-SuiteInstallerSelections $release $releaseDirectory $tamperOutput (Join-Path $tamperOutput 'include.iss') } 'Changed payload bytes are refused before projection'
    [IO.File]::WriteAllBytes($tamperedSource, $originalSource)
    $descriptorPath = Join-Path $installation $selections[0].file
    $originalDescriptor = [IO.File]::ReadAllBytes($descriptorPath)
    [IO.File]::AppendAllText($descriptorPath, 'tampered', $utf8)
    Assert-ComponentRefusal { Read-SuiteInstallerSelection $metadata $installation @('Analyze') } 'Tampered descriptor is rejected before parsing'
    [IO.File]::WriteAllBytes($descriptorPath, $originalDescriptor)
    foreach ($change in @('file', 'hash', 'tools', 'publisher', 'version', 'duplicate')) {
        $invalid = Copy-ComponentObject $metadata
        switch ($change) {
            'file' { $invalid.selections[0].file = '..\outside.json' }
            'hash' { $invalid.selections[0].sha256 = '0' * 64 }
            'tools' { $invalid.selections[0].tools = @('Convert') }
            'publisher' { $invalid.publisher = 'CN=Other' }
            'version' { $invalid.version = '0.0.1.0' }
            'duplicate' { $invalid.selections += $invalid.selections[0] }
        }
        Assert-ComponentRefusal { Read-SuiteInstallerSelection $invalid $installation @('Analyze') } "Selection reader refuses $change defects"
    }

    if ($Compile) {
        $pins = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-inputs.json') -Raw | ConvertFrom-Json
        $compiler = Join-Path $repository ("artifacts\installer-inputs\inno-$($pins.inno.version)\ISCC.exe")
        Assert-ComponentTest ((Get-FileHash -LiteralPath $compiler).Hash -ceq $pins.inno.compilerSha256 -and
            (Get-FileHash -LiteralPath (Join-Path (Split-Path $compiler -Parent) 'ISCmplr.dll')).Hash -ceq $pins.inno.compilerEngineSha256) 'Compile uses the pinned local Inno compiler and engine'
        Copy-Item -LiteralPath (Join-Path $repository 'packaging\inno\ContextSuite.iss') -Destination $fixture
        [IO.File]::WriteAllText((Join-Path $fixture 'BuildConfig.iss'),
            "#define SuiteVersion `"0.0.0.0`"`r`n#define OutputDirectory `"output`"`r`n#define DesktopRuntimeFile `"runtime-fixture.exe`"`r`n", $utf8)
        $runtimes = Join-Path $fixture 'payload\runtimes'
        New-Item -ItemType Directory -Path $runtimes | Out-Null
        foreach ($name in @('runtime-fixture.exe', 'vc_redist.x64.exe')) { [IO.File]::WriteAllText((Join-Path $runtimes $name), 'Never execute this inert compiler fixture.', $utf8) }
        [IO.File]::WriteAllText((Join-Path $installation 'Invoke-InstallerAction.ps1'), 'throw "Compile-only fixture must never be installed."', $utf8)
        Add-Type -AssemblyName System.Drawing
        $iconStream = [IO.File]::Create((Join-Path $installation 'ContextSuite.ico'))
        try { [Drawing.SystemIcons]::Application.Save($iconStream) } finally { $iconStream.Dispose() }
        & $compiler /Qp (Join-Path $fixture 'ContextSuite.iss') *> (Join-Path $run 'compile.log')
        Assert-ComponentTest ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath (Join-Path $fixture 'output\ContextSuite-0.0.0.0-win-x64-internal.exe'))) 'Real Inno compiler accepts current setup source and all seven generated selection mappings'
    }
    $success = $true
} finally {
    $receipt = [ordered]@{ passed = $success; checks = $script:componentChecks; compileRequested = [bool]$Compile;
        scope = 'Synthetic component projections and optional compile-only installer; no installation or registration.';
        reproduction = "powershell -NoProfile -File tools/release/Test-InstallerComponents.ps1" + $(if ($Compile) { ' -Compile' } else { '' });
        sources = @('tools/release/New-InstallerSelections.ps1', 'packaging/inno/InstallComponents.ps1', 'packaging/inno/ContextSuite.iss') | ForEach-Object {
            [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $repository $_)).Hash }
        }; fixtureRetained = $true }
    if ($success) {
        $expected = [IO.Path]::GetFullPath((Join-Path $run 'fixture'))
        $actual = [IO.Path]::GetFullPath($fixture)
        if ($actual -cne $expected -or -not $actual.StartsWith([IO.Path]::GetFullPath($repository) + '\.codex-temp\installer-components\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing fixture cleanup outside the exact test-owned directory.'
        }
        $ancestor = $fixture
        while ($ancestor -and $ancestor -ne $repository) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing linked fixture ancestor.' }
            $ancestor = Split-Path $ancestor -Parent
        }
        $pending = [Collections.Generic.Stack[string]]::new()
        $pending.Push($fixture)
        while ($pending.Count) {
            foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing linked fixture cleanup.' }
                if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            }
        }
        Remove-Item -LiteralPath $fixture -Recurse -Force
        $receipt.fixtureRetained = $false
    }
    [IO.File]::WriteAllText((Join-Path $run 'result.json'), ($receipt | ConvertTo-Json -Depth 8), $utf8)
    Write-Output "Component receipt: $run\result.json"
}
Write-Output "Passed $script:componentChecks installer component checks. No installer was executed."
