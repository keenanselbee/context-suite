using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Diagnostics;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Tiff;
using ContextSuite.Core.Transport;
using ContextSuite.Core.ContractTests;

internal static class TiffWorkflowContracts
{
    public static async Task RunInterruptionAsync(string root, string executable, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new TIFF interruption directory.");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.tif");
        await TiffFixtureFactory.CreateLargeAsync(path);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var clock = new ImageInterruptionContracts.DeadlineClock();
        await using var worker = new WorkerClient(executable, Path.Combine(root, "workers"), clock);
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new WindowsFileRecycler());
        foreach (var fault in new[] { "cancel", "crash", "timeout" })
        {
            var source = await worker.ProbeTiffAsync(new(Guid.NewGuid(), path), default);
            var outputId = Guid.NewGuid();
            var reservation = await publisher.ReserveAsync(new(outputId, path, "png", new("convert", new()), PageNumber: 1));
            using var cancellation = new CancellationTokenSource();
            using var owned = Process.GetProcessById(worker.ProcessId!.Value);
            var startingCpu = owned.TotalProcessorTime;
            var conversion = worker.ConvertTiffAsync(new(source, new(TiffTarget.Png), 0, outputId, reservation.TemporaryPath),
                cancellation.Token);
            var observed = Stopwatch.StartNew();
            while (!conversion.IsCompleted && observed.Elapsed < TimeSpan.FromSeconds(10))
            {
                owned.Refresh();
                if (owned.TotalProcessorTime - startingCpu >= TimeSpan.FromMilliseconds(30)) break;
                await Task.Delay(5);
            }
            owned.Refresh();
            check(!conversion.IsCompleted && owned.TotalProcessorTime - startingCpu >= TimeSpan.FromMilliseconds(30),
                "TIFF active work: worker is computing before " + fault);
            if (fault == "cancel") cancellation.Cancel();
            else if (fault == "crash") owned.Kill(true);
            else
            {
                check(clock.Current!.Due == TimeSpan.FromSeconds(120), "TIFF active work: conversion retains 120-second deadline");
                clock.Current.Expire();
            }
            try { await conversion.WaitAsync(TimeSpan.FromSeconds(15)); throw new InvalidOperationException("Interrupted TIFF returned success."); }
            catch (OperationCanceledException) when (fault == "cancel")
            { check(true, "TIFF active work: cancellation remains cancellation"); }
            catch (MediaWorkerException error) when (fault != "cancel")
            {
                check(error.Failure == (fault == "crash" ? ImageFailure.WorkerTerminated : ImageFailure.TimedOut),
                    "TIFF active work: typed failure for " + fault);
            }
            await publisher.AbandonAsync(reservation, fault == "cancel");
            check(!File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(hash),
                "TIFF active work: no partial publication and source retained after " + fault);
            await worker.ProbeTiffAsync(new(Guid.NewGuid(), path), default);
            check(worker.ProcessId != owned.Id, "TIFF active work: next probe starts a fresh worker after " + fault);
        }
    }

    public static async Task RunWorkerAsync(string root, string executable, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new TIFF workflow directory.");
        Directory.CreateDirectory(root);
        var fixtures = Path.Combine(root, "fixtures");
        await TiffFixtureFactory.CreateAsync(fixtures);
        var sourcePath = Path.Combine(root, "pages.tiff");
        File.Copy(Path.Combine(fixtures, "bgra32-lzw.tif"), sourcePath);
        var originalHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
        var trialPath = Path.Combine(root, "trial.json");
        await using var worker = new WorkerClient(executable, Path.Combine(root, "workers"));
        var source = await worker.ProbeTiffAsync(new(Guid.NewGuid(), sourcePath), default);
        check(source.Pages.Length == 2 && source.Pages.All(page => page.UnsupportedReason is null) &&
            source.Pages[0].Orientation == 1 && source.Pages[1].Orientation == 6 &&
            !File.Exists(trialPath), "TIFF worker: two ordered pages probe without starting trial");
        var preview = await worker.PreviewTiffAsync(new(source, 1, 0xffffff), default);
        check(preview.Width is > 0 and <= 512 && preview.Height is > 0 and <= 512 &&
            preview.BgraPixels.Length == (long)preview.Width * preview.Height * 4 && !File.Exists(trialPath),
            "TIFF worker: first transparent-page preview is bounded and leaves trial unstarted");
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new WindowsFileRecycler(), replacementVerified: true);
        var access = new LocalTrialStore(trialPath);
        var plan = TiffConversionPlan.Create(Guid.NewGuid(), [source], new(TiffTarget.Png),
            new("convert", new ToolSettings(ReplaceOriginals: true))).Confirm();
        var first = await new TiffConversionExecutor(worker, publisher, access).ExecuteAsync(plan, null, default);
        if (first.Pages.Any(page => page.Result.State != OperationState.Succeeded))
        {
            var id = Guid.NewGuid();
            var reservation = await publisher.ReserveAsync(new(id, sourcePath, "png", plan.Plan.Settings, PageNumber: 1));
            try { await worker.ConvertTiffAsync(new(source, plan.Plan.Options, 0, id, reservation.TemporaryPath), default); }
            finally { await publisher.AbandonAsync(reservation, false); }
        }
        check(first.Admission.IsAllowed && first.Pages.Count == 2 && first.Pages.All(page => page.Result.Publication?.Outcome == PublicationOutcome.CopyCreated) &&
            File.Exists(trialPath), "TIFF worker: both PNG pages publish as copies under one admission (" +
            string.Join("; ", first.Pages.Select(page => $"{page.PageIndex}: {page.Result.State} {page.Result.Message}")) + ")");
        var outputsBeforeBudgetRetry = Directory.GetFiles(root, "pages - Page *.png").Length;
        var budgetRetry = await new TiffConversionExecutor(worker, publisher, access).ExecuteAdmittedAsync(plan,
            new(new(true, "Available"), plan.Plan.BatchId), null, default,
            new Dictionary<(Guid SourceId, int PageIndex), long> { [(source.ItemId, 0)] = 2L * 1024 * 1024 * 1024 });
        check(budgetRetry.Pages.Count == 1 && budgetRetry.Pages[0].PageIndex == 1 &&
            budgetRetry.Pages[0].Result.State == OperationState.Failed &&
            Directory.GetFiles(root, "pages - Page *.png").Length == outputsBeforeBudgetRetry &&
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))) == originalHash,
            "TIFF worker: retry includes completed output in the 2 GiB budget before publishing another page");
        foreach (var page in first.Pages)
        {
            var output = page.Result.Publication!.OutputPath!;
            var png = await worker.ProbeAsync(new(Guid.NewGuid(), output), default);
            var expected = source.Pages[page.PageIndex];
            check(png.Width == expected.OutputWidth && png.Height == expected.OutputHeight,
                "TIFF worker: PNG copy has oriented page dimensions");
        }
        var retry = await new TiffConversionExecutor(worker, publisher, access).ExecuteAdmittedAsync(plan,
            first.Admission, null, default, first.Pages.ToDictionary(page => (page.SourceId, page.PageIndex),
                page => page.Result.Publication!.OutputBytes));
        check(retry.Pages.Count == 0, "TIFF worker: retry does not duplicate published pages");
        var cancelPath = Path.Combine(root, "cancel.tif");
        File.Copy(sourcePath, cancelPath);
        var cancelSource = await worker.ProbeTiffAsync(new(Guid.NewGuid(), cancelPath), default);
        var cancelPlan = TiffConversionPlan.Create(Guid.NewGuid(), [cancelSource], new(TiffTarget.Png),
            new("convert", new ToolSettings())).Confirm();
        using var cancellation = new CancellationTokenSource();
        var interrupted = await new TiffConversionExecutor(worker, publisher, access).ExecuteAsync(cancelPlan,
            (_, _, result) => { if (result.Publication?.IsCommitted == true) cancellation.Cancel(); }, cancellation.Token);
        check(interrupted.Pages[0].Result.Publication?.IsCommitted == true &&
            interrupted.Pages[1].Result.State == OperationState.Cancelled,
            "TIFF worker: cancellation retains first page and leaves second unfinished");
        var resumed = await new TiffConversionExecutor(worker, publisher, access).ExecuteAdmittedAsync(cancelPlan,
            interrupted.Admission, null, default, new Dictionary<(Guid SourceId, int PageIndex), long>
            { [(cancelSource.ItemId, 0)] = interrupted.Pages[0].Result.Publication!.OutputBytes });
        check(resumed.Pages.Count == 1 && resumed.Pages[0].PageIndex == 1 && resumed.Pages[0].Result.Publication?.IsCommitted == true &&
            Directory.GetFiles(root, "cancel - Page 001*.png").Length == 1,
            "TIFF worker: retry saves only unfinished page without duplicating the first");
        var jpegPlan = TiffConversionPlan.Create(Guid.NewGuid(), [source], new(TiffTarget.Jpeg, 0xffffff),
            new("convert", new ToolSettings())).Confirm();
        var jpeg = await new TiffConversionExecutor(worker, publisher, access).ExecuteAsync(jpegPlan, null, default);
        check(jpeg.Pages.Count == 2 && jpeg.Pages.All(page => page.Result.Publication?.IsCommitted == true),
            "TIFF worker: explicit white matte produces both JPEG copies");
        foreach (var page in jpeg.Pages)
        {
            var image = await worker.ProbeAsync(new(Guid.NewGuid(), page.Result.Publication!.OutputPath!), default);
            check(image.Width == source.Pages[page.PageIndex].OutputWidth && image.Height == source.Pages[page.PageIndex].OutputHeight &&
                !image.HasTransparency, "TIFF worker: JPEG page is oriented and opaque");
        }
        var mixedAccess = new CountingAccess(new LocalTrialStore(Path.Combine(root, "mixed-trial.json")));
        await using (var vm = new MainViewModel(worker, new SuiteSettings(), publisher, mixedAccess))
        {
            var jpegPath = jpeg.Pages[0].Result.Publication!.OutputPath!;
            var request = new OperationRequest(Guid.NewGuid(), "convert", "png", [sourcePath, jpegPath]);
            check(vm.Admit(request).Accepted, "TIFF application: mixed raster and TIFF request enters one batch");
            await vm.WaitForIdleAsync();
            check(vm.Rows.Count == 2 && vm.Rows.All(row => row.Result.State == OperationState.Succeeded) &&
                vm.Rows.Single(row => row.Path == sourcePath).SavedTiffPages == 2 && mixedAccess.Admissions == 1,
                "TIFF application: mixed raster and TIFF complete with one access admission (" +
                string.Join("; ", vm.Rows.Select(row => $"{row.Name}: {row.Result.State} {row.Result.Message}")) +
                $"; admissions={mixedAccess.Admissions})");
        }
        var changedPath = Path.Combine(root, "changed.tif");
        File.Copy(sourcePath, changedPath);
        await using (var retryWorker = new WorkerClient(executable, Path.Combine(root, "retry-workers")))
        {
            var oldSource = await retryWorker.ProbeTiffAsync(new(Guid.NewGuid(), changedPath), default);
            var prior = new FileRow(1, "convert", changedPath, plan.Plan.Settings, "png");
            prior.BeginTiff(oldSource, false);
            prior.ApplyTiffPage(0, new(changedPath, OperationState.Succeeded, "Saved",
                new PublicationResult(changedPath, PublicationOutcome.CopyCreated, "Saved", Path.Combine(root, "prior-page.png"))));
            prior.ApplyTiffPage(1, new(changedPath, OperationState.Failed, "Interrupted"));
            await File.AppendAllTextAsync(changedPath, "changed source canary");
            await using var retryVm = new MainViewModel(retryWorker, new SuiteSettings(), publisher,
                new LocalTrialStore(Path.Combine(root, "retry-trial.json")));
            retryVm.Rows.Add(prior);
            check(retryVm.RetryFailed() == "", "TIFF application: unfinished source enters retry queue");
            await retryVm.WaitForIdleAsync();
            var latest = retryVm.Rows.Last();
            check(latest.TiffResumeBlocked && latest.SavedTiffPages == 1 && latest.Result.State == OperationState.Failed &&
                !File.Exists(Path.Combine(root, "retry-trial.json")),
                "TIFF application: changed source blocks retry before admission and retains prior page");
        }
        if (worker.HasTiffPdfConverter)
        {
            await using var pdfWorker = new WorkerClient(executable, Path.Combine(root, "pdf-workers"));
            var pdfSources = new[]
            {
                await pdfWorker.ProbeTiffAsync(new(Guid.NewGuid(), sourcePath), default),
                await pdfWorker.ProbeTiffAsync(new(Guid.NewGuid(), cancelPath), default)
            };
            var pdfPlan = TiffConversionPlan.Create(Guid.NewGuid(), pdfSources, new(TiffTarget.Pdf),
                new("convert", new ToolSettings(ReplaceOriginals: true))).Confirm();
            var pdfResults = await new TiffConversionExecutor(pdfWorker, publisher,
                new LocalTrialStore(Path.Combine(root, "pdf-trial.json"))).ExecuteAsync(pdfPlan, null, default);
            check(pdfResults.Pages.Count == 2 && pdfResults.Pages.All(page => page.PageIndex == -1 &&
                page.Result.Publication?.Outcome == PublicationOutcome.CopyCreated &&
                File.ReadAllBytes(page.Result.Publication.OutputPath!).AsSpan().StartsWith("%PDF-"u8)),
                "TIFF worker: each TIFF publishes one ordered PDF copy and keeps originals");
            var pdfAccess = new CountingAccess(new LocalTrialStore(Path.Combine(root, "pdf-mixed-trial.json")));
            await using var pdfVm = new MainViewModel(pdfWorker, new SuiteSettings(), publisher, pdfAccess);
            var rasterPath = jpeg.Pages[0].Result.Publication!.OutputPath!;
            check(pdfVm.Admit(new(Guid.NewGuid(), "convert", "pdf", [sourcePath, rasterPath])).Accepted,
                "TIFF application: TIFF and raster PDF paths enter one request");
            await pdfVm.WaitForIdleAsync();
            check(pdfVm.Rows.Count == 2 && pdfVm.Rows.All(row => row.Result.State == OperationState.Succeeded) &&
                pdfVm.Rows.Single(row => row.Path == sourcePath).SavedTiffPages == 1 && pdfAccess.Admissions == 1,
                "TIFF application: mixed TIFF and raster PDFs share one access admission (" +
                string.Join("; ", pdfVm.Rows.Select(row => $"{row.Name}: {row.Result.State} {row.Result.Message}")) +
                $"; admissions={pdfAccess.Admissions})");
        }
        check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))) == originalHash,
            "TIFF worker: PNG and JPEG batches retain source bytes");
    }

    private sealed class CountingAccess(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitConversionAsync(confirmed, token); }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedTiffConversion confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitConversionAsync(confirmed, token); }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImagePdf confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitConversionAsync(confirmed, token); }
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) =>
            throw new InvalidOperationException("Unexpected PNG optimization admission.");
    }

    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "pages.tiff");
        var source = new TiffSourceFacts(Guid.NewGuid(), path, new('A', 64), 100,
            [new(0, 32, 24, 1, false, null, null), new(1, 32, 24, 1, true, null, null)]);
        source.Validate();
        var settings = new BatchSettings("convert", new ToolSettings(ReplaceOriginals: true));
        var batchId = Guid.NewGuid();
        check(OutputNames.Create(path, "convert", "png", pageNumber: 1) == "pages - Page 001.png" &&
            OutputNames.Create(path, "convert", "jpg", 2, pageNumber: 2) == "pages - Page 002 (2).jpg",
            "TIFF: numbered PNG and JPEG names retain collision suffixes");
        var png = TiffConversionPlan.Create(batchId, [source], new(TiffTarget.Png), settings).Confirm();
        check(png.Plan.HasExecutableItems && !png.Plan.NeedsMatte, "TIFF: PNG plan retains both pages without background choice");
        var jpeg = TiffConversionPlan.Create(batchId, [source], new(TiffTarget.Jpeg), settings);
        check(jpeg.NeedsMatte, "TIFF: transparent JPEG page requires explicit batch background");
        try { jpeg.Confirm(); check(false, "TIFF JPEG plan accepted no background."); }
        catch (InvalidDataException) { check(true, "TIFF: unconfirmed JPEG background is refused"); }
        check(TiffConversionPlan.Create(batchId, [source], new(TiffTarget.Jpeg, 0xffffff), settings).Confirm().Plan.Options.MatteRgb == 0xffffff,
            "TIFF: one chosen background binds the batch");
        var unsupported = source with { Pages = source.Pages.SetItem(1, source.Pages[1] with { UnsupportedReason = "Unsupported TIFF compression." }) };
        check(TiffConversionPlan.Create(batchId, [unsupported], new(TiffTarget.Png), settings).HasExecutableItems &&
            !TiffConversionPlan.Create(batchId, [unsupported], new(TiffTarget.Pdf), settings).HasExecutableItems,
            "TIFF: image pages permit partial work while PDF requires every page");
        var extreme = source with { Pages = source.Pages.SetItem(0, source.Pages[0] with
            { Width = 100, Height = 100, Resolution = new ImageResolution(1, 1, 2, 10) }) };
        check(extreme.Pages[0].GetUnsupportedReason(TiffTarget.Pdf) is not null &&
            extreme.Pages[0].GetUnsupportedReason(TiffTarget.Png) is null &&
            !TiffConversionPlan.Create(batchId, [extreme], new(TiffTarget.Pdf), settings).HasExecutableItems &&
            TiffConversionPlan.Create(batchId, [extreme], new(TiffTarget.Png), settings).HasExecutableItems,
            "TIFF: extreme physical DPI refuses PDF while retaining PNG eligibility");
        var probe = new WorkerCommand(1, Guid.NewGuid(), "tiff-probe", TiffProbe: new(source.ItemId, source.Path));
        probe.Validate();
        try { (probe with { PdfFile = new(source.ItemId, path) }).Validate(); check(false, "Mixed TIFF/PDF payload accepted."); }
        catch (InvalidDataException) { check(true, "TIFF: worker rejects unrelated request payloads"); }
        try { (probe with { Command = "image-probe" }).Validate(); check(false, "TIFF payload reached image command."); }
        catch (InvalidDataException) { check(true, "TIFF: worker isolates TIFF data from other commands"); }
        var row = new FileRow(1, "convert", path, settings, "png");
        row.BeginTiff(source, false);
        var copy = new PublicationResult(path, PublicationOutcome.CopyCreated, "Saved", Path.Combine(root, "pages - Page 001.png"),
            OutputBytes: 42);
        row.ApplyTiffPage(0, new(path, OperationState.Succeeded, "Saved", copy));
        row.ApplyTiffPage(1, new(path, OperationState.Unsupported, "Page 2 compression is unsupported."));
        check(row.SavedTiffPages == 1 && row.Result.PartialOutput && row.ResultDetails.Contains("Page 2 compression"),
            "TIFF: partial result shows unsupported page and preserves completed output");
        var retry = new FileRow(2, "convert", path, settings, "png");
        retry.ResumeTiffFrom(row);
        retry.BeginTiff(source, false);
        check(retry.CompletedTiffOutputs.SequenceEqual([(0, 42L)]) && retry.HasIncompleteTiffPages,
            "TIFF: retry carries only committed pages");
        await using var worker = new WorkerClient(Path.Combine(root, "missing-worker.exe"));
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new WindowsFileRecycler());
        var executor = new TiffConversionExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "trial.json")));
        var admission = new OperationAdmission(new(true, "synthetic"), batchId);
        var skipped = await executor.ExecuteAdmittedAsync(png, admission, null, default,
            new Dictionary<(Guid SourceId, int PageIndex), long> { [(source.ItemId, 0)] = 42, [(source.ItemId, 1)] = 42 });
        check(skipped.Pages.Count == 0 && worker.ProcessId is null, "TIFF: committed pages are skipped without starting a worker");
        var overflowingReceiptRefused = false;
        try
        {
            await executor.ExecuteAdmittedAsync(png, admission, null, default,
                new Dictionary<(Guid SourceId, int PageIndex), long> { [(source.ItemId, 0)] = long.MaxValue });
        }
        catch (InvalidDataException) { overflowingReceiptRefused = true; }
        check(overflowingReceiptRefused && worker.ProcessId is null,
            "TIFF: overflowing prior output is refused before starting a worker");
        try { await executor.ExecuteAdmittedAsync(png, admission with { BatchId = Guid.NewGuid() }, null, default); check(false, "Wrong admission accepted."); }
        catch (InvalidDataException) { check(true, "TIFF: admission from another batch is refused"); }
    }
}
