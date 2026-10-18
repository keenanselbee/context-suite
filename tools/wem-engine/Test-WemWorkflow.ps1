[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $DecoderDistribution,
    [Parameter(Mandatory)][string] $Fixtures,
    [string] $WwiseConsole,
    [string] $AudioDistributionDirectory,
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$DecoderDistribution = (Resolve-Path -LiteralPath $DecoderDistribution).Path
$Fixtures = (Resolve-Path -LiteralPath $Fixtures).Path
if ($AudioDistributionDirectory -and -not $WwiseConsole) {
    throw 'MP3-to-WEM workflow checks require an installed WwiseConsole.'
}
if ($AudioDistributionDirectory) { $AudioDistributionDirectory = (Resolve-Path -LiteralPath $AudioDistributionDirectory).Path }
foreach ($codec in 'pcm', 'vorbis') {
    $folder = Join-Path $Fixtures "output-$codec\Windows"
    if (-not (Test-Path -LiteralPath $folder -PathType Container) -or
        @(Get-ChildItem -LiteralPath $folder -Filter '*.wem' -File).Count -ne 4) {
        throw 'Use the eight authored PCM/Vorbis fixtures from Test-WwiseConversion.py.'
    }
}
$scratch = Join-Path $repository ('.codex-temp\wem-workflow-' + [guid]::NewGuid().ToString('N'))
$build = Join-Path $scratch 'managed'
$workerProject = Join-Path $repository 'src\ContextSuite.Worker\ContextSuite.Worker.csproj'
$tests = Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj'
New-Item -ItemType Directory -Path $scratch -ErrorAction Stop | Out-Null
& dotnet build $workerProject -c $Configuration --nologo "-p:ContextSuiteManagedBuildRoot=$build"
if ($LASTEXITCODE -ne 0) { throw 'Isolated WEM worker build failed.' }
$payload = Join-Path $build "bin\ContextSuite.Worker\$Configuration\net10.0-windows"
& python -B (Join-Path $PSScriptRoot 'Stage-WemPayload.py') --distribution $DecoderDistribution --payload $payload
if ($LASTEXITCODE -ne 0) { throw 'WEM decoder provenance or staging verification failed.' }
& dotnet run --project $tests -c $Configuration -- --wem-workflow (Join-Path $scratch 'evidence') `
    (Join-Path $payload 'ContextSuite.Worker.exe') $Fixtures
if ($LASTEXITCODE -ne 0) { throw 'WEM worker/application publication checks failed.' }
if ($WwiseConsole) {
    $WwiseConsole = (Resolve-Path -LiteralPath $WwiseConsole).Path
    & dotnet run --project $tests -c $Configuration -- --wwise-workflow (Join-Path $scratch 'encoding-evidence') `
        (Join-Path $payload 'ContextSuite.Worker.exe') $Fixtures $WwiseConsole
    if ($LASTEXITCODE -ne 0) { throw 'Installed-Wwise worker/application publication checks failed.' }
    if ($AudioDistributionDirectory) {
        & python -B (Join-Path (Split-Path $PSScriptRoot -Parent) 'audio-engine\Stage-AudioPayload.py') `
            --distribution $AudioDistributionDirectory --payload $payload
        if ($LASTEXITCODE -ne 0) { throw 'Curated audio runtime staging failed.' }
        & dotnet run --project $tests -c $Configuration -- --wwise-workflow (Join-Path $scratch 'mp3-encoding-evidence') `
            (Join-Path $payload 'ContextSuite.Worker.exe') $Fixtures $WwiseConsole
        if ($LASTEXITCODE -ne 0) { throw 'Installed-Wwise MP3/float worker/application checks failed.' }
    }
}
Write-Host "WEM workflow evidence: $scratch"
