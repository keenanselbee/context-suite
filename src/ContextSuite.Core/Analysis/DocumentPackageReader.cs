using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ContextSuite.Core.Analysis;

// A narrow ZIP32 reader for document declarations. No extraction, recursion or
// reads of unrelated payloads. All allocation limits precede attacker-sized reads.
internal sealed class DocumentPackageReader(Stream stream, int headerBytes, CancellationToken cancellationToken)
{
    internal const int MaximumEntries = 4096;
    internal const int MaximumPartBytes = 256 * 1024;
    private const int MaximumDirectoryBytes = 1024 * 1024;
    private const int MaximumReadBytes = 4 * 1024 * 1024;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<(long Start, long End)> _ranges = [(0, headerBytes)];
    private long _directoryOffset;
    private int _expandedBytes;
    public int BytesRead { get; private set; }
    public int Count => _entries.Count;
    public bool Contains(string name) => _entries.ContainsKey(name);

    public int InspectedBytes
    {
        get
        {
            long total = 0, end = 0;
            foreach (var range in _ranges.OrderBy(range => range.Start))
            {
                total += Math.Max(0, range.End - Math.Max(end, range.Start));
                end = Math.Max(end, range.End);
            }
            return checked((int)total);
        }
    }

    public async Task InitializeAsync()
    {
        if (!stream.CanSeek || !stream.CanRead || stream.Length < 22) throw new InvalidDataException("Unavailable ZIP directory.");
        var tailOffset = Math.Max(0, stream.Length - 65557);
        var tail = await ReadAsync(tailOffset, checked((int)(stream.Length - tailOffset)));
        var end = -1;
        for (var index = tail.Length - 22; index >= 0; index--)
        {
            if (U32(tail, index) == 0x06054b50 && index + 22 + U16(tail, index + 20) == tail.Length)
            { end = index; break; }
        }
        if (end < 0) throw new InvalidDataException("Unavailable ZIP directory.");
        var count = U16(tail, end + 10);
        var size = U32(tail, end + 12);
        _directoryOffset = U32(tail, end + 16);
        if (U16(tail, end + 4) != 0 || U16(tail, end + 6) != 0 || U16(tail, end + 8) != count ||
            count > MaximumEntries || size > MaximumDirectoryBytes || _directoryOffset + size != tailOffset + end)
            throw new InvalidDataException("Split, ZIP64 or oversized ZIP directory is outside the analysis limit.");
        var directory = await ReadAsync(_directoryOffset, (int)size);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position + 46 > directory.Length || U32(directory, position) != 0x02014b50)
                throw new InvalidDataException("Invalid ZIP directory entry.");
            var nameLength = U16(directory, position + 28);
            var next = position + 46 + nameLength + U16(directory, position + 30) + U16(directory, position + 32);
            if (next > directory.Length || nameLength is 0 or > 1024 || U16(directory, position + 34) != 0)
                throw new InvalidDataException("Invalid ZIP directory entry.");
            var flags = U16(directory, position + 8);
            var rawName = directory.AsSpan(position + 46, nameLength).ToArray();
            // Relevant package names are ASCII. Non-UTF8 legacy names remain opaque
            // and cannot turn into an ASCII declaration name through codepage decoding.
            var name = (flags & 0x800) != 0 ? new UTF8Encoding(false, true).GetString(rawName) : Encoding.Latin1.GetString(rawName);
            if (!names.Add(name)) throw new InvalidDataException("Ambiguous duplicate ZIP entry names.");
            _entries.Add(name, new(rawName, flags, U16(directory, position + 10), U32(directory, position + 16),
                U32(directory, position + 20), U32(directory, position + 24), U32(directory, position + 42)));
            position = next;
        }
        if (position != directory.Length) throw new InvalidDataException("Unexpected ZIP directory records.");
    }

    public async Task<byte[]> ReadPartAsync(string name)
    {
        if (!_entries.TryGetValue(name, out var entry)) throw new InvalidDataException("Required document part is missing.");
        if ((entry.Flags & ~0x080e) != 0 || entry.Method is not (0 or 8) ||
            entry.Compressed > MaximumPartBytes || entry.Expanded > MaximumPartBytes ||
            entry.Offset + 30L > _directoryOffset || _expandedBytes + entry.Expanded > 1024 * 1024)
            throw new InvalidDataException("Encrypted or oversized document part is outside the analysis limit.");
        var local = await ReadAsync(entry.Offset, 30);
        if (U32(local, 0) != 0x04034b50 || U16(local, 6) != entry.Flags || U16(local, 8) != entry.Method ||
            U16(local, 26) != entry.Name.Length || ((entry.Flags & 8) == 0 &&
            (U32(local, 14) != entry.Crc || U32(local, 18) != entry.Compressed || U32(local, 22) != entry.Expanded)))
            throw new InvalidDataException("Conflicting ZIP part declarations.");
        var dataOffset = entry.Offset + 30L + U16(local, 26) + U16(local, 28);
        if (dataOffset + entry.Compressed > _directoryOffset) throw new InvalidDataException("Invalid ZIP part extent.");
        var localName = await ReadAsync(entry.Offset + 30L, entry.Name.Length);
        if (!localName.AsSpan().SequenceEqual(entry.Name)) throw new InvalidDataException("Conflicting ZIP part name.");
        var compressed = await ReadAsync(dataOffset, (int)entry.Compressed);
        byte[] expanded;
        if (entry.Method == 0) expanded = compressed;
        else
        {
            using var source = new MemoryStream(compressed, writable: false);
            await using var inflater = new DeflateStream(source, CompressionMode.Decompress);
            expanded = new byte[checked((int)entry.Expanded)];
            await inflater.ReadExactlyAsync(expanded, cancellationToken);
            if (await inflater.ReadAsync(new byte[1], cancellationToken) != 0)
                throw new InvalidDataException("Document part exceeds its declared size.");
        }
        if (expanded.Length != entry.Expanded) throw new InvalidDataException("Conflicting ZIP part size.");
        uint crc = uint.MaxValue;
        foreach (var value in expanded)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
        }
        if (~crc != entry.Crc) throw new InvalidDataException("Document part checksum mismatch.");
        _expandedBytes += expanded.Length;
        cancellationToken.ThrowIfCancellationRequested();
        return expanded;
    }

    private async Task<byte[]> ReadAsync(long offset, int length)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (offset < 0 || length < 0 || offset > stream.Length - length || BytesRead + length > MaximumReadBytes)
            throw new InvalidDataException("Document package read limit exceeded.");
        var bytes = new byte[length];
        stream.Position = offset;
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        BytesRead += length;
        _ranges.Add((offset, offset + length));
        return bytes;
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private sealed record Entry(byte[] Name, ushort Flags, ushort Method, uint Crc, uint Compressed, uint Expanded, uint Offset);
}
