using System.Collections.Immutable;

namespace ContextSuite.Core.Operations;

public sealed record OperationRequest(Guid RequestId, string Operation, string Action, ImmutableArray<string> Paths)
{
    public const int MaximumPaths = 4096;
    public bool IsSettingsRequest => Action == "settings" && Operation is "convert" or "optimize";
    public bool IsQuickOptimization => Operation == "optimize" && Action is "auto" or "lossless" or "balanced" or "smallest";
    public bool IsQuickConversion => Operation == "convert" && Action is "png" or "jpeg" or "webp" or "bmp" or "tga";
    public bool IsQuickAction => IsQuickOptimization || IsQuickConversion;

    public void Validate(bool requireExistingFiles = true)
    {
        if (RequestId == Guid.Empty || Paths.IsDefault || Paths.Length > MaximumPaths)
            throw new InvalidDataException("The selection identifier or size is invalid.");

        if (IsSettingsRequest)
        {
            if (!Paths.IsEmpty) throw new InvalidDataException("Settings activation must not contain selected files.");
            return;
        }
        if (Paths.IsEmpty) throw new InvalidDataException("A media operation requires selected files.");

        var expectedAction = Operation switch
        {
            "analyze" => "open-details",
            "convert" => "choose-format",
            "optimize" => "choose-preset",
            _ => throw new InvalidDataException("The operation is not supported.")
        };
        if (Action != expectedAction && !IsQuickAction)
            throw new InvalidDataException("The action does not match its operation.");

        foreach (var path in Paths)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 ||
                path.IndexOfAny(['\0', '\r', '\n']) >= 0 || !Path.IsPathFullyQualified(path))
                throw new InvalidDataException("Selected paths must be absolute file paths.");
            if (requireExistingFiles && !File.Exists(path))
                throw new InvalidDataException("A selected file is missing or is not a file.");
        }
    }
}
