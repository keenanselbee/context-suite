using System.Text;

namespace ContextSuite.Core.Analysis;

// Count decoded text without retaining its contents or allocating individual lines.
// Format identification and structured parsing keep their smaller header budget.
public static class TextLineAnalysis
{
    public const int MaximumBytes = 256 * 1024 * 1024;
    private const int BufferBytes = 64 * 1024;

    public static async Task<FileAnalysis> AddAsync(FileAnalysis analysis, ReadOnlyMemory<byte> prefix,
        Stream remainder, CancellationToken cancellationToken, CancellationToken readToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!analysis.Facts.Any(fact => fact.Id == "text.lines-complete" && fact.Boolean == false)) return analysis;
        if (prefix.Length > HeaderAnalyzer.MaximumBytes || prefix.Length > analysis.FileBytes)
            throw new ArgumentOutOfRangeException(nameof(prefix));

        var decoder = HeaderAnalyzer.CreateTextDecoder(prefix.Span, out var skip, out _);
        var buffer = new byte[BufferBytes];
        var characters = new char[BufferBytes + 2];
        var counter = new LineCounter();
        long inspected = 0;
        var complete = false;
        var scope = "Byte limit reached";
        try
        {
            Decode(prefix.Span[skip..], false);
            inspected = prefix.Length;
            var limit = Math.Min(analysis.FileBytes, MaximumBytes);
            while (inspected < limit)
            {
                readToken.ThrowIfCancellationRequested();
                var count = await remainder.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - inspected)), readToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0) throw new IOException("The file changed during analysis. Try again after it finishes saving.");
                Decode(buffer.AsSpan(0, count), inspected + count == analysis.FileBytes);
                inspected += count;
            }
            complete = inspected == analysis.FileBytes;
            if (complete) scope = "Entire file counted";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && readToken.IsCancellationRequested)
        { scope = "Read time limit reached"; }
        catch (DecoderFallbackException)
        {
            scope = "Later content is not valid text in the detected encoding";
            analysis = analysis with { Warnings = analysis.Warnings.Add(
                "Later content could not be read as the detected text encoding. The line count covers only the validated prefix.") };
        }
        cancellationToken.ThrowIfCancellationRequested();
        return analysis with { Facts = [
            .. analysis.Facts.Where(fact => fact.Id is not ("text.lines" or "text.lines-complete" or "text.inspected" or "text.lines-scope")),
            new("text.lines", "Text", "Lines", Integer: counter.Lines, Availability: FactAvailability.Derived),
            new("text.lines-complete", "Text", "Entire file counted", Boolean: complete, Availability: FactAvailability.Derived),
            new("text.inspected", "Text", "Text bytes checked", Integer: inspected, Availability: FactAvailability.Derived),
            new("text.lines-scope", "Text", "Line count scope", Text: scope, Availability: FactAvailability.Derived)
        ] };

        void Decode(ReadOnlySpan<byte> bytes, bool flush)
        {
            decoder.Convert(bytes, characters, flush, out var used, out var count, out var finished);
            if (used != bytes.Length || !finished) throw new InvalidOperationException("Text decoding buffer is too small.");
            // Validate before counting so a malformed chunk cannot inflate a lower bound.
            foreach (var character in characters.AsSpan(0, count))
                if (char.IsControl(character) && character is not ('\r' or '\n' or '\t' or '\f'))
                    throw new DecoderFallbackException("Binary control character in text.");
            counter.Add(characters.AsSpan(0, count));
        }
    }

    private sealed class LineCounter
    {
        private long _endings;
        private bool _previousCr;
        private bool _unterminatedLine;
        public long Lines => _endings + (_unterminatedLine ? 1 : 0);

        public void Add(ReadOnlySpan<char> characters)
        {
            foreach (var character in characters)
            {
                if (character == '\r' || character == '\n' && !_previousCr) _endings++;
                _previousCr = character == '\r';
                _unterminatedLine = character is not ('\r' or '\n');
            }
        }
    }
}
