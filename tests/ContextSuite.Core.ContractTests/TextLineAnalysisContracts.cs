using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;

internal static class TextLineAnalysisContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetFullPath(scratch), "text-lines-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var encoding in new Encoding[] {
            new UTF8Encoding(false), new UTF8Encoding(true), new UnicodeEncoding(false, true),
            new UnicodeEncoding(true, true), new UTF32Encoding(false, true), new UTF32Encoding(true, true)
        })
        {
            var content = new string('a', 65535) + "\r\nbeta\r\ngamma\ndelta\r\u20ac\U0001f600";
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray();
            var prefix = bytes.AsMemory(0, HeaderAnalyzer.MaximumBytes);
            var report = HeaderAnalyzer.Analyze("sample.txt", prefix.Span, bytes.Length);
            using var stream = new FragmentedStream(bytes) { Position = prefix.Length };
            var result = await TextLineAnalysis.AddAsync(report, prefix, stream, default, default);
            check(Number(result, "text.lines") == 5 && Complete(result) && Number(result, "text.inspected") == bytes.Length,
                "text streaming: fragmented CRLF and Unicode count exactly for " + encoding.WebName + "/" + encoding.GetPreamble().Length);
            check(result.InspectedBytes == HeaderAnalyzer.MaximumBytes && result.Identity == report.Identity,
                "text streaming: full line count does not expand structural identification");
        }

        foreach (var suffix in new[] { "\u20ac\nend", "\U0001f600\nend" })
        {
            var bytes = Encoding.UTF8.GetBytes(new string('a', 65535) + suffix);
            var result = await CountAsync(bytes);
            check(Number(result, "text.lines") == 2 && Complete(result), "text streaming: UTF-8 scalar crosses header boundary");
        }
        foreach (var ending in new[] { "", "\r", "\n", "\r\n" })
        {
            var result = await CountAsync(Encoding.UTF8.GetBytes(new string('a', 65536) + "\nlast" + ending));
            check(Number(result, "text.lines") == 2 && Complete(result), "text streaming: final line ending has no phantom line");
        }

        var prefixBytes = Enumerable.Repeat((byte)'\n', HeaderAnalyzer.MaximumBytes).ToArray();
        var huge = HeaderAnalyzer.Analyze("huge.txt", prefixBytes, (long)TextLineAnalysis.MaximumBytes + 1);
        using (var stream = new RepeatingStream(huge.FileBytes - prefixBytes.Length))
        {
            var result = await TextLineAnalysis.AddAsync(huge, prefixBytes, stream, default, default);
            check(!Complete(result) && Number(result, "text.lines") == TextLineAnalysis.MaximumBytes &&
                Number(result, "text.inspected") == TextLineAnalysis.MaximumBytes &&
                stream.BytesRead == TextLineAnalysis.MaximumBytes - prefixBytes.Length,
                "text streaming: byte cap stops reads and retains an accurate lower bound");
        }
        using (var deadline = new CancellationTokenSource())
        using (var stream = new RepeatingStream(huge.FileBytes, deadline))
        {
            var result = await TextLineAnalysis.AddAsync(huge, prefixBytes, stream, default, deadline.Token);
            check(!Complete(result) && Number(result, "text.lines") == 2 * HeaderAnalyzer.MaximumBytes &&
                Number(result, "text.inspected") == 2 * HeaderAnalyzer.MaximumBytes &&
                result.Facts.Any(fact => fact.Id == "text.lines-scope" && fact.Text!.Contains("time")),
                "text streaming: expired read budget preserves completed chunks");
            var card = AnalysisSnapshot.Create(result).Highlights.Single(item => item.Label == "Lines");
            check(card.Value.EndsWith('+') && card.Detail.Contains((2 * HeaderAnalyzer.MaximumBytes).ToString("N0")),
                "text streaming: partial card reports text bytes rather than header bytes");
        }
        using (var cancellation = new CancellationTokenSource())
        using (var stream = new RepeatingStream(huge.FileBytes, cancellation))
        {
            try
            {
                await TextLineAnalysis.AddAsync(huge, prefixBytes, stream, cancellation.Token, cancellation.Token);
                check(false, "text streaming: user cancellation must propagate");
            }
            catch (OperationCanceledException) { check(true, "text streaming: user cancellation propagates during reading"); }
        }
        foreach (var tail in new byte[][] { [0xff], [0], [0xe2, 0x82] })
        {
            var result = await CountAsync(prefixBytes.Concat(tail).ToArray());
            check(!Complete(result) && Number(result, "text.lines") == prefixBytes.Length &&
                Number(result, "text.inspected") == prefixBytes.Length && result.Warnings.Length > 0,
                "text streaming: malformed, binary or incomplete tail never produces an exact total");
        }
        using (var truncated = new MemoryStream())
        {
            try { await TextLineAnalysis.AddAsync(huge, prefixBytes, truncated, default, default); check(false, "text streaming: unexpected EOF must fail"); }
            catch (IOException) { check(true, "text streaming: unexpected EOF does not claim completeness"); }
        }

        var path = Path.Combine(root, "large.html");
        var html = "<!doctype html>\n" + string.Concat(Enumerable.Repeat("<p>hello</p>\r\n", 20000));
        await File.WriteAllTextAsync(path, html, new UTF8Encoding(false));
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var modified = File.GetLastWriteTimeUtc(path);
        var actual = await FileAnalysisReader.ReadAsync(path, default);
        check(Complete(actual) && Number(actual, "text.lines") == 20001 &&
            AnalysisSnapshot.Create(actual).Highlights.Single(item => item.Label == "Lines").Value == 20001.ToString("N0"),
            "text streaming: real reader and presentation show exact large HTML line count");
        var header = await FileAnalysisReader.ReadAsync(path, default, headerOnly: true);
        check(!Complete(header) && header.Facts.All(fact => fact.Id != "text.inspected"),
            "text streaming: header-only routing does not trigger full text counting");
        var after = SHA256.HashData(await File.ReadAllBytesAsync(path));
        check(before.SequenceEqual(after) && modified == File.GetLastWriteTimeUtc(path),
            "text streaming: source bytes and timestamp are unchanged");
    }

    public static async Task InspectFileAsync(string path)
    {
        var before = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        var result = await FileAnalysisReader.ReadAsync(Path.GetFullPath(path), default);
        timer.Stop();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
            result.FileBytes, Lines = Number(result, "text.lines"), Complete = Complete(result),
            TextBytes = Number(result, "text.inspected"), Milliseconds = timer.Elapsed.TotalMilliseconds,
            ManagedAllocatedBytes = GC.GetTotalAllocatedBytes(true) - before, result.Warnings
        }));
    }

    private static long? Number(FileAnalysis report, string id) => report.Facts.FirstOrDefault(fact => fact.Id == id)?.Integer;
    private static bool Complete(FileAnalysis report) => report.Facts.Any(fact => fact.Id == "text.lines-complete" && fact.Boolean == true);

    private static async Task<FileAnalysis> CountAsync(byte[] bytes)
    {
        var prefix = bytes.AsMemory(0, HeaderAnalyzer.MaximumBytes);
        using var stream = new MemoryStream(bytes) { Position = prefix.Length };
        return await TextLineAnalysis.AddAsync(HeaderAnalyzer.Analyze("sample.txt", prefix.Span, bytes.Length), prefix, stream, default, default);
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], token);
    }

    private sealed class RepeatingStream(long remaining, CancellationTokenSource? cancel = null) : Stream
    {
        public long BytesRead { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (BytesRead > 0) cancel?.Cancel();
            token.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, remaining);
            buffer.Span[..count].Fill((byte)'\n');
            remaining -= count;
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }
}
