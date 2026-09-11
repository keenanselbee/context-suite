using System.Buffers.Binary;
using System.Text;

// Independently authored CFB layout writer for tiny structural fixtures, not documents for rendering.
internal sealed class CompoundFixture
{
    private const uint End = 0xfffffffe, Free = 0xffffffff;
    public required byte[] Bytes { get; init; }
    public required int SectorSize { get; init; }
    public required uint[] FatIds { get; init; }
    public required uint[] MiniFatIds { get; init; }
    public required Dictionary<string, int> DirectoryOffsets { get; init; }
    public required Dictionary<string, int> DataOffsets { get; init; }
    public required Dictionary<string, uint> Starts { get; init; }
    public int FatOffset(uint sector) => checked(((int)FatIds[sector / (SectorSize / 4)] + 1) * SectorSize + (int)(sector % (SectorSize / 4)) * 4);

    public static CompoundFixture Create(IEnumerable<(string Name, byte[] Data)> input, int sectorSize = 512, bool small = true, bool fragmented = false, bool nested = false)
    {
        var items = input.Select(item => (item.Name, Data: !small && item.Name != "Current User" && item.Data.Length < 4096
            ? item.Data.Concat(new byte[4096 - item.Data.Length]).ToArray() : item.Data)).ToArray();
        var sectors = new List<byte[]>(); var fat = new List<uint>();
        uint[] Allocate(byte[] bytes)
        {
            var ids = new List<uint>();
            for (var offset = 0; offset < bytes.Length; offset += sectorSize)
            {
                var id = (uint)sectors.Count; ids.Add(id);
                var sector = new byte[sectorSize]; bytes.AsSpan(offset, Math.Min(sectorSize, bytes.Length - offset)).CopyTo(sector);
                sectors.Add(sector); fat.Add(End);
                if (fragmented) { sectors.Add(new byte[sectorSize]); fat.Add(Free); }
            }
            for (var index = 0; index + 1 < ids.Count; index++) fat[(int)ids[index]] = ids[index + 1];
            return ids.ToArray();
        }
        var directory = new byte[((items.Length + (nested ? 2 : 1)) * 128 + sectorSize - 1) / sectorSize * sectorSize];
        var directoryIds = Allocate(directory);
        var miniData = new List<byte>(); var miniFat = new List<uint>();
        var starts = new Dictionary<string, uint>(); var normal = new Dictionary<string, uint[]>();
        foreach (var item in items)
        {
            if (item.Data.Length >= 4096) { var ids = Allocate(item.Data); normal.Add(item.Name, ids); starts.Add(item.Name, ids[0]); }
            else
            {
                starts.Add(item.Name, item.Data.Length == 0 ? End : (uint)miniFat.Count);
                for (var offset = 0; offset < item.Data.Length; offset += 64)
                {
                    var sector = new byte[64]; item.Data.AsSpan(offset, Math.Min(64, item.Data.Length - offset)).CopyTo(sector);
                    miniData.AddRange(sector); miniFat.Add(offset + 64 < item.Data.Length ? (uint)miniFat.Count + 1 : End);
                }
            }
        }
        var miniIds = Allocate(miniData.ToArray());
        var miniFatBytes = new byte[(miniFat.Count * 4 + sectorSize - 1) / sectorSize * sectorSize];
        Array.Fill(miniFatBytes, (byte)255);
        for (var index = 0; index < miniFat.Count; index++) Set32(miniFatBytes, index * 4, miniFat[index]);
        var miniFatIds = Allocate(miniFatBytes);
        var baseCount = sectors.Count; var fatCount = 0; var difatCount = 0;
        while (true)
        {
            var nextFat = (baseCount + fatCount + difatCount + sectorSize / 4 - 1) / (sectorSize / 4);
            var nextDifat = Math.Max(0, (nextFat - 109 + sectorSize / 4 - 2) / (sectorSize / 4 - 1));
            if (nextFat == fatCount && nextDifat == difatCount) break;
            fatCount = nextFat; difatCount = nextDifat;
        }
        var fatIds = Enumerable.Range(baseCount, fatCount).Select(index => (uint)index).ToArray();
        var difatIds = Enumerable.Range(baseCount + fatCount, difatCount).Select(index => (uint)index).ToArray();
        foreach (var id in fatIds.Concat(difatIds)) { sectors.Add(new byte[sectorSize]); fat.Add(id < baseCount + fatCount ? 0xfffffffd : 0xfffffffc); }
        foreach (var id in fatIds) Array.Fill(sectors[(int)id], (byte)255);
        for (var index = 0; index < fat.Count; index++) Set32(sectors[(int)fatIds[index / (sectorSize / 4)]], index % (sectorSize / 4) * 4, fat[index]);
        for (var index = 0; index < difatIds.Length; index++)
        {
            var sector = sectors[(int)difatIds[index]]; Array.Fill(sector, (byte)255);
            for (var slot = 0; slot < sectorSize / 4 - 1; slot++)
            {
                var fatIndex = 109 + index * (sectorSize / 4 - 1) + slot;
                if (fatIndex < fatIds.Length) Set32(sector, slot * 4, fatIds[fatIndex]);
            }
            Set32(sector, sectorSize - 4, index + 1 < difatIds.Length ? difatIds[index + 1] : End);
        }
        void Entry(int id, string name, byte type, uint start, long size)
        {
            var offset = id * 128; Encoding.Unicode.GetBytes(name + '\0').CopyTo(directory, offset);
            Set16(directory, offset + 64, (ushort)((name.Length + 1) * 2)); directory[offset + 66] = type; directory[offset + 67] = 1;
            Set32(directory, offset + 68, Free); Set32(directory, offset + 72, Free); Set32(directory, offset + 76, Free);
            Set32(directory, offset + 116, start); BinaryPrimitives.WriteUInt64LittleEndian(directory.AsSpan(offset + 120), (ulong)size);
        }
        Entry(0, "Root Entry", 5, miniIds.Length > 0 ? miniIds[0] : End, miniData.Count);
        for (var index = 0; index < items.Length; index++) Entry(index + 1, items[index].Name, 2, starts[items[index].Name], items[index].Data.Length);
        var ordered = Enumerable.Range(1, items.Length).OrderBy(index => items[index - 1].Name.Length)
            .ThenBy(index => items[index - 1].Name.ToUpperInvariant(), StringComparer.Ordinal).ToArray();
        var levels = new Dictionary<int, int>();
        uint Tree(int begin, int end, int depth)
        {
            if (begin == end) return Free;
            var middle = (begin + end) / 2; var id = ordered[middle]; levels[id] = depth;
            Set32(directory, id * 128 + 68, Tree(begin, middle, depth + 1));
            Set32(directory, id * 128 + 72, Tree(middle + 1, end, depth + 1));
            return (uint)id;
        }
        var children = Tree(0, ordered.Length, 0);
        if (levels.Count > 1)
            foreach (var pair in levels.Where(pair => pair.Value == levels.Values.Max())) directory[pair.Key * 128 + 67] = 0;
        if (nested)
        {
            Entry(items.Length + 1, "Embedded", 1, 0, 0);
            Set32(directory, (items.Length + 1) * 128 + 76, children); Set32(directory, 76, (uint)items.Length + 1);
        }
        else Set32(directory, 76, children);
        for (var index = 0; index < directoryIds.Length; index++) directory.AsSpan(index * sectorSize, sectorSize).CopyTo(sectors[(int)directoryIds[index]]);
        var bytes = new byte[(sectors.Count + 1) * sectorSize];
        new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }.CopyTo(bytes, 0);
        Set16(bytes, 24, 0x003e); Set16(bytes, 26, (ushort)(sectorSize == 512 ? 3 : 4)); Set16(bytes, 28, 0xfffe);
        Set16(bytes, 30, (ushort)(sectorSize == 512 ? 9 : 12)); Set16(bytes, 32, 6);
        Set32(bytes, 40, sectorSize == 512 ? 0 : (uint)directoryIds.Length); Set32(bytes, 44, (uint)fatIds.Length);
        Set32(bytes, 48, directoryIds[0]); Set32(bytes, 56, 4096);
        Set32(bytes, 60, miniFatIds.Length > 0 ? miniFatIds[0] : End); Set32(bytes, 64, (uint)miniFatIds.Length);
        Set32(bytes, 68, difatIds.Length > 0 ? difatIds[0] : End); Set32(bytes, 72, (uint)difatIds.Length);
        for (var index = 0; index < 109; index++) Set32(bytes, 76 + index * 4, index < fatIds.Length ? fatIds[index] : Free);
        for (var index = 0; index < sectors.Count; index++) sectors[index].CopyTo(bytes, (index + 1) * sectorSize);
        var offsets = new Dictionary<string, int>(); var dataOffsets = new Dictionary<string, int>();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index]; var logical = (index + 1) * 128;
            offsets.Add(item.Name, checked(((int)directoryIds[logical / sectorSize] + 1) * sectorSize + logical % sectorSize));
            if (item.Data.Length == 0) continue;
            var miniOffset = checked((int)starts[item.Name] * 64);
            dataOffsets.Add(item.Name, item.Data.Length >= 4096 ? checked(((int)starts[item.Name] + 1) * sectorSize) :
                checked(((int)miniIds[miniOffset / sectorSize] + 1) * sectorSize + miniOffset % sectorSize));
        }
        return new() { Bytes = bytes, SectorSize = sectorSize, FatIds = fatIds, MiniFatIds = miniFatIds,
            DirectoryOffsets = offsets, DataOffsets = dataOffsets, Starts = starts };
    }
    public static void Set16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    public static void Set32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
