using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Core.ContractTests;

internal static class ImagePdfOrderContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var first = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(scratch, "B", "same.png"), new('A', 64), 100,
            ImageFormat.Png, 4, 3, 8, 1, false, false, "sRGB", []);
        var second = first with { ItemId = Guid.NewGuid(), Path = Path.Combine(scratch, "A", "same.png") };
        var third = first with { ItemId = Guid.NewGuid(), Path = Path.Combine(scratch, "C", "last.png") };
        var plan = ImagePdfPlan.Create(Guid.NewGuid(), [first, second, third], new("convert", new(ReplaceOriginals: true)));
        var access = new Access(); using var model = new ImagePdfOrderViewModel(plan, access);
        check(model.Pages.Select(page => page.Page.Source).SequenceEqual(new[] { first, second, third }) && model.SelectedPage == model.Pages[0],
            "PDF order: captured request order is preserved without filename sorting");
        check(model.Pages[0].Name == model.Pages[1].Name && model.Pages[0].AccessibleName != model.Pages[1].AccessibleName &&
            model.Pages[0].AccessibleName.Contains("Page 1") && model.Pages[1].Folder.EndsWith("A"),
            "PDF order: duplicate names have visible folders and numbered accessible names");
        check(!model.CanConfirm && !model.MoveUpCommand.CanExecute(null) && model.MoveDownCommand.CanExecute(null),
            "PDF order: confirmation waits for access and movement stops at first boundary");
        await model.RefreshAccessAsync();
        check(!model.CanConfirm && model.LicenseActionLabel.Contains("Activate") && access.Admissions == 0,
            "PDF order: expiry has an activation action without admission");
        model.SelectedPage = model.Pages[2]; model.MoveDownCommand.Execute(null);
        check(model.SelectedPage.Page.Source == third && model.Pages[2].Number == 3 && !model.MoveDownCommand.CanExecute(null),
            "PDF order: last boundary cannot discard or duplicate a page");
        model.MoveUpCommand.Execute(null); model.MoveUpCommand.Execute(null);
        var ordered = new[] { third, first, second };
        check(model.Pages.Select(page => page.Page.Source).SequenceEqual(ordered) && model.SelectedPage == model.Pages[0] &&
            model.Pages.Select(page => page.Number).SequenceEqual(new[] { 1, 2, 3 }),
            "PDF order: moving preserves every page identity and selection with contiguous numbers");
        check(model.Destination == Path.Combine(scratch, "C", "last - Combined.pdf"),
            "PDF order: reviewed first page changes destination folder and output stem");
        access.Allowed = true; await model.RefreshAccessAsync();
        check(model.CanConfirm && model.Pages.Select(page => page.Page.Source).SequenceEqual(ordered) && model.Message.Contains("originals are kept"),
            "PDF order: activation enables the exact edited order with mandatory copies");
        access.Allowed = false; await model.RefreshAccessAsync();
        check(!model.CanConfirm && model.Pages.Select(page => page.Page.Source).SequenceEqual(ordered),
            "PDF order: deactivation preserves edited pages and blocks conversion");
        access.Allowed = true; await model.RefreshAccessAsync();
        ConfirmedImagePdf? confirmed = null; var count = 0;
        model.Confirmed += value => { confirmed = value; count++; };
        model.ConfirmCommand.Execute(null); model.ConfirmCommand.Execute(null); model.MoveDownCommand.Execute(null);
        check(count == 1 && confirmed!.Plan.Pages.Select(page => page.Source).SequenceEqual(ordered) &&
            ReferenceEquals(confirmed.Plan.Settings, plan.Settings) && confirmed.Plan.Output.Mode == OutputMode.SiblingCopy && !model.CanEdit && access.Admissions == 0,
            "PDF order: one immutable confirmation retains settings and blocks later edits without admitting work");
        using var destination = new ImagePdfOrderViewModel(ImagePdfPlan.Create(Guid.NewGuid(), [first, second],
            new("convert", new(OutputDirectory: Path.Combine(scratch, "saved")))), access);
        destination.MoveDownCommand.Execute(null);
        check(destination.Destination == Path.Combine(scratch, "saved", "same - Combined.pdf"),
            "PDF order: saved output folder overrides first-image folder");
        using var longName = new ImagePdfOrderViewModel(ImagePdfPlan.Create(Guid.NewGuid(),
            [first with { Path = Path.Combine(scratch, new string('x', 250) + ".png") }, second], plan.Settings), access);
        await longName.RefreshAccessAsync();
        check(!longName.CanConfirm && longName.Message.Contains("Move another image to page 1"),
            "PDF order: an overlong output name blocks confirmation with a usable remedy");
        longName.MoveDownCommand.Execute(null);
        check(longName.CanConfirm && longName.Destination.EndsWith("same - Combined.pdf"),
            "PDF order: moving a valid first image resolves the output name without losing pages");
        var delayed = new DelayedAccess(); using var racing = new ImagePdfOrderViewModel(plan, delayed);
        var older = racing.RefreshAccessAsync(); var newer = racing.RefreshAccessAsync();
        delayed.Replies[1].SetResult(new(false, "New expired status")); await newer;
        delayed.Replies[0].SetResult(new(true, "Old active status")); await older;
        check(!racing.CanConfirm && racing.Message == "New expired status", "PDF order: late stale license response cannot enable conversion");
        var waiting = racing.RefreshAccessAsync(); racing.Dispose(); await waiting;
        check(!racing.CanConfirm && !racing.CanEdit && delayed.Replies[2].Task.IsCanceled,
            "PDF order: closing cancels access and prevents late confirmation");
        await using var worker = new WorkerClient(Path.Combine(scratch, "missing-worker.exe"));
        await using var main = new MainViewModel(worker, trial: access);
        var prompts = 0;
        main.ImagePdfOrderRequested += async (decision, token) =>
        {
            prompts++; decision.SelectedPage = decision.Pages[2]; decision.MoveUpCommand.Execute(null); decision.MoveUpCommand.Execute(null);
            await decision.RefreshAccessAsync();
            ConfirmedImagePdf? result = null; decision.Confirmed += value => result = value; decision.ConfirmCommand.Execute(null); return result;
        };
        var single = await main.ConfirmImagePdfOrderAsync(ImagePdfPlan.Create(Guid.NewGuid(), [first], plan.Settings), default);
        check(prompts == 0 && single!.Plan.Pages.Length == 1 && access.Admissions == 0,
            "PDF order orchestration: one image bypasses the prompt without starting worker or admission");
        var many = await main.ConfirmImagePdfOrderAsync(plan, default);
        check(prompts == 1 && many!.Plan.Pages.Select(page => page.Source).SequenceEqual(ordered) && access.Admissions == 0,
            "PDF order orchestration: several images use one focused review and return reviewed order");
        await using var cancelWorker = new WorkerClient(Path.Combine(scratch, "missing-worker.exe"));
        await using var cancelMain = new MainViewModel(cancelWorker, trial: access);
        ImagePdfOrderViewModel? cancelledModel = null;
        cancelMain.ImagePdfOrderRequested += (decision, token) => { cancelledModel = decision; return Task.FromResult<ConfirmedImagePdf?>(null); };
        check(await cancelMain.ConfirmImagePdfOrderAsync(plan, default) is null && cancelledModel?.CanEdit == false && access.Admissions == 0,
            "PDF order orchestration: cancellation disposes the review without creating work");
    }

    private class Access : IOperationAccess
    {
        public bool Allowed { get; set; }
        public int Admissions { get; private set; }
        public virtual Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => Task.FromResult(new OperationAccessStatus(Allowed, Allowed ? "Active" : "Activate your license to convert."));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImagePdf plan, CancellationToken token)
        { Admissions++; throw new InvalidOperationException("Page-order review must not admit work."); }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch plan, CancellationToken token) => throw new InvalidOperationException("Unexpected admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization plan, CancellationToken token) => throw new InvalidOperationException("Unexpected admission.");
    }
    private sealed class DelayedAccess : Access
    {
        public List<TaskCompletionSource<OperationAccessStatus>> Replies { get; } = [];
        public override Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default)
        {
            var result = new TaskCompletionSource<OperationAccessStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            Replies.Add(result); token.Register(() => result.TrySetCanceled(token)); return result.Task;
        }
    }
}
