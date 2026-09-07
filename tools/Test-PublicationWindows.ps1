[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release', [switch] $Recycle, [switch] $Images)

$ErrorActionPreference = 'Stop'
if (-not $Recycle) { throw 'Specify -Recycle to authorize recycling only freshly created disposable test originals. The Recycle Bin is never emptied.' }
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $repositoryRoot '.codex-temp\publication-windows'
$project = Join-Path $repositoryRoot 'tests\ContextSuite.Core.ContractTests\ContextSuite.Core.ContractTests.csproj'
if ($Images) { & (Join-Path $PSScriptRoot 'Build-Production.ps1') -Configuration $Configuration -SkipShell }
& dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publication probe build failed.' }
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$runId = [guid]::NewGuid().ToString('N')
$stdout = Join-Path $scratch "$runId.stdout.txt"
$stderr = Join-Path $scratch "$runId.stderr.txt"
$executable = Join-Path $repositoryRoot "artifacts\managed\bin\ContextSuite.Core.ContractTests\$Configuration\net10.0-windows\ContextSuite.Core.ContractTests.exe"
$probeArguments = @('--recycle', ('"{0}"' -f $scratch))
if ($Images) {
    $worker = Join-Path $repositoryRoot "artifacts\production\$Configuration\ContextSuite.Worker.exe"
    $probeArguments = @('--recycle-images', ('"{0}"' -f $scratch), ('"{0}"' -f $worker))
}
$process = Start-Process -FilePath $executable -ArgumentList $probeArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$null = $process.Handle
try {
    if (-not $process.WaitForExit(45000)) {
        $process.Kill()
        $process.WaitForExit()
        throw "Publication probe timed out; only its test process was stopped. Review $stdout and $stderr."
    }
    Get-Content -LiteralPath $stdout
    Get-Content -LiteralPath $stderr
    if ($process.ExitCode -ne 0) { throw 'Windows publication/recycling verification failed. Replacement must remain disabled.' }
} finally { $process.Dispose() }
