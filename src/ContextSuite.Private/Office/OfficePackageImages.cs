using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;

namespace ContextSuite.Private.Office;

public static partial class OfficePackageOptimizer
{
    private const long MaximumImagePixels = 80_000_000;
    private const int MaximumImageParts = 128;
    private sealed record ImageReplacement(byte[] Bytes, string SourceSha256, int SourceBytes, string OutputSha256,
        ImageFormat Format, uint Width, uint Height, ImageOptimizationPreset Preset);

    private static Dictionary<string, ImageReplacement> OptimizeImages(byte[] source, Package package,
        ImageOptimizationPreset preset, CancellationToken token)
    {
        var types = ReadXml(source, package.Entries.Single(entry => entry.Name == "[Content_Types].xml"), token);
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in types.Elements())
        {
            var type = (string?)declaration.Attribute("ContentType") ?? "";
            if (declaration.Name == XName.Get("Default", TypesNamespace))
            {
                var extension = (string?)declaration.Attribute("Extension") ?? "";
                if (!defaults.TryAdd(extension, type)) throw new InvalidDataException("Ambiguous document image content type.");
            }
            else if (declaration.Name == XName.Get("Override", TypesNamespace))
            {
                var name = (string?)declaration.Attribute("PartName") ?? "";
                if (!overrides.TryAdd(name, type)) throw new InvalidDataException("Ambiguous document part content type.");
            }
        }
        var replacements = new Dictionary<string, ImageReplacement>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var otherTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in package.Entries.Where(entry => entry.Name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var relationships = ReadXml(source, entry, token);
            if (relationships.Elements().Any(element => element.Name != XName.Get("Relationship", RelationshipsNamespace))) return replacements;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relationship in relationships.Elements(XName.Get("Relationship", RelationshipsNamespace)))
            {
                if (!ids.Add((string?)relationship.Attribute("Id") ?? "")) throw new InvalidDataException("Ambiguous document image relationship.");
                var type = (string?)relationship.Attribute("Type");
                var mode = (string?)relationship.Attribute("TargetMode");
                if (mode == "External") continue;
                if (mode is not (null or "Internal")) return replacements;
                var target = ResolveImageTarget(entry.Name, (string?)relationship.Attribute("Target") ?? "");
                // An uncertain URI may alias an otherwise eligible image through
                // an attachment or another role. Keep every image exact until
                // all internal roles are understood; package recompression is safe.
                if (target is null || !package.Entries.Any(part => part.Name.Equals(target, StringComparison.OrdinalIgnoreCase))) return replacements;
                if (type is "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" or
                    "http://purl.oclc.org/ooxml/officeDocument/relationships/image") targets.Add(target);
                else otherTargets.Add(target);
            }
        }
        targets.ExceptWith(otherTargets);
        var admitted = new List<(Entry Entry, byte[] Bytes, ImageContainer Container)>();
        long pixels = 0; var images = 0;
        foreach (var entry in package.Entries.Where(entry => targets.Contains(entry.Name)))
        {
            token.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(entry.Name).TrimStart('.');
            var type = overrides.GetValueOrDefault("/" + entry.Name) ?? defaults.GetValueOrDefault(extension);
            var format = type switch { "image/jpeg" => ImageFormat.Jpeg, "image/webp" => ImageFormat.WebP, _ => (ImageFormat?)null };
            if (format is null) continue;
            var bytes = Expand(source, entry, token);
            ImageContainer container;
            try { container = ImageContainer.Read(bytes); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or ImageFailureException) { continue; }
            if (container.Format != format) continue;
            pixels += (long)container.Width * container.Height;
            if (++images > MaximumImageParts || pixels > MaximumImagePixels)
                throw new NotSupportedException("Document images exceed the aggregate optimization budget.");
            admitted.Add((entry, bytes, container));
        }
        // Establish the complete aggregate budget before starting any encoder.
        foreach (var (entry, bytes, container) in admitted)
        {
            token.ThrowIfCancellationRequested();
            byte[] candidate;
            try { candidate = ImageAdapter.OptimizeImageBytes(bytes, preset, token).Bytes; }
            catch (ImageFailureException error) when (error.Failure == ImageFailure.UnsupportedInput) { continue; }
            if (candidate.Length >= bytes.Length) continue;
            var after = ImageContainer.Read(candidate);
            if (after.Format != container.Format || after.Width != container.Width || after.Height != container.Height)
                throw new InvalidDataException("Document image identity or dimensions changed.");
            replacements.Add(entry.Name, new(candidate, entry.Sha256, bytes.Length,
                Convert.ToHexString(SHA256.HashData(candidate)), container.Format, container.Width, container.Height, preset));
        }
        return replacements;
    }

    private static string? ResolveImageTarget(string relationshipName, string target)
    {
        if (target.Length == 0 || target.Any(character => char.IsControl(character) || character is '\\' or ':' or '?' or '#' or '%')) return null;
        string directory;
        if (relationshipName.StartsWith("_rels/", StringComparison.Ordinal) && !relationshipName[6..].Contains('/')) directory = "";
        else
        {
            var marker = relationshipName.LastIndexOf("/_rels/", StringComparison.Ordinal);
            if (marker < 0 || !relationshipName.EndsWith(".rels", StringComparison.Ordinal)) return null;
            directory = relationshipName[..(marker + 1)];
        }
        var parts = new List<string>();
        foreach (var part in (target.StartsWith('/') ? target[1..] : directory + target).Split('/'))
        {
            if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
            else if (part == ".") continue;
            else if (part.Length == 0) return null;
            else parts.Add(part);
        }
        return parts.Count == 0 ? null : string.Join('/', parts);
    }

    private static void RequireUnprotected(byte[] source, List<Entry> entries, CancellationToken token)
    {
        var types = ReadXml(source, entries.Single(entry => entry.Name == "[Content_Types].xml"), token);
        var xmlExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "xml" };
        var xmlParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in types.Elements())
        {
            var type = (string?)declaration.Attribute("ContentType") ?? "";
            if (!type.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) && type is not ("application/xml" or "text/xml")) continue;
            if (declaration.Name == XName.Get("Default", TypesNamespace)) xmlExtensions.Add((string?)declaration.Attribute("Extension") ?? "");
            if (declaration.Name == XName.Get("Override", TypesNamespace)) xmlParts.Add(((string?)declaration.Attribute("PartName") ?? "").TrimStart('/'));
        }
        foreach (var entry in entries.Where(entry => xmlExtensions.Contains(Path.GetExtension(entry.Name).TrimStart('.')) || xmlParts.Contains(entry.Name)))
        {
            var xml = ReadXml(source, entry, token);
            foreach (var element in xml.DescendantsAndSelf())
            {
                token.ThrowIfCancellationRequested();
                var ns = element.Name.NamespaceName;
                // Ordinary openpyxl output includes this inactive declaration.
                // Namespace declarations alone do not enable any workbook lock.
                if (element.Name.LocalName == "workbookProtection" &&
                    ns is "http://schemas.openxmlformats.org/spreadsheetml/2006/main" or "http://purl.oclc.org/ooxml/spreadsheetml/main" &&
                    !element.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration) && !element.Nodes().Any()) continue;
                if ((ns.StartsWith("http://schemas.openxmlformats.org/", StringComparison.Ordinal) ||
                    ns.StartsWith("http://purl.oclc.org/ooxml/", StringComparison.Ordinal) ||
                    ns.StartsWith("http://schemas.microsoft.com/office/", StringComparison.Ordinal)) &&
                    element.Name.LocalName is "documentProtection" or "writeProtection" or "workbookProtection" or "sheetProtection" or "fileSharing" or "modifyVerifier")
                    throw new NotSupportedException("Protected document packages cannot be optimized.");
            }
        }
    }

    private static XElement ReadXml(byte[] source, Entry entry, CancellationToken token)
    {
        if (entry.Expanded > MaximumXmlBytes) throw new NotSupportedException("Document XML exceeds its inspection budget.");
        var bytes = Expand(source, entry, token);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumXmlBytes };
            using (var reader = XmlReader.Create(stream, settings))
                while (reader.Read()) { token.ThrowIfCancellationRequested(); if (reader.Depth > 64) throw new InvalidDataException("Document XML depth exceeds its budget."); }
            stream.Position = 0;
            using var second = XmlReader.Create(stream, settings);
            return XElement.Load(second);
        }
        catch (XmlException error) { throw new InvalidDataException("Malformed document XML.", error); }
    }

    private static uint Crc(byte[] bytes, CancellationToken token)
    {
        uint crc = uint.MaxValue;
        for (var index = 0; index < bytes.Length; index++)
        {
            if ((index & 65535) == 0) token.ThrowIfCancellationRequested();
            crc = CrcTable[(crc ^ bytes[index]) & 255] ^ (crc >> 8);
        }
        return ~crc;
    }

    internal static void RequireUnchangedMembers(byte[] source, byte[] candidate, CancellationToken token)
    {
        RequirePreserved(Read(source, token), Read(candidate, token));
    }
}
