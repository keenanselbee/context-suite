using System.Diagnostics;
using System.Security.Cryptography;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;

namespace ContextSuite.Core.ContractTests;

internal static partial class PublicationContracts
{
    public static async Task JournalLocksAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "journal-locks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var transient in new[] { true, false })
        {
            var source = Path.Combine(root, transient ? "transient.png" : "persistent.png");
            await File.WriteAllTextAsync(source, "Original fixture bytes");
            using var io = new LockedJournalMove(transient);
            var recycler = new RetainingRecycler();
            var publisher = new OutputPublisher(Path.Combine(root, "records"), recycler, io: io);
            var reservation = await publisher.ReserveAsync(new(Guid.NewGuid(), source, "webp", new("convert", new())));
            io.RecordPath = reservation.RecordPath;
            await File.WriteAllTextAsync(reservation.TemporaryPath, "Validated candidate bytes");
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(reservation.TemporaryPath)));
            var elapsed = Stopwatch.StartNew();
            var result = await publisher.PublishAsync(reservation, new(reservation.Intent.ItemId, hash, true));
            if (transient) await io.Release;
            check(io.Locked && result.Outcome == PublicationOutcome.CopyCreated && recycler.Calls == 0 &&
                await File.ReadAllTextAsync(source) == "Original fixture bytes" &&
                await File.ReadAllTextAsync(result.OutputPath!) == "Validated candidate bytes",
                "publication journal: locked record never changes a published copy or original");
            check(transient ? !result.CleanupWarning && result.RecoveryRecordPath is null &&
                !File.Exists(reservation.RecordPath) && !File.Exists(reservation.RecordPath + ".tmp") :
                result.CleanupWarning && result.RecoveryRecordPath == reservation.RecordPath &&
                File.Exists(reservation.RecordPath) && File.Exists(reservation.RecordPath + ".tmp") && elapsed.Elapsed < TimeSpan.FromSeconds(10),
                transient ? "publication journal: delayed reader release completes quietly" :
                    "publication journal: persistent lock retains recovery evidence with a bounded warning");
        }
    }

    private sealed class LockedJournalMove(bool transient) : PublicationIo, IDisposable
    {
        private FileStream? _reader;
        public string RecordPath { get; set; } = "";
        public bool Locked { get; private set; }
        public Task Release { get; private set; } = Task.CompletedTask;
        public override void Move(string source, string destination)
        {
            base.Move(source, destination);
            _reader = new FileStream(RecordPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Locked = true;
            if (transient) Release = ReleaseAsync();
        }
        private async Task ReleaseAsync()
        {
            await Task.Delay(250);
            _reader!.Dispose();
        }
        public void Dispose() => _reader?.Dispose();
    }
}
