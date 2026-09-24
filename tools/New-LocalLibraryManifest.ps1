param(
    [Parameter(Mandatory)][ValidateSet('Heif', 'Audio')][string]$Engine,
    [Parameter(Mandatory)][string]$EngineDirectory
)

$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $EngineDirectory).Path
if (-not [System.IO.Directory]::Exists($directory)) { throw 'Choose an engine directory.' }
$anchor = if ($Engine -eq 'Heif') { 'ContextSuite.Heif.Native.dll' } else { 'ffprobe.exe' }
$required = if ($Engine -eq 'Heif') { @('heif.dll', 'libde265.dll') } else {
    @('avcodec-63.dll', 'avfilter-12.dll', 'avformat-63.dll', 'avutil-61.dll', 'swresample-7.dll')
}
$destination = Join-Path $directory 'ContextSuite.LocalLibraries.json'
if (Test-Path -LiteralPath $destination) { throw 'A local library manifest already exists. Preserve or remove it explicitly before creating another.' }
$libraries = @(Get-ChildItem -LiteralPath $directory -File -Filter '*.dll' | Where-Object Name -ne $anchor | Sort-Object Name)
if ($libraries.Count -gt 32) { throw 'At most 32 replacement or supporting libraries are supported.' }
foreach ($name in $required) {
    if ($name -notin $libraries.Name) { throw "Required library is missing: $name" }
}
foreach ($library in $libraries) {
    if ($library.Name.Length -gt 128 -or $library.Name -notmatch '^[A-Za-z0-9_-][A-Za-z0-9_.-]*\.dll$' -or $library.Name.Contains('..')) {
        throw "Unsupported library filename: $($library.Name)"
    }
}
$leases = [System.Collections.Generic.List[System.IO.FileStream]]::new()
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $identities = [ordered]@{}
    foreach ($name in @($anchor) + @($libraries.Name)) {
        $path = Join-Path $directory $name
        $lease = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        $leases.Add($lease)
        $identities[$name] = [System.BitConverter]::ToString($sha.ComputeHash($lease)).Replace('-', '')
    }
    $manifest = [ordered]@{ formatVersion = 1; anchorName = $anchor; anchorSha256 = $identities[$anchor]; libraries = [ordered]@{} }
    foreach ($library in $libraries) { $manifest.libraries[$library.Name] = $identities[$library.Name] }
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes(($manifest | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
    $output = [System.IO.File]::Open($destination, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try { $output.Write($bytes, 0, $bytes.Length); $output.Flush($true) }
    finally { $output.Dispose() }
    Write-Output "Created $destination"
    Write-Output 'This opts into the selected local DLLs. Context Suite still verifies its original bridge/executables and validates media outputs.'
}
finally {
    $sha.Dispose()
    foreach ($lease in $leases) { $lease.Dispose() }
}
