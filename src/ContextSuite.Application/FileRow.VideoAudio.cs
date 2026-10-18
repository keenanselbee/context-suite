using ContextSuite.Core.Audio;

namespace ContextSuite.Application;

internal sealed partial class FileRow
{
    internal AudioFileSource? VideoAudioSource { get; set; }
    internal bool VideoAudioResumeBlocked { get; set; }
}
