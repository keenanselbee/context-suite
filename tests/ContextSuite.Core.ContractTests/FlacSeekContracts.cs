using System.Buffers.Binary;
using System.Collections.Immutable;
using ContextSuite.Core.Audio;

internal static class FlacSeekContracts
{
    public static void Run(Action<bool, string> check)
    {
        ImmutableArray<FlacFrame> original = [new(0, 0, 16, 20), new(16, 20, 16, 30), new(32, 50, 16, 40), new(48, 90, 8, 15)];
        ImmutableArray<FlacFrame> encoded = [new(0, 0, 32, 25), new(32, 25, 24, 30)];
        var table = Table(new FlacSeekPoint(0, 0, 16), new(16, 20, 16), new(32, 50, 16), new(48, 90, 8), new(ulong.MaxValue, 99, 0));
        FlacSeekTable.RequireMatches(table, original);
        var rebuilt = FlacSeekTable.Rebuild(table, encoded);
        FlacSeekTable.RequireMatches(rebuilt, encoded);
        var points = FlacSeekTable.Read(rebuilt);
        check(points.Length == 5 && points[0] == new FlacSeekPoint(0, 0, 32) && points[1] == new FlacSeekPoint(32, 25, 24) &&
            points.Skip(2).All(point => point.Sample == ulong.MaxValue), "FLAC seeking: changed boundaries coalesce to unique preceding frames and preserve reserved slots");
        check(FlacSeekTable.Rebuild([], encoded).Length == 0, "FLAC seeking: empty table retains empty structure");
        check(FlacSeekTable.Read(Table(new FlacSeekPoint(ulong.MaxValue, ulong.MaxValue, ushort.MaxValue))).Length == 1, "FLAC seeking: placeholder fields are undefined");
        Reject(() => FlacSeekTable.Read(new byte[17]), "partial point");
        Reject(() => FlacSeekTable.Read(new byte[(FlacSeekTable.MaximumPoints + 1) * 18]), "point budget");
        Reject(() => FlacSeekTable.Read(Table(new FlacSeekPoint(16, 20, 16), new(0, 0, 16))), "descending samples");
        Reject(() => FlacSeekTable.Read(Table(new FlacSeekPoint(0, 0, 16), new(0, 20, 16))), "duplicate sample");
        Reject(() => FlacSeekTable.Read(Table(new FlacSeekPoint(0, 20, 16), new(16, 0, 16))), "descending offsets");
        Reject(() => FlacSeekTable.Read(Table(new FlacSeekPoint(ulong.MaxValue, 0, 0), new(16, 20, 16))), "real point after placeholder");
        Reject(() => FlacSeekTable.Read(Table(new FlacSeekPoint(0, 0, 0))), "zero frame samples");
        Reject(() => FlacSeekTable.RequireMatches(Table(new FlacSeekPoint(16, 21, 16)), original), "stale byte offset");
        Reject(() => FlacSeekTable.RequireMatches(Table(new FlacSeekPoint(16, 20, 15)), original), "wrong frame size");
        Reject(() => FlacSeekTable.RequireMatches(Table(new FlacSeekPoint(17, 20, 16)), original), "not first sample of frame");
        Reject(() => FlacSeekTable.Rebuild(Table(new FlacSeekPoint(56, 0, 16)), encoded), "seek beyond decoded end");
        Reject(() => FlacSeekTable.Rebuild(table, []), "missing index");
        Reject(() => FlacSeekTable.ValidateFrames(original, 104, 56), "truncated audio extent");
        Reject(() => FlacSeekTable.ValidateFrames(original, 106, 56), "trailing unindexed audio");
        Reject(() => FlacSeekTable.ValidateFrames(original, 105, 57), "wrong declared sample count");
        Reject(() => FlacSeekTable.ValidateFrames([new(1, 0, 56, 105)], 105, 56), "missing initial sample");
        Reject(() => FlacSeekTable.ValidateFrames([new(0, 0, 32, 25), new(31, 25, 25, 30)], 55, 56), "overlapping sample ranges");
        Reject(() => FlacSeekTable.ValidateFrames([new(0, 0, 32, 25), new(32, 26, 24, 29)], 55, 56), "gap between encoded frames");
        Reject(() => FlacSeekTable.ValidateFrames([new(0, 0, 8, 20), new(8, 20, 48, 30)], 50, 56), "short nonfinal frame");
        void Reject(Action action, string name)
        {
            try { action(); check(false, "FLAC seeking: " + name); }
            catch (InvalidDataException) { check(true, "FLAC seeking: " + name); }
        }
    }

    internal static byte[] Table(params FlacSeekPoint[] points)
    {
        var data = new byte[points.Length * 18];
        for (var index = 0; index < points.Length; index++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(index * 18), points[index].Sample);
            BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(index * 18 + 8), points[index].Offset);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(index * 18 + 16), points[index].Samples);
        }
        return data;
    }
}
