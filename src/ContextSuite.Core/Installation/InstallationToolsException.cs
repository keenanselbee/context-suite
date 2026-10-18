namespace ContextSuite.Core.Installation;

public sealed class InstallationToolsException(string message, Exception? innerException = null) : IOException(message, innerException);
