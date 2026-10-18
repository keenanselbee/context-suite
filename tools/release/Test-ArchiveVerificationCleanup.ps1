[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $PSScriptRoot 'New-ReleaseCandidate.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref] $tokens, [ref] $parseErrors)
if ($parseErrors.Count) { throw 'Release candidate script has parse errors.' }
foreach ($name in 'Remove-VerifiedArchiveExtraction', 'Complete-ArchiveVerification') {
    $function = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $name }, $true))
    if ($function.Count -ne 1) { throw "Expected one $name function." }
    Invoke-Expression $function[0].Extent.Text
}
$scratch = Join-Path $repository ('.codex-temp\archive-cleanup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $archive = Join-Path $scratch 'ContextSuite-win-x64-unsigned.zip'
    [IO.File]::WriteAllText($archive, 'disposable archive fixture')
    $passingVerifier = Join-Path $scratch 'Pass.ps1'
    $failingVerifier = Join-Path $scratch 'Fail.ps1'
    [IO.File]::WriteAllText($passingVerifier, 'param([string] $Candidate) if (-not (Test-Path -LiteralPath $Candidate -PathType Container)) { throw "Missing candidate" }; Write-Output "Verified"')
    [IO.File]::WriteAllText($failingVerifier, 'param([string] $Candidate) throw "Verification failed"')
    $encoding = [Text.UTF8Encoding]::new($false)

    $success = Join-Path $scratch 'success'
    $successExtraction = Join-Path $success 'archive-verification'
    New-Item -ItemType Directory -Path (Join-Path $success 'ContextSuite') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $successExtraction 'ContextSuite') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $successExtraction 'ContextSuite\fixture.txt'), 'extracted fixture')
    $hash = Complete-ArchiveVerification -Output $success -Extraction $successExtraction -Archive $archive `
        -Verifier $passingVerifier -Encoding $encoding
    if (Test-Path -LiteralPath $successExtraction) { throw 'Successful verification retained extraction.' }
    if (-not (Test-Path -LiteralPath (Join-Path $success 'ContextSuite') -PathType Container) -or
        -not (Test-Path -LiteralPath $archive -PathType Leaf) -or
        (Get-Content -LiteralPath (Join-Path $success 'archive.sha256') -Raw) -ne "$hash  ContextSuite-win-x64-unsigned.zip`n") {
        throw 'Cleanup changed candidate output or archive evidence.'
    }

    $failure = Join-Path $scratch 'failure'
    $failureExtraction = Join-Path $failure 'archive-verification'
    New-Item -ItemType Directory -Path (Join-Path $failureExtraction 'ContextSuite') -Force | Out-Null
    $failed = $false
    try {
        Complete-ArchiveVerification -Output $failure -Extraction $failureExtraction -Archive $archive `
            -Verifier $failingVerifier -Encoding $encoding | Out-Null
    } catch { $failed = $true }
    if (-not $failed -or -not (Test-Path -LiteralPath $failureExtraction -PathType Container) -or
        (Test-Path -LiteralPath (Join-Path $failure 'archive.sha256'))) {
        throw 'Failed verification did not retain extraction.'
    }

    $hashFailure = Join-Path $scratch 'hash-failure'
    $hashFailureExtraction = Join-Path $hashFailure 'archive-verification'
    New-Item -ItemType Directory -Path (Join-Path $hashFailureExtraction 'ContextSuite') -Force | Out-Null
    $failed = $false
    try {
        Complete-ArchiveVerification -Output $hashFailure -Extraction $hashFailureExtraction `
            -Archive (Join-Path $scratch 'missing.zip') -Verifier $passingVerifier -Encoding $encoding | Out-Null
    } catch { $failed = $true }
    if (-not $failed -or -not (Test-Path -LiteralPath $hashFailureExtraction -PathType Container) -or
        (Test-Path -LiteralPath (Join-Path $hashFailure 'archive.sha256'))) {
        throw 'Hash failure did not retain extraction.'
    }

    $receiptFailure = Join-Path $scratch 'receipt-failure'
    $receiptFailureExtraction = Join-Path $receiptFailure 'archive-verification'
    New-Item -ItemType Directory -Path (Join-Path $receiptFailureExtraction 'ContextSuite') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $receiptFailure 'archive.sha256') | Out-Null
    $failed = $false
    try {
        Complete-ArchiveVerification -Output $receiptFailure -Extraction $receiptFailureExtraction `
            -Archive $archive -Verifier $passingVerifier -Encoding $encoding | Out-Null
    } catch { $failed = $true }
    if (-not $failed -or -not (Test-Path -LiteralPath $receiptFailureExtraction -PathType Container)) {
        throw 'Receipt write failure did not retain extraction.'
    }

    $outside = Join-Path $scratch 'outside'
    New-Item -ItemType Directory -Path $outside | Out-Null
    $sentinel = Join-Path $outside 'keep.txt'
    [IO.File]::WriteAllText($sentinel, 'keep')
    foreach ($unsafe in @($outside, (Join-Path $failureExtraction 'ContextSuite'))) {
        $rejected = $false
        try { Remove-VerifiedArchiveExtraction -Output $failure -Extraction $unsafe } catch { $rejected = $true }
        if (-not $rejected -or -not (Test-Path -LiteralPath $sentinel -PathType Leaf) -or
            -not (Test-Path -LiteralPath $failureExtraction -PathType Container)) {
            throw "Unsafe cleanup path was not rejected: $unsafe"
        }
    }

    $linkedOutput = Join-Path $scratch 'linked-output'
    New-Item -ItemType Junction -Path $linkedOutput -Target $failure | Out-Null
    $rejected = $false
    try {
        Remove-VerifiedArchiveExtraction -Output $linkedOutput -Extraction (Join-Path $linkedOutput 'archive-verification')
    } catch { $rejected = $true }
    if (-not $rejected -or -not (Test-Path -LiteralPath $failureExtraction -PathType Container)) {
        throw 'Reparse-point output was not rejected.'
    }

    $realParent = Join-Path $scratch 'real-parent'
    $ancestorOutput = Join-Path $realParent 'output'
    $ancestorExtraction = Join-Path $ancestorOutput 'archive-verification'
    New-Item -ItemType Directory -Path $ancestorExtraction -Force | Out-Null
    $linkedAncestor = Join-Path $scratch 'linked-parent'
    New-Item -ItemType Junction -Path $linkedAncestor -Target $realParent | Out-Null
    $rejected = $false
    try {
        Remove-VerifiedArchiveExtraction -Output (Join-Path $linkedAncestor 'output') `
            -Extraction (Join-Path $linkedAncestor 'output\archive-verification')
    } catch { $rejected = $true }
    if (-not $rejected -or -not (Test-Path -LiteralPath $ancestorExtraction -PathType Container)) {
        throw 'Reparse-point ancestor was not rejected.'
    }

    $linkedChild = Join-Path $failureExtraction 'linked-child'
    New-Item -ItemType Junction -Path $linkedChild -Target $outside | Out-Null
    $rejected = $false
    try { Remove-VerifiedArchiveExtraction -Output $failure -Extraction $failureExtraction } catch { $rejected = $true }
    if (-not $rejected -or -not (Test-Path -LiteralPath $sentinel -PathType Leaf) -or
        -not (Test-Path -LiteralPath $failureExtraction -PathType Container)) {
        throw 'Reparse point inside extraction was not rejected.'
    }
    Write-Output 'PASS: cleanup, verification/hash/receipt failures, path boundaries and reparse guards.'
} finally {
    foreach ($link in @($linkedChild, $linkedOutput, $linkedAncestor)) {
        if (-not $link) { continue }
        $item = Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
        if ($item) {
            if (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Refusing to remove non-junction test path: $link"
            }
            [IO.Directory]::Delete($link)
        }
    }
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
