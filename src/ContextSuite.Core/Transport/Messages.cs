using ContextSuite.Core.Operations;
using ContextSuite.Core.Images;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Office;
using ContextSuite.Core.Tiff;
using ContextSuite.Core.Svg;
using ContextSuite.Core.Gif;

namespace ContextSuite.Core.Transport;

public sealed record ActivationMessage(int Version, OperationRequest? Request);
public sealed record ActivationReply(int Version, Guid RequestId, bool Accepted, string Message);
public sealed record ActivationReceipt(int Version, Guid RequestId);
public sealed record WorkerCommand(int Version, Guid RequestId, string Command,
    ImageProbe? Probe = null, ImageWork? Work = null, ImagePreviewRequest? Preview = null, PngOptimizationWork? Optimization = null,
    byte[]? AudioBytes = null, byte[]? PdfBytes = null, AudioFileProbe? AudioFile = null, FlacOptimizationWork? FlacWork = null,
    AudioFormat? AudioTarget = null, AudioConversionWork? AudioWork = null, PdfFileProbe? PdfFile = null, PdfOptimizationWork? PdfWork = null,
    PdfPageWork? PdfPage = null, ImagePdfWork? ImagePdf = null, OfficeExportWork? OfficeWork = null, OfficePdfWork? OfficePdf = null,
    PdfDocxWork? PdfDocx = null, TiffProbe? TiffProbe = null, TiffWork? TiffWork = null, TiffPreviewRequest? TiffPreview = null,
    SvgProbe? SvgProbe = null, SvgWork? SvgWork = null, GifProbe? GifProbe = null, GifWork? GifWork = null,
    PdfFileProbe? PdfDarkProbe = null, PdfDarkWork? PdfDarkWork = null)
{
    public const int MaximumAudioProbeBytes = 1024 * 1024;
    // A complete seekable PDF snapshot is required; base64 stays below the IPC frame limit.
    public const int MaximumPdfProbeBytes = 16 * 1024 * 1024;
    public void Validate()
    {
        if (Version != 1 || RequestId == Guid.Empty) throw new InvalidDataException("Invalid worker request identity.");
        if (Command is "pdf-dark-probe" or "pdf-dark-convert")
        {
            if (Probe is not null || Work is not null || Preview is not null || Optimization is not null || AudioBytes is not null ||
                PdfBytes is not null || AudioFile is not null || FlacWork is not null || AudioTarget is not null || AudioWork is not null ||
                PdfFile is not null || PdfWork is not null || PdfPage is not null || ImagePdf is not null || OfficeWork is not null ||
                OfficePdf is not null || PdfDocx is not null || TiffProbe is not null || TiffWork is not null || TiffPreview is not null ||
                SvgProbe is not null || SvgWork is not null || GifProbe is not null || GifWork is not null)
                throw new InvalidDataException("Unexpected PDF dark-mode request data.");
            if (Command == "pdf-dark-probe" && PdfDarkProbe is { } probe && PdfDarkWork is null) probe.Validate();
            else if (Command == "pdf-dark-convert" && PdfDarkWork is { } work && PdfDarkProbe is null) work.Validate();
            else throw new InvalidDataException("Invalid PDF dark-mode worker payload.");
            return;
        }
        if (PdfDarkProbe is not null || PdfDarkWork is not null) throw new InvalidDataException("Unexpected PDF dark-mode payload.");
        if (Command is "gif-probe" or "gif-convert" or "gif-release")
        {
            if (Probe is not null || Work is not null || Preview is not null || Optimization is not null || AudioBytes is not null ||
                PdfBytes is not null || AudioFile is not null || FlacWork is not null || AudioTarget is not null || AudioWork is not null ||
                PdfFile is not null || PdfWork is not null || PdfPage is not null || ImagePdf is not null || OfficeWork is not null ||
                OfficePdf is not null || PdfDocx is not null || TiffProbe is not null || TiffWork is not null || TiffPreview is not null ||
                SvgProbe is not null || SvgWork is not null)
                throw new InvalidDataException("Unexpected GIF request data.");
            if (Command == "gif-probe" && GifProbe is { } probe && GifWork is null) probe.Validate();
            else if (Command == "gif-convert" && GifWork is { } work && GifProbe is null) work.Validate();
            else if (Command != "gif-release" || GifProbe is not null || GifWork is not null)
                throw new InvalidDataException("Invalid GIF worker payload.");
            return;
        }
        if (GifProbe is not null || GifWork is not null) throw new InvalidDataException("Unexpected GIF payload.");
        if (Command is "svg-probe" or "svg-convert")
        {
            if (Probe is not null || Work is not null || Preview is not null || Optimization is not null || AudioBytes is not null ||
                PdfBytes is not null || AudioFile is not null || FlacWork is not null || AudioTarget is not null || AudioWork is not null ||
                PdfFile is not null || PdfWork is not null || PdfPage is not null || ImagePdf is not null || OfficeWork is not null ||
                OfficePdf is not null || PdfDocx is not null || TiffProbe is not null || TiffWork is not null || TiffPreview is not null)
                throw new InvalidDataException("Unexpected SVG request data.");
            if (Command == "svg-probe" && SvgProbe is { } probe && SvgWork is null) probe.Validate();
            else if (Command == "svg-convert" && SvgWork is { } work && SvgProbe is null) work.Validate();
            else throw new InvalidDataException("Invalid SVG worker payload.");
            return;
        }
        if (SvgProbe is not null || SvgWork is not null) throw new InvalidDataException("Unexpected SVG payload.");
        if (Command is "tiff-probe" or "tiff-convert" or "tiff-preview")
        {
            if (Probe is not null || Work is not null || Preview is not null || Optimization is not null || AudioBytes is not null ||
                PdfBytes is not null || AudioFile is not null || FlacWork is not null || AudioTarget is not null || AudioWork is not null ||
                PdfFile is not null || PdfWork is not null || PdfPage is not null || ImagePdf is not null || OfficeWork is not null ||
                OfficePdf is not null || PdfDocx is not null ||
                (TiffProbe is not null ? 1 : 0) + (TiffWork is not null ? 1 : 0) + (TiffPreview is not null ? 1 : 0) != 1)
                throw new InvalidDataException("Unexpected TIFF request data.");
            if (Command == "tiff-probe" && TiffProbe is { } probe && TiffWork is null && TiffPreview is null) probe.Validate();
            else if (Command == "tiff-convert" && TiffWork is { } work && TiffProbe is null && TiffPreview is null) work.Validate();
            else if (Command == "tiff-preview" && TiffPreview is { } preview && TiffProbe is null && TiffWork is null) preview.Validate();
            else throw new InvalidDataException("Invalid TIFF worker payload.");
            return;
        }
        if (TiffProbe is not null || TiffWork is not null || TiffPreview is not null)
            throw new InvalidDataException("Unexpected TIFF payload.");
        if (Command is "pdf-docx-probe" or "pdf-to-docx")
        {
            if (Probe is not null || Work is not null || Preview is not null || Optimization is not null || AudioBytes is not null ||
                PdfBytes is not null || AudioFile is not null || FlacWork is not null || AudioTarget is not null || AudioWork is not null ||
                PdfWork is not null || PdfPage is not null || ImagePdf is not null || OfficeWork is not null || OfficePdf is not null)
                throw new InvalidDataException("Unexpected data in PDF-to-DOCX request.");
            if (Command == "pdf-docx-probe")
            {
                if (PdfFile is null || PdfDocx is not null) throw new InvalidDataException("Invalid PDF-to-DOCX probe.");
                PdfFile.Validate();
            }
            else
            {
                if (PdfDocx is null || PdfFile is not null) throw new InvalidDataException("Invalid PDF-to-DOCX work.");
                PdfDocx.Validate();
            }
            return;
        }
        if (PdfDocx is not null) throw new InvalidDataException("Unexpected PDF-to-DOCX payload.");
        if (Command == "office-pdf-validate")
        {
            if (OfficePdf is null || OfficeWork is not null || Probe is not null || Work is not null || Preview is not null || Optimization is not null ||
                AudioBytes is not null || PdfBytes is not null || AudioFile is not null || FlacWork is not null || AudioTarget is not null ||
                AudioWork is not null || PdfFile is not null || PdfWork is not null || PdfPage is not null || ImagePdf is not null)
                throw new InvalidDataException("Unexpected data in Office PDF validation request.");
            OfficePdf.Validate();
            return;
        }
        if (OfficePdf is not null) throw new InvalidDataException("Unexpected Office PDF validation payload.");
        if (Command == "office-export")
        {
            if (OfficeWork is null || Probe is not null || Work is not null || Preview is not null || Optimization is not null ||
                AudioBytes is not null || PdfBytes is not null || AudioFile is not null || FlacWork is not null || AudioTarget is not null ||
                AudioWork is not null || PdfFile is not null || PdfWork is not null || PdfPage is not null || ImagePdf is not null)
                throw new InvalidDataException("Unexpected data in Office export request.");
            OfficeWork.Validate();
            return;
        }
        if (OfficeWork is not null) throw new InvalidDataException("Unexpected Office export payload.");
        if (Command == "images-to-pdf")
        {
            if (ImagePdf is null || Probe is not null || Work is not null || Preview is not null || Optimization is not null ||
                AudioBytes is not null || PdfBytes is not null || AudioFile is not null || FlacWork is not null || AudioTarget is not null ||
                AudioWork is not null || PdfFile is not null || PdfWork is not null || PdfPage is not null)
                throw new InvalidDataException("Unexpected data in combined PDF request.");
            ImagePdf.Validate();
            return;
        }
        if (ImagePdf is not null) throw new InvalidDataException("Unexpected combined PDF payload.");
        if (Command is "pdf-raster-probe" or "pdf-render-page")
        {
            if (Probe is not null || Work is not null || Preview is not null || Optimization is not null || AudioBytes is not null ||
                PdfBytes is not null || AudioFile is not null || FlacWork is not null || AudioTarget is not null || AudioWork is not null || PdfWork is not null)
                throw new InvalidDataException("Unexpected data in PDF rendering request.");
            if (Command == "pdf-raster-probe")
            {
                if (PdfFile is null || PdfPage is not null) throw new InvalidDataException("Invalid PDF page inspection payload.");
                PdfFile.Validate();
            }
            else
            {
                if (PdfPage is null || PdfFile is not null) throw new InvalidDataException("Invalid PDF page rendering payload.");
                PdfPage.Validate();
            }
            return;
        }
        if (PdfPage is not null) throw new InvalidDataException("Unexpected PDF page payload.");
        if (Command is "pdf-file-probe" or "pdf-optimize")
        {
            if (Probe is not null || Work is not null || Preview is not null || Optimization is not null || AudioBytes is not null ||
                PdfBytes is not null || AudioFile is not null || FlacWork is not null || AudioTarget is not null || AudioWork is not null)
                throw new InvalidDataException("Unexpected data in PDF file request.");
            if (Command == "pdf-file-probe")
            {
                if (PdfFile is null || PdfWork is not null) throw new InvalidDataException("Invalid PDF file probe payload.");
                PdfFile.Validate();
            }
            else
            {
                if (PdfWork is null || PdfFile is not null) throw new InvalidDataException("Invalid PDF optimization payload.");
                PdfWork.Validate();
            }
            return;
        }
        if (PdfFile is not null || PdfWork is not null) throw new InvalidDataException("Unexpected PDF file payload.");
        if (Command is "audio-file-probe" or "video-audio-probe" or "audio-convert")
        {
            if (Probe is not null || Work is not null || Preview is not null || Optimization is not null || AudioBytes is not null ||
                PdfBytes is not null || FlacWork is not null) throw new InvalidDataException("Unexpected data in audio conversion request.");
            if (Command == "video-audio-probe")
            {
                if (AudioWork is not null || AudioTarget is not null || AudioFile is null || AudioFile.ItemId == Guid.Empty ||
                    !PdfFileProbe.ValidPath(AudioFile.Path) || !VideoAudioSelection.IsVideoPath(AudioFile.Path))
                    throw new InvalidDataException("Invalid video audio probe source.");
            }
            else if (Command == "audio-file-probe")
            {
                if (AudioWork is not null || AudioTarget is not { } target || !Enum.IsDefined(target) || AudioFile is null ||
                    AudioFile.ItemId == Guid.Empty || string.IsNullOrWhiteSpace(AudioFile.Path) || !Path.IsPathFullyQualified(AudioFile.Path) ||
                    AudioFile.Path.Length > 32700 || AudioFile.Path.IndexOfAny(['\0', '\r', '\n']) >= 0)
                    throw new InvalidDataException("Invalid audio file probe target or source.");
            }
            else
            {
                if (AudioFile is not null || AudioTarget is not null || AudioWork is null) throw new InvalidDataException("Invalid audio conversion payload.");
                AudioWork.Validate();
            }
            return;
        }
        if (AudioTarget is not null || AudioWork is not null) throw new InvalidDataException("Unexpected audio conversion payload.");
        if (Command is "flac-probe" or "flac-optimize")
        {
            if (Probe is not null || Work is not null || Preview is not null || Optimization is not null || AudioBytes is not null || PdfBytes is not null)
                throw new InvalidDataException("Unexpected data in FLAC worker request.");
            if (Command == "flac-probe")
            {
                if (AudioFile is null || FlacWork is not null || AudioFile.ItemId == Guid.Empty || string.IsNullOrWhiteSpace(AudioFile.Path) ||
                    !Path.IsPathFullyQualified(AudioFile.Path) || AudioFile.Path.Length > 32700 || AudioFile.Path.IndexOfAny(['\0', '\r', '\n']) >= 0)
                    throw new InvalidDataException("Invalid FLAC probe identity or path.");
            }
            else
            {
                if (FlacWork?.Source is null || AudioFile is not null || FlacWork.Policy != FlacOptimizationPlan.Policy ||
                    string.IsNullOrWhiteSpace(FlacWork.TemporaryPath) || !Path.IsPathFullyQualified(FlacWork.TemporaryPath) ||
                    FlacWork.TemporaryPath.Length > 32700 || FlacWork.TemporaryPath.IndexOfAny(['\0', '\r', '\n']) >= 0)
                    throw new InvalidDataException("Invalid FLAC optimization request.");
                FlacWork.Source.Validate();
            }
            return;
        }
        if (AudioFile is not null || FlacWork is not null) throw new InvalidDataException("Unexpected file-audio payload.");
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
    AudioProbeFacts? Audio = null, PdfProbeFacts? Pdf = null, AudioFileSource? AudioSource = null, AudioWorkResult? AudioResult = null,
    PdfFileSource? PdfSource = null, PdfWorkResult? PdfResult = null,
    PdfRasterSource? PdfRaster = null, PdfPageResult? PdfPageResult = null, ImagePdfResult? ImagePdfResult = null,
    OfficeExportCandidate? OfficeCandidate = null, OfficePdfResult? OfficePdfResult = null,
    PdfDocxSource? PdfDocxSource = null, PdfDocxResult? PdfDocxResult = null,
    TiffSourceFacts? TiffSource = null, TiffWorkResult? TiffResult = null, ImagePreview? TiffPreview = null,
    SvgSourceFacts? SvgSource = null, SvgWorkResult? SvgResult = null,
    GifSourceFacts? GifSource = null, GifWorkResult? GifResult = null,
    PdfDarkSource? PdfDarkSource = null, PdfDarkResult? PdfDarkResult = null,
    int? PdfDarkFailurePage = null);
