[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $repository ('.codex-temp\analyze-benchmark\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -ErrorAction Stop | Out-Null
$machine = [ordered]@{
    OS = (Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, OSArchitecture)
    CPU = @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors)
    MemoryBytes = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
    Utc = [DateTime]::UtcNow.ToString('o')
}
$machine | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $scratch 'machine.json') -Encoding UTF8
& dotnet build (Join-Path $repository 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj') -c Release --nologo
if ($LASTEXITCODE) { throw 'Benchmark host build failed.' }
& python -B (Join-Path $PSScriptRoot 'Benchmark-Analyze.py') --scratch $scratch
if ($LASTEXITCODE) { throw "Analyze benchmark failed; evidence retained at $scratch" }
