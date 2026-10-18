namespace ContextSuite.Core.Audio;

// The absolute demuxed stream index is bound to AudioFileSource's complete
// inventory and source digest, never to the order of a filtered UI list.
public sealed record VideoAudioSelection(int StreamIndex)
{
    public static bool IsVideoPath(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".webm" or ".mkv";

    public AudioStreamFacts Validate(AudioProbeFacts facts)
    {
        facts.Validate();
        if (StreamIndex < 0 || facts.Container is not ("mov,mp4,m4a,3gp,3g2,mj2" or "matroska,webm"))
            throw new InvalidDataException("Invalid video container or audio track selection.");
        return facts.Streams.SingleOrDefault(stream => stream.Index == StreamIndex && stream.Kind == "audio") ??
            throw new InvalidDataException("The selected audio track is absent from this video.");
    }
}
