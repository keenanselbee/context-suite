using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Analysis;

public static class LegacyDocumentAnalysis
{
    // Enrich only content-identified CFB, under the caller's existing read lease/deadline.
    public static async Task<FileAnalysis> AddCompoundAsync(FileAnalysis header, Stream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (header.Identity.FormatId != "ole" || header.Identity.Basis != IdentificationBasis.Content) return header;
        var compound = new CompoundFileReader(stream, header.InspectedBytes, cancellationToken);
        try
        {
            await compound.InitializeAsync();
            var facts = ImmutableArray.CreateBuilder<AnalysisFact>();
            var word = compound.Contains("WordDocument");
            var workbooks = new[] { "Workbook", "Book" }.Where(compound.Contains).ToArray();
            var powerpoint = compound.Contains("PowerPoint Document") || compound.Contains("Current User");
            if ((word ? 1 : 0) + (workbooks.Length > 0 ? 1 : 0) + (powerpoint ? 1 : 0) > 1 || workbooks.Length > 1)
                throw new InvalidDataException("Conflicting root document streams.");
            string? id = null;
            if (word)
            {
                var main = compound.Get("WordDocument");
                var fib = await compound.ReadPrefixAsync(main, 32);
                var version = U16(fib, 2); var flags = U16(fib, 10);
                if (U16(fib, 0) != 0xa5ec || version is not (0x00c1 or 0x00d9 or 0x0101 or 0x010c or 0x0112) ||
                    U16(fib, 12) is not (0x00bf or 0x00c1) || (flags & 0x1000) == 0)
                    throw new InvalidDataException("Unsupported Word base header.");
                var table = compound.Get((flags & 0x0200) == 0 ? "0Table" : "1Table");
                if (table.Size > 0) await compound.ReadPrefixAsync(table, 1);
                id = "doc";
                facts.Add(new("document.base-version", "Document", "Word base version (nFib; later override not inspected)", Text: $"0x{version:X4}"));
                facts.Add(new("document.template", "Document", "Template declared", Boolean: (flags & 1) != 0));
                facts.Add(new("document.encrypted-content", "Document", "Encryption or obfuscation declared", Boolean: (flags & 0x0100) != 0));
                facts.Add(new("document.main-stream-bytes", "Document", "Declared main stream bytes", Integer: main.Size));
            }
            else if (workbooks.Length == 1)
            {
                var main = compound.Get(workbooks[0]);
                var bof = await compound.ReadPrefixAsync(main, 20);
                // Initial legacy Excel scope is BIFF8 workbook globals, not standalone sheets or early BIFF.
                if (U16(bof, 0) != 0x0809 || U16(bof, 2) != 16 || U16(bof, 4) != 0x0600 || U16(bof, 6) != 5)
                    throw new InvalidDataException("Unsupported Excel workbook BOF.");
                id = "xls";
                facts.Add(new("document.binary-version", "Document", "Workbook format", Text: "BIFF8"));
                facts.Add(new("document.encrypted-content", "Document", "Encryption (not inspected)", Availability: FactAvailability.Unavailable));
                facts.Add(new("document.sheets", "Document", "Sheets (not inspected)", Availability: FactAvailability.Unavailable));
                facts.Add(new("document.main-stream-bytes", "Document", "Declared workbook stream bytes", Integer: main.Size));
            }
            else if (powerpoint)
            {
                var current = compound.Get("Current User"); var main = compound.Get("PowerPoint Document");
                var user = await compound.ReadPrefixAsync(current, 28);
                var nameBytes = U16(user, 20); var recordBytes = U32(user, 4); var token = U32(user, 12);
                if (U16(user, 0) != 0 || U16(user, 2) != 0x0ff6 || U32(user, 8) != 20 || nameBytes > 255 ||
                    recordBytes != 24 + nameBytes && recordBytes != 24 + nameBytes * 3 || current.Size != 8L + recordBytes ||
                    token is not (0xe391c05f or 0xf3d1c4df) || U16(user, 22) != 0x03f4 || U16(user, 24) != 3 ||
                    U32(user, 16) > main.Size - 8)
                    throw new InvalidDataException("Unsupported PowerPoint current-user header.");
                var document = await compound.ReadPrefixAsync(main, 8);
                if (token == 0xe391c05f && (U16(document, 0) != 0x000f || U16(document, 2) != 0x03e8 || U32(document, 4) > main.Size - 8))
                    throw new InvalidDataException("Unsupported leading PowerPoint document record.");
                id = "ppt";
                facts.Add(new("document.binary-version", "Document", "Presentation storage version", Text: "3.0 (file version 0x03F4)"));
                facts.Add(new("document.encrypted-content", "Document", "Encrypted-document token declared", Boolean: token == 0xf3d1c4df));
                facts.Add(new("document.slides", "Document", "Slides (not inspected)", Availability: FactAvailability.Unavailable));
                facts.Add(new("document.main-stream-bytes", "Document", "Declared presentation stream bytes", Integer: main.Size));
            }
            if (id is not null)
            {
                facts.Add(new("document.pages", "Document", "Rendered pages", Availability: FactAvailability.Unavailable));
                facts.Add(new("document.active-content", "Document", "Macros and embedded content (not inspected)", Availability: FactAvailability.Unavailable));
            }
            else if (compound.Contains("EncryptionInfo") && compound.Contains("EncryptedPackage"))
                facts.Add(new("compound.encrypted-package-streams", "Compound file", "Encrypted-package stream names found (contents not validated)", Boolean: true));
            facts.Add(new("compound.version", "Compound file", "Container version", Integer: compound.MajorVersion));
            facts.Add(new("compound.sector-bytes", "Compound file", "Sector bytes", Integer: compound.SectorBytes));
            facts.Add(new("compound.entries", "Compound file", "Reachable directory objects", Integer: compound.ReachableEntries));
            facts.Add(new("compound.root-streams", "Compound file", "Root-level streams", Integer: compound.RootStreams));
            facts.Add(new("compound.bytes-read", "Compound file", "Additional bytes read (including repeat reads)", Integer: compound.BytesRead));
            var evidence = ImmutableArray.Create("Compound directory and selected allocation chains inspected within fixed limits; unrelated stream contents, tree ordering and the complete document were not validated.");
            var identity = header.Identity with { Evidence = evidence };
            var warnings = header.Warnings;
            if (id is not null)
            {
                var type = FileTypeCatalog.Default.Get(id);
                identity = new(type.Id, type.Name, type.Family, type.CommonUses, IdentificationConfidence.Likely,
                    evidence.Add("Root document stream names agree with supported binary header declarations. No rendering, macro execution, decryption or signature verification was performed."), IdentificationBasis.Content);
                warnings = warnings.Remove("The container alone does not confirm the document type suggested by the filename.").Remove(HeaderAnalyzer.FilenameOnlyWarning);
                if (!header.FilenameHints.IsDefaultOrEmpty && header.FilenameHints.All(hint => hint.Id != id && hint.Id != "ole"))
                    warnings = warnings.Add("The compound document indicates a different type from the filename hint.");
            }
            return header with { Identity = identity, Facts = header.Facts.AddRange(facts), Warnings = warnings, InspectedBytes = compound.InspectedBytes };
        }
        catch (Exception error) when (error is IOException or InvalidDataException or DecoderFallbackException)
        {
            return header with
            {
                Identity = header.Identity with { Evidence = ["Compound-file signature found. Bounded inspection did not establish a supported legacy document family."] },
                Warnings = header.Warnings.Add("Legacy document details are unavailable: the compound file is unsupported, inconsistent or exceeds the analysis limits. Basic file information is shown."),
                InspectedBytes = compound.InspectedBytes
            };
        }
    }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
