using System.Text.Json;
using ContextSuite.Core.Transport;
using ContextSuite.Private.Pdf;

if (args is ["--pdf-dark-diagnostics"])
{
    PdfDarkDiagnosticContracts.Run();
    return;
}

if (args is ["--docx-render", var renderedEvidence, var rendererDirectory])
{
    using var renderer = new PdfRasterAdapter(Path.GetFullPath(rendererDirectory), Path.Combine(Path.GetFullPath(renderedEvidence), "render-scratch"));
    foreach (var path in Directory.GetFiles(renderedEvidence, "*.pdf"))
    {
        var source = await renderer.ProbeAsync(new(Guid.NewGuid(), Path.GetFullPath(path)), default);
        for (var page = 0; page < source.Document.Pages.Length; page++)
        {
            var png = await renderer.RenderAsync(source, page, default);
            var output = Path.ChangeExtension(path, null) + $"-{page + 1}.png";
            if (File.Exists(output)) throw new IOException("Do not overwrite visual evidence.");
            await File.WriteAllBytesAsync(output, png.Bytes);
            Console.WriteLine(output);
        }
    }
    return;
}

if (args is ["--docx", var docxWithRenderer, var docxRenderer])
{
    await PdfDocxContracts.RunAsync(docxWithRenderer, docxRenderer);
    return;
}
if (args is ["--docx", var docxEvidence])
{
    await PdfDocxContracts.RunAsync(docxEvidence);
    return;
}

if (args is ["--office-io", var ioWorker, var ioFixtures, var ioEvidence] && OperatingSystem.IsWindows())
{
    await OfficeIoContracts.RunAsync(ioWorker, ioFixtures, ioEvidence);
    return;
}

if (args is ["--office-resources", var resourceProbe, var resourceHash, var resourceStage] && OperatingSystem.IsWindows())
{
    await OfficeResourceContracts.RunAsync(resourceProbe, resourceHash, resourceStage);
    return;
}

if (args is ["--runtime-file-leases", var leaseEvidence])
{
    if (!Path.IsPathFullyQualified(leaseEvidence) || Directory.Exists(leaseEvidence)) throw new ArgumentException("Use a new absolute evidence directory.");
    Directory.CreateDirectory(leaseEvidence);
    var leaseChecks = new List<string>();
    OfficeRuntimeFileContracts.Run(leaseEvidence, leaseChecks);
    File.WriteAllText(Path.Combine(leaseEvidence, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = leaseChecks }));
    Console.WriteLine($"Passed {leaseChecks.Count} runtime file lease checks.");
    return;
}

if (args is ["--office-runtime-timing", var timingPackage, var timingEvidence] && OperatingSystem.IsWindows())
{
    await OfficeRuntimeTimingContracts.RunAsync(timingPackage, timingEvidence);
    return;
}

if (args is ["--office-workbook-evidence", var workbookRetained, var workbookEvidence] && OperatingSystem.IsWindows())
{
    await OfficeWorkbookContracts.RunAsync(workbookRetained, workbookEvidence);
    return;
}
if (args is ["--office-date-refusal", var dateFixtures, var dateEvidence] && OperatingSystem.IsWindows())
{
    await OfficeDateRefusalContracts.RunAsync(dateFixtures, dateEvidence);
    return;
}

if (args is ["--office-pdf-worker-validation", var validationWorker, var workerValidationExports, var workerValidationEvidence])
{
    await OfficePdfValidationContracts.RunAsync("", "", workerValidationExports, workerValidationEvidence, validationWorker);
    return;
}

if (args is ["--office-pdf-validation", var validationEngine, var validationRenderer, var validationExports, var validationEvidence])
{
    await OfficePdfValidationContracts.RunAsync(validationEngine, validationRenderer, validationExports, validationEvidence);
    return;
}

if (args is ["--office-owner-loss-recover", var retainedLossWorker, var retainedLossStage])
{
    await OfficeOwnerLossContracts.RecoverStageAsync(retainedLossWorker, retainedLossStage);
    return;
}

if (args is ["--office-owner-loss-child", var lossWorker, var lossSource, var lossFormat, var lossStage])
{
    await OfficeOwnerLossContracts.HoldAsync(lossWorker, lossSource, lossFormat, lossStage);
    return;
}

if (args is ["--office-recovery-cleanup", var retainedRecoveryStage] && OperatingSystem.IsWindows())
{
    await OfficeRecoveryContracts.RecoverStageAsync(retainedRecoveryStage);
    return;
}

if (args is ["--office-recovery-owner", var recoveryStage, var recoveryMode] && OperatingSystem.IsWindows())
{
    await OfficeRecoveryContracts.HoldAsync(recoveryStage, recoveryMode);
    return;
}

