using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Activation;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Core.ContractTests;

internal static class ImagePdfDirectContracts
{
    public static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use new direct image-PDF evidence.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Check(bool pass, string name) { check(pass, "Image PDF direct: " + name); checks.Add(name); }
        var paths = new[] { "common.bmp", "alpha.tga", "orientation-6.jpg" }.Select(name => Path.Combine(root, name)).ToArray();
        foreach (var path in paths) File.Copy(Path.Combine(fixtures, Path.GetFileName(path)), path);
        var hashes = paths.Select(Hash).ToArray();
        var request = ActivationParser.Parse(Encoding.UTF8.GetBytes($"ContextSuiteActivation/1\nrequestId={Guid.NewGuid():D}\noperation=convert\naction=pdf\npathCount=3\n" + string.Join("\n", paths.Select(path => "path=" + path))));
        Check(request.IsImagePdfConversion && request.IsQuickAction && request.Paths.SequenceEqual(paths), "bounded activation retains explicit PDF target and selection order");
        var access = new Access(); var prompts = 0;
        await using (var vm = Create("success", access))
        {
            vm.ImagePdfOrderRequested += Reverse;
            var quiet = new QuietWorkflow(); var started = 0; var finished = 0; var sound = false;
            vm.QuickBatchStarted += (r, rows) => { started++; quiet.Begin(r.RequestId, true, DateTimeOffset.UtcNow); };
            vm.QuickBatchCompleted += (r, rows) => { finished++; sound = quiet.Complete(r.RequestId, rows.Select(row => row.Result).ToArray()); };
            vm.Admit(request); await vm.WaitForIdleAsync();
            var row = vm.Rows.Single();
            Check(row.Result.State == OperationState.Succeeded && row.Name == "3 images → PDF" &&
                row.ReviewedImagePdf!.Pages.Select(page => page.Source.Path).SequenceEqual(paths.Reverse()) &&
                Path.GetFileName(row.OutputPath) == "orientation-6 - Combined.pdf", "one row and PDF copy use the reviewed first image and exact order");
            Check(prompts == 1 && access.Admissions == 1 && started == 1 && finished == 1 && sound && !quiet.NeedsAttention && !vm.HasProblems,
                "one prompt, admission and quiet completion cover all images");
            Check(row.ResultDetails.Contains("1. " + paths[2]) && row.ResultDetails.Contains("3. " + paths[0]) && !vm.Summary.Contains("No files changed") &&
                row.Result.Publication!.SourceBytes == paths.Sum(path => new FileInfo(path).Length), "details identify every page and aggregate source size");
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [paths[0]])); await vm.WaitForIdleAsync();
            Check(prompts == 1 && vm.Rows.Last().Result.State == OperationState.Succeeded && Path.GetFileName(vm.Rows.Last().OutputPath) == "common - Converted.pdf",
                "single image converts directly without order review");
            Check(!vm.CanRetry && vm.RetryFailed().Contains("No files"), "completed documents are never retried");
        }
        var failedAccess = new Access(); var io = new FailMoves { Remaining = 1 }; var beforePrompts = prompts;
        await using (var vm = Create("retry", failedAccess, io))
        {
            vm.ImagePdfOrderRequested += Reverse;
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", paths.ToImmutableArray())); await vm.WaitForIdleAsync();
            var failed = vm.Rows.Single(); var order = failed.ReviewedImagePdf!.Pages.Select(page => page.Source.Path).ToArray();
            Check(failed.Result.State == OperationState.Failed && vm.CanRetry && !failed.HasOutput, "failed publication retains whole-document retry");
            var target = Path.Combine(root, "retry-new-folder"); Directory.CreateDirectory(target);
            vm.Settings = vm.Settings with { Convert = new(OutputDirectory: target, ReplaceOriginals: true) };
            vm.RetryFailed(); vm.RetryFailed(); await vm.WaitForIdleAsync();
            var retried = vm.DisplayRows.Single();
            Check(vm.Rows.Count == 2 && failed.WasRetried && retried.Result.State == OperationState.Succeeded && failedAccess.Admissions == 2 &&
                prompts == beforePrompts + 1 && retried.ReviewedImagePdf!.Pages.Select(page => page.Source.Path).SequenceEqual(order),
                "retry queues once, keeps reviewed order and does not prompt again");
            Check(Path.GetDirectoryName(retried.OutputPath) == target && !vm.CanRetry && paths.Select(Hash).SequenceEqual(hashes),
                "retry uses current Convert folder while preserving all originals under overwrite preference");
        }
        var changed = Path.Combine(root, "changed.bmp"); File.Copy(paths[0], changed);
        var changedAccess = new Access();
        await using (var vm = Create("changed", changedAccess, new FailMoves { Remaining = 1 }))
        {
            vm.ImagePdfOrderRequested += Reverse;
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [paths[1], changed])); await vm.WaitForIdleAsync();
            var content = File.ReadAllBytes(changed); content[^1] ^= 1; File.WriteAllBytes(changed, content);
            vm.RetryFailed(); await vm.WaitForIdleAsync();
            Check(vm.Rows.Last().Result.State == OperationState.Failed && vm.Rows.Last().Status.Contains("changed since") &&
                changedAccess.Admissions == 1 && !vm.CanRetry, "changed secondary input requires a new command before another admission");
        }
        var cancelAccess = new Access();
        await using (var vm = Create("cancel", cancelAccess))
        {
            Func<ImagePdfOrderViewModel, CancellationToken, Task<ConfirmedImagePdf?>> cancel = (_, _) => Task.FromResult<ConfirmedImagePdf?>(null);
            vm.ImagePdfOrderRequested += cancel;
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [paths[0], paths[1]])); await vm.WaitForIdleAsync();
            Check(vm.Rows.Single().Result.State == OperationState.Cancelled && cancelAccess.Admissions == 0 && !vm.Rows.Single().HasOutput,
                "cancelled review creates no output or admission");
            vm.ImagePdfOrderRequested -= cancel; vm.ImagePdfOrderRequested += Reverse;
            vm.RetryFailed(); await vm.WaitForIdleAsync();
            Check(vm.DisplayRows.Single().Result.State == OperationState.Succeeded && cancelAccess.Admissions == 1,
                "retry of an unconfirmed cancellation still requires review");
        }
        var overlappingAccess = new Access();
        await using (var vm = Create("overlapping", overlappingAccess, new FailMoves { Remaining = 2 }))
        {
            vm.ImagePdfOrderRequested += Reverse;
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [paths[0], paths[1]]));
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [paths[0], paths[2]])); await vm.WaitForIdleAsync();
            vm.RetryFailed(); await vm.WaitForIdleAsync();
            Check(vm.Rows.Count == 4 && vm.DisplayRows.Count() == 2 && vm.DisplayRows.All(row => row.Result.State == OperationState.Succeeded) &&
                vm.DisplayRows.Select(row => row.OutputPath).Distinct().Count() == 2 && overlappingAccess.Admissions == 4,
                "overlapping selections with the same first path remain separate complete retry jobs");
        }
        var unsupported = Path.Combine(root, "document.pdf"); File.WriteAllText(unsupported, "%PDF-1.7\nnot an image");
        var unsupportedAccess = new Access(); beforePrompts = prompts;
        await using (var vm = Create("unsupported", unsupportedAccess))
        {
            vm.ImagePdfOrderRequested += Reverse;
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [paths[0], unsupported])); await vm.WaitForIdleAsync();
            Check(vm.Rows.Count == 1 && vm.HasProblems && !vm.Rows.Single().HasOutput && unsupportedAccess.Admissions == 0 && prompts == beforePrompts &&
                !Directory.GetFiles(Path.Combine(root, "unsupported"), "*.pdf").Any(), "unsupported member prevents the entire document before review and admission");
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [paths[0], paths[0]])); await vm.WaitForIdleAsync();
            Check(vm.Rows.Last().Result.State == OperationState.Unsupported && unsupportedAccess.Admissions == 0, "duplicate selected paths cannot become repeated pages silently");
        }
        var expiredAccess = new Access { Allowed = false };
        await using (var vm = Create("expired", expiredAccess))
        {
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [paths[0]])); await vm.WaitForIdleAsync();
            Check(vm.Rows.Single().Result.State == OperationState.Failed && vm.Rows.Single().Status.Contains("Activate") && vm.CanRetry && !vm.Rows.Single().HasOutput,
                "expired single-image access gives an actionable failure without saving output");
            expiredAccess.Allowed = true; vm.RetryFailed(); await vm.WaitForIdleAsync();
            Check(vm.DisplayRows.Single().Result.State == OperationState.Succeeded && expiredAccess.Admissions == 2, "activation permits a fresh whole-document admission");
        }
        await using (var missing = new MainViewModel(new WorkerClient(Path.Combine(root, "missing.exe")), publisher: new OutputPublisher(Path.Combine(root, "missing-records"), new ForbiddenRecycle()), trial: access))
        {
            var admissions = access.Admissions;
            missing.Admit(new(Guid.NewGuid(), "convert", "pdf", [paths[0], paths[1]])); await missing.WaitForIdleAsync();
            Check(missing.Rows.Single().Result.State == OperationState.Unsupported && missing.Rows.Single().Status.Contains("unavailable in this build") && access.Admissions == admissions,
                "normal payload without validator declines before probing or admission");
            var repeated = Enumerable.Repeat(paths[0], 4096).ToImmutableArray();
            var accepted = Enumerable.Range(0, 4).Select(_ => missing.Admit(new(Guid.NewGuid(), "convert", "pdf", repeated)).Accepted).ToArray();
            Check(accepted.SequenceEqual(new[] { true, true, true, false }), "queue limit counts every retained selected image despite combined rows");
            await missing.WaitForIdleAsync();
        }
        Check(paths.Select(Hash).SequenceEqual(hashes), "all original fixture hashes remain unchanged");
        File.WriteAllText(Path.Combine(root, "image-pdf-direct.json"), JsonSerializer.Serialize(new { Checks = checks, Inputs = paths }, new JsonSerializerOptions { WriteIndented = true }));

        MainViewModel Create(string name, Access a, PublicationIo? faults = null)
        {
            var folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
            return new(new WorkerClient(executable, Path.Combine(folder, "worker")),
                new() { Convert = new(OutputDirectory: folder, ReplaceOriginals: true) },
                new OutputPublisher(Path.Combine(folder, "records"), new ForbiddenRecycle(), replacementVerified: true, io: faults), a);
        }
        async Task<ConfirmedImagePdf?> Reverse(ImagePdfOrderViewModel decision, CancellationToken token)
        {
            prompts++;
            var pages = decision.Pages.Reverse().ToArray();
            for (var target = 0; target < pages.Length; target++)
            { decision.SelectedPage = pages[target]; while (decision.SelectedPage.Number > target + 1) decision.MoveUpCommand.Execute(null); }
            await decision.RefreshAccessAsync();
            ConfirmedImagePdf? result = null; decision.Confirmed += value => result = value; decision.ConfirmCommand.Execute(null); return result;
        }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class Access : IOperationAccess
    {
        public bool Allowed { get; set; } = true;
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => Task.FromResult(new OperationAccessStatus(Allowed, Allowed ? "Active" : "Activate your license to convert."));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImagePdf plan, CancellationToken token)
        { Admissions++; return Task.FromResult(new OperationAdmission(new(Allowed, Allowed ? "Active" : "Activate your license to convert."), plan.Plan.BatchId)); }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch plan, CancellationToken token) => throw new InvalidOperationException("Unexpected image admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization plan, CancellationToken token) => throw new InvalidOperationException("Unexpected optimization admission.");
    }
    private sealed class FailMoves : PublicationIo
    {
        public int Remaining { get; set; }
        public override void Move(string source, string destination)
        { if (Remaining > 0) { Remaining--; throw new IOException("Generated publication failure."); } base.Move(source, destination); }
    }
    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new InvalidOperationException("Image PDF must keep originals.");
    }
}
