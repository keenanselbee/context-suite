using ContextSuite.Core.Operations;
using ContextSuite.Core.Images;

namespace ContextSuite.Core.Transport;

public sealed record ActivationMessage(int Version, OperationRequest? Request);
public sealed record ActivationReply(int Version, Guid RequestId, bool Accepted, string Message);
public sealed record ActivationReceipt(int Version, Guid RequestId);
public sealed record WorkerCommand(int Version, Guid RequestId, string Command,
    ImageProbe? Probe = null, ImageWork? Work = null, ImagePreviewRequest? Preview = null)
{
    public void Validate()
    {
        if (Version != 1 || RequestId == Guid.Empty) throw new InvalidDataException("Invalid worker request identity.");
        var valid = Command switch
        {
            "shutdown" or "capabilities" or "engine-info" => Probe is null && Work is null && Preview is null,
            "image-probe" => Probe is not null && Work is null && Preview is null,
            "image-convert" => Work is not null && Probe is null && Preview is null,
            "image-preview" => Preview is not null && Probe is null && Work is null,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Worker command payload does not match its operation.");
    }
}
public sealed record ImageEngineIdentity(string Package, string Version, string NativeVersion);
public sealed record WorkerReply(int Version, Guid RequestId, MediaCapability[] Capabilities, ImageEngineIdentity? Engine = null,
    ImageSourceFacts? Source = null, ImageWorkResult? ImageResult = null, ImagePreview? Preview = null, ImageFailure? Failure = null);
