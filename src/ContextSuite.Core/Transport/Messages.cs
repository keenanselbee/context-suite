using ContextSuite.Core.Operations;

namespace ContextSuite.Core.Transport;

public sealed record ActivationMessage(int Version, OperationRequest? Request);
public sealed record ActivationReply(int Version, Guid RequestId, bool Accepted, string Message);
public sealed record ActivationReceipt(int Version, Guid RequestId);
public sealed record WorkerCommand(int Version, Guid RequestId, string Command);
public sealed record WorkerReply(int Version, Guid RequestId, MediaCapability[] Capabilities);
