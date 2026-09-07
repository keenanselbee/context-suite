using System.Security.Cryptography;
using System.Runtime.InteropServices;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class PublicationContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var recycler = new RetainingRecycler();
        var publisher = new OutputPublisher(Path.Combine(root, "records"), recycler);
        var source = Path.Combine(root, "gamma - Copy.png");
        const string original = "original pixels are represented by this long test-only text; not a media codec";
        await File.WriteAllTextAsync(source, original);
        OutputIntent Intent(string file, string operation = "convert", string extension = "webp", bool replace = false) =>
            new(Guid.NewGuid(), file, extension, new(operation, new(replace)), replace, replace);
        async Task<OutputValidation> Candidate(OutputReservation reservation, string text = "output")
        {
            await File.WriteAllTextAsync(reservation.TemporaryPath, text);
            return new(reservation.Intent.ItemId, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(reservation.TemporaryPath))), true);
        }

        var first = await publisher.ReserveAsync(Intent(source));
        var second = await publisher.ReserveAsync(Intent(source));
        check(Path.GetFileName(first.Record.OutputPath) == "gamma - Copy - Converted.webp" &&
            Path.GetFileName(second.Record.OutputPath) == "gamma - Copy - Converted (2).webp", "publication: duplicate inputs reserve unique names");
        var copied = await publisher.PublishAsync(first, await Candidate(first));
        check(copied.Outcome == PublicationOutcome.CopyCreated && await File.ReadAllTextAsync(source) == original &&
            await File.ReadAllTextAsync(copied.OutputPath!) == "output" && recycler.Calls == 0, "publication: validated copy keeps original");
        await File.WriteAllTextAsync(second.Record.OutputPath, "unrelated racing destination");
        var collision = await publisher.PublishAsync(second, await Candidate(second));
        check(collision.Outcome == PublicationOutcome.Failed &&
            await File.ReadAllTextAsync(second.Record.OutputPath) == "unrelated racing destination", "publication: collision after reservation never overwrites");

        var changed = await publisher.ReserveAsync(Intent(source));
        var changedValidation = await Candidate(changed);
        await File.WriteAllTextAsync(source, original + " changed");
        check((await publisher.PublishAsync(changed, changedValidation)).Outcome == PublicationOutcome.Failed &&
            !File.Exists(changed.Record.OutputPath), "publication: source changed after planning rejected");
        await File.WriteAllTextAsync(source, original);
        var bad = await publisher.ReserveAsync(Intent(source));
        var staleValidation = await Candidate(bad);
        await File.AppendAllTextAsync(bad.TemporaryPath, " altered");
        check((await publisher.PublishAsync(bad, staleValidation)).Outcome == PublicationOutcome.Failed &&
            !File.Exists(bad.Record.OutputPath), "publication: changed output digest rejected");
        var wrong = await publisher.ReserveAsync(Intent(source));
        var valid = await Candidate(wrong);
        check((await publisher.PublishAsync(wrong, valid with { ItemId = Guid.NewGuid() })).Outcome == PublicationOutcome.Failed,
            "publication: mismatched validation item rejected");
        var failedWorker = await publisher.ReserveAsync(Intent(source));
        check((await publisher.AbandonAsync(failedWorker, false)).Outcome == PublicationOutcome.Failed &&
            !File.Exists(failedWorker.TemporaryPath), "publication: failed worker abandons temporary without publishing");
        var cancelled = await publisher.ReserveAsync(Intent(source));
        check((await publisher.AbandonAsync(cancelled, true)).Outcome == PublicationOutcome.Cancelled &&
            !File.Exists(cancelled.Record.OutputPath), "publication: pre-commit cancellation preserves source");
        var large = await publisher.ReserveAsync(Intent(source, "optimize", "png"));
        check((await publisher.PublishAsync(large, await Candidate(large, original + " bigger"))).Outcome == PublicationOutcome.Unchanged &&
            !File.Exists(large.Record.OutputPath), "publication: Optimize does not publish larger result");

        foreach (var exception in new Exception[] { new IOException("Injected disk full", unchecked((int)0x80070070)), new UnauthorizedAccessException("Injected permission denial") })
        {
            var failing = new OutputPublisher(Path.Combine(root, Guid.NewGuid().ToString("N")), recycler, io: new FailingMove(exception));
            var reservation = await failing.ReserveAsync(Intent(source));
            check((await failing.PublishAsync(reservation, await Candidate(reservation))).Outcome == PublicationOutcome.Failed &&
                await File.ReadAllTextAsync(source) == original, "publication: injected IO failure retains input (" + exception.GetType().Name + ")");
        }
        var occupiedIntent = Intent(source);
        var occupiedTemp = Path.Combine(root, $".context-suite-{occupiedIntent.ItemId:N}.tmp");
        await File.WriteAllTextAsync(occupiedTemp, "not owned");
        await RejectAsync(() => publisher.ReserveAsync(occupiedIntent), check, "publication: occupied temporary is rejected");
        check(await File.ReadAllTextAsync(occupiedTemp) == "not owned", "publication: failed reservation cannot delete unrelated temporary");
        await RejectAsync(() => publisher.ReserveAsync(Intent(source, replace: true)), check, "publication: production replacement gate defaults closed");
        await RejectAsync(() => publisher.ReserveAsync(Intent(source, "optimize", "webp")), check, "publication: Optimize cannot change extension");

        var preparedFailure = new OutputPublisher(Path.Combine(root, "prepared-failure"), recycler, true, new FailPreparedOnce());
        await RejectAsync(() => preparedFailure.ReserveAsync(Intent(source, "optimize", "png", true)), check,
            "publication: preparation checkpoint failure is surfaced");
        var retryPrepared = await preparedFailure.ReserveAsync(Intent(source, "optimize", "png", true));
        check((await preparedFailure.AbandonAsync(retryPrepared, true)).Outcome == PublicationOutcome.Cancelled,
            "publication: failed preparation cannot retain an in-memory source reservation");

        var caseIntent = Intent(source);
        var caseName = Path.Combine(root, "GAMMA - COPY - CONVERTED (3).WEBP");
        await File.WriteAllTextAsync(caseName, "existing uppercase output");
        var caseReservation = await publisher.ReserveAsync(caseIntent);
        check(!string.Equals(caseReservation.Record.OutputPath, caseName, StringComparison.OrdinalIgnoreCase),
            "publication: existing case-insensitive collision is respected");
        await publisher.AbandonAsync(caseReservation, true);
        var shared = Path.Combine(root, "shared");
        Directory.CreateDirectory(shared);
        var sharedReservations = new List<OutputReservation>();
        foreach (var subfolder in new[] { "one", "two" })
        {
            var folder = Path.Combine(root, subfolder);
            Directory.CreateDirectory(folder);
            var input = Path.Combine(folder, "same.png");
            await File.WriteAllTextAsync(input, original);
            sharedReservations.Add(await publisher.ReserveAsync(Intent(input) with { Settings = new("convert", new(false, shared)) }));
        }
        check(sharedReservations.Select(r => r.Record.OutputPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
            "publication: same basenames from different folders have unique reserved destinations");
        foreach (var reservation in sharedReservations) await publisher.AbandonAsync(reservation, true);
        var longDirectory = Path.Combine(root, new string('a', 65), new string('b', 65), new string('c', 65));
        Directory.CreateDirectory(longDirectory);
        var unicodeSource = Path.Combine(longDirectory, "猫 été.png");
        await File.WriteAllTextAsync(unicodeSource, original);
        var unicodeReservation = await publisher.ReserveAsync(Intent(unicodeSource));
        var unicodeResult = await publisher.PublishAsync(unicodeReservation, await Candidate(unicodeReservation));
        check(unicodeResult.IsCommitted && unicodeResult.OutputPath!.Length > 260 && File.Exists(unicodeResult.OutputPath),
            "publication: real long Unicode paths publish safely");
        var hardlink = Path.Combine(root, "linked.png");
        if (!CreateHardLink(hardlink, source, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        await RejectAsync(() => publisher.ReserveAsync(Intent(hardlink)), check, "publication: hard-linked inputs rejected");
        File.Delete(hardlink); // Exact newly-created test link, not its source.
        foreach (var alterSource in new[] { true, false })
        {
            var lateIo = new MutateBeforeCommit();
            var latePublisher = new OutputPublisher(Path.Combine(root, "late-" + Guid.NewGuid().ToString("N")), recycler, io: lateIo);
            var late = await latePublisher.ReserveAsync(Intent(source));
            lateIo.Path = alterSource ? late.Record.SourcePath : late.TemporaryPath;
            // Replacing the path simulates a concurrent editor's save, even while the old source handle is open.
            var lateResult = await latePublisher.PublishAsync(late, await Candidate(late));
            check(!lateResult.IsCommitted && !File.Exists(late.Record.OutputPath), "publication: final check rejects late " + (alterSource ? "source" : "candidate") + " change");
            await File.WriteAllTextAsync(source, original);
        }

        // Native ReplaceFile is exercised against disposable local files. This is NOT proof of Shell recycling.
        var replacement = new OutputPublisher(Path.Combine(root, "replacement-records"), recycler, replacementVerified: true);
        var same = await replacement.ReserveAsync(Intent(source, "optimize", "png", true));
        var replaced = await replacement.PublishAsync(same, await Candidate(same, "small"));
        check(replaced.Outcome == PublicationOutcome.BackupRetained && await File.ReadAllTextAsync(source) == "small" &&
            await File.ReadAllTextAsync(replaced.RetainedOriginalPath!) == original, "replacement: native same-path commit preserves exact original backup");
        check(File.Exists(replaced.RecoveryRecordPath) && recycler.Calls == 1, "replacement: recycle failure retains recovery evidence");

        await File.WriteAllTextAsync(source, original);
        var different = await replacement.ReserveAsync(Intent(source, replace: true));
        var differentResult = await replacement.PublishAsync(different, await Candidate(different));
        check(differentResult.Outcome == PublicationOutcome.OriginalRetained && File.Exists(differentResult.OutputPath) &&
            await File.ReadAllTextAsync(source) == original, "replacement: different-extension cleanup failure keeps both files");

        var locked = await replacement.ReserveAsync(Intent(source, "optimize", "png", true));
        var lockedValidation = await Candidate(locked);
        using (var held = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            check((await replacement.PublishAsync(locked, lockedValidation)).Outcome == PublicationOutcome.Failed,
                "replacement: locked source fails safely");
        check(await File.ReadAllTextAsync(source) == original, "replacement: locked-source bytes unchanged");

        var partial = new OutputPublisher(Path.Combine(root, "partial-records"), recycler, true, new PartialReplace());
        var partialReservation = await partial.ReserveAsync(Intent(source, "optimize", "png", true));
        var partialResult = await partial.PublishAsync(partialReservation, await Candidate(partialReservation));
        check(partialResult.Outcome == PublicationOutcome.Failed &&
            await File.ReadAllTextAsync(partialReservation.Record.BackupPath!) == original &&
            File.Exists(partialReservation.TemporaryPath), "replacement: partial failure retains moved original and candidate");
        File.Move(partialReservation.Record.BackupPath!, source);

        using var cancelAfterCommit = new CancellationTokenSource();
        var postCommit = new OutputPublisher(Path.Combine(root, "post-commit"), recycler, true,
            new CancelOnCommit(cancelAfterCommit));
        var cancelledCommit = await postCommit.ReserveAsync(Intent(source, "optimize", "png", true));
        var commitResult = await postCommit.PublishAsync(cancelledCommit, await Candidate(cancelledCommit), cancelAfterCommit.Token);
        check(commitResult.Outcome == PublicationOutcome.BackupRetained && commitResult.IsCommitted,
            "replacement: post-commit cancellation reports completion with retained backup");
        var summary = PublicationSummary.From([copied, collision, replaced, differentResult, commitResult]);
        check(summary.Completed == 4 && summary.Failed == 1 && summary.Warnings == 3 && summary.Cancelled == 0,
            "publication: aggregate outcomes reconcile warnings without double-counting");
        check(new OutputPublisher(Path.Combine(root, "partial-records"), recycler).FindRecoveryRecords().Count == 1 &&
            File.Exists(partialReservation.TemporaryPath), "recovery: new publisher reports journal without purging artifacts");

        var disrupted = new OutputPublisher(Path.Combine(root, "disrupted-cleanup"), new MovingThenFailingRecycler(), true);
        var disruptedReservation = await disrupted.ReserveAsync(Intent(source, "optimize", "png", true));
        var disruptedResult = await disrupted.PublishAsync(disruptedReservation, await Candidate(disruptedReservation, "x"));
        check(disruptedResult.Outcome == PublicationOutcome.RecoveryRequired && disruptedResult.RetainedOriginalPath is null &&
            File.Exists(disruptedResult.RecoveryRecordPath), "recovery: cleanup exception cannot claim a missing backup is retained");
    }

    private sealed class RetainingRecycler : IFileRecycler
    {
        public int Calls { get; private set; }
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RecycleResult(false, "Test recycler unavailable; original retained."));
        }
    }
    private sealed class FailingMove(Exception error) : PublicationIo
    {
        public override void Move(string source, string destination) { throw error; }
    }
    private sealed class FailPreparedOnce : PublicationIo
    {
        private bool _failed;
        public override void Checkpoint(PublicationStage stage)
        {
            if (stage != PublicationStage.Prepared || _failed) return;
            _failed = true;
            throw new IOException("Injected preparation failure");
        }
    }
    private sealed class MovingThenFailingRecycler : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken)
        {
            File.Move(path, path + ".test-retained"); // Keep the exact disposable original, but invalidate its reported path.
            throw new IOException("Injected failure after original moved");
        }
    }
    private sealed class PartialReplace : PublicationIo
    {
        public override void Replace(string temporary, string source, string backup)
        {
            File.Move(source, backup, overwrite: true); // Only the publisher-owned empty backup marker.
            throw new IOException("Injected ERROR_UNABLE_TO_MOVE_REPLACEMENT_2", unchecked((int)0x80070499));
        }
    }
    private sealed class CancelOnCommit(CancellationTokenSource cancellation) : PublicationIo
    {
        public override void Checkpoint(PublicationStage stage)
        {
            if (stage == PublicationStage.Committed) cancellation.Cancel();
        }
    }
    private sealed class MutateBeforeCommit : PublicationIo
    {
        public string Path { get; set; } = "";
        public override void Checkpoint(PublicationStage stage)
        {
            if (stage != PublicationStage.Publishing) return;
            var edited = Path + ".edited";
            File.WriteAllText(edited, "late modification");
            File.Move(edited, Path, overwrite: true); // Only the explicitly assigned fixture or candidate.
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string link, string existing, nint reserved);
    private static async Task RejectAsync(Func<Task> action, Action<bool, string> check, string name)
    {
        try { await action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        { check(true, name); return; }
        throw new InvalidOperationException("FAILED to reject: " + name);
    }
}
