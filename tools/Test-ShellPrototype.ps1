[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $SkipBuild,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration -OutputDirectory $OutputDirectory
}

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot "artifacts\bin\x64\$Configuration" }
$hostExecutable = Join-Path $OutputDirectory 'ContextSuite.Host.exe'
$contractTests = Join-Path $OutputDirectory 'ContextSuite.Shell.ContractTests.exe'
$shellLibrary = Join-Path $OutputDirectory 'ContextSuite.Shell.dll'
$packageDefinitions = @(
    @{
        Operation = 'Analyze'
        PackageName = 'Keenan.ContextSuite.Analyze.ShellPrototype'
        Manifest = 'packaging\ContextSuite.Analyze.ShellPrototype\AppxManifest.xml'
        Verb = 'ContextSuiteAnalyze'
        ClassId = '7F71BFC7-0125-47B6-A163-54EADDB56E8B'
    },
    @{
        Operation = 'Convert'
        PackageName = 'Keenan.ContextSuite.Convert.ShellPrototype'
        Manifest = 'packaging\ContextSuite.Convert.ShellPrototype\AppxManifest.xml'
        Verb = 'ContextSuiteConvert'
        ClassId = 'C0A78640-7313-4AD7-870C-2F0B838A28C2'
    },
    @{
        Operation = 'Optimize'
        PackageName = 'Keenan.ContextSuite.Optimize.ShellPrototype'
        Manifest = 'packaging\ContextSuite.Optimize.ShellPrototype\AppxManifest.xml'
        Verb = 'ContextSuiteOptimize'
        ClassId = '8C4CA2A6-99A2-436A-9021-3D004687FC4C'
    }
)
if (-not (Test-Path -LiteralPath $hostExecutable)) {
    throw "Host executable was not found: $hostExecutable"
}
if (-not (Test-Path -LiteralPath $contractTests) -or -not (Test-Path -LiteralPath $shellLibrary)) {
    throw 'The native shell contract binaries were not found.'
}

$registeredClassIds = @()
foreach ($definition in $packageDefinitions) {
    $manifestPath = Join-Path $repositoryRoot $definition.Manifest
    [xml] $manifest = Get-Content -LiteralPath $manifestPath -Raw
    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaceManager.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $namespaceManager.AddNamespace('com', 'http://schemas.microsoft.com/appx/manifest/com/windows10')
    $namespaceManager.AddNamespace('desktop5', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/5')

    $identity = $manifest.SelectSingleNode('/f:Package/f:Identity', $namespaceManager)
    $applications = $manifest.SelectNodes('/f:Package/f:Applications/f:Application', $namespaceManager)
    $verbs = $manifest.SelectNodes('//desktop5:Verb', $namespaceManager)
    $classes = $manifest.SelectNodes('//com:Class', $namespaceManager)
    if ($identity.Name -ne $definition.PackageName -or
        $applications.Count -ne 1 -or $verbs.Count -ne 1 -or $classes.Count -ne 1) {
        throw "$($definition.Operation) must have one package identity, application, verb, and COM class."
    }

    $application = $applications[0]
    $verb = $verbs[0]
    $class = $classes[0]
    if ($application.Id -ne $definition.Operation -or
        $application.VisualElements.DisplayName -ne $definition.Operation -or
        $verb.Id -ne $definition.Verb -or
        $verb.Clsid -ne $definition.ClassId -or
        $class.Id -ne $definition.ClassId) {
        throw "$($definition.Operation) package registration does not match its stable identifiers."
    }
    $registeredClassIds += $class.Id
}
if (@($registeredClassIds | Sort-Object -Unique).Count -ne 3) {
    throw 'Every shell package must use a distinct COM class identifier.'
}

$temporaryRoot = Join-Path $repositoryRoot ".codex-temp\shell-prototype-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

function ConvertTo-NativeArgument {
    param([Parameter(Mandatory)][string] $Value)

    return '"{0}"' -f $Value.Replace('"', '\"')
}

function Invoke-NativeProcess {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [string[]] $Arguments
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments -join ' '
    $startInfo.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    return $process.ExitCode
}

function Invoke-HostValidation {
    param(
        [Parameter(Mandatory)]
        [string] $RequestPath,

        [string] $ResultPath
    )

    $arguments = @('--validate-only', '--activation-file', (ConvertTo-NativeArgument $RequestPath))
    if ($ResultPath) {
        $arguments += @('--result-file', (ConvertTo-NativeArgument $ResultPath))
    }
    return Invoke-NativeProcess -FilePath $hostExecutable -Arguments $arguments
}

try {
    $selectedFiles = @(
        (Join-Path $temporaryRoot 'first image.png'),
        (Join-Path $temporaryRoot 'second-image.jpg'),
        (Join-Path $temporaryRoot "$([char]0x97F3)$([char]0x58F0) sample.webp")
    )
    foreach ($selectedFile in $selectedFiles) {
        New-Item -ItemType File -Path $selectedFile | Out-Null
    }

    $operationActions = @{
        analyze = 'open-details'
        convert = 'choose-format'
        optimize = 'choose-preset'
    }
    foreach ($operation in @('analyze', 'convert', 'optimize')) {
        $action = $operationActions[$operation]
        $requestId = [guid]::NewGuid().ToString().ToUpperInvariant()
        $requestPath = Join-Path $temporaryRoot "$operation.request"
        $resultPath = Join-Path $temporaryRoot "$operation.result"
        $requestLines = @(
            'ContextSuiteActivation/1',
            "requestId=$requestId",
            "operation=$operation",
            "action=$action",
            "pathCount=$($selectedFiles.Count)"
        )
        $requestLines += $selectedFiles | ForEach-Object { "path=$_" }
        [System.IO.File]::WriteAllLines(
            $requestPath,
            $requestLines,
            [System.Text.UTF8Encoding]::new($false))

        $exitCode = Invoke-HostValidation -RequestPath $requestPath -ResultPath $resultPath
        if ($exitCode -ne 0) {
            throw "$operation batch validation failed with exit code $exitCode."
        }

        $result = Get-Content -LiteralPath $resultPath
        if ($result[0] -ne 'VALID' -or
            $result -notcontains "operation=$operation" -or
            $result -notcontains "action=$action" -or
            $result -notcontains "pathCount=$($selectedFiles.Count)") {
            throw "$operation batch validation returned an unexpected result."
        }
    }

    $invalidRequest = Join-Path $temporaryRoot 'invalid.request'
    [System.IO.File]::WriteAllText($invalidRequest, "ContextSuiteActivation/999`n")
    $invalidExitCode = Invoke-HostValidation -RequestPath $invalidRequest
    if ($invalidExitCode -eq 0) {
        throw 'The host accepted an unknown activation schema.'
    }

    $contractArguments = @(
        (ConvertTo-NativeArgument $shellLibrary),
        (ConvertTo-NativeArgument $temporaryRoot)
    )
    $contractArguments += $selectedFiles | ForEach-Object { ConvertTo-NativeArgument $_ }
    $contractExitCode = Invoke-NativeProcess -FilePath $contractTests -Arguments $contractArguments
    if ($contractExitCode -ne 0) {
        throw "The native shell activation contracts failed with exit code $contractExitCode."
    }
}
finally {
    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
    $resolvedExpectedParent = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot '.codex-temp'))
    if ($resolvedTemporaryRoot.StartsWith($resolvedExpectedParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}

Write-Output 'Shell prototype contracts passed: three independent identity packages and three-file activation batches.'
