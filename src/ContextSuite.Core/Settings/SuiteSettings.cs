using ContextSuite.Core.Operations;

namespace ContextSuite.Core.Settings;

public sealed record ToolSettings(bool ReplaceOriginals = false, string? OutputDirectory = null);

public sealed record SuiteSettings
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public ToolSettings Convert { get; init; } = new();
    public ToolSettings Optimize { get; init; } = new();
    public bool PlayCompletionSound { get; init; } = true;

    public BatchSettings Capture(string operation)
    {
        var preferences = operation switch
        {
            "convert" => Convert,
            "optimize" => Optimize,
            "analyze" => new ToolSettings(),
            _ => throw new InvalidDataException("Unknown settings section.")
        };
        return new BatchSettings(operation, preferences, PlayCompletionSound);
    }
}

public sealed record BatchSettings(string Operation, ToolSettings Preferences, bool PlayCompletionSound = true)
{
    public OutputPolicy SelectOutput(bool requestReplacement, bool confirmed, bool quickAction,
        bool replacementAvailable)
    {
        if (Operation is not ("convert" or "optimize"))
            throw new InvalidDataException("This operation does not publish output.");
        if (requestReplacement && (!Preferences.ReplaceOriginals || !confirmed ||
            !replacementAvailable || Preferences.OutputDirectory is not null))
            throw new InvalidDataException("Replacement requires an available, explicitly confirmed source-folder plan.");
        return new OutputPolicy(requestReplacement ? OutputMode.RecoverableReplacement : OutputMode.SiblingCopy,
            SkipIfLarger: Operation == "optimize");
    }
}
