using System.Globalization;
using ContextSuite.Core.Dds;
using ContextSuite.Core.Images;

namespace ContextSuite.Application;

internal sealed partial class ConversionViewModel
{
    private DdsFormat _ddsFormat = DdsFormat.Bc7Srgb;
    private DdsPurpose _ddsPurpose;
    private DdsInterpretation _ddsInterpretation;
    private DdsMipPolicy _ddsMips;
    private DdsAlphaPolicy _ddsAlpha;
    private DdsColorOperation _ddsColorOperation;
    private DdsHeaderMode _ddsHeader;
    private DdsChannel _ddsRed, _ddsGreen = DdsChannel.Green;
    private bool _ddsChannelLoss, _ddsCoverage = true, _hasDdsPreview;
    private string _ddsCutout = "0.5", _ddsMip = "0";
    private uint _ddsQuality = 1;

    public bool IsDdsTarget => Target == ImageFormat.Dds;
    public bool IsDdsWorkflow => IsDdsTarget || Rows.Any(row => row.Selection.Facts?.Texture is not null);
    public bool IsDdsExport => IsDdsWorkflow && !IsDdsTarget && Target is not null;
    public bool CanResize => !IsDdsWorkflow;
    public IReadOnlyList<DdsFormat> DdsFormats { get; } = [DdsFormat.Bc1, DdsFormat.Bc1Srgb,
        DdsFormat.Bc2, DdsFormat.Bc2Srgb, DdsFormat.Bc3, DdsFormat.Bc3Srgb, DdsFormat.Bc4, DdsFormat.Bc4Snorm,
        DdsFormat.Bc5, DdsFormat.Bc5Snorm, DdsFormat.Bc7, DdsFormat.Bc7Srgb, DdsFormat.R8, DdsFormat.Rg8,
        DdsFormat.Rgba8, DdsFormat.Rgba8Srgb, DdsFormat.Bgra8, DdsFormat.Bgra8Srgb];
    public IReadOnlyList<DdsPurpose> DdsPurposes { get; } = Enum.GetValues<DdsPurpose>();
    public IReadOnlyList<DdsInterpretation> DdsInterpretations { get; } = Enum.GetValues<DdsInterpretation>();
    public IReadOnlyList<DdsMipPolicy> DdsMipPolicies { get; } = Enum.GetValues<DdsMipPolicy>();
    public IReadOnlyList<DdsAlphaPolicy> DdsAlphaPolicies { get; } = Enum.GetValues<DdsAlphaPolicy>();
    public IReadOnlyList<DdsColorOperation> DdsColorOperations { get; } = Enum.GetValues<DdsColorOperation>();
    public IReadOnlyList<DdsHeaderMode> DdsHeaders { get; } = Enum.GetValues<DdsHeaderMode>();
    public IReadOnlyList<DdsChannel> DdsChannels { get; } = Enum.GetValues<DdsChannel>();
    public IReadOnlyList<uint> DdsQualities { get; } = [0, 1, 2];
    public DdsFormat DdsFormat { get => _ddsFormat; set { if (Set(ref _ddsFormat, value)) RefreshPlan(); } }
    public DdsPurpose DdsPurpose { get => _ddsPurpose; set { if (Set(ref _ddsPurpose, value)) RefreshPlan(); } }
    public DdsInterpretation DdsInterpretation { get => _ddsInterpretation; set { if (Set(ref _ddsInterpretation, value)) RefreshPlan(); } }
    public DdsMipPolicy DdsMips { get => _ddsMips; set { if (Set(ref _ddsMips, value)) RefreshPlan(); } }
    public DdsAlphaPolicy DdsAlpha { get => _ddsAlpha; set { if (Set(ref _ddsAlpha, value)) RefreshPlan(); } }
    public DdsColorOperation DdsColorOperation { get => _ddsColorOperation; set { if (Set(ref _ddsColorOperation, value)) RefreshPlan(); } }
    public DdsHeaderMode DdsHeader { get => _ddsHeader; set { if (Set(ref _ddsHeader, value)) RefreshPlan(); } }
    public DdsChannel DdsRed { get => _ddsRed; set { if (Set(ref _ddsRed, value)) RefreshPlan(); } }
    public DdsChannel DdsGreen { get => _ddsGreen; set { if (Set(ref _ddsGreen, value)) RefreshPlan(); } }
    public bool DdsChannelLoss { get => _ddsChannelLoss; set { if (Set(ref _ddsChannelLoss, value)) RefreshPlan(); } }
    public bool DdsCoverage { get => _ddsCoverage; set { if (Set(ref _ddsCoverage, value)) RefreshPlan(); } }
    public string DdsCutout { get => _ddsCutout; set { if (Set(ref _ddsCutout, value)) RefreshPlan(); } }
    public string DdsMip { get => _ddsMip; set { if (Set(ref _ddsMip, value)) RefreshPlan(); } }
    public uint DdsQuality { get => _ddsQuality; set { if (Set(ref _ddsQuality, value)) RefreshPlan(); } }

    private uint SelectedMip()
    {
        if (!uint.TryParse(DdsMip, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > 14)
            throw new InvalidDataException("Select a whole-number mip level from 0 (largest) to 14. The level must exist in every exported DDS.");
        return value;
    }

    private DdsConversionOptions TextureOptions(uint? matte)
    {
        if (!float.TryParse(DdsCutout, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var threshold))
            throw new InvalidDataException("Cutout threshold must be a decimal between 0 and 1, such as 0.5.");
        return new(Format: IsDdsExport ? DdsFormat.Rgba8Srgb : DdsFormat,
            Purpose: DdsPurpose, SourceInterpretation: DdsInterpretation,
            ColorOperation: DdsColorOperation, Mips: IsDdsExport ? DdsMipPolicy.Remove : DdsMips,
            Alpha: DdsAlpha, MatteRgb: matte, CutoutThreshold: threshold, PreserveCutoutCoverage: DdsCoverage,
            RedChannel: DdsRed, GreenChannel: DdsGreen, ConfirmChannelLoss: DdsChannelLoss,
            Header: IsDdsExport ? DdsHeaderMode.Dx10 : DdsHeader, Quality: DdsQuality);
    }
}
