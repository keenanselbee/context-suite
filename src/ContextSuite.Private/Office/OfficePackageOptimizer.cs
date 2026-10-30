using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Office;

// Only compressed storage and explicitly certified embedded image payloads may change.
public static partial class OfficePackageOptimizer
{
    public const int MaximumSourceBytes = 64 * 1024 * 1024;
    public const int MaximumExpandedBytes = 256 * 1024 * 1024;
    private const int MaximumPartBytes = 64 * 1024 * 1024;
    private const int MaximumXmlBytes = 8 * 1024 * 1024;
    private const string TypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static string Probe(byte[] source, CancellationToken token)
    {
        return Read(source, token).Family;
    }

    public static byte[] Optimize(byte[] source, CancellationToken token)
        => Optimize(source, ImageOptimizationPreset.Lossless, token);

    public static byte[] Optimize(byte[] source, ImageOptimizationPreset preset, CancellationToken token)
    {
        if (!Enum.IsDefined(preset)) throw new InvalidDataException("Unknown document image optimization preset.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(105));
        try { return OptimizeCore(source, preset, deadline.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new ImageFailureException(ImageFailure.TimedOut); }
    }

    private static byte[] OptimizeCore(byte[] source, ImageOptimizationPreset preset, CancellationToken token)
    {
        var before = Read(source, token);
        if (before.Family is "odt" or "ods" or "odp" or "epub" && preset is not (ImageOptimizationPreset.Lossless or ImageOptimizationPreset.Auto))
            throw new NotSupportedException("This document container supports Auto and Lossless only.");
        var replacements = preset is ImageOptimizationPreset.Balanced or ImageOptimizationPreset.Smallest
            ? OptimizeImages(source, before, preset, token) : new Dictionary<string, ImageReplacement>(StringComparer.Ordinal);
        using var output = new MemoryStream();
        var directories = new List<byte[]>();
        foreach (var entry in before.Entries)
        {
            token.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> payload = source.AsSpan(entry.DataOffset, entry.Compressed);
            var method = entry.Method;
            var expanded = Expand(source, entry, token);
            var replaced = replacements.TryGetValue(entry.Name, out var replacement);
            if (replaced) expanded = replacement!.Bytes;
            var crc = replaced ? Crc(expanded, token) : entry.Crc;
            // A replaced member must first use its new complete bytes. Keep the
            // original storage method and extraction compatibility when possible.
            if (replaced) { payload = expanded; method = 0; }
            using var encoded = new MemoryStream();
            using (var deflater = new DeflateStream(encoded, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                for (var offset = 0; offset < expanded.Length; offset += 65536)
                {
                    token.ThrowIfCancellationRequested();
                    deflater.Write(expanded, offset, Math.Min(65536, expanded.Length - offset));
                }
            }
            // Preserve the declared extraction version, including ZIP 1.0 stores.
            if (entry.Name != "mimetype" && (encoded.Length < payload.Length || replaced && entry.Descriptor.Length != 0) && U16(entry.Local, 4) >= 20)
            { payload = encoded.GetBuffer().AsSpan(0, checked((int)encoded.Length)); method = 8; }
            var local = entry.Local.ToArray(); var directory = entry.Directory.ToArray();
            Put32(directory, 42, checked((uint)output.Position));
            Put16(local, 8, method); Put16(directory, 10, method);
            if (replaced)
            {
                if (entry.Descriptor.Length == 0 || U32(local, 14) != 0) Put32(local, 14, crc);
                if (entry.Descriptor.Length == 0 || U32(local, 22) != 0) Put32(local, 22, (uint)expanded.Length);
                Put32(directory, 16, crc); Put32(directory, 24, (uint)expanded.Length);
            }
            if (entry.Descriptor.Length == 0 || U32(local, 18) != 0) Put32(local, 18, (uint)payload.Length);
            Put32(directory, 20, (uint)payload.Length);
            output.Write(local); output.Write(payload);
            if (entry.Descriptor.Length != 0)
            {
                var descriptor = entry.Descriptor.ToArray();
                Put32(descriptor, descriptor.Length - 8, (uint)payload.Length);
                if (replaced) { Put32(descriptor, descriptor.Length - 12, crc); Put32(descriptor, descriptor.Length - 4, (uint)expanded.Length); }
                output.Write(descriptor);
            }
            directories.Add(directory);
        }
        var directoryOffset = checked((uint)output.Position);
        foreach (var directory in directories) output.Write(directory);
        var end = before.End.ToArray();
        Put32(end, 12, checked((uint)output.Position - directoryOffset)); Put32(end, 16, directoryOffset);
        output.Write(end);
        token.ThrowIfCancellationRequested();
        if (output.Length >= source.Length) return source;
        var candidate = output.ToArray();
        RequirePreserved(before, Read(candidate, token), replacements);
        token.ThrowIfCancellationRequested();
        return candidate;
    }

    private static Package Read(byte[] source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.Length is < 22 or > MaximumSourceBytes) throw new NotSupportedException("Office package exceeds its ZIP32 size limit.");
        var endOffset = -1;
        for (var index = source.Length - 22; index >= Math.Max(0, source.Length - 65557); index--)
            if (U32(source, index) == 0x06054b50 && index + 22 + U16(source, index + 20) == source.Length) { endOffset = index; break; }
        if (endOffset < 0) throw new InvalidDataException("Office ZIP end record is missing or has trailing data.");
        var count = U16(source, endOffset + 10); var size = U32(source, endOffset + 12); var start = U32(source, endOffset + 16);
        if (U16(source, endOffset + 4) != 0 || U16(source, endOffset + 6) != 0 || U16(source, endOffset + 8) != count ||
            count is 0 or > 4096 || size > 4 * 1024 * 1024 || (long)start + size != endOffset)
            throw new NotSupportedException("Split, ZIP64, or oversized Office ZIP directory is unsupported.");
        var entries = new List<Entry>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = (int)start; var nextLocal = 0; long expandedTotal = 0;
        for (var index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (position > endOffset - 46 || U32(source, position) != 0x02014b50) throw new InvalidDataException("Invalid Office ZIP directory.");
            var nameLength = U16(source, position + 28); var extraLength = U16(source, position + 30); var commentLength = U16(source, position + 32);
            var length = 46 + nameLength + extraLength + commentLength;
            if (nameLength is 0 or > 1024 || length > endOffset - position || extraLength != 0 || U16(source, position + 34) != 0)
                throw new NotSupportedException("Unsupported Office ZIP names, extra fields, or disk records.");
            var flags = U16(source, position + 8); var method = U16(source, position + 10); var needed = U16(source, position + 6);
            var hasDescriptor = (flags & 8) != 0;
            if ((flags & ~0x0808) != 0 || method is not (0 or 8) || needed is not (10 or 20) ||
                method == 8 && needed != 20 || hasDescriptor && (method != 8 || needed != 20))
                throw new NotSupportedException("Encrypted or unsupported Office ZIP storage.");
            var rawName = source.AsSpan(position + 46, nameLength);
            string name;
            try
            {
                if ((flags & 0x0800) == 0 && rawName.ContainsAnyInRange((byte)128, byte.MaxValue))
                    throw new NotSupportedException("Legacy non-ASCII Office ZIP names need a preservation policy.");
                name = new UTF8Encoding(false, true).GetString(rawName);
            }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid Office ZIP UTF-8 name.", error); }
            RequireName(name);
            if (!names.Add(name)) throw new InvalidDataException("Duplicate or case-ambiguous Office ZIP names.");
            if (name.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase) || name.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("vbaData.xml", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Signed or macro-enabled Office packages are unsupported.");
            var compressed = U32(source, position + 20); var expanded = U32(source, position + 24); var localOffset = U32(source, position + 42);
            var crc = U32(source, position + 16);
            expandedTotal += expanded;
            if (expanded > MaximumPartBytes || compressed > MaximumSourceBytes || expandedTotal > MaximumExpandedBytes || localOffset != nextLocal || localOffset + 30L > start)
                throw new InvalidDataException("Office ZIP extent or expansion budget is invalid.");
            var local = (int)localOffset;
            if (U32(source, local) != 0x04034b50 || U16(source, local + 4) != needed || U16(source, local + 6) != flags || U16(source, local + 8) != method ||
                U32(source, local + 10) != U32(source, position + 12) ||
                !LocalValueMatches(14, crc) || !LocalValueMatches(18, compressed) || !LocalValueMatches(22, expanded) ||
                U16(source, local + 26) != nameLength || U16(source, local + 28) != 0)
                throw new InvalidDataException("Conflicting Office ZIP local header.");
            var dataOffset = local + 30 + nameLength;
            if ((long)dataOffset + compressed > start || !source.AsSpan(local + 30, nameLength).SequenceEqual(rawName) ||
                method == 0 && compressed != expanded || name.EndsWith('/') && expanded != 0) throw new InvalidDataException("Invalid Office ZIP payload extent or name.");
            var payloadEnd = checked(dataOffset + (int)compressed);
            byte[] descriptor = [];
            if (hasDescriptor)
            {
                var unsigned = DescriptorMatches(payloadEnd);
                var signed = payloadEnd + 16L <= start && U32(source, payloadEnd) == 0x08074b50 && DescriptorMatches(payloadEnd + 4);
                if (unsigned == signed) throw new InvalidDataException("Missing, ambiguous, or conflicting Office ZIP data descriptor.");
                descriptor = source.AsSpan(payloadEnd, signed ? 16 : 12).ToArray();
            }
            var entry = new Entry(name, method, dataOffset, (int)compressed, (int)expanded, crc,
                source.AsSpan(local, dataOffset - local).ToArray(), source.AsSpan(position, length).ToArray(), descriptor, "");
            var payload = Expand(source, entry, token);
            entries.Add(entry with { Sha256 = Convert.ToHexString(SHA256.HashData(payload)) });
            nextLocal = checked(payloadEnd + descriptor.Length); position += length;

            bool LocalValueMatches(int offset, uint expected) => U32(source, local + offset) == expected ||
                hasDescriptor && U32(source, local + offset) == 0;
            bool DescriptorMatches(int offset) => offset + 12L <= start && U32(source, offset) == crc &&
                U32(source, offset + 4) == compressed && U32(source, offset + 8) == expanded;
        }
        if (position != endOffset || nextLocal != start) throw new InvalidDataException("Opaque Office ZIP gaps or records are unsupported.");
        var family = entries.Any(entry => entry.Name == "mimetype") ? IdentifyDocument(source, entries, token) : Identify(source, entries, token);
        if (family is "docx" or "xlsx" or "pptx") RequireUnprotected(source, entries, token);
        return new(family, entries, source.AsSpan(endOffset).ToArray());
    }

    private static byte[] Expand(byte[] source, Entry entry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[] bytes;
        if (entry.Method == 0) bytes = source.AsSpan(entry.DataOffset, entry.Compressed).ToArray();
        else
        {
            OfficePackageDeflate.Validate(source.AsSpan(entry.DataOffset, entry.Compressed), entry.Expanded, token);
            using var input = new MemoryStream(source, entry.DataOffset, entry.Compressed, writable: false);
            using var inflater = new DeflateStream(input, CompressionMode.Decompress);
            bytes = new byte[entry.Expanded];
            for (var offset = 0; offset < bytes.Length; offset += 65536)
            {
                token.ThrowIfCancellationRequested();
                inflater.ReadExactly(bytes.AsSpan(offset, Math.Min(65536, bytes.Length - offset)));
            }
            if (inflater.ReadByte() != -1) throw new InvalidDataException("Office ZIP payload exceeds its declaration.");
        }
        uint crc = uint.MaxValue;
        for (var index = 0; index < bytes.Length; index++)
        {
            if ((index & 65535) == 0) token.ThrowIfCancellationRequested();
            crc = CrcTable[(crc ^ bytes[index]) & 255] ^ (crc >> 8);
        }
        if (~crc != entry.Crc) throw new InvalidDataException("Office ZIP payload checksum mismatch.");
        return bytes;
    }

    private static string Identify(byte[] source, List<Entry> entries, CancellationToken token)
    {
        XElement Xml(string name)
        {
            var entry = entries.SingleOrDefault(item => item.Name == name) ?? throw new InvalidDataException("Required Office package declaration is missing.");
            if (entry.Expanded > MaximumXmlBytes) throw new NotSupportedException("Office package declaration exceeds its XML budget.");
            var bytes = Expand(source, entry, token);
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumXmlBytes };
                using (var reader = XmlReader.Create(stream, settings))
                    while (reader.Read())
                    {
                        token.ThrowIfCancellationRequested();
                        if (reader.Depth > 64) throw new InvalidDataException("Office declaration nesting exceeds its budget.");
                    }
                stream.Position = 0;
                using var second = XmlReader.Create(stream, settings);
                return XElement.Load(second);
            }
            catch (XmlException error) { throw new InvalidDataException("Malformed Office package declaration.", error); }
        }
        var types = Xml("[Content_Types].xml"); var links = Xml("_rels/.rels");
        if (types.Name != XName.Get("Types", TypesNamespace) || links.Name != XName.Get("Relationships", RelationshipsNamespace))
            throw new InvalidDataException("Unexpected Office package declaration root.");
        foreach (var declaration in types.Elements())
        {
            var contentType = (string?)declaration.Attribute("ContentType") ?? "";
            if (contentType.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) || contentType.Contains("vba", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("template", StringComparison.OrdinalIgnoreCase) || contentType.Contains("digital-signature", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Signed, macro-enabled, or template Office packages are unsupported.");
        }
        foreach (var entry in entries.Where(entry => entry.Name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var relationships = entry.Name == "_rels/.rels" ? links : Xml(entry.Name);
            if (relationships.Name != XName.Get("Relationships", RelationshipsNamespace)) throw new InvalidDataException("Unexpected Office relationship root.");
            foreach (var relationship in relationships.Elements())
                if (((string?)relationship.Attribute("Type") ?? "").Contains("digital-signature", StringComparison.OrdinalIgnoreCase) ||
                    ((string?)relationship.Attribute("Type") ?? "").Contains("vbaProject", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("Signed or macro-enabled Office relationships are unsupported.");
        }
        var mainLinks = links.Elements(XName.Get("Relationship", RelationshipsNamespace)).Where(link => (string?)link.Attribute("Type") is
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" or "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument").ToArray();
        if (mainLinks.Length != 1 || (string?)mainLinks[0].Attribute("TargetMode") is not (null or "Internal"))
            throw new InvalidDataException("Office main relationship is missing, ambiguous, or external.");
        var target = (string?)mainLinks[0].Attribute("Target") ?? "";
        if (target.StartsWith('/')) target = target[1..];
        RequireName(target);
        var declarations = types.Elements(XName.Get("Override", TypesNamespace)).Where(type => (string?)type.Attribute("PartName") == "/" + target).ToArray();
        if (declarations.Length != 1) throw new InvalidDataException("Office main content type is missing or ambiguous.");
        var family = (string?)declarations[0].Attribute("ContentType") switch
        {
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" => "docx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" => "xlsx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml" => "pptx",
            _ => throw new NotSupportedException("Only ordinary DOCX, XLSX, and PPTX packages are supported.")
        };
        var main = Xml(target); var format = family switch { "docx" => "wordprocessingml", "xlsx" => "spreadsheetml", _ => "presentationml" };
        var ns = main.Name.NamespaceName;
        if (main.Name.LocalName != (family switch { "docx" => "document", "xlsx" => "workbook", _ => "presentation" }) ||
            ns != $"http://schemas.openxmlformats.org/{format}/2006/main" && ns != $"http://purl.oclc.org/ooxml/{format}/main")
            throw new InvalidDataException("Office main declaration conflicts with its family.");
        token.ThrowIfCancellationRequested();
        return family;
    }

    private static void RequirePreserved(Package before, Package after, IReadOnlyDictionary<string, ImageReplacement>? replacements = null)
    {
        if (before.Family != after.Family || before.Entries.Count != after.Entries.Count || !before.End.AsSpan(0, 12).SequenceEqual(after.End.AsSpan(0, 12)) ||
            !before.End.AsSpan(20).SequenceEqual(after.End.AsSpan(20))) throw new InvalidDataException("Office package identity changed.");
        for (var index = 0; index < before.Entries.Count; index++)
        {
            var left = before.Entries[index]; var right = after.Entries[index];
            if (left.Descriptor.Length != 0 && new[] { 14, 18, 22 }.Any(offset =>
                U32(left.Local, offset) == 0 && U32(right.Local, offset) != 0))
                throw new InvalidDataException("Document ZIP descriptor local declaration style changed.");
            var local = right.Local.ToArray(); var directory = right.Directory.ToArray();
            Put16(local, 8, left.Method); Put32(local, 18, U32(left.Local, 18));
            Put16(directory, 10, left.Method); Put32(directory, 20, (uint)left.Compressed); Put32(directory, 42, U32(left.Directory, 42));
            ImageReplacement? replacement = null;
            var replaced = replacements is not null && replacements.TryGetValue(left.Name, out replacement);
            if (replaced)
            {
                if (replacement!.SourceSha256 != left.Sha256 || replacement.SourceBytes != left.Expanded ||
                    replacement.OutputSha256 != right.Sha256 || replacement.Bytes.Length != right.Expanded ||
                    replacement.Bytes.Length >= replacement.SourceBytes ||
                    Convert.ToHexString(SHA256.HashData(replacement.Bytes)) != replacement.OutputSha256 ||
                    replacement.Preset is not (ImageOptimizationPreset.Balanced or ImageOptimizationPreset.Smallest))
                    throw new InvalidDataException("Document image replacement does not match its validation certificate.");
                var image = ContextSuite.Private.Images.ImageContainer.Read(replacement.Bytes);
                if (image.Format != replacement.Format || image.Width != replacement.Width || image.Height != replacement.Height)
                    throw new InvalidDataException("Document image replacement changed its certified identity.");
                Put32(local, 14, U32(left.Local, 14)); Put32(local, 22, U32(left.Local, 22));
                Put32(directory, 16, left.Crc); Put32(directory, 24, (uint)left.Expanded);
            }
            var descriptor = right.Descriptor.ToArray();
            if (descriptor.Length == left.Descriptor.Length && descriptor.Length != 0)
                Put32(descriptor, descriptor.Length - 8, (uint)left.Compressed);
            if (replaced && descriptor.Length == left.Descriptor.Length && descriptor.Length != 0)
            { Put32(descriptor, descriptor.Length - 12, left.Crc); Put32(descriptor, descriptor.Length - 4, (uint)left.Expanded); }
            if (left.Name != right.Name || !replaced && (left.Expanded != right.Expanded || left.Sha256 != right.Sha256) ||
                !left.Local.AsSpan().SequenceEqual(local) || !left.Directory.AsSpan().SequenceEqual(directory) ||
                !left.Descriptor.AsSpan().SequenceEqual(descriptor))
                throw new InvalidDataException("Office package payload or preserved ZIP metadata changed.");
        }
        if (replacements is not null && replacements.Keys.Any(name => before.Entries.All(entry => entry.Name != name)))
            throw new InvalidDataException("Document image certificate refers to a missing member.");
    }

    private static void RequireName(string name)
    {
        var path = name.EndsWith('/') ? name[..^1] : name;
        if (path.Length == 0 || path.Any(character => char.IsControl(character) || character is '\\' or ':' or '?' or '#' or '%') ||
            path.Split('/').Any(segment => segment is "" or "." or "..")) throw new InvalidDataException("Unsafe or noncanonical Office package name.");
    }
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320);
            table[index] = value;
        }
        return table;
    }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Put16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private sealed record Entry(string Name, ushort Method, int DataOffset, int Compressed, int Expanded, uint Crc,
        byte[] Local, byte[] Directory, byte[] Descriptor, string Sha256);
    private sealed record Package(string Family, List<Entry> Entries, byte[] End);
}
