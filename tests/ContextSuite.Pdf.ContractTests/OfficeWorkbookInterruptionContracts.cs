using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ContextSuite.Core.Office;
using ContextSuite.Private.IO;
using ContextSuite.Private.Office;

internal static class OfficeWorkbookInterruptionContracts
{
    internal static async Task CheckAsync(OfficeExportWork work, OfficeHostCompletion completion, byte[] bytes, Action<bool, string> check)
    {
        var styles = Part(bytes, "xl/styles.xml");
        var sheet = Part(bytes, "xl/worksheets/sheet1.xml");
        foreach (var phase in new[] { "hash", "worksheet" })
        {
            using var canceled = new CancellationTokenSource();
            using (var file = MediaFiles.OpenRead(work.WorkbookPath))
            using (var input = new PausedRead(file, phase, styles.Header, sheet.Data, sheet.Bytes))
            {
                var operation = OfficeWorkbookValidation.CheckContentAsync(input, work.WorkbookPath, completion, canceled.Token);
                try
                {
                    await input.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    check(!operation.IsCompleted && input.ReadBytes > 0 &&
                        (phase == "hash" ? input.HashBytes < bytes.Length : input.StylesRead && input.HashBytes == bytes.Length),
                        phase + ": validation is active after actual bytes at the selected phase");
                    canceled.Cancel();
                    input.Release.TrySetResult();
                    var stopped = false;
                    try { await operation.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException error) when (error.CancellationToken == canceled.Token) { stopped = true; }
                    check(stopped && input.ReturnedAfterCancellation && input.CanRead,
                        phase + ": production validation propagates cancellation after the instrumented read returns normally");
                }
                finally
                {
                    canceled.Cancel(); input.Release.TrySetResult();
                    try { await operation; } catch (OperationCanceledException) { }
                }
            }
            using var exclusive = new FileStream(work.WorkbookPath, FileMode.Open, FileAccess.Read, FileShare.None);
            var observed = new byte[bytes.Length]; exclusive.ReadExactly(observed);
            check(exclusive.Length == bytes.Length && observed.SequenceEqual(bytes),
                phase + ": caller releases the unchanged workbook lease after cancellation");
        }
    }

    // Independently locate a declared ZIP part; no production parser offsets or
    // mutable timing thresholds determine the cancellation point.
    private static (long Header, long Data, int Bytes) Part(byte[] bytes, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes, false));
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException("Missing retained workbook part: " + name);
        var encoded = Encoding.UTF8.GetBytes(name); var matches = new List<(long, long, int)>();
        for (var offset = 0; offset + 30 + encoded.Length <= bytes.Length; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) != 0x04034b50 ||
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 26)) != encoded.Length ||
                !bytes.AsSpan(offset + 30, encoded.Length).SequenceEqual(encoded)) continue;
            var data = offset + 30 + encoded.Length + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28));
            if (entry.CompressedLength <= 1 || data > bytes.Length - entry.CompressedLength) throw new InvalidDataException("Invalid retained part extent.");
            matches.Add((offset, data, checked((int)entry.CompressedLength)));
        }
        return matches.Single();
    }

    private sealed class PausedRead(Stream source, string phase, long stylesHeader, long sheetData, int sheetBytes) : Stream
    {
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ReadBytes { get; private set; }
        internal long HashBytes { get; private set; }
        internal bool StylesRead { get; private set; }
        internal bool ReturnedAfterCancellation { get; private set; }
        private bool _hashed;
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => source.CanSeek;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position { get => source.Position; set => source.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Expected cancellable asynchronous reads.");
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            var position = source.Position;
            if (_hashed && position == stylesHeader && buffer.Length == 30) StylesRead = true;
            var pause = !Reached.Task.IsCompleted && (phase == "hash" && !_hashed ||
                phase == "worksheet" && _hashed && StylesRead && position == sheetData && buffer.Length == sheetBytes);
            var limit = pause ? Math.Max(1, Math.Min(buffer.Length / 2, 1024)) : buffer.Length;
            var read = await source.ReadAsync(buffer[..limit], token);
            if (!_hashed) { HashBytes += read; if (read == 0) _hashed = true; }
            if (pause)
            {
                ReadBytes = read; Reached.TrySetResult();
                // Deliberately return bytes normally after cancellation. The
                // production consumer must observe its token on continuation.
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                ReturnedAfterCancellation = token.IsCancellationRequested;
            }
            return read;
        }
    }
}
