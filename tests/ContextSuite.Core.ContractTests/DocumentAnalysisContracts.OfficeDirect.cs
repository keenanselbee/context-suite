using System.Collections.Immutable;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

internal static partial class DocumentAnalysisContracts
{
    internal static async Task OfficeDirectContractsAsync(string scratch, Action<bool, string> check)
    {
        var stage = Path.GetFullPath(Path.Combine(scratch, "office-direct-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(stage);
        var originals = new Dictionary<string, byte[]>();
        foreach (var format in new[] { "docx", "xlsx", "pptx" })
        {
            var path = Path.Combine(stage, "authored." + format);
            File.WriteAllBytes(path, originals[path] = OpenXml(format));
        }
        var paths = originals.Keys.ToImmutableArray();
        var worker = Path.Combine(stage, "runtime", "ContextSuite.Worker.exe");
        var unavailable = new DirectOfficeAccess();
        await using (var vm = Create("absent", unavailable))
        {
            vm.Admit(Request(paths)); await vm.WaitForIdleAsync();
            check(vm.Rows.Count == 3 && vm.Rows.All(row => !row.IsImagePdf && row.Result.State == OperationState.Unsupported) && unavailable.Admissions == 0,
                "Office direct: missing engines report each document without access admission");
        }
        // Presence markers permit orchestration preflight only. Access always
        // denies, so these fixtures are never executable engines or native profiles.
        foreach (var name in new[] { "office-engine/ContextSuite.OfficeHost.exe", "office-engine/runtime-files.txt", "pdf-engine/qpdf.exe", "pdf-renderer/ContextSuite.PdfRenderer.exe" })
        {
            var path = Path.Combine(Path.GetDirectoryName(worker)!, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "non-executable presence marker");
        }
        var denied = new DirectOfficeAccess(); var prompts = 0; var started = 0; var completed = 0;
        await using (var vm = Create("denied", denied))
        {
            vm.OfficeCalculationRequested += _ => { prompts++; return Task.FromResult<string?>("recalculate"); };
            vm.QuickBatchStarted += (_, _) => started++;
            vm.QuickBatchCompleted += (_, _) => completed++;
            vm.Admit(Request(paths)); await vm.WaitForIdleAsync();
            check(denied.Admissions == 1 && prompts == 1 && started == 1 && completed == 1 &&
                vm.Rows.All(row => row.Result.State == OperationState.Failed && row.Status.Contains("Activate")),
                "Office direct: one calculation prompt, admission and completion cover the document batch");
            check(denied.Last!.Plan.Sources.Select(source => source.Format).SequenceEqual(new[] { "docx", "xlsx", "pptx" }) &&
                denied.Last.Plan.Sources[1].Calculation == "recalculate" && denied.Last.Plan.Output.Mode == OutputMode.SiblingCopy,
                "Office direct: explicit calculation and source order survive overwrite settings with mandatory copies");
            vm.Settings = vm.Settings with { Convert = new(OutputDirectory: Path.Combine(stage, "changed-output")) };
            vm.RetryFailed(); vm.RetryFailed(); await vm.WaitForIdleAsync();
            check(denied.Admissions == 2 && prompts == 1 && vm.Rows.Count == 6 && vm.DisplayRows.Count() == 3 &&
                denied.Last.Plan.Sources[1].Calculation == "recalculate" && denied.Last.Plan.Settings.Preferences.OutputDirectory!.EndsWith("changed-output"),
                "Office direct: retry retains calculation and uses current output settings without duplicate queuing");
        }
        foreach (var choice in new string?[] { "cached", null })
        {
            var access = new DirectOfficeAccess();
            await using var vm = Create(choice ?? "cancel", access);
            vm.OfficeCalculationRequested += _ => Task.FromResult(choice);
            vm.Admit(Request(paths)); await vm.WaitForIdleAsync();
            check(choice is null ? access.Admissions == 0 && vm.Rows.All(row => row.Result.State == OperationState.Cancelled) :
                access.Admissions == 1 && access.Last!.Plan.Sources[1].Calculation == "cached",
                "Office direct: explicit saved values or cancellation has no implicit calculation default: " + (choice ?? "cancel"));
        }
        var invalid = Path.Combine(stage, "invalid.docx"); File.WriteAllText(invalid, "Not an Office package.");
        var mismatch = Path.Combine(stage, "wrong.xlsx"); File.WriteAllBytes(mismatch, OpenXml("docx"));
        var mixed = new DirectOfficeAccess();
        await using (var vm = Create("malformed", mixed))
        {
            vm.Admit(Request([invalid, paths[0], mismatch])); await vm.WaitForIdleAsync();
            check(vm.Rows.Count(row => row.Result.State == OperationState.Unsupported) == 2 && mixed.Admissions == 1 && mixed.Last!.Plan.Sources.Length == 1,
                "Office direct: malformed and mislabeled documents do not prevent good-file admission");
        }
        var duplicate = new DirectOfficeAccess();
        await using (var vm = Create("duplicate", duplicate))
        {
            vm.Admit(Request([paths[0], paths[0]])); await vm.WaitForIdleAsync();
            check(duplicate.Admissions == 0 && vm.Rows.All(row => row.Result.State == OperationState.Unsupported),
                "Office direct: duplicate documents refuse before publication or admission");
        }
        var image = Path.Combine(stage, "image.bmp"); File.WriteAllBytes(image, [0x42, 0x4d]);
        var grouping = new DirectOfficeAccess();
        await using (var vm = Create("grouping", grouping))
        {
            vm.Admit(Request([paths[0], image, paths[1], image, paths[2]]));
            check(vm.Rows.Count == 4 && vm.Rows[1].IsImagePdf && vm.Rows[1].ImagePdfPaths.SequenceEqual(new[] { image, image }) &&
                vm.Rows.Where(row => !row.IsImagePdf).Select(row => row.Path).SequenceEqual(paths),
                "Office direct: mixed selection groups images once and retains independent Office rows");
            vm.CancelCommand.Execute(null); await vm.WaitForIdleAsync();
        }
        var recoveryAccess = new DirectOfficeAccess();
        var recoveryRoot = Path.Combine(stage, "review", "contexts");
        var orphan = Path.Combine(recoveryRoot, "office-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(orphan);
        await using (var vm = Create("review", recoveryAccess))
        {
            vm.Admit(Request([paths[0]])); await vm.WaitForIdleAsync();
            check(recoveryAccess.Admissions == 0 && vm.Rows.Single().Status.Contains("needs review") && vm.RecoveryNotice.Contains(recoveryRoot) && Directory.Exists(orphan),
                "Office direct: unresolved startup recovery blocks native work and retains evidence");
        }
        check(originals.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) &&
            !Directory.Exists(Path.Combine(stage, "denied", "contexts")) && !Directory.Exists(Path.Combine(stage, "denied", "publications")),
            "Office direct: denied and cancelled workflows preserve originals without contexts or reservations");

        MainViewModel Create(string name, DirectOfficeAccess access) => new(new WorkerClient(worker, Path.Combine(stage, name, "scratch")),
            new SuiteSettings { Convert = new(ReplaceOriginals: true) }, new OutputPublisher(Path.Combine(stage, name, "publications"), null!),
            access, Path.Combine(stage, name, "contexts"));
        static OperationRequest Request(ImmutableArray<string> files) => new(Guid.NewGuid(), "convert", "pdf", files);
    }

    private sealed class DirectOfficeAccess : IOperationAccess
    {
        internal int Admissions;
        internal ConfirmedOfficeConversion? Last;
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken cancellationToken = default) => Task.FromResult(new OperationAccessStatus(false, "Activate a license."));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedOfficeConversion confirmed, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Admissions++; Last = confirmed;
            return Task.FromResult(new OperationAdmission(new(false, "Activate a license."), confirmed.Plan.BatchId));
        }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected raster admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected Optimize admission.");
    }
}