if (args is ["--office-worker-stop-diagnostic" or "--office-worker-validation-diagnostic", var diagnosticWorker, var diagnosticSource, var diagnosticFormat, var diagnosticEvidence])
{
    await OfficeWorkerStopContracts.DiagnoseAsync(diagnosticWorker, diagnosticSource, diagnosticFormat, diagnosticEvidence,
        args[0] == "--office-worker-validation-diagnostic");
    return;
}

if (args is ["--office-app-recovery", var appWorker, var appLargeFixtures, var appFixtures, var appEvidence, var appHost])
{
    await OfficeOwnerLossContracts.RunAsync(appWorker, appLargeFixtures, appFixtures, appEvidence, true, appHost);
    return;
}

if (args is ["--office-worker-stop", var stopWorker, var stopLargeFixtures, var stopFixtures, var stopEvidence])
{
    await OfficeWorkerStopContracts.RunAsync(stopWorker, stopLargeFixtures, stopFixtures, stopEvidence);
    return;
}

if (args is ["--office-worker-stop", var modeWorker, var modeLargeFixtures, var modeFixtures, var modeEvidence, var stopMode])
{
    if (stopMode is "owner-loss" or "startup-recovery") await OfficeOwnerLossContracts.RunAsync(modeWorker, modeLargeFixtures, modeFixtures, modeEvidence, stopMode == "startup-recovery");
    else await OfficeWorkerStopContracts.RunAsync(modeWorker, modeLargeFixtures, modeFixtures, modeEvidence, stopMode);
    return;
}
if (args is ["--office-workbook-stop", var workbookWorker, var workbookLargeFixtures, var workbookFixtures, var workbookStopEvidence, var workbookStopMode])
{
    await OfficeWorkerStopContracts.RunAsync(workbookWorker, workbookLargeFixtures, workbookFixtures, workbookStopEvidence, workbookStopMode, workbookCopy: true);
    return;
}
if (args is ["--office-unfinished-workbook-stop", var unfinishedWorker, var unfinishedLarge, var unfinishedOrdinary, var unfinishedEvidence, var unfinishedMode])
{
    await OfficeWorkerStopContracts.RunAsync(unfinishedWorker, unfinishedLarge, unfinishedOrdinary, unfinishedEvidence, unfinishedMode,
        workbookCopy: true, unfinishedWorkbook: true);
    return;
}

if (args is ["--office-worker-readiness", var readinessWorker, var readinessSource])
{
    using var input = ContextSuite.Private.IO.MediaFiles.OpenRead(readinessSource);
    var admission = await ContextSuite.Core.Analysis.OfficeSourcePreflight.InspectOpenXmlAsync(readinessSource, input, CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(new { admission.FormatId, admission.Refusal,
        ReadOnlySnapshot = File.GetAttributes(readinessSource).HasFlag(FileAttributes.ReadOnly) }));
    var package = Path.Combine(Path.GetDirectoryName(readinessWorker)!, "office-engine");
    using var runtime = await ContextSuite.Private.Office.OfficeRuntimeLease.OpenAsync(Path.Combine(package, "ContextSuite.OfficeHost.exe"),
        Path.Combine(package, "runtime"), Path.Combine(package, "runtime-files.txt"), CancellationToken.None);
    Console.WriteLine("Complete pinned runtime read lease acquired; no Office execution or profile creation.");
    return;
}

if (args is ["--office-worker", var exportWorker, var exportFixtures, var exportEvidence])
{
    await OfficeWorkerContracts.RunAsync(exportWorker, exportFixtures, exportEvidence);
    return;
}

if (args is ["--office-runtime", var runtimeHost, var officeRuntime, var runtimeInventory, var runtimeEvidence])
{
    await OfficeRuntimeContracts.RunAsync(runtimeHost, officeRuntime, runtimeInventory, runtimeEvidence);
    return;
}

if (args is ["--office-profile-owner", var ownerProbe, var ownerHash, var ownerStage] && OperatingSystem.IsWindows())
{
    await OfficeOwnerContracts.RunAsync(ownerProbe, ownerHash, ownerStage);
    return;
}
if (args is ["--office-profile-worker", var profileProbe, var profileStage, var profileHash] && OperatingSystem.IsWindows())
{
    await OfficeOwnerContracts.WorkerAsync(profileProbe, profileStage, profileHash);
    return;
}

if (args is ["--office-host-process", var officeHost, var officeCase, var officeHostHash, var officeMode])
{
    await OfficeSandboxContracts.RunAsync(officeHost, officeCase, officeHostHash, officeMode);
    return;
}

