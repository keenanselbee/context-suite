using System.Globalization;
using System.Collections.Immutable;
using System.Text;
using ContextSuite.Core.Operations;

namespace ContextSuite.Core.Activation;

public static class ActivationParser
{
    public const int MaximumBytes = 4 * 1024 * 1024;

    public static OperationRequest Parse(ReadOnlySpan<byte> bytes, bool requireExistingFiles = true)
    {
        if (bytes.Length is 0 or > MaximumBytes)
            throw new InvalidDataException("The activation request size is invalid.");
        var text = new UTF8Encoding(false, true).GetString(bytes);
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (lines[^1] == "") lines.RemoveAt(lines.Count - 1);
        if (lines.Count < 6 || lines[0] != "ContextSuiteActivation/1")
            throw new InvalidDataException("The activation schema is unknown.");
        if (!Guid.TryParseExact(Field(lines[1], "requestId="), "D", out var id) ||
            !int.TryParse(Field(lines[4], "pathCount="), NumberStyles.None,
                CultureInfo.InvariantCulture, out var count) || count is < 1 or > OperationRequest.MaximumPaths ||
            lines.Count != count + 5)
            throw new InvalidDataException("The activation identifier or count is invalid.");
        var request = new OperationRequest(id, Field(lines[2], "operation="), Field(lines[3], "action="),
            lines.Skip(5).Select(line => Field(line, "path=")).ToImmutableArray());
        request.Validate(requireExistingFiles);
        return request;
    }

    private static string Field(string line, string prefix)
    {
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || line.Length == prefix.Length)
            throw new InvalidDataException("The activation request is missing a required field.");
        return line[prefix.Length..];
    }
}
