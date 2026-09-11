using System.Buffers.Binary;
using System.Text;

namespace ContextSuite.Core.Analysis;

// Bounded CFB directory/allocation reader for selected legacy Office headers.
// No COM storage activation, extraction, payload decoding or arbitrary recursion.
internal sealed class CompoundFileReader(Stream stream, int headerBytes, CancellationToken cancellationToken)
{
    private const uint Free = 0xffffffff, End = 0xfffffffe, Fat = 0xfffffffd, Difat = 0xfffffffc;
    private const int MaximumReadBytes = 2 * 1024 * 1024, MaximumEntries = 4096, MaximumSteps = 65536;
    private readonly List<(long Start, long End)> _ranges = [(0, headerBytes)];
    private readonly Dictionary<uint, string> _owners = [];
    private readonly Dictionary<uint, uint> _miniOwners = [];
    private readonly Dictionary<uint, uint[]> _streamChains = [];
    private readonly Dictionary<string, Entry> _rootEntries = new(StringComparer.OrdinalIgnoreCase);
    private uint[] _fat = [], _miniFat = [], _miniStream = [];
    private long _sectors;
    private int _steps;
    private Entry _root = null!;
    public int MajorVersion { get; private set; }
    public int SectorBytes { get; private set; }
    public int BytesRead { get; private set; }
    public int ReachableEntries { get; private set; }
    public int RootStreams => _rootEntries.Values.Count(entry => entry.Type == 2);
    public bool Contains(string name) => _rootEntries.TryGetValue(name, out var entry) && entry.Type == 2;
    public Entry Get(string name) => Contains(name) ? _rootEntries[name] : throw new InvalidDataException("Missing root stream.");
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
        if (!stream.CanRead || !stream.CanSeek) throw new InvalidDataException("CFB analysis requires a seekable read stream.");
        var header = await ReadAsync(0, 512);
        if (!header.AsSpan(0, 8).SequenceEqual(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }) ||
            header.AsSpan(8, 16).ContainsAnyExcept((byte)0) || header.AsSpan(34, 6).ContainsAnyExcept((byte)0) ||
            U16(header, 28) != 0xfffe || U16(header, 32) != 6 || U32(header, 56) != 4096)
            throw new InvalidDataException("Unsupported compound header.");
        MajorVersion = U16(header, 26);
        if (MajorVersion is not (3 or 4) || U16(header, 30) != (MajorVersion == 3 ? 9 : 12))
            throw new InvalidDataException("Unsupported compound sectors.");
        SectorBytes = MajorVersion == 3 ? 512 : 4096;
        if (stream.Length % SectorBytes != 0 || stream.Length < SectorBytes * 2L)
            throw new InvalidDataException("Incomplete compound sectors.");
        _sectors = stream.Length / SectorBytes - 1;
        var fatCount = U32(header, 44); var difatCount = U32(header, 72); var directoryCount = U32(header, 40);
        if (fatCount is < 1 or > 1024 || difatCount > 8 || directoryCount > MaximumEntries * 128 / SectorBytes ||
            MajorVersion == 3 && directoryCount != 0 || _sectors > fatCount * (long)(SectorBytes / 4))
            throw new InvalidDataException("Compound allocation declarations exceed limits.");
        var fatIds = new List<uint>();
        void AddFatIds(byte[] bytes, int offset, int count)
        {
            for (var index = 0; index < count; index++)
            {
                var id = U32(bytes, offset + index * 4);
                if (fatIds.Count < fatCount) { Claim(id, "FAT"); fatIds.Add(id); }
                else if (id != Free) throw new InvalidDataException("Excess FAT declarations.");
            }
        }
        AddFatIds(header, 76, 109);
        var difatIds = new List<uint>(); var next = U32(header, 68);
        for (var index = 0; index < difatCount; index++)
        {
            Claim(next, "DIFAT"); difatIds.Add(next);
            var sector = await ReadSectorAsync(next);
            AddFatIds(sector, 0, SectorBytes / 4 - 1); next = U32(sector, SectorBytes - 4);
        }
        if (next != End || fatIds.Count != fatCount) throw new InvalidDataException("Incomplete DIFAT chain.");
        _fat = new uint[checked((int)fatCount * (SectorBytes / 4))];
        for (var index = 0; index < fatIds.Count; index++)
        {
            var sector = await ReadSectorAsync(fatIds[index]);
            for (var item = 0; item < SectorBytes / 4; item++) _fat[index * (SectorBytes / 4) + item] = U32(sector, item * 4);
        }
        if (fatIds.Any(id => _fat[id] != Fat) || difatIds.Any(id => _fat[id] != Difat))
            throw new InvalidDataException("Inconsistent allocation-sector markers.");
        var directory = Chain(U32(header, 48), null, "directory", MaximumEntries * 128 / SectorBytes);
        if (MajorVersion == 4 && directory.Length != directoryCount) throw new InvalidDataException("Directory count mismatch.");
        var entries = new List<Entry>();
        foreach (var id in directory)
        {
            var sector = await ReadSectorAsync(id);
            for (var offset = 0; offset < SectorBytes; offset += 128) entries.Add(ParseEntry(sector.AsSpan(offset, 128), checked((uint)entries.Count)));
        }
        if (entries.Count == 0 || entries[0].Type != 5 || entries[0].Name != "Root Entry" ||
            entries[0].Left != Free || entries[0].Right != Free) throw new InvalidDataException("Invalid compound root.");
        _root = entries[0];
        var visited = new HashSet<uint> { 0 };
        var names = new Dictionary<uint, HashSet<string>>();
        var pending = new Stack<(uint Id, uint Parent, int Depth)>(); pending.Push((_root.Child, 0, 0));
        while (pending.TryPop(out var node))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.Id == Free) continue;
            if (node.Depth > 64 || node.Id >= entries.Count || !visited.Add(node.Id)) throw new InvalidDataException("Invalid directory graph.");
            var entry = entries[(int)node.Id];
            if (entry.Type is not (1 or 2) || entry.Type == 2 && entry.Child != Free) throw new InvalidDataException("Invalid directory object.");
            if (!names.TryGetValue(node.Parent, out var siblings)) names[node.Parent] = siblings = new(StringComparer.OrdinalIgnoreCase);
            if (!siblings.Add(entry.Name)) throw new InvalidDataException("Ambiguous sibling names.");
            if (node.Parent == 0) _rootEntries.Add(entry.Name, entry);
            pending.Push((entry.Left, node.Parent, node.Depth + 1)); pending.Push((entry.Right, node.Parent, node.Depth + 1));
            if (entry.Type == 1) pending.Push((entry.Child, entry.Id, node.Depth + 1));
        }
        ReachableEntries = visited.Count - 1;
        var miniFatCount = U32(header, 64);
        if (miniFatCount > 128 * 1024 / SectorBytes || _root.Size > 2 * 1024 * 1024 || _root.Size % 64 != 0)
            throw new InvalidDataException("Mini-stream analysis limit exceeded.");
        var miniFatChain = Chain(U32(header, 60), checked((long)miniFatCount * SectorBytes), "mini FAT", 128 * 1024 / SectorBytes);
        _miniFat = new uint[checked((int)miniFatCount * (SectorBytes / 4))];
        for (var index = 0; index < miniFatChain.Length; index++)
        {
            var sector = await ReadSectorAsync(miniFatChain[index]);
            for (var item = 0; item < SectorBytes / 4; item++) _miniFat[index * (SectorBytes / 4) + item] = U32(sector, item * 4);
        }
        _miniStream = Chain(_root.Start, _root.Size, "root mini-stream", MaximumSteps);
    }

    public async Task<byte[]> ReadPrefixAsync(Entry entry, int count)
    {
        if (count is < 1 or > 4096 || entry.Type != 2 || entry.Size < count) throw new InvalidDataException("Compound stream header unavailable.");
        var mini = entry.Size < 4096;
        if (!_streamChains.TryGetValue(entry.Id, out var chain))
        {
            if (!mini) chain = Chain(entry.Start, entry.Size, "stream " + entry.Id, MaximumSteps);
            else
            {
                var ids = new List<uint>(); var next = entry.Start;
                var expected = (entry.Size + 63) / 64;
                for (var index = 0; index < expected; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++_steps > MaximumSteps || next >= _miniFat.Length || (long)next * 64 + 64 > _root.Size || !_miniOwners.TryAdd(next, entry.Id))
                        throw new InvalidDataException("Invalid mini-sector chain.");
                    ids.Add(next); next = _miniFat[next];
                }
                if (next != End) throw new InvalidDataException("Overlong mini-sector chain.");
                chain = ids.ToArray();
            }
            _streamChains.Add(entry.Id, chain);
        }
        var result = new byte[count]; var written = 0;
        foreach (var id in chain)
        {
            var take = Math.Min(count - written, mini ? 64 : SectorBytes);
            long offset;
            if (mini)
            {
                var logical = (long)id * 64;
                offset = ((long)_miniStream[checked((int)(logical / SectorBytes))] + 1) * SectorBytes + logical % SectorBytes;
            }
            else offset = ((long)id + 1) * SectorBytes;
            (await ReadAsync(offset, take)).CopyTo(result, written); written += take;
            if (written == count) break;
        }
        if (written != count) throw new InvalidDataException("Truncated stream prefix.");
        return result;
    }

    private Entry ParseEntry(ReadOnlySpan<byte> bytes, uint id)
    {
        var type = bytes[66];
        if (type == 0) return new(id, "", 0, Free, Free, Free, End, 0);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[64..]);
        if (type is not (1 or 2 or 5) || bytes[67] > 1 || length is < 2 or > 64 || length % 2 != 0 ||
            bytes[length - 2] != 0 || bytes[length - 1] != 0) throw new InvalidDataException("Invalid compound directory entry.");
        var name = new UnicodeEncoding(false, false, true).GetString(bytes[..(length - 2)]);
        if (name.Length == 0 || name.IndexOfAny(['\0', '/', '\\', ':', '!']) >= 0) throw new InvalidDataException("Invalid compound name.");
        // Older v3 writers leave garbage in the high DWORD; MS-CFB recommends ignoring it.
        var size = MajorVersion == 3 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[120..]) : BinaryPrimitives.ReadUInt64LittleEndian(bytes[120..]);
        if (size > (ulong)stream.Length || type == 1 && size != 0) throw new InvalidDataException("Invalid compound stream size.");
        return new(id, name, type, BinaryPrimitives.ReadUInt32LittleEndian(bytes[68..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[72..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[76..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[116..]), (long)size);
    }

    private uint[] Chain(uint first, long? bytes, string owner, int maximumSectors)
    {
        var expected = bytes.HasValue ? (bytes.Value + SectorBytes - 1) / SectorBytes : -1;
        if (expected > maximumSectors) throw new InvalidDataException("Compound chain budget exceeded.");
        var ids = new List<uint>(); var next = first;
        while (next != End)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++_steps > MaximumSteps || ids.Count >= maximumSectors || expected >= 0 && ids.Count >= expected)
                throw new InvalidDataException("Compound chain exceeds its declaration or budget.");
            Claim(next, owner); ids.Add(next); next = _fat[next];
        }
        if (expected >= 0 && ids.Count != expected) throw new InvalidDataException("Truncated compound chain.");
        return ids.ToArray();
    }
    private void Claim(uint id, string owner)
    {
        if (id >= _sectors || !_owners.TryAdd(id, owner)) throw new InvalidDataException("Invalid or shared compound sector.");
    }
    private Task<byte[]> ReadSectorAsync(uint id) => ReadAsync(((long)id + 1) * SectorBytes, SectorBytes);
    private async Task<byte[]> ReadAsync(long offset, int count)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (offset < 0 || offset > stream.Length - count || count < 0 || count > MaximumReadBytes - BytesRead)
            throw new InvalidDataException("Compound read budget or range exceeded.");
        var bytes = new byte[count]; stream.Position = offset; var read = 0;
        while (read < count)
        {
            var amount = await stream.ReadAsync(bytes.AsMemory(read), cancellationToken);
            if (amount == 0) throw new EndOfStreamException();
            BytesRead += amount;
            var start = offset + read; var end = start + amount;
            if (_ranges[^1].End == start) _ranges[^1] = (_ranges[^1].Start, end);
            else _ranges.Add((start, end));
            read += amount;
        }
        return bytes;
    }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    internal sealed record Entry(uint Id, string Name, byte Type, uint Left, uint Right, uint Child, uint Start, long Size);
}
