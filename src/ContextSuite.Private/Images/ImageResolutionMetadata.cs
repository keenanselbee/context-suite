using ContextSuite.Core.Images;
using ImageMagick;
using System.Globalization;
using System.Xml.Linq;

namespace ContextSuite.Private.Images;

internal static class ImageResolutionMetadata
{
    public static ImageResolution? Read(IExifProfile? exif, ImageResolution? container, XDocument? xmp = null)
    {
        var x = exif?.GetValue(ExifTag.XResolution)?.Value;
        var y = exif?.GetValue(ExifTag.YResolution)?.Value;
        var unit = exif?.GetValue(ExifTag.ResolutionUnit)?.Value;
        var result = Resolve(x, y, unit);
        if (xmp is not null)
        {
            var xmpX = ReadProperty("XResolution");
            var xmpY = ReadProperty("YResolution");
            var xmpUnit = ReadProperty("ResolutionUnit");
            var fromXmp = Resolve(ParseRational(xmpX), ParseRational(xmpY),
                xmpUnit is null ? null : ushort.TryParse(xmpUnit, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    ? value : throw new InvalidDataException("Invalid XMP resolution unit."));
            if (result is not null && fromXmp is not null && !Equivalent(result, fromXmp, exact: true))
                throw new InvalidDataException("Conflicting EXIF and XMP resolutions cannot be preserved safely.");
            result ??= fromXmp;

            string? ReadProperty(string localName)
            {
                var name = XName.Get(localName, "http://ns.adobe.com/tiff/1.0/");
                var values = xmp.Descendants(name).Select(element => element.Value)
                    .Concat(xmp.Descendants().Attributes(name).Select(attribute => attribute.Value)).Distinct().ToArray();
                if (values.Length > 1) throw new InvalidDataException("Conflicting XMP resolution values.");
                return values.SingleOrDefault();
            }
        }
        if (result is null) return container;
        if (container is not null && !Equivalent(result, container))
            throw new InvalidDataException("Conflicting metadata and container resolutions cannot be preserved safely.");
        return result;
    }

    private static Rational? ParseRational(string? text)
    {
        if (text is null) return null;
        var parts = text.Split('/');
        uint denominator = 1;
        if (parts.Length is < 1 or > 2 || !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var numerator) ||
            (parts.Length == 2 && !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out denominator)) || denominator == 0)
            throw new InvalidDataException("Invalid XMP resolution rational.");
        return new Rational(numerator, denominator);
    }

    private static ImageResolution? Resolve(Rational? x, Rational? y, ushort? unit)
    {
        if (x is null && y is null && unit is null) return null;
        if (x is null || y is null || unit is null || x.Value.Denominator == 0 || y.Value.Denominator == 0)
            throw new InvalidDataException("Incomplete image resolution cannot be preserved safely.");
        var a = x.Value.Denominator;
        var b = y.Value.Denominator;
        while (b != 0) (a, b) = (b, a % b);
        var divisor = (ulong)x.Value.Denominator / a * y.Value.Denominator;
        var numeratorX = (ulong)x.Value.Numerator * (divisor / x.Value.Denominator);
        var numeratorY = (ulong)y.Value.Numerator * (divisor / y.Value.Denominator);
        if (divisor > uint.MaxValue || numeratorX > uint.MaxValue || numeratorY > uint.MaxValue)
            throw new InvalidDataException("EXIF resolution precision exceeds the supported representation.");
        var result = new ImageResolution((uint)numeratorX, (uint)numeratorY, unit.Value, (uint)divisor);
        result.Validate();
        return result;
    }

    private static bool Equivalent(ImageResolution exif, ImageResolution container, bool exact = false)
    {
        if (exact)
        {
            if (exif.Unit == 1 || container.Unit == 1)
                return exif.Unit == container.Unit && (ulong)exif.X * container.Y == (ulong)container.X * exif.Y;
            // Compare rational physical values without container rounding tolerance.
            var leftScale = exif.Unit == 2 ? 100m : 254m;
            var rightScale = container.Unit == 2 ? 100m : 254m;
            return exif.X * leftScale * container.Divisor == container.X * rightScale * exif.Divisor &&
                exif.Y * leftScale * container.Divisor == container.Y * rightScale * exif.Divisor;
        }
        if (exif.Unit == 1 || container.Unit == 1)
            return exif.Unit == container.Unit && Math.Abs((double)exif.X / exif.Y - (double)container.X / container.Y) < 0.000001;
        // Container fields are rounded integers (PNG pixels/metre; JPEG pixels/inch or cm).
        // Keep exact EXIF rationals only when that quantization explains the difference.
        var scale = exif.Unit == container.Unit ? 1 : exif.Unit == 2 ? 1 / 2.54 : 2.54;
        var tolerance = 1.000001 / container.Divisor;
        return Math.Abs((double)exif.X / exif.Divisor * scale - (double)container.X / container.Divisor) <= tolerance &&
            Math.Abs((double)exif.Y / exif.Divisor * scale - (double)container.Y / container.Divisor) <= tolerance;
    }
}
