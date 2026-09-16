using System.Text.Json.Serialization;

namespace ContextSuite.Core.Office;

// The application creates/owns this isolated context and its profile/grants.
// Only a copied source enters it; no customer directory is granted to Office.
public sealed record OfficeExportWork(Guid ItemId, string DirectoryPath, string ProfileName, string Format,
    string Calculation, long SourceBytes, string SourceSha256, string Policy = OfficeHostProtocol.Policy)
{
    [JsonIgnore] public string SourcePath => Path.Combine(DirectoryPath, "input", "source." + Format);
    [JsonIgnore] public string CandidatePath => Path.Combine(DirectoryPath, "output", "candidate.pdf");
    [JsonIgnore] public string WorkbookPath => Path.Combine(DirectoryPath, "output", "calculated.xlsx");
    [JsonIgnore] public string EngineProfilePath => Path.Combine(DirectoryPath, "profile", "engine");

    public void Validate()
    {
        if (ItemId == Guid.Empty || Format is not ("docx" or "xlsx" or "pptx") ||
            (Format == "xlsx" ? Calculation is not ("cached" or "recalculate") : Calculation != "none") ||
            Policy != OfficeHostProtocol.Policy || SourceBytes is <= 0 or > OfficeHostProtocol.MaximumSourceBytes ||
            SourceSha256 is not { Length: 64 } || !SourceSha256.All(char.IsAsciiHexDigit) ||
            (ProfileName != "ContextSuite.Office." + ItemId.ToString("N") &&
             ProfileName != "ContextSuite.Office.Evaluation." + ItemId.ToString("N")))
            throw new InvalidDataException("Invalid Office export identity or fixed policy.");
        if (string.IsNullOrWhiteSpace(DirectoryPath) || DirectoryPath.Length is < 4 or > 240 ||
            !char.IsAsciiLetter(DirectoryPath[0]) || DirectoryPath[1] != ':' || DirectoryPath[2] != '\\' ||
            DirectoryPath[3..].IndexOfAny([':', '\0', '\r', '\n', '/', '"']) >= 0 ||
            DirectoryPath[3..].Split('\\').Any(part => part.Length == 0 || part.EndsWith(' ') || part.EndsWith('.')) ||
            !string.Equals(Path.GetFullPath(DirectoryPath), DirectoryPath, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(DirectoryPath) != "office-" + ItemId.ToString("N"))
            throw new InvalidDataException("Invalid owned Office context path.");
    }
}

// This intentionally contains no OutputValidation or publication permission.
public sealed record OfficeExportCandidate(Guid ItemId, OfficeHostCompletion Completion, string Policy)
{
    public void Validate(OfficeExportWork work, Guid requestId)
    {
        work.Validate();
        if (requestId == Guid.Empty || ItemId != work.ItemId || Policy != work.Policy || Completion is not { } value ||
            value.RequestId != requestId || value.Format != work.Format || value.Calculation != work.Calculation ||
            value.SourceBytes != work.SourceBytes || !string.Equals(value.SourceSha256, work.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
            value.OutputBytes is <= 0 or > OfficeHostProtocol.MaximumOutputBytes ||
            value.OutputSha256 is not { Length: 64 } || !value.OutputSha256.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Office candidate does not match its export request.");
        OfficeHostProtocol.ValidateFontFamilies(value.MissingFontFamilies);
        OfficeHostProtocol.ValidateWorkbookIdentity(value.Format, value.WorkbookBytes, value.WorkbookSha256);
    }
}
