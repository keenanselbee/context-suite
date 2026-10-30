using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Core.Tiff;

namespace ContextSuite.Private.Tiff;

internal sealed record TiffDirectory(TiffPageFacts Facts, ushort Photometric, byte[]? IccProfile)
{
    // WIC can silently accept a truncated final IFD and lose pages or DPI. Walk
    // the complete classic-TIFF directory and all value/strip extents first.
    internal static ImmutableArray<TiffDirectory> Read(byte[] bytes, CancellationToken token)
    {
        if (bytes.LongLength is < 8 or > TiffSourceFacts.MaximumFileBytes)
            throw new ImageFailureException(ImageFailure.ResourceLimit);
        var little = bytes.AsSpan(0, 2).SequenceEqual("II"u8);
        if (!little && !bytes.AsSpan(0, 2).SequenceEqual("MM"u8)) throw new NotSupportedException("Not a TIFF image.");
        ushort Short(int at) => little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2)) : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at, 2));
        uint Long(int at) => little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at, 4));
        void Extent(ulong offset, ulong length)
        {
            if (offset > (ulong)bytes.Length || length > (ulong)bytes.Length - offset)
                throw new InvalidDataException("TIFF directory or data is truncated.");
        }
        if (Short(2) != 42) throw new NotSupportedException("Only classic TIFF is supported; BigTIFF requires a separate reader.");
        var next = Long(4);
        var seen = new HashSet<uint>();
        var pages = ImmutableArray.CreateBuilder<TiffDirectory>();
        long pixels = 0;
        while (next != 0)
        {
            token.ThrowIfCancellationRequested();
            if (pages.Count >= TiffSourceFacts.MaximumPages) throw new ImageFailureException(ImageFailure.ResourceLimit);
            if (next < 8 || (next & 1) != 0 || !seen.Add(next)) throw new InvalidDataException("TIFF directory chain is invalid or cyclic.");
            Extent(next, 2);
            var count = Short((int)next);
            if (count is 0 or > 256) throw new InvalidDataException("TIFF directory is empty or too complex.");
            Extent(next, 2UL + (ulong)count * 12 + 4);
            var tags = new Dictionary<ushort, (ushort Type, uint Count, int At)>();
            for (var i = 0; i < count; i++)
            {
                var entry = checked((int)next + 2 + i * 12);
                var tag = Short(entry); var type = Short(entry + 2); var amount = Long(entry + 4);
                var unit = type switch { 1 or 2 or 6 or 7 => 1u, 3 or 8 => 2u, 4 or 9 or 11 or 13 => 4u, 5 or 10 or 12 => 8u, _ => 0u };
                if (unit == 0 || amount == 0 || !tags.TryAdd(tag, (type, amount, 0)))
                    throw new InvalidDataException("TIFF directory has unknown types, empty or duplicate fields.");
                var length = (ulong)unit * amount;
                var offset = length <= 4 ? (uint)(entry + 8) : Long(entry + 8);
                Extent(offset, length);
                if (length > 4 && offset < 8) throw new InvalidDataException("TIFF value overlaps its header.");
                tags[tag] = (type, amount, checked((int)offset));
            }
            uint[] Values(ushort tag, uint[]? fallback = null)
            {
                if (!tags.TryGetValue(tag, out var field)) return fallback ?? throw new InvalidDataException("TIFF is missing a required field.");
                if (field.Type is not (3 or 4) || field.Count > 16384)
                    throw new InvalidDataException("TIFF numeric field has an invalid type or length.");
                var values = new uint[field.Count];
                for (var i = 0; i < values.Length; i++) values[i] = field.Type == 3 ? Short(field.At + i * 2) : Long(field.At + i * 4);
                return values;
            }
            uint One(ushort tag, uint? fallback = null)
            {
                var values = Values(tag, fallback is { } value ? [value] : null);
                if (values.Length != 1) throw new InvalidDataException("TIFF scalar field contains multiple values.");
                return values[0];
            }
            var width = One(256); var height = One(257);
            if (width is 0 or > 16384 || height is 0 or > 16384 || (ulong)width * height > TiffSourceFacts.MaximumPagePixels)
                throw new ImageFailureException(ImageFailure.ResourceLimit);
            pixels += (long)width * height;
            if (pixels > TiffSourceFacts.MaximumDocumentPixels) throw new ImageFailureException(ImageFailure.ResourceLimit);
            var photo = One(262); var samples = One(277, 1); var bits = Values(258, [1]);
            var compression = One(259, 1); var orientation = One(274, 1);
            if (orientation is < 1 or > 8 || samples is 0 or > 16 || bits.Length != samples)
                throw new InvalidDataException("TIFF orientation or sample declaration is invalid.");
            var extra = Values(338, []);
            var format = Values(339, [1]);
            string? unsupported = null;
            if (format.Any(value => value != 1) || format.Length != 1 && format.Length != samples ||
                photo > 2 || photo <= 1 && (samples != 1 || bits[0] is not (1 or 8)) ||
                photo == 2 && (samples is not (3 or 4) || bits.Any(value => value != 8)) ||
                samples == 4 && (extra.Length != 1 || extra[0] is not (1 or 2)) || samples != 4 && extra.Length != 0)
                unsupported = "This TIFF color, precision or alpha variant has not been verified. Use ordinary bilevel, 8-bit gray, RGB or RGBA TIFF.";
            if (compression is not (1 or 3 or 4 or 5 or 8 or 32773) || compression is 3 or 4 && (samples != 1 || bits[0] != 1))
                unsupported ??= "This TIFF compression is not supported. Use uncompressed, LZW, ZIP, PackBits or bilevel CCITT Group 3/4.";
            if (One(284, 1) != 1 || tags.Keys.Any(tag => tag is 322 or 323 or 324 or 325 or 330))
                unsupported ??= "Tiled, separate-plane and sub-image TIFFs require a separately verified reader.";
            if ((One(254, 0) & ~2u) != 0 || One(255, 1) == 2 ||
                tags.Keys.Any(tag => tag is 301 or 318 or 319 or 529 or 530 or 531 or 532 or 34680 or 37724))
                unsupported ??= "This TIFF has an unverified color transform, reduced image or auxiliary representation.";
            if (One(266, 1) is not (1 or 2) || One(317, 1) is not (1 or 2) ||
                One(317, 1) == 2 && (bits.Any(value => value != 8) || compression is not (5 or 8)))
                unsupported ??= "This TIFF bit order or prediction variant has not been verified.";
            if (!tags.ContainsKey(324))
            {
                var rows = One(278, uint.MaxValue);
                if (rows == 0) throw new InvalidDataException("TIFF strips have zero rows.");
                var offsets = Values(273); var lengths = Values(279);
                var strips = ((ulong)height + rows - 1) / rows;
                if (offsets.Length != lengths.Length || (ulong)offsets.Length != strips)
                    throw new InvalidDataException("TIFF strips do not cover the declared page.");
                for (var strip = 0; strip < offsets.Length; strip++)
                {
                    if (offsets[strip] < 8 || lengths[strip] == 0) throw new InvalidDataException("TIFF strip has no data.");
                    Extent(offsets[strip], lengths[strip]);
                    if (compression == 1)
                    {
                        var stripRows = Math.Min((ulong)rows, height - (ulong)strip * rows);
                        var rowBytes = ((ulong)width * (ulong)bits.Sum(value => (long)value) + 7) / 8;
                        if (lengths[strip] < rowBytes * stripRows) throw new InvalidDataException("Uncompressed TIFF strip is incomplete.");
                    }
                }
            }
            ImageResolution? resolution = null;
            if (tags.ContainsKey(282) || tags.ContainsKey(283))
            {
                (uint N, uint D) Rational(ushort tag)
                {
                    if (!tags.TryGetValue(tag, out var value) || value.Type != 5 || value.Count != 1 || Long(value.At) == 0 || Long(value.At + 4) == 0)
                        throw new InvalidDataException("TIFF resolution must contain two complete positive rationals.");
                    var n = Long(value.At); var d = Long(value.At + 4); var gcd = Gcd(n, d);
                    return (n / gcd, d / gcd);
                }
                var x = Rational(282); var y = Rational(283); var unit = One(296, 2);
                var divisor = (ulong)x.D / Gcd(x.D, y.D) * y.D;
                var xn = (ulong)x.N * (divisor / x.D); var yn = (ulong)y.N * (divisor / y.D);
                if (unit is < 1 or > 3 || divisor > uint.MaxValue || xn > uint.MaxValue || yn > uint.MaxValue)
                    unsupported ??= "This TIFF resolution cannot be represented accurately by the current output policy.";
                else resolution = new((uint)xn, (uint)yn, (ushort)unit, (uint)divisor);
            }
            byte[]? profile = null;
            if (tags.TryGetValue(34675, out var icc))
            {
                if (icc.Type is not (1 or 7) || icc.Count is < 132 or > 4 * 1024 * 1024)
                    throw new InvalidDataException("TIFF ICC profile has an invalid type or size.");
                profile = bytes.AsSpan(icc.At, checked((int)icc.Count)).ToArray();
            }
            var facts = new TiffPageFacts(pages.Count, width, height, orientation, samples == 4, resolution,
                profile is null ? null : Convert.ToHexString(SHA256.HashData(profile)), unsupported);
            facts.Validate();
            pages.Add(new(facts, checked((ushort)photo), profile));
            next = Long(checked((int)next + 2 + count * 12));
        }
        if (pages.Count == 0) throw new InvalidDataException("TIFF contains no image pages.");
        return pages.ToImmutable();
    }

    private static uint Gcd(uint a, uint b)
    {
        while (b != 0) { var remainder = a % b; a = b; b = remainder; }
        return a;
    }
}
