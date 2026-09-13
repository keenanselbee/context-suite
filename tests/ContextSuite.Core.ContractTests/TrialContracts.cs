using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;

namespace ContextSuite.Core.ContractTests;

internal static class TrialContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "trial-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "Access", "trial.json");
        var clock = new TestClock(new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
        var store = new LocalTrialStore(path, clock);
        var facts = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(root, "fixture.png"), new string('A', 64), 100,
            ImageFormat.Png, 10, 10, 8, 1, false, false, "sRGB", []);
        var confirmed = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.WebP), new("convert", new()))
            .Confirm(false, false, false);
        check((await store.ReadStatusAsync()).State == LocalTrialState.NotStarted && !Directory.Exists(root),
            "trial: reading status/preview does not create storage or start trial");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { await store.AdmitAsync(confirmed, cancellation.Token); throw new InvalidOperationException("Admitted cancelled trial"); }
            catch (OperationCanceledException) { check(!File.Exists(path), "trial: cancelled admission cannot start trial"); }
        }
        var first = await store.AdmitAsync(confirmed);
        var start = clock.Utc;
        check(first.IsAllowed && first.Status.ExpiresUtc == start.AddHours(168) && File.Exists(path), "trial: first confirmed conversion atomically starts 168 hours");
        var firstBytes = await File.ReadAllBytesAsync(path);
        clock.Advance(TimeSpan.FromHours(1));
        var restarted = new LocalTrialStore(path, clock);
        check((await restarted.ReadStatusAsync()).ExpiresUtc == first.Status.ExpiresUtc, "trial: restart retains initial expiry");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(!(await store.AdmitAsync(confirmed)).IsAllowed, "trial: locked destination refuses admission");
        check((await File.ReadAllBytesAsync(path)).SequenceEqual(firstBytes), "trial: failed save retains previous record bytes");
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => new LocalTrialStore(path, clock).AdmitAsync(confirmed)));
        check(concurrent.All(a => a.IsAllowed && a.Status.ExpiresUtc == first.Status.ExpiresUtc), "trial: concurrent admissions serialize without resetting start");
        clock.Utc = clock.Utc.ToOffset(TimeSpan.FromHours(-7));
        check((await store.AdmitAsync(confirmed)).IsAllowed, "trial: UTC policy ignores local timezone offset");
        clock.Advance(TimeSpan.FromHours(167) - TimeSpan.FromTicks(1));
        var lastMoment = await store.AdmitAsync(confirmed);
        check(lastMoment.IsAllowed, "trial: last tick before expiry admits work");
        clock.Advance(TimeSpan.FromTicks(1));
        check(!(await store.AdmitAsync(confirmed)).IsAllowed && (await store.ReadStatusAsync()).State == LocalTrialState.Expired,
            "trial: exact 168-hour boundary blocks new work");
        check(lastMoment.IsAllowed, "trial: existing admission remains valid after expiry");
        clock.Utc -= TimeSpan.FromHours(5);
        check((await store.ReadStatusAsync()).State == LocalTrialState.Unavailable, "trial: large rollback unavailable");
        clock.Utc += TimeSpan.FromHours(5);
        check((await store.ReadStatusAsync()).State == LocalTrialState.Expired, "trial: corrected clock cannot restore expired trial");

        var legacyPath = Path.Combine(root, "legacy-trial.json");
        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, StartedUtc = start, LastObservedUtc = start.AddDays(4)
        }));
        var legacyClock = new TestClock(start.AddDays(5));
        var legacy = new LocalTrialStore(legacyPath, legacyClock);
        var extended = await legacy.ReadStatusAsync();
        check(extended.State == LocalTrialState.Active && extended.ExpiresUtc == start.AddDays(7),
            "trial: previous three-day record receives seven days total from original start");
        check((await legacy.AdmitAsync(confirmed)).IsAllowed, "trial: day-five legacy trial can admit work");
        legacyClock.Advance(TimeSpan.FromDays(2));
        check(!(await new LocalTrialStore(legacyPath, legacyClock).AdmitAsync(confirmed)).IsAllowed,
            "trial: upgraded legacy record still expires seven days after original start");

        var smallPath = Path.Combine(root, "small", "trial.json");
        var smallClock = new TestClock(start);
        var small = new LocalTrialStore(smallPath, smallClock);
        await small.AdmitAsync(confirmed);
        smallClock.Advance(TimeSpan.FromHours(1));
        smallClock.Utc -= TimeSpan.FromMinutes(4);
        check((await small.AdmitAsync(confirmed)).IsAllowed, "trial: small correction clamped to monotonic elapsed time");
        using (var json = JsonDocument.Parse(await File.ReadAllBytesAsync(smallPath)))
            check(json.RootElement.GetProperty("LastObservedUtc").GetDateTimeOffset() == start.AddHours(1), "trial: clamped observation never moves backwards");
        smallClock.Utc -= TimeSpan.FromMinutes(2);
        check(!(await small.AdmitAsync(confirmed)).IsAllowed, "trial: repeated small rollbacks exceeding tolerance denied");
        var restartClock = new TestClock(start);
        check((await new LocalTrialStore(smallPath, restartClock).ReadStatusAsync()).State == LocalTrialState.Unavailable,
            "trial: persisted high-water mark catches rollback after restart");

        foreach (var malformed in new[] { "", "{", "{}", "null", "[]", "{\"SchemaVersion\":99}",
            "{\"SchemaVersion\":1,\"StartedUtc\":\"2026-09-06T12:00:00Z\",\"LastObservedUtc\":\"2020-01-01T00:00:00Z\"}", new string('x', 4097) })
        {
            var invalidPath = Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(invalidPath, malformed);
            var invalid = new LocalTrialStore(invalidPath, new TestClock(start));
            check((await invalid.ReadStatusAsync()).State == LocalTrialState.Unavailable && !(await invalid.AdmitAsync(confirmed)).IsAllowed &&
                await File.ReadAllTextAsync(invalidPath) == malformed, "trial: invalid/unknown record never renewed or overwritten");
        }
        var blockedPath = Path.Combine(root, "not-a-folder");
        await File.WriteAllTextAsync(blockedPath, "fixture");
        check(!(await new LocalTrialStore(Path.Combine(blockedPath, "trial.json"), new TestClock(start)).AdmitAsync(confirmed)).IsAllowed,
            "trial: unusable storage cannot start trial");
        var disappearing = new LocalTrialStore(smallPath, new TestClock(start.AddHours(1)));
        await disappearing.ReadStatusAsync();
        File.Delete(smallPath); // Only this test's known disposable record.
        check((await disappearing.ReadStatusAsync()).State == LocalTrialState.Unavailable && !(await disappearing.AdmitAsync(confirmed)).IsAllowed,
            "trial: missing record observed within same app is not silently renewed");
    }

    private sealed class TestClock(DateTimeOffset initial) : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = initial;
        private long _ticks;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan elapsed) { Utc += elapsed; _ticks += elapsed.Ticks; }
    }
}
