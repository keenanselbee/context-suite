namespace ContextSuite.Core.Images;

public enum ImageFailure
{
    InvalidInput, UnsupportedInput, SourceChanged, FileAccess, ResourceLimit,
    EngineFailure, ValidationFailed, TimedOut, WorkerTerminated
}

// Only stable categories cross IPC, never native diagnostics or private metadata.
public sealed class ImageFailureException(ImageFailure failure, Exception? inner = null)
    : IOException(Describe(failure), inner)
{
    public ImageFailure Failure { get; } = failure;

    public static string Describe(ImageFailure failure) => failure switch
    {
        ImageFailure.InvalidInput => "The image is damaged or invalid. Check the source file and try another copy.",
        ImageFailure.UnsupportedInput => "This image uses an unsupported format or feature. Choose a supported still image.",
        ImageFailure.SourceChanged => "The source changed after planning. Select it again to prepare a fresh plan.",
        ImageFailure.FileAccess => "A required file could not be accessed. Check permissions, available space and file locks.",
        ImageFailure.ResourceLimit => "The image exceeds a processing limit. Try a smaller image.",
        ImageFailure.EngineFailure => "The image engine could not finish. Retry the file or choose another format.",
        ImageFailure.ValidationFailed => "The output failed validation and was not published. Try another format or policy.",
        ImageFailure.TimedOut => "The image worker exceeded its time limit. No unvalidated output was published.",
        ImageFailure.WorkerTerminated => "The image worker stopped unexpectedly. This item was not retried; later items can continue.",
        _ => throw new InvalidDataException("Unknown image failure category.")
    };
}
