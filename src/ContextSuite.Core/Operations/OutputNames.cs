using System.Globalization;

namespace ContextSuite.Core.Operations;

public enum DdsCompression { BC1, BC2, BC3, BC4, BC5, BC6H, BC7, R8, RG8, RGBA8, BGRA8 }
public enum TextureTransfer { Linear, Srgb }

// This models naming, not an advertised encoder capability. Adapters still validate support.
public sealed record DdsRepresentation(DdsCompression Compression, TextureTransfer Transfer, bool Signed = false)
{
    public string Suffix
    {
        get
        {
            if (!Enum.IsDefined(Compression) || !Enum.IsDefined(Transfer) ||
                (Transfer == TextureTransfer.Srgb && Compression is DdsCompression.BC4 or DdsCompression.BC5 or DdsCompression.BC6H or DdsCompression.R8 or DdsCompression.RG8) ||
                (Signed && Compression is not (DdsCompression.BC4 or DdsCompression.BC5 or DdsCompression.BC6H)))
                throw new InvalidDataException("The DDS representation is invalid.");
            return Compression switch
            {
                DdsCompression.BC4 or DdsCompression.BC5 => $"{Compression}-{(Signed ? "SNORM" : "UNORM")}",
                DdsCompression.BC6H => $"BC6H-{(Signed ? "SF16" : "UF16")}",
                DdsCompression.R8 or DdsCompression.RG8 => $"{Compression}-UNORM",
                _ => $"{Compression}-{(Transfer == TextureTransfer.Srgb ? "sRGB" : "Linear")}"
            };
        }
    }
}

public static class OutputNames
{
    public static string Create(string sourcePath, string operation, string targetExtension,
        int ordinal = 1, DdsRepresentation? representation = null, bool replaceSource = false, int? pageNumber = null)
    {
        if (operation is not ("convert" or "optimize") || ordinal < 1)
            throw new InvalidDataException("The output operation or collision number is invalid.");
        var extension = targetExtension.TrimStart('.').ToLowerInvariant();
        if (extension.Length is < 1 or > 12 || !extension.All(char.IsAsciiLetterOrDigit))
            throw new InvalidDataException("The target extension is invalid.");
        if (representation is not null && (operation != "convert" || extension != "dds"))
            throw new InvalidDataException("DDS representation names require DDS conversion.");
        if (pageNumber is not null && (pageNumber is < 1 or > 4096 || operation != "convert" || extension != "png" ||
            !string.Equals(Path.GetExtension(sourcePath), ".pdf", StringComparison.OrdinalIgnoreCase) || representation is not null || replaceSource))
            throw new InvalidDataException("Numbered page outputs require PDF-to-PNG copies.");
        var basename = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(basename)) throw new InvalidDataException("The source basename is empty.");
        var suffix = representation?.Suffix ?? (operation == "convert" ? "Converted" : "Optimized");
        if (pageNumber is { } page) suffix = "Page " + page.ToString("D3", CultureInfo.InvariantCulture);
        var number = ordinal == 1 ? "" : $" ({ordinal.ToString(CultureInfo.InvariantCulture)})";
        var name = $"{basename}{(replaceSource ? "" : " - " + suffix)}{number}.{extension}";
        if (name.Length > 255 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("The output name is too long or contains unsupported characters. Rename the source first.");
        return name;
    }
}
