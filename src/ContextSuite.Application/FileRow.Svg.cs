using ContextSuite.Core.Operations;
using ContextSuite.Core.Svg;

namespace ContextSuite.Application;

internal sealed partial class FileRow
{
    internal SvgSourceFacts? SvgSource { get; private set; }
    internal SvgConversionOptions? SvgOptions { get; private set; }
    internal bool SvgResumeBlocked { get; private set; }

    internal void ResumeSvgFrom(FileRow previous)
    {
        SvgSource = previous.SvgSource;
        SvgOptions = previous.SvgOptions;
    }

    internal bool BeginSvg(SvgSourceFacts source)
    {
        if (SvgSource is not null && (SvgSource.Sha256 != source.Sha256 || SvgSource.FontFingerprint != source.FontFingerprint ||
            SvgSource.IntrinsicWidth != source.IntrinsicWidth || SvgSource.IntrinsicHeight != source.IntrinsicHeight ||
            SvgSource.MaximumSide != source.MaximumSide || SvgSource.MaximumPixels != source.MaximumPixels ||
            !SvgSource.FontNotices.SequenceEqual(source.FontNotices)))
        {
            BlockSvgResume("The SVG or its fonts changed. Start a new Convert command for this version. Completed copies were kept.");
            return false;
        }
        SvgSource = source;
        ApplyResult(new(Path, OperationState.Pending, "Preparing image copy"));
        return true;
    }

    internal void RememberSvgOptions(SvgConversionOptions options) => SvgOptions = options;

    internal void BlockSvgResume(string message)
    {
        SvgResumeBlocked = true;
        ApplyResult(new(Path, OperationState.Failed, message));
    }
}
