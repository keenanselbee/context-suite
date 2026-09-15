namespace ContextSuite.Core.Office;

// A host completion reply is not output validation or permission to publish.
public sealed record OfficeHostCompletion(Guid RequestId, string Format, string Calculation,
    long SourceBytes, long OutputBytes, string SourceSha256, string OutputSha256);
