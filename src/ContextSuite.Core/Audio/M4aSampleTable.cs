using System.Buffers.Binary;

namespace ContextSuite.Core.Audio;

internal static class M4aSampleTable
{
    public const int MaximumSamples = 1000000;

    public static int Validate(List<M4aBox> boxes, IReadOnlyList<(long Start, long End)> media, ulong mediaDuration, CancellationToken token)
    {
        var sizes = M4aBoxes.One(boxes, "stsz").Span;
        M4aBoxes.FullBox(sizes, 12);
        var fixedSize = M4aBoxes.Number(sizes, 4); var samples = M4aBoxes.Number(sizes, 8);
        if (samples is 0 or > MaximumSamples || sizes.Length != 12L + (fixedSize == 0 ? samples * 4L : 0))
            throw new InvalidDataException("M4A sample sizes exceed their count or extent.");
        var timing = M4aBoxes.One(boxes, "stts").Span;
        var timeEntries = Table(timing, 8); ulong timedSamples = 0, duration = 0;
        for (var i = 0; i < timeEntries; i++)
        {
            var count = M4aBoxes.Number(timing, 8 + i * 8); var delta = M4aBoxes.Number(timing, 12 + i * 8);
            if (count == 0 || delta == 0) throw new InvalidDataException("M4A time table contains an empty run.");
            timedSamples += count; duration += (ulong)count * delta;
            if (timedSamples > samples || duration > mediaDuration) throw new InvalidDataException("M4A timing exceeds its declared samples or duration.");
        }
        if (timedSamples != samples || duration != mediaDuration) throw new InvalidDataException("M4A timing and media declarations disagree.");
        var offsetBoxes = boxes.Where(box => box.Type is "stco" or "co64").ToArray();
        if (offsetBoxes.Length != 1) throw new InvalidDataException("M4A requires one chunk-offset table.");
        var width = offsetBoxes[0].Type == "stco" ? 4 : 8;
        var offsets = offsetBoxes[0].Data.Span; var chunks = Table(offsets, width);
        var mapping = M4aBoxes.One(boxes, "stsc").Span; var mappings = Table(mapping, 12);
        if (chunks == 0 || mappings == 0 || M4aBoxes.Number(mapping, 8) != 1) throw new InvalidDataException("M4A sample-to-chunk mapping is empty or does not start at one.");
        for (var i = 0; i < mappings; i++)
        {
            var first = M4aBoxes.Number(mapping, 8 + i * 12);
            if (first == 0 || first > chunks || M4aBoxes.Number(mapping, 12 + i * 12) == 0 || M4aBoxes.Number(mapping, 16 + i * 12) != 1 ||
                (i > 0 && first <= M4aBoxes.Number(mapping, 8 + (i - 1) * 12)))
                throw new InvalidDataException("M4A chunk mapping is unordered or refers to another sample description.");
        }
        var spans = new List<(long Start, long End)>(); var run = 0; var sampleIndex = 0;
        for (var i = 0; i < chunks; i++)
        {
            token.ThrowIfCancellationRequested();
            if (run + 1 < mappings && i + 1 == M4aBoxes.Number(mapping, 8 + (run + 1) * 12)) run++;
            var count = M4aBoxes.Number(mapping, 12 + run * 12);
            if (count > samples - sampleIndex) throw new InvalidDataException("M4A chunk contains undeclared samples.");
            ulong length = 0;
            for (var j = 0; j < count; j++, sampleIndex++)
            {
                var size = fixedSize == 0 ? M4aBoxes.Number(sizes, 12 + sampleIndex * 4) : fixedSize;
                if (size == 0) throw new InvalidDataException("M4A contains an empty encoded sample.");
                length += size;
            }
            var start = width == 4 ? M4aBoxes.Number(offsets, 8 + i * width) : BinaryPrimitives.ReadUInt64BigEndian(offsets[(8 + i * width)..]);
            if (length > AudioFileSource.MaximumFileBytes || start > (ulong)AudioFileSource.MaximumFileBytes - length ||
                !media.Any(region => start >= (ulong)region.Start && start + length <= (ulong)region.End))
                throw new InvalidDataException("M4A samples refer outside a local media-data atom.");
            spans.Add(((long)start, (long)(start + length)));
        }
        if (sampleIndex != samples) throw new InvalidDataException("M4A samples are absent from the chunk map.");
        spans.Sort((left, right) => left.Start.CompareTo(right.Start)); var index = 0;
        foreach (var region in media.OrderBy(region => region.Start))
        {
            var cursor = region.Start;
            while (index < spans.Count && spans[index].Start < region.End)
            {
                if (spans[index].Start != cursor) throw new NotSupportedException("M4A overlapping or unreferenced media data needs a policy.");
                cursor = spans[index++].End;
            }
            if (cursor != region.End) throw new NotSupportedException("M4A contains unreferenced media bytes.");
        }
        ValidateRoll(boxes, samples);
        return (int)samples;
    }

    private static int Table(ReadOnlySpan<byte> data, int width)
    {
        M4aBoxes.FullBox(data, 8); var count = M4aBoxes.Number(data, 4);
        if (count > MaximumSamples || data.Length != 8L + count * (long)width) throw new InvalidDataException("M4A sample table exceeds its count or extent.");
        return (int)count;
    }
    private static void ValidateRoll(List<M4aBox> boxes, uint samples)
    {
        var descriptions = boxes.Where(box => box.Type == "sgpd").ToArray(); var groups = boxes.Where(box => box.Type == "sbgp").ToArray();
        if (descriptions.Length == 0 && groups.Length == 0) return;
        if (descriptions.Length != 1 || groups.Length != 1) throw new NotSupportedException("M4A sample groups need a paired roll-recovery mapping.");
        var data = descriptions[0].Data.Span; M4aBoxes.FullBox(data, 16, 1);
        var count = M4aBoxes.Number(data, 12);
        if (!data.Slice(4, 4).SequenceEqual("roll"u8) || M4aBoxes.Number(data, 8) != 2 || count is 0 or > 32 || data.Length != 16 + count * 2)
            throw new NotSupportedException("This M4A sample-group description needs a handler.");
        for (var i = 0; i < count; i++)
            if (BinaryPrimitives.ReadInt16BigEndian(data[(16 + i * 2)..]) is < -32 or > 0) throw new NotSupportedException("M4A roll distance exceeds the reviewed AAC policy.");
        var grouping = groups[0].Data.Span; M4aBoxes.FullBox(grouping, 12);
        var entries = M4aBoxes.Number(grouping, 8);
        if (!grouping.Slice(4, 4).SequenceEqual("roll"u8) || entries > MaximumSamples || grouping.Length != 12L + entries * 8L)
            throw new NotSupportedException("This M4A sample-group mapping needs a handler.");
        ulong total = 0;
        for (var i = 0; i < entries; i++)
        {
            var run = M4aBoxes.Number(grouping, 12 + i * 8); var group = M4aBoxes.Number(grouping, 16 + i * 8);
            if (run == 0 || group > count) throw new InvalidDataException("M4A sample-group index is invalid.");
            total += run;
        }
        if (total != samples) throw new InvalidDataException("M4A roll groups do not cover the declared samples.");
    }
}
