using ContextSuite.Private.IO;

namespace ContextSuite.Private.Office;

// Preserve the Office-specific call sites while sharing the proven handle checks.
internal sealed class OfficeDirectoryLeases : RuntimeDirectoryLeases { }