if (args.Length == 3 && args[0] == "--image-pdf-precision-resources")
{
    await ImagePdfPrecisionResourceContracts.RunAsync(args[1], args[2]);
    return;
}

if (args.Length == 3 && args[0] == "--image-pdf-resources")
{
    await ImagePdfResourceContracts.RunAsync(args[1], args[2]);
    return;
}

if (args.Length == 3 && args[0] == "--raster-pdf-reuse")
{
    await RasterPdfReuseContracts.RunAsync(args[1], args[2]);
    return;
}

if (args.Length is 4 or 5 && args[0] == "--image-pdf")
{
    await ImagePdfContracts.RunAsync(args[1], args[2], args[3], args.Length == 5 ? args[4] : null);
    return;
}

if (args.Length == 5 && args[0] == "--raster")
{
    await RasterContracts.RunAsync(args[1], args[2], args[3], args[4]);
    return;
}

if (args.Length != 3) throw new ArgumentException("Expected pinned engine directory, generated fixtures and new evidence directory.");
if (Directory.Exists(args[2])) throw new IOException("Evidence directory already exists.");
Directory.CreateDirectory(args[2]);
var scratch = Path.Combine(args[2], "probe ü scratch");
var checks = new List<string>();
var reports = new List<object>();
using var adapter = new PdfProbeAdapter(args[0], scratch);
foreach (var name in new[] { "authored original ü.pdf", "optimized copy ü.pdf", "encrypted.pdf", "owner-protected.pdf", "signature-field-canary.pdf" })
{
    var facts = await adapter.ProbeAsync(await File.ReadAllBytesAsync(Path.Combine(args[1], name)), CancellationToken.None);
    var valid = name switch
    {
        "encrypted.pdf" => facts.IsEncrypted && facts.PageCount is null && facts.HasForms is null,
        "owner-protected.pdf" => facts.IsEncrypted && facts.PageCount == 2,
        "signature-field-canary.pdf" => facts.ReportedSignatureFieldCount == 1,
        _ => facts.PageCount == 2 && facts.FormFieldCount == 1 && facts.AttachmentCount == 1 && facts.OutlineCount == 1
    };
    if (!valid) throw new Exception("Unexpected PDF facts for " + name);
    checks.Add("real PDF facts: " + name);
    reports.Add(new { name, facts });
    if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new Exception("Temporary PDF snapshot retained after success.");
}
checks.Add("Unicode scratch and successful snapshot cleanup");
try { await adapter.ProbeAsync(await File.ReadAllBytesAsync(Path.Combine(args[1], "broken-xref.pdf")), CancellationToken.None); throw new Exception("Malformed PDF was accepted."); }
catch (InvalidDataException) { checks.Add("damaged cross-reference rejected"); }
if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new Exception("Temporary PDF retained after failure.");
checks.Add("failed snapshot cleanup");
using (var cancelled = new CancellationTokenSource())
using (var watcher = new FileSystemWatcher(scratch, "pdf-probe-*.pdf"))
{
    var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    watcher.Created += (_, _) => created.TrySetResult();
    watcher.EnableRaisingEvents = true;
    var source = new byte[WorkerCommand.MaximumPdfProbeBytes];
    "%PDF-1.7\n"u8.CopyTo(source);
    var pending = adapter.ProbeAsync(source, cancelled.Token);
    await created.Task.WaitAsync(TimeSpan.FromSeconds(5));
    cancelled.Cancel();
    try { await pending; throw new Exception("In-flight cancellation ignored."); }
    catch (OperationCanceledException) { checks.Add("cancellation after snapshot creation starts"); }
    if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new Exception("Temporary snapshot retained after cancellation.");
    checks.Add("cancelled snapshot cleanup");
}
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await adapter.ProbeAsync("%PDF-1.7"u8.ToArray(), cancelled.Token); throw new Exception("Cancellation ignored."); }
    catch (OperationCanceledException) { checks.Add("pre-cancellation"); }
}
try { await adapter.ProbeAsync(new byte[WorkerCommand.MaximumPdfProbeBytes + 1], CancellationToken.None); throw new Exception("Input limit ignored."); }
catch (InvalidDataException) { checks.Add("input byte limit"); }
try { await adapter.ProbeAsync(new byte[] { 0, 1, 2 }, CancellationToken.None); throw new Exception("Non-PDF accepted."); }
catch (InvalidDataException) { checks.Add("non-PDF rejected before snapshot"); }
try
{
    using var writer = new FileStream(Path.Combine(args[0], "qpdf30.dll"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
    throw new Exception("Verified DLL remains writable.");
}
catch (IOException) { checks.Add("engine identity lease excludes writers"); }
try { using var invalid = new PdfProbeAdapter(args[0], scratch + "\nunsafe"); throw new Exception("Argument line injection accepted."); }
catch (ArgumentException) { checks.Add("scratch argument line breaks rejected"); }
adapter.Dispose();
try { await adapter.ProbeAsync("%PDF-1.7"u8.ToArray(), CancellationToken.None); throw new Exception("Disposed adapter ran."); }
catch (ObjectDisposedException) { checks.Add("disposed adapter rejected"); }
using (var optimizer = new PdfProbeAdapter(args[0], Path.Combine(args[2], "optimize scratch")))
{
    var original = await File.ReadAllBytesAsync(Path.Combine(args[1], "authored original ü.pdf"));
    var candidate = await optimizer.OptimizeAsync(original, default);
    if (!candidate.IsSmaller || candidate.Bytes.Length >= original.Length) throw new Exception("Structural optimization did not reduce the generated input.");
    await File.WriteAllBytesAsync(Path.Combine(args[2], "optimized-candidate.pdf"), candidate.Bytes);
    checks.Add("structural candidate is smaller with matching reachable document graph"); reports.Add(new { candidate.IsSmaller, candidate.SourceSha256, candidate.OutputSha256, candidate.Facts });
    var repeated = await optimizer.OptimizeAsync(candidate.Bytes, default);
    if (!repeated.IsSmaller && !repeated.Bytes.SequenceEqual(candidate.Bytes)) throw new Exception("No-smaller candidate changed original bytes.");
    checks.Add("repeated structural optimization retains content and unchanged bytes when not smaller");
    var originalText = System.Text.Encoding.Latin1.GetString(original);
    var oldXref = System.Text.RegularExpressions.Regex.Matches(originalText, @"startxref\s+(\d+)")[^1].Groups[1].Value;
    var nextObject = int.Parse(System.Text.RegularExpressions.Regex.Matches(originalText, @"/Size\s+(\d+)")[^1].Groups[1].Value);
    var rootReference = System.Text.RegularExpressions.Regex.Matches(originalText, @"/Root\s+(\d+\s+\d+\s+R)")[^1].Groups[1].Value;
    var newObject = $"\n{nextObject} 0 obj\n<< /Producer (Revision test) >>\nendobj\n";
    var newXref = original.Length + System.Text.Encoding.ASCII.GetByteCount(newObject);
    var revision = original.Concat(System.Text.Encoding.ASCII.GetBytes(newObject +
        $"xref\n{nextObject} 1\n{original.Length + 1:D10} 00000 n \ntrailer\n<< /Size {nextObject + 1} /Root {rootReference} /Info {nextObject} 0 R /Prev {oldXref} >>\nstartxref\n{newXref}\n%%EOF\n")).ToArray();
    await File.WriteAllBytesAsync(Path.Combine(args[2], "incremental.pdf"), revision);
    if ((await optimizer.ProbeAsync(revision, default)).PageCount != 2) throw new Exception("Incremental fixture is not a readable two-page PDF.");
    checks.Add("generated incremental revision remains readable for Analyze");
    try { await optimizer.OptimizeAsync(revision, default); throw new Exception("Uninspected revision history was rewritten."); }
    catch (NotSupportedException) { checks.Add("structural optimization refuses actual incremental history"); }
    foreach (var name in new[] { "encrypted.pdf", "owner-protected.pdf", "signature-field-canary.pdf", "unattached-signature-canary.pdf", "broken-xref.pdf" })
    {
        try { await optimizer.OptimizeAsync(await File.ReadAllBytesAsync(Path.Combine(args[1], name)), default); throw new Exception("Unsafe PDF rewrite admitted: " + name); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { checks.Add("structural optimization refuses " + name); }
    }
    using var stop = new CancellationTokenSource(); stop.Cancel();
    try { await optimizer.OptimizeAsync(original, stop.Token); throw new Exception("Cancelled optimization ran."); }
    catch (OperationCanceledException) { checks.Add("structural optimization pre-cancellation"); }
    if (Directory.EnumerateFileSystemEntries(Path.Combine(args[2], "optimize scratch")).Any()) throw new Exception("Optimization retained owned snapshots.");
    checks.Add("structural optimization cleans snapshots after success, refusal and cancellation");
}
await File.WriteAllTextAsync(Path.Combine(args[2], "probe-adapter.json"), JsonSerializer.Serialize(new { checks, reports }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Passed {checks.Count} private PDF adapter checks. Evidence: {args[2]}");
