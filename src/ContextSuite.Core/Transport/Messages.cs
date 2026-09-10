using ContextSuite.Core.Operations;
using ContextSuite.Core.Images;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Analysis;

namespace ContextSuite.Core.Transport;

public sealed record ActivationMessage(int Version, OperationRequest? Request);
public sealed record ActivationReply(int Version, Guid RequestId, bool Accepted, string Message);
public sealed record ActivationReceipt(int Version, Guid RequestId);
public sealed record WorkerCommand(int Version, Guid RequestId, string Command,
    ImageProbe? Probe = null, ImageWork? Work = null, ImagePreviewRequest? Preview = null, PngOptimizationWork? Optimization = null,
    byte[]? AudioBytes = null, byte[]? PdfBytes = null)
{
    public const int MaximumAudioProbeBytes = 1024 * 1024;
    // A complete seekable PDF snapshot is required; base64 stays below the IPC frame limit.
    public const int MaximumPdfProbeBytes = 16 * 1024 * 1024;
    public void Validate()
    {
        if (Version != 1 || RequestId == Guid.Empty) throw new InvalidDataException("Invalid worker request identity.");
        if (Command == "pdf-probe")
        {
            if (PdfBytes is not { Length: > 0 and <= MaximumPdfProbeBytes } || AudioBytes is not null || Probe is not null ||
                Work is not null || Preview is not null || Optimization is not null) throw new InvalidDataException("Invalid PDF probe payload.");
            return;
        }
        if (PdfBytes is not null) throw new InvalidDataException("Unexpected PDF probe payload.");
        if (Command == "audio-probe")
        {
            if (AudioBytes is not { Length: > 0 and <= MaximumAudioProbeBytes } || Probe is not null || Work is not null ||
                Preview is not null || Optimization is not null) throw new InvalidDataException("Invalid audio probe payload.");
            return;
        }
        if (AudioBytes is not null) throw new InvalidDataException("Unexpected audio probe payload.");
        var valid = Command switch
        {
            "shutdown" or "capabilities" or "engine-info" => Probe is null && Work is null && Preview is null && Optimization is null,
            "image-probe" or "png-probe" => Probe is not null && Work is null && Preview is null && Optimization is null,
            "image-convert" => Work is not null && Probe is null && Preview is null && Optimization is null,
            "image-preview" => Preview is not null && Probe is null && Work is null && Optimization is null,
            "png-optimize" => Optimization is not null && Probe is null && Work is null && Preview is null,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Worker command payload does not match its operation.");
    }
}
public sealed record ImageEngineIdentity(string Package, string Version, string NativeVersion);
public sealed record WorkerReply(int Version, Guid RequestId, MediaCapability[] Capabilities, ImageEngineIdentity? Engine = null,
    ImageSourceFacts? Source = null, ImageWorkResult? ImageResult = null, ImagePreview? Preview = null, ImageFailure? Failure = null,
    AudioProbeFacts? Audio = null, PdfProbeFacts? Pdf = null);
