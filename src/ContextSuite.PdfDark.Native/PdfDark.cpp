#include <qpdf/Buffer.hh>
#include <qpdf/Pipeline.hh>
#include <qpdf/BufferInputSource.hh>
#include <qpdf/QPDF.hh>
#include <qpdf/QPDFExc.hh>
#include <qpdf/QPDFObjectHandle.hh>
#include <qpdf/QPDFPageObjectHelper.hh>
#include <qpdf/QPDFWriter.hh>
#include "PdfDarkImagePolicy.h"
#include "PdfDarkDiagramPolicy.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <exception>
#include <iomanip>
#include <iostream>
#include <limits>
#include <memory>
#include <optional>
#include <map>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
#include <fcntl.h>
#include <io.h>
#include <d2d1.h>
#include <dwrite_3.h>

namespace {
constexpr size_t MaximumSourceBytes = 64ull * 1024 * 1024;
constexpr size_t MaximumOutputBytes = 128ull * 1024 * 1024;
constexpr size_t MaximumDecodedBytes = 128ull * 1024 * 1024;
constexpr size_t MaximumImagePixels = 16ull * 1024 * 1024;
// Aggregate work includes repeated mask/contrast validation across pages;
// individual image and stream allocations retain their smaller limits.
constexpr size_t MaximumTotalPixels = 256ull * 1024 * 1024;
constexpr size_t MaximumContentBytes = 16ull * 1024 * 1024;
constexpr size_t MaximumOperations = 2'000'000;
constexpr size_t MaximumTotalOperations = 8'000'000;
constexpr size_t MaximumTotalDecodedBytes = 1024ull * 1024 * 1024;
constexpr unsigned MaximumPages = 512;
constexpr unsigned MaximumObjects = 32768;
constexpr unsigned MaximumDepth = 16;
// Font-wide boxes can include broad math/symbol glyphs. Retain their actual
// extent for conservative contrast checks; never clamp it to ordinary text width.
constexpr double MaximumFontMetricEm = 64;
constexpr std::array<double, 3> Paper{18.0 / 255, 19.0 / 255, 20.0 / 255};
constexpr std::array<double, 3> Ink{244.0 / 255, 235.0 / 255, 216.0 / 255};
constexpr std::array<double, 3> Panel{.105, .110, .120};
using Color = std::array<double, 3>;

class Problem final : public std::runtime_error {
  public:
    Problem(int code, std::string const& message) : std::runtime_error(message), code(code) {}
    int code;
};

void Require(bool condition, char const* message)
{
    if (!condition) throw Problem(4, message);
}
void RequireLimit(bool condition, char const* message)
{
    if (!condition) throw Problem(5, message);
}
[[noreturn]] void Unsupported(char const* message) { throw Problem(3, message); }

class BoundedPdfStreamSink final : public Pipeline {
  public:
    BoundedPdfStreamSink(std::string& bytes, size_t maximum, char const* message)
        : Pipeline("bounded PDF stream", nullptr), _bytes(bytes), _maximum(maximum), _message(message) {}
    void write(unsigned char const* data, size_t length) override
    {
        if (length > _maximum - _bytes.size()) {
            _exceeded = true;
            RequireLimit(false, _message);
        }
        _bytes.append(reinterpret_cast<char const*>(data), length);
    }
    void finish() override {}
    bool Exceeded() const { return _exceeded; }
  private:
    std::string& _bytes;
    size_t _maximum;
    char const* _message;
    bool _exceeded = false;
};

std::shared_ptr<Buffer> ReadBoundedPdfStream(QPDFObjectHandle stream, size_t maximum, char const* message)
{
    Require(stream.isStream(), "Invalid bounded PDF stream.");
    std::string bytes;
    BoundedPdfStreamSink sink(bytes, maximum, message);
    bool decoded = stream.pipeStreamData(&sink, nullptr, 0, qpdf_dl_all);
    RequireLimit(!sink.Exceeded(), message);
    Require(decoded, "Bounded PDF stream cannot be decoded.");
    return std::make_shared<Buffer>(std::move(bytes));
}

#include "PdfDarkStructurePolicy.h"

double Clamp(double value) { return std::clamp(value, 0.0, 1.0); }
double Light(Color color) { return (*std::max_element(color.begin(), color.end()) + *std::min_element(color.begin(), color.end())) / 2; }
double Saturation(Color color) { return *std::max_element(color.begin(), color.end()) - *std::min_element(color.begin(), color.end()); }

double Luminance(Color color)
{
    for (auto& channel : color)
        channel = channel <= .04045 ? channel / 12.92 : std::pow((channel + .055) / 1.055, 2.4);
    return .2126 * color[0] + .7152 * color[1] + .0722 * color[2];
}

Color Tone(Color source, double luminance)
{
    if (std::abs(Luminance(source) - luminance) <= .000002) return source;
    bool lighten = Luminance(source) < luminance;
    double low = 0, high = 1;
    Color result{};
    for (unsigned iteration = 0; iteration < 24; ++iteration) {
        double mix = (low + high) / 2;
        for (size_t channel = 0; channel < 3; ++channel)
            result[channel] = source[channel] * (1 - mix) + (lighten ? mix : 0);
        if ((Luminance(result) < luminance) == lighten) low = mix;
        else high = mix;
    }
    return result;
}

Color Recolor(Color source, bool foreground, bool large)
{
    double span = Saturation(source);
    if (!foreground) {
        if (large) return Paper;
        if (std::abs(Luminance(source) - .135) <= .000002) return source;
        if (span < .15) return Panel;
        // Colored fills must remain visible against paper while allowing light
        // labels. HSL lightness alone leaves saturated blue effectively dark.
        return Tone(source, .135);
    }
    // Map into a stable readable range instead of repeatedly scaling lightness.
    // The map is a projection: applying it to its own result does not drift.
    double luminance = Luminance(source);
    // Lightening a muted accent can reduce its channel span below the neutral
    // threshold. Recognize both output endpoints before classifying neutral ink.
    if (std::abs(luminance - .225) <= .000002 ||
        std::abs(luminance - .800) <= .000002) return source;
    if (span < .15) return Ink;
    if (luminance < .225 - .000002) return Tone(source, .225);
    if (luminance > .800 + .000002) return Tone(source, .800);
    return source;
}

bool SameColor(Color left, Color right)
{
    for (size_t i = 0; i < left.size(); ++i)
        if (std::abs(left[i] - right[i]) > .000002) return false;
    return true;
}

bool PalettePanel(Color color)
{
    return SameColor(color, Panel) || SameColor(color, Paper) ||
        std::abs(Luminance(color) - .135) <= .000002;
}

bool PaletteForeground(Color color)
{
    return SameColor(color, Ink) || std::abs(Luminance(color) - .225) <= .000002;
}

Color Composite(Color color, Color background, double alpha)
{
    for (size_t i = 0; i < color.size(); ++i)
        color[i] = color[i] * alpha + background[i] * (1 - alpha);
    return color;
}

double Contrast(Color first, Color second)
{
    double a = Luminance(first), b = Luminance(second);
    return (std::max(a, b) + .05) / (std::min(a, b) + .05);
}

Color ToneComposite(Color source, double alpha, double target)
{
    double current = Luminance(Composite(source, Paper, alpha));
    if (std::abs(current - target) <= .000002) return source;
    bool lighten = current < target;
    Color extreme = lighten ? Color{1, 1, 1} : Color{0, 0, 0};
    double limit = Luminance(Composite(extreme, Paper, alpha));
    if (lighten ? limit < target : limit > target)
        Unsupported("PDF opacity cannot reach the reading-palette contrast target.");
    double low = 0, high = 1;
    Color result{};
    for (unsigned iteration = 0; iteration < 24; ++iteration) {
        double mix = (low + high) / 2;
        for (size_t i = 0; i < source.size(); ++i)
            result[i] = source[i] * (1 - mix) + extreme[i] * mix;
        if ((Luminance(Composite(result, Paper, alpha)) < target) == lighten) low = mix;
        else high = mix;
    }
    return result;
}

#include "PdfDarkShadingPolicy.h"
bool IsLineArt(std::string const& pixels, int channels, std::string const& mask);
#include "PdfDarkRetainedImagePolicy.h"

std::string Number(double value)
{
    Require(std::isfinite(value), "Nonfinite PDF number.");
    std::ostringstream output;
    output << std::fixed << std::setprecision(6) << value;
    return output.str();
}

std::string SetColor(Color color, bool stroke)
{
    return Number(color[0]) + " " + Number(color[1]) + " " + Number(color[2]) + (stroke ? " RG\n" : " rg\n");
}

Color ReadColor(std::vector<QPDFObjectHandle> const& operands, std::string const& op)
{
    size_t count = op == "g" || op == "G" ? 1 : op == "k" || op == "K" ? 4 : 3;
    Require(operands.size() == count, "Malformed PDF color operands.");
    std::array<double, 4> values{};
    for (size_t i = 0; i < count; ++i) {
        Require(operands[i].isNumber(), "Invalid PDF color value.");
        values[i] = operands[i].getNumericValue();
        Require(std::isfinite(values[i]) && values[i] >= 0 && values[i] <= 1, "PDF color component is outside its range.");
    }
    if (count == 1) return {values[0], values[0], values[0]};
    if (count == 3) return {values[0], values[1], values[2]};
    return {1 - std::min(1.0, values[0] + values[3]),
        1 - std::min(1.0, values[1] + values[3]), 1 - std::min(1.0, values[2] + values[3])};
}

struct Bounds {
    double left = 0, bottom = 0, right = 0, top = 0;
    bool valid = false;
};

struct ImageBacking {
    std::vector<Bounds> rectangles;
    Color color;
    std::set<size_t> preservedText;
    bool pageCoordinates = false;
    std::array<double, 6> matrix{1, 0, 0, 1, 0, 0};
};

struct ImageBackingInsertion {
    size_t offset;
    ImageBacking backing;
};

std::string LocalImageBacking(ImageBacking backing)
{
    for (double value : backing.color)
        Require(std::isfinite(value) && value >= 0 && value <= 1, "PDF image backing color changed.");
    Require(!backing.rectangles.empty() && backing.rectangles.size() <= 64, "PDF image backing region count changed.");
    std::string result;
    for (auto rectangle : backing.rectangles) {
        Require(rectangle.valid && (backing.pageCoordinates ||
            rectangle.left >= 0 && rectangle.bottom >= 0 && rectangle.right <= 1 && rectangle.top <= 1) && rectangle.left < rectangle.right &&
            rectangle.bottom < rectangle.top, "PDF image backing placement changed.");
        for (double coordinate : {rectangle.left, rectangle.bottom, rectangle.right, rectangle.top})
            Require(std::isfinite(coordinate) && std::abs(coordinate) <= 200000000,
                "PDF image backing coordinate changed.");
        result += "/Artifact BMC\nq\n";
        if (backing.pageCoordinates) {
            for (double value : backing.matrix) {
                Require(std::isfinite(value) && std::abs(value) <= 200000000,
                    "PDF image backing transform changed.");
                result += Number(value) + " ";
            }
            result += "cm\n";
        }
        result += SetColor(backing.color, false) + Number(rectangle.left) + " " + Number(rectangle.bottom) +
            " " + Number(rectangle.right - rectangle.left) + " " + Number(rectangle.top - rectangle.bottom) +
            " re\nf\nQ\nEMC\n";
    }
    return result;
}

void Include(Bounds& bounds, double x, double y)
{
    Require(std::isfinite(x) && std::isfinite(y), "Nonfinite PDF paint position.");
    if (!bounds.valid) bounds = {x, y, x, y, true};
    else {
        bounds.left = std::min(bounds.left, x);
        bounds.bottom = std::min(bounds.bottom, y);
        bounds.right = std::max(bounds.right, x);
        bounds.top = std::max(bounds.top, y);
    }
}

bool Intersects(Bounds a, Bounds b)
{
    return a.valid && b.valid && a.left < b.right && b.left < a.right &&
        a.bottom < b.top && b.bottom < a.top;
}

bool Contains(Bounds outer, Bounds inner)
{
    constexpr double margin = .25;
    return outer.valid && inner.valid && inner.left >= outer.left + margin &&
        inner.right <= outer.right - margin && inner.bottom >= outer.bottom + margin &&
        inner.top <= outer.top - margin;
}

#include "PdfDarkFontPolicy.h"
#include "PdfDarkSystemFontPolicy.h"
#include "PdfDarkCffFontPolicy.h"

struct FontMetrics {
    bool cid = false;
    bool approximateAdvance = false;
    double defaultWidth = 1000;
    double left = -.5;
    double bottom = -1.5;
    double right = 1.5;
    double top = 1.5;
    std::map<unsigned, double> widths;
    std::optional<TrueTypeGlyphBounds> glyphBounds;
    std::optional<SystemFontGlyphBounds> systemGlyphBounds;
    std::shared_ptr<CffFontGlyphBounds> cffGlyphBounds;

    double Width(unsigned code) const
    {
        auto found = widths.find(code);
        return found == widths.end() ? defaultWidth : found->second;
    }
};

FontMetrics ReadFont(QPDFObjectHandle font)
{
    FontMetrics result;
    bool hasBounds = false;
    Require(font.isDictionary(), "PDF text font is missing.");
    auto subtype = font.getKey("/Subtype");
    result.cid = subtype.isNameAndEquals("/Type0");
    QPDFObjectHandle descriptor;
    if (result.cid) {
        if (!font.getKey("/Encoding").isNameAndEquals("/Identity-H"))
            Unsupported("PDF text encoding cannot be measured for contrast.");
        auto descendants = font.getKey("/DescendantFonts");
        Require(descendants.isArray() && descendants.getArrayNItems() == 1,
            "PDF text descendant is missing.");
        auto descendant = descendants.getArrayItem(0);
        descriptor = descendant.getKey("/FontDescriptor");
        auto dw = descendant.getKey("/DW");
        if (dw.isNumber()) result.defaultWidth = dw.getNumericValue();
        auto widths = descendant.getKey("/W");
        if (!widths.isNull()) {
            Require(widths.isArray() && widths.getArrayNItems() <= 65536,
                "Invalid PDF CID widths.");
            for (int i = 0; i < widths.getArrayNItems();) {
                auto start = widths.getArrayItem(i++);
                Require(start.isInteger() && start.getIntValue() >= 0 &&
                    start.getIntValue() <= 65535 && i < widths.getArrayNItems(),
                    "Invalid PDF CID width range.");
                unsigned first = static_cast<unsigned>(start.getIntValue());
                auto next = widths.getArrayItem(i++);
                if (next.isArray()) {
                    Require(static_cast<unsigned>(next.getArrayNItems()) <= 65536u - first,
                        "Invalid PDF CID width range.");
                    for (int j = 0; j < next.getArrayNItems(); ++j) {
                        auto width = next.getArrayItem(j);
                        Require(width.isNumber() && first + static_cast<unsigned>(j) <= 65535,
                            "Invalid PDF CID width.");
                        result.widths[first + static_cast<unsigned>(j)] = width.getNumericValue();
                    }
                } else {
                    Require(next.isInteger() && i < widths.getArrayNItems(), "Invalid PDF CID width range.");
                    auto width = widths.getArrayItem(i++);
                    Require(width.isNumber() && next.getIntValue() >= first &&
                        next.getIntValue() <= 65535, "Invalid PDF CID width range.");
                    for (unsigned code = first; code <= static_cast<unsigned>(next.getIntValue()); ++code)
                        result.widths[code] = width.getNumericValue();
                }
            }
        }
    } else {
        descriptor = font.getKey("/FontDescriptor");
        auto first = font.getKey("/FirstChar");
        auto widths = font.getKey("/Widths");
        if (first.isInteger() && widths.isArray()) {
            Require(first.getIntValue() >= 0 && first.getIntValue() <= 255 &&
                widths.getArrayNItems() <= 256 - first.getIntValue(),
                "Invalid PDF font width range.");
            for (int i = 0; i < widths.getArrayNItems(); ++i) {
                auto width = widths.getArrayItem(i);
                Require(width.isNumber() && first.getIntValue() + i <= 255,
                    "Invalid PDF font width.");
                result.widths[static_cast<unsigned>(first.getIntValue() + i)] = width.getNumericValue();
            }
        } else {
            auto base = font.getKey("/BaseFont");
            static std::set<std::string> const standard{
                "/Helvetica", "/Helvetica-Bold", "/Helvetica-Oblique", "/Helvetica-BoldOblique",
                "/Times-Roman", "/Times-Bold", "/Times-Italic", "/Times-BoldItalic",
                "/Courier", "/Courier-Bold", "/Courier-Oblique", "/Courier-BoldOblique",
                "/Symbol", "/ZapfDingbats"};
            if (!base.isName() || !standard.count(base.getName()))
                Unsupported("PDF text font widths cannot be measured for contrast.");
            // Standard 14 glyphs fit within one em; this overbounds their advances.
            result.approximateAdvance = true;
            result.left = -.25;
            result.bottom = -.35;
            result.right = 1.15;
            result.top = 1.15;
            hasBounds = true;
        }
    }
    if (descriptor.isDictionary()) {
        auto bbox = descriptor.getKey("/FontBBox");
        if (bbox.isArray() && bbox.getArrayNItems() == 4) {
            std::array<double, 4> values{};
            for (size_t i = 0; i < 4; ++i) {
                auto value = bbox.getArrayItem(static_cast<int>(i));
                Require(value.isNumber(), "Invalid PDF font bounding box.");
                values[i] = value.getNumericValue() / 1000;
                Require(std::isfinite(values[i]), "Nonfinite PDF font bounding box.");
                RequireLimit(std::abs(values[i]) <= MaximumFontMetricEm,
                    "PDF font bounding box exceeds the resource bound.");
            }
            Require(values[0] <= values[2] && values[1] <= values[3],
                "Invalid PDF font bounding box.");
            result.left = values[0];
            result.bottom = values[1];
            result.right = values[2];
            result.top = values[3];
            hasBounds = true;
        }
    }
    if (!hasBounds)
        Unsupported("PDF font bounding box cannot be measured for contrast.");
    Require(result.defaultWidth >= 0 && result.defaultWidth <= 2000,
        "PDF text width is outside the contrast policy.");
    for (auto const& [code, width] : result.widths)
        if (!std::isfinite(width) || width < 0 || width > 2000)
            Unsupported("PDF text width is outside the contrast policy.");
    result.glyphBounds = ReadEmbeddedTrueTypeGlyphBounds(font);
    if (!result.glyphBounds) result.cffGlyphBounds = ReadEmbeddedCffGlyphBounds(font);
    if (!result.glyphBounds && !result.cffGlyphBounds) result.systemGlyphBounds = ReadSystemTrueTypeGlyphBounds(font);
    return result;
}

struct PaintColorSpace {
    size_t components = 1;
    bool overprintSafe = false;
    std::shared_ptr<GrayIccCurve> gray;
    std::optional<std::array<double, 3>> calibrated;
    std::shared_ptr<RetainedIccTransform> rgb;
};

PaintColorSpace ReadPaintColorSpace(QPDFObjectHandle space, RetainedImageBudget& budget)
{
    if (space.isNameAndEquals("/DeviceGray")) return {1, false};
    if (space.isNameAndEquals("/DeviceRGB")) return {3, true};
    if (space.isNameAndEquals("/DeviceCMYK")) return {4, false};
    if (space.isArray() && space.getArrayNItems() == 2) {
        if (space.getArrayItem(0).isNameAndEquals("/ICCBased")) {
            auto profile = space.getArrayItem(1);
            Require(profile.isStream(), "Invalid PDF paint ICC profile.");
            auto components = profile.getDict().getKey("/N");
            Require(components.isInteger(), "Invalid PDF paint ICC component count.");
            if (components.getIntValue() == 1)
                return {1, true, std::make_shared<GrayIccCurve>(ReadGrayIcc(profile, budget))};
            if (components.getIntValue() == 3)
                return {3, true, {}, {}, std::make_shared<RetainedIccTransform>(
                    ReadRetainedIccTransform(profile, budget))};
            Unsupported("PDF paint ICC component count is unsupported.");
        }
        if (space.getArrayItem(0).isNameAndEquals("/CalRGB"))
            return {3, true, {}, ReadCalRgb(space)};
    }
    Unsupported("PDF named paint color space is unsupported.");
}

Color ReadPaintColor(std::vector<QPDFObjectHandle> const& operands, PaintColorSpace const& space)
{
    auto color = ReadColor(operands, space.components == 1 ? "g" : space.components == 4 ? "k" : "rg");
    if (space.gray) {
        double value = Clamp(space.gray->Convert(color[0]));
        return {value, value, value};
    }
    if (space.rgb) return space.rgb->Convert(color);
    if (space.calibrated) for (size_t c = 0; c < 3; ++c) {
        GrayIccCurve curve; curve.gamma = (*space.calibrated)[c];
        color[c] = Clamp(curve.Convert(color[c]));
    }
    return color;
}

struct State {
    Color fill{0, 0, 0};
    Color stroke{0, 0, 0};
    PaintColorSpace fillSpace, strokeSpace;
    bool fillOverprint = false, strokeOverprint = false;
    std::array<double, 6> matrix{1, 0, 0, 1, 0, 0};
    double fillAlpha = 1;
    double strokeAlpha = 1;
    int textMode = 0;
    bool fillKnown = true;
    bool strokeKnown = true;
    bool clipped = false;
    Bounds rectangularClip;
    std::array<double, 6> textMatrix{1, 0, 0, 1, 0, 0};
    std::array<double, 6> lineMatrix{1, 0, 0, 1, 0, 0};
    double fontSize = 0;
    double charSpacing = 0;
    double wordSpacing = 0;
    double horizontalScale = 1;
    double leading = 0;
    double rise = 0;
    std::string fontName;
    bool textPositionKnown = true;
    double lineWidth = 1;
    double miterLimit = 10;
};

void ReadGraphicsStrokeStyle(QPDFObjectHandle entry, State* state = nullptr)
{
    for (auto const* key : {"/LC", "/LJ"}) {
        auto value = entry.getKey(key);
        Require(value.isNull() || value.isInteger() && value.getIntValue() >= 0 &&
            value.getIntValue() <= 2, "Invalid PDF graphics-state line style.");
    }
    auto width = entry.getKey("/LW"), miter = entry.getKey("/ML");
    Require(width.isNull() || width.isNumber() && std::isfinite(width.getNumericValue()) &&
        width.getNumericValue() >= 0 && width.getNumericValue() <= 10000,
        "Invalid PDF graphics-state stroke width.");
    Require(miter.isNull() || miter.isNumber() && std::isfinite(miter.getNumericValue()) &&
        miter.getNumericValue() >= 1 && miter.getNumericValue() <= 10000,
        "Invalid PDF graphics-state miter limit.");
    if (state) {
        if (!width.isNull()) state->lineWidth = width.getNumericValue();
        if (!miter.isNull()) state->miterLimit = miter.getNumericValue();
    }
    // Cap/join values remain in the original gs dictionary. Stroke bounds use
    // the same conservative envelope as explicit J/j operators.
}

struct PaintRegion {
    Bounds bounds;
    size_t operation = 0;
    bool rectangle = false;
    Color sourceColor{0, 0, 0};
    // The curve-aware inset bounds safe ink placement without treating rounded
    // corner cutouts or control-point rounding defects as covered fill.
    std::vector<std::array<double, 2>> interior;
    bool stroke = false;
    bool text = false;
    bool clippedRectangle = false;
    Bounds rectangularClip;
};

struct ContrastPlan {
    std::set<size_t> fills;
    std::set<size_t> strokes;
    std::set<size_t> preservedStrokes;
};

struct OccludedTextPlan {
    std::map<size_t, std::set<size_t>> fills;
    std::set<size_t> strokes;
    std::set<size_t> provedBackdrops;
};

struct PaintSummary {
    size_t operations = 0;
    size_t lightPaints = 0;
    size_t darkPaints = 0;
    bool darkBackground = false;
    bool hasText = false;
    bool darkFilledRegion = false;
    bool lightText = false;
    bool paperPresent = false;
    bool paletteChanges = false;
    size_t photoSceneStart = 0;
    bool hasForm = false;
    bool fractionalOutsidePhoto = false;
    RetainedImageBudget retainedImageProbe;
    size_t decodedFontBytes = 0;
};

struct MaskSupport {
    int width, height;
    std::string alpha;
};

struct ImageAlphaSupport {
    std::vector<Bounds> regions;
    size_t width, height;
    std::shared_ptr<Buffer> pixels;
};

struct DiagramSupport {
    int width, height;
    std::string pixels, alpha, sourceGray;
};

struct ImagePlacement {
    int object;
    size_t operation;
    std::array<double, 6> matrix;
    bool clipped;
    Bounds rectangularClip;
    Color paper;
    size_t paperOperation;
    bool pathPending;
    bool opaque;
};

struct FormPlacement {
    int object;
    size_t operation;
    Bounds bounds;
    std::array<double, 6> matrix;
};

struct FormContrast {
    bool darkFill = false;
    bool text = false;
    std::vector<Color> textColors;
    bool knownTextColors = false;
    std::vector<Bounds> textBounds;
};

struct FractionalFill {
    PaintRegion region;
    double alpha;
    bool darkPaper;
};

struct LocalPhotoPlan {
    std::set<size_t> paintOperations;
    std::set<int> imageObjects;
};

struct AnnotatedImageComposite {
    std::set<size_t> paint;
    std::set<int> images;
    std::map<size_t, ImageBacking> backings;
    std::map<size_t, Color> fills;
};

class ContentRewrite final : public QPDFObjectHandle::ParserCallbacks {
  public:
    ContentRewrite(double pageArea, bool write, QPDFObjectHandle resources = {},
        std::array<double, 4> pageBox = {}, bool form = false,
        std::set<size_t> retainedRegions = {}, std::set<size_t> foregroundRegions = {},
        std::map<size_t, Color> fractionalColors = {}, std::set<size_t> preservedPaint = {},
        std::set<size_t> backgroundStrokes = {}, std::map<size_t, ImageBacking> imageBackings = {},
        std::set<size_t> preservedStrokes = {}, std::map<size_t, ImageBacking> pageBackings = {}) :
        _pageArea(pageArea), _write(write), _resources(resources), _pageBox(pageBox),
        _retainedRegions(std::move(retainedRegions)), _foregroundRegions(std::move(foregroundRegions)),
        _fractionalColors(std::move(fractionalColors)), _preservedPaint(std::move(preservedPaint)),
        _backgroundStrokes(std::move(backgroundStrokes)), _preservedStrokes(std::move(preservedStrokes)),
        _imageBackings(std::move(imageBackings)), _pageBackings(std::move(pageBackings))
    {
        _state.fillKnown = !form;
        _state.strokeKnown = !form;
        if (_write && _pageBackings.count(0)) {
            Require(!form, "Implicit-paper image composites require a page.");
            auto backing = _pageBackings.at(0);
            imageBackingOffsets.push_back({_output.size(), backing});
            _output += LocalImageBacking(backing);
            summary.paletteChanges = true;
        }
    }

    void handleObject(QPDFObjectHandle object) override
    {
        if (!object.isOperator()) {
            _operands.push_back(object);
            Require(_operands.size() <= 256, "PDF operation has too many operands.");
            return;
        }
        RequireLimit(++summary.operations <= MaximumOperations, "PDF operation budget exceeded.");
        auto op = object.getOperatorValue();
        if (op == "BI" || op == "ID" || op == "EI")
            Unsupported("Inline PDF images are outside the dark-mode policy.");
        bool namedFill = op == "cs" || op == "sc" || op == "scn";
        bool namedStroke = op == "CS" || op == "SC" || op == "SCN";
        if (op == "cs" || op == "CS") {
            Require(_operands.size() == 1 && _operands[0].isName(), "Invalid PDF color-space selection.");
            auto name = _operands[0].getName();
            if (!_paintSpaces.count(name)) {
                RequireLimit(_paintSpaces.size() < 64, "PDF paint color-space budget exceeded.");
                auto space = _operands[0];
                if (name != "/DeviceGray" && name != "/DeviceRGB" && name != "/DeviceCMYK") {
                    auto spaces = _resources.getKey("/ColorSpace");
                    Require(spaces.isDictionary(), "PDF color-space resources are missing.");
                    space = spaces.getKey(name);
                }
                _paintSpaces[name] = ReadPaintColorSpace(space, summary.retainedImageProbe);
            }
            auto& space = namedFill ? _state.fillSpace : _state.strokeSpace;
            space = _paintSpaces.at(name);
            (namedFill ? _state.fill : _state.stroke) = {0, 0, 0};
            (namedFill ? _state.fillKnown : _state.strokeKnown) = true;
        } else if (op == "sc" || op == "scn" || op == "SC" || op == "SCN") {
            (namedFill ? _state.fill : _state.stroke) =
                ReadPaintColor(_operands, namedFill ? _state.fillSpace : _state.strokeSpace);
            (namedFill ? _state.fillKnown : _state.strokeKnown) = true;
        }
        if (op == "sh") {
            Require(_operands.size() == 1 && _operands[0].isName(), "Invalid PDF shading operation.");
            if (_state.fillAlpha != 1 || (_state.clipped && !_state.rectangularClip.valid))
                Unsupported("Transparent or irregularly clipped PDF shading is unsupported.");
            if (_state.fillOverprint) Unsupported("PDF shading overprint is unsupported.");
            auto shadings = _resources.getKey("/Shading");
            Require(shadings.isDictionary(), "PDF shading resources are missing.");
            auto shading = shadings.getKey(_operands[0].getName());
            auto box = ReadAxialShadingBox(shading);
            _overprintObstacles.push_back({}); // Arbitrary shading bounds stay conservative.
            RequireLimit(_overprintObstacles.size() <= 32768, "PDF overprint obstacle budget exceeded.");
            Bounds bounds;
            for (double x : {box[0], box[2]}) for (double y : {box[1], box[3]}) {
                auto point = Transform(x, y);
                RequireLimit(std::isfinite(point[0]) && std::isfinite(point[1]) &&
                    std::abs(point[0]) <= 200000000 && std::abs(point[1]) <= 200000000,
                    "PDF shading placement exceeds its limit.");
                Include(bounds, point[0], point[1]);
            }
            if (_state.clipped) {
                bounds.left = std::max(bounds.left, _state.rectangularClip.left);
                bounds.bottom = std::max(bounds.bottom, _state.rectangularClip.bottom);
                bounds.right = std::min(bounds.right, _state.rectangularClip.right);
                bounds.top = std::min(bounds.top, _state.rectangularClip.top);
            }
            if (!_write) {
                RecordPlacement({bounds, summary.operations, false});
                auto extend = shading.getKey("/Extend");
                if (std::abs(_state.matrix[1]) < .000001 && std::abs(_state.matrix[2]) < .000001 &&
                    extend.isArray() && extend.getArrayNItems() == 2 &&
                    extend.getArrayItem(0).isBool() && extend.getArrayItem(0).getBoolValue() &&
                    extend.getArrayItem(1).isBool() && extend.getArrayItem(1).getBoolValue()) {
                    _fills.push_back({bounds, summary.operations, true});
                    RequireLimit(_fills.size() <= 8192, "PDF fill budget exceeded.");
                    _shadingOperations.insert(summary.operations);
                }
                RequireLimit(_placementPaint.size() <= 32768, "PDF placement paint budget exceeded.");
            }
            _firstPaint = false;
        } else if (op == "q") {
            RequireLimit(_stack.size() < MaximumDepth, "PDF graphics-state nesting exceeds its limit.");
            _stack.push_back(_state);
        } else if (op == "Q") {
            Require(!_stack.empty(), "Unbalanced PDF graphics state.");
            _state = _stack.back();
            _stack.pop_back();
        } else if (op == "rg" || op == "g" || op == "k") {
            _state.fillSpace = {op == "g" ? 1u : op == "k" ? 4u : 3u, op == "rg"};
            _state.fill = ReadColor(_operands, op);
            _state.fillKnown = true;
        } else if (op == "RG" || op == "G" || op == "K") {
            _state.strokeSpace = {op == "G" ? 1u : op == "K" ? 4u : 3u, op == "RG"};
            _state.stroke = ReadColor(_operands, op);
            _state.strokeKnown = true;
        } else if (op == "Tr") {
            Require(_operands.size() == 1 && _operands[0].isInteger(), "Invalid PDF text rendering mode.");
            _state.textMode = static_cast<int>(_operands[0].getIntValue());
            Require(_state.textMode >= 0 && _state.textMode <= 7, "PDF text rendering mode is outside its range.");
        } else if (op == "BT") {
            _state.textMatrix = {1, 0, 0, 1, 0, 0};
            _state.lineMatrix = _state.textMatrix;
            _state.textPositionKnown = true;
        } else if (op == "M") {
            Require(_operands.size() == 1 && _operands[0].isNumber(), "Invalid PDF miter limit.");
            _state.miterLimit = _operands[0].getNumericValue();
            Require(std::isfinite(_state.miterLimit) && _state.miterLimit >= 1 &&
                _state.miterLimit <= 10000, "PDF miter limit exceeds its bound.");
        } else if (op == "Tf") {
            Require(_operands.size() == 2 && _operands[0].isName() && _operands[1].isNumber(),
                "Invalid PDF text font selection.");
            _state.fontName = _operands[0].getName();
            _state.fontSize = _operands[1].getNumericValue();
            Require(std::isfinite(_state.fontSize) && _state.fontSize > 0 &&
                _state.fontSize <= 100000, "PDF text size exceeds the contrast policy.");
            if (!_fonts.count(_state.fontName)) {
                auto fonts = _resources.getKey("/Font");
                Require(fonts.isDictionary(), "PDF text font resources are missing.");
                auto measured = ReadFont(fonts.getKey(_state.fontName));
                if (measured.glyphBounds || measured.cffGlyphBounds) {
                    summary.decodedFontBytes += measured.glyphBounds ? measured.glyphBounds->DecodedBytes() :
                        measured.cffGlyphBounds->DecodedBytes();
                    RequireLimit(summary.decodedFontBytes <= MaximumDecodedBytes,
                        "PDF font data exceeds the decoded resource bound.");
                }
                _fonts.emplace(_state.fontName, std::move(measured));
            }
        } else if (op == "Tc" || op == "Tw" || op == "Tz" || op == "TL" || op == "Ts") {
            Require(_operands.size() == 1 && _operands[0].isNumber(),
                "Invalid PDF text spacing or scale.");
            double value = _operands[0].getNumericValue();
            Require(std::isfinite(value) && std::abs(value) <= 100000,
                "PDF text spacing exceeds the contrast policy.");
            if (op == "Tc") _state.charSpacing = value;
            if (op == "Tw") _state.wordSpacing = value;
            if (op == "Tz") _state.horizontalScale = value / 100;
            if (op == "TL") _state.leading = value;
            if (op == "Ts") _state.rise = value;
        } else if (op == "Tm") {
            Require(_operands.size() == 6, "Invalid PDF text matrix.");
            for (size_t i = 0; i < 6; ++i) {
                Require(_operands[i].isNumber(), "Invalid PDF text matrix coordinate.");
                _state.textMatrix[i] = _operands[i].getNumericValue();
                Require(std::isfinite(_state.textMatrix[i]), "Nonfinite PDF text matrix coordinate.");
            }
            _state.lineMatrix = _state.textMatrix;
            _state.textPositionKnown = true;
        } else if (op == "Td" || op == "TD") {
            Require(_operands.size() == 2 && _operands[0].isNumber() && _operands[1].isNumber(),
                "Invalid PDF text line offset.");
            double x = _operands[0].getNumericValue(), y = _operands[1].getNumericValue();
            Require(std::isfinite(x) && std::isfinite(y), "Nonfinite PDF text line offset.");
            if (op == "TD") _state.leading = -y;
            MoveText(x, y);
        } else if (op == "T*") {
            MoveText(0, -_state.leading);
        } else if (op == "w") {
            Require(_operands.size() == 1 && _operands[0].isNumber(),
                "Invalid PDF stroke width.");
            _state.lineWidth = _operands[0].getNumericValue();
            Require(std::isfinite(_state.lineWidth) && _state.lineWidth >= 0,
                "Invalid PDF stroke width.");
        } else if (op == "gs") {
            Require(_operands.size() == 1 && _operands[0].isName() && _resources.isDictionary(),
                "Invalid PDF graphics-state name.");
            auto states = _resources.getKey("/ExtGState");
            Require(states.isDictionary(), "PDF graphics-state resource is missing.");
            auto entry = states.getKey(_operands[0].getName());
            Require(entry.isDictionary(), "PDF graphics-state entry is missing.");
            ReadGraphicsStrokeStyle(entry, &_state);
            if (entry.hasKey("/OP")) {
                _state.strokeOverprint = entry.getKey("/OP").getBoolValue();
                if (!entry.hasKey("/op")) _state.fillOverprint = _state.strokeOverprint;
            }
            if (entry.hasKey("/op")) _state.fillOverprint = entry.getKey("/op").getBoolValue();
            if (entry.hasKey("/ca")) _state.fillAlpha = entry.getKey("/ca").getNumericValue();
            if (entry.hasKey("/CA")) _state.strokeAlpha = entry.getKey("/CA").getNumericValue();
        } else if (op == "cm") {
            Require(_operands.size() == 6, "Invalid PDF transformation matrix.");
            std::array<double, 6> v{};
            for (size_t i = 0; i < 6; ++i) {
                Require(_operands[i].isNumber(), "Invalid PDF transformation coordinate.");
                v[i] = _operands[i].getNumericValue();
                Require(std::isfinite(v[i]), "Nonfinite PDF transformation coordinate.");
            }
            auto m = _state.matrix;
            _state.matrix = {v[0] * m[0] + v[1] * m[2], v[0] * m[1] + v[1] * m[3],
                v[2] * m[0] + v[3] * m[2], v[2] * m[1] + v[3] * m[3],
                v[4] * m[0] + v[5] * m[2] + m[4], v[4] * m[1] + v[5] * m[3] + m[5]};
        } else if (op == "W" || op == "W*") {
            // A rectangular MediaBox clip is already enforced by the page and
            // does not make later rectangle containment ambiguous.
            if (!(PathRectangle() && IsFullPage(_pathBounds) && !_state.clipped)) {
                if (PathRectangle() && (!_state.clipped || _state.rectangularClip.valid)) {
                    auto clip = _pathBounds;
                    if (_state.clipped) {
                        clip.left = std::max(clip.left, _state.rectangularClip.left);
                        clip.right = std::min(clip.right, _state.rectangularClip.right);
                        clip.bottom = std::max(clip.bottom, _state.rectangularClip.bottom);
                        clip.top = std::min(clip.top, _state.rectangularClip.top);
                        clip.valid = clip.left < clip.right && clip.bottom < clip.top;
                    }
                    _state.rectangularClip = clip;
                } else _state.rectangularClip = {};
                _state.clipped = true;
            }
        } else if (op == "Do") {
            Require(_operands.size() == 1 && _operands[0].isName() && _resources.isDictionary(),
                "Invalid PDF XObject name.");
            usedXObjects.insert(_operands[0].getName());
            auto objects = _resources.getKey("/XObject");
            Require(objects.isDictionary() && objects.getKey(_operands[0].getName()).isStream(),
                "PDF XObject reference is missing.");
            auto xobject = objects.getKey(_operands[0].getName());
            Bounds overprintImage;
            if (xobject.getDict().getKey("/Subtype").isNameAndEquals("/Image")) {
                for (double x : {0., 1.}) for (double y : {0., 1.}) {
                    auto point = Transform(x, y); Include(overprintImage, point[0], point[1]);
                }
            }
            _overprintObstacles.push_back({overprintImage});
            RequireLimit(_overprintObstacles.size() <= 32768, "PDF overprint obstacle budget exceeded.");
            if (_state.fillOverprint || _state.strokeOverprint) {
                if (!xobject.getDict().getKey("/Subtype").isNameAndEquals("/Image"))
                    Unsupported("Inherited PDF form overprint is unsupported.");
                auto color = xobject.getDict().getKey("/ColorSpace");
                bool calibrated = color.isArray() && color.getArrayNItems() == 2 &&
                    (color.getArrayItem(0).isNameAndEquals("/ICCBased") ||
                     color.getArrayItem(0).isNameAndEquals("/CalRGB"));
                if (_state.fillOverprint && !color.isNameAndEquals("/DeviceRGB") && !calibrated)
                    Unsupported("PDF image overprint is unsupported.");
            }
            if (_write && _imageBackings.count(summary.operations)) {
                Require(xobject.getDict().getKey("/Subtype").isNameAndEquals("/Image") &&
                    _state.fillAlpha == 1 && !_pathStarted,
                    "PDF image backing placement changed.");
                auto backing = _imageBackings.at(summary.operations);
                imageBackingOffsets.push_back({_output.size(), backing});
                _output += LocalImageBacking(backing);
                summary.paletteChanges = true;
            }
            if (xobject.getDict().getKey("/Subtype").isNameAndEquals("/Form"))
                summary.hasForm = true;
            if (!summary.photoSceneStart && _state.fillAlpha != 0 && _state.fillAlpha != 1)
                Unsupported("Fractional PDF image or form placement is unsupported.");
            if (_state.fillAlpha > 0) {
                Bounds bounds;
                if (xobject.getDict().getKey("/Subtype").isNameAndEquals("/Image")) {
                    for (double x : {0., 1.}) for (double y : {0., 1.}) {
                        auto point = Transform(x, y);
                        Include(bounds, point[0], point[1]);
                    }
                    if (!_write) {
                        auto dictionary = xobject.getDict();
                        auto stencil = dictionary.getKey("/ImageMask");
                        auto embeddedAlpha = dictionary.getKey("/SMaskInData");
                        bool opaque = _state.fillAlpha == 1 && dictionary.getKey("/Mask").isNull() &&
                            dictionary.getKey("/SMask").isNull() &&
                            (stencil.isNull() || stencil.isBool() && !stencil.getBoolValue()) &&
                            (embeddedAlpha.isNull() || embeddedAlpha.isInteger() && embeddedAlpha.getIntValue() == 0);
                        _imagePlacements.push_back({xobject.getObjectID(), summary.operations, _state.matrix,
                            _state.clipped, _state.rectangularClip, _paperColor, _paperOperation, _pathStarted, opaque});
                        RequireLimit(_imagePlacements.size() <= 8192, "PDF image placement budget exceeded.");
                    }
                    auto m = _state.matrix;
                    if (!summary.photoSceneStart && _state.fillAlpha == 1 && !_state.clipped &&
                        std::abs(m[1]) < .000001 && std::abs(m[2]) < .000001 &&
                        m[0] > 0 && m[3] > 0 && IsFullPage(bounds) &&
                        IsOpaqueRetainedJpeg(xobject, summary.retainedImageProbe))
                        summary.photoSceneStart = summary.operations;
                } else if (xobject.getDict().getKey("/Subtype").isNameAndEquals("/Form")) {
                    auto box = xobject.getDict().getKey("/BBox");
                    Require(box.isArray() && box.getArrayNItems() == 4, "Invalid PDF form bounding box.");
                    std::array<double, 4> b{};
                    for (int i = 0; i < 4; ++i) {
                        Require(box.getArrayItem(i).isNumber(), "Invalid PDF form bound.");
                        b[i] = box.getArrayItem(i).getNumericValue();
                        RequireLimit(std::isfinite(b[i]) && std::abs(b[i]) <= 200000000,
                            "PDF form bound exceeds its limit.");
                    }
                    Require(b[0] < b[2] && b[1] < b[3], "Invalid PDF form bounding box order.");
                    std::array<double, 6> matrix{1, 0, 0, 1, 0, 0};
                    auto declared = xobject.getDict().getKey("/Matrix");
                    if (!declared.isNull()) {
                        Require(declared.isArray() && declared.getArrayNItems() == 6, "Invalid PDF form matrix.");
                        for (int i = 0; i < 6; ++i) {
                            Require(declared.getArrayItem(i).isNumber(), "Invalid PDF form matrix value.");
                            matrix[i] = declared.getArrayItem(i).getNumericValue();
                            Require(std::isfinite(matrix[i]), "Nonfinite PDF form matrix value.");
                        }
                    }
                    for (double x : {b[0], b[2]}) for (double y : {b[1], b[3]}) {
                        auto point = Transform(matrix[0] * x + matrix[2] * y + matrix[4],
                            matrix[1] * x + matrix[3] * y + matrix[5]);
                        RequireLimit(std::isfinite(point[0]) && std::isfinite(point[1]) &&
                            std::abs(point[0]) <= 200000000 && std::abs(point[1]) <= 200000000,
                            "PDF form placement exceeds its limit.");
                        Include(bounds, point[0], point[1]);
                    }
                    if (!_write) {
                        auto m = _state.matrix;
                        std::array<double, 6> placed{
                            matrix[0] * m[0] + matrix[1] * m[2], matrix[0] * m[1] + matrix[1] * m[3],
                            matrix[2] * m[0] + matrix[3] * m[2], matrix[2] * m[1] + matrix[3] * m[3],
                            matrix[4] * m[0] + matrix[5] * m[2] + m[4],
                            matrix[4] * m[1] + matrix[5] * m[3] + m[5]};
                        _formPlacements.push_back({xobject.getObjectID(), summary.operations, bounds, placed});
                        RequireLimit(_formPlacements.size() <= 8192, "PDF form placement budget exceeded.");
                    }
                }
                // The form's declared BBox clips its paint before placement.
                if (!_write) {
                    RecordPlacement({bounds, summary.operations, false});
                    RequireLimit(_placementPaint.size() <= 32768, "PDF placement paint budget exceeded.");
                }
            }
        }
        if (op == "Do") _firstPaint = false;
        if (op == "m" || op == "l" || op == "c" || op == "v" || op == "y" || op == "h" || op == "re")
            RecordPanelPath(op);
        if (op == "m" || op == "l" || op == "c" || op == "v" || op == "y" || op == "h") {
            if (op == "l" || op == "v" || op == "y" || (op == "m" && _pathStarted) ||
                (op == "c" && _operands.size() != 6)) _smoothCurvePath = false;
            if (op == "m") BeginRuleSubpath();
            if (op == "c" || op == "v" || op == "y") _linearRulePath = false;
            if (op == "m" && _pathStarted) _pathComplex = true;
            _pathStarted = true;
            _lastFullRect = false;
            if (op != "h") {
                Require(_operands.size() >= 2 && _operands.size() % 2 == 0,
                    "Invalid PDF path coordinates.");
                for (size_t i = 0; i < _operands.size(); i += 2) {
                    Require(_operands[i].isNumber() && _operands[i + 1].isNumber(),
                        "Invalid PDF path coordinate.");
                    PathPoint(_operands[i].getNumericValue(), _operands[i + 1].getNumericValue());
                }
                if (op == "c" || op == "v" || op == "y") _pathComplex = true;
            }
        }
        if (op == "re") {
            _smoothCurvePath = false;
            BeginRuleSubpath();
            bool soleRectangle = !_pathStarted;
            _pathStarted = true;
            if (!soleRectangle) _pathComplex = true;
            _lastFullRect = false;
            _lastRectArea = 0;
            Require(_operands.size() == 4, "Invalid rectangle geometry.");
            std::array<double, 4> v{};
            for (size_t i = 0; i < 4; ++i) {
                Require(_operands[i].isNumber(), "Invalid rectangle coordinate.");
                v[i] = _operands[i].getNumericValue();
                Require(std::isfinite(v[i]), "Nonfinite rectangle coordinate.");
            }
            auto m = _state.matrix;
            PathPoint(v[0], v[1]);
            PathPoint(v[0] + v[2], v[1]);
            PathPoint(v[0] + v[2], v[1] + v[3]);
            PathPoint(v[0], v[1] + v[3]);
            if (std::abs(m[1]) < .000001 && std::abs(m[2]) < .000001 &&
                m[0] > 0 && m[3] > 0 && !_state.clipped) {
                double x0 = v[0] * m[0] + m[4];
                double y0 = v[1] * m[3] + m[5];
                double x1 = (v[0] + v[2]) * m[0] + m[4];
                double y1 = (v[1] + v[3]) * m[3] + m[5];
                _lastRectArea = std::abs((x1 - x0) * (y1 - y0));
                _lastFullRect = soleRectangle && _pageArea > 0 && std::abs(x0 - _pageBox[0]) < .01 &&
                    std::abs(y0 - _pageBox[1]) < .01 && std::abs(x1 - _pageBox[2]) < .01 &&
                    std::abs(y1 - _pageBox[3]) < .01;
            }
        }
        bool text = op == "Tj" || op == "TJ" || op == "'" || op == "\"";
        bool fill = op == "f" || op == "F" || op == "f*" || op == "B" || op == "B*" || op == "b" || op == "b*";
        bool stroke = op == "S" || op == "s" || op == "B" || op == "B*" || op == "b" || op == "b*";
        if (text || fill || stroke) {
            bool preservePaint = (summary.photoSceneStart && summary.operations > summary.photoSceneStart) ||
                _preservedPaint.count(summary.operations);
            Bounds textBounds;
            bool textPresent = false;
            if (text) {
                if (op == "'" || op == "\"") MoveText(0, -_state.leading);
                if (op == "\"") {
                    Require(_operands.size() == 3 && _operands[0].isNumber() &&
                        _operands[1].isNumber(), "Invalid PDF quoted text spacing.");
                    _state.wordSpacing = _operands[0].getNumericValue();
                    _state.charSpacing = _operands[1].getNumericValue();
                }
                Require(!_operands.empty(), "Missing PDF text operand.");
                textPresent = HasGlyphBytes(_operands.back(), op == "TJ");
                textBounds = MeasureText(_operands.back(), op == "TJ");
            }
            bool textFill = text && (_state.textMode == 0 || _state.textMode == 2 ||
                _state.textMode == 4 || _state.textMode == 6);
            bool textStroke = text && (_state.textMode == 1 || _state.textMode == 2 ||
                _state.textMode == 5 || _state.textMode == 6);
            bool large = fill && (_lastFullRect ||
                (PathRectangle() && IsFullPage(_pathBounds) && !_state.clipped));
            double light = Light(_state.fill);
            if (text) summary.hasText = true;
            if ((textFill || fill) && !_state.fillKnown)
                Unsupported("PDF form inherits an unknown fill color.");
            if ((textStroke || stroke) && !_state.strokeKnown)
                Unsupported("PDF form inherits an unknown stroke color.");
            bool visibleFill = (textFill || fill) && _state.fillAlpha > 0;
            bool visibleStroke = (textStroke || stroke) && _state.strokeAlpha > 0;
            // RGB and calibrated colors paint all process components. DeviceGray,
            // CMYK, and spot-color overprint need a separation-aware model.
            auto clip = _state.rectangularClip;
            bool clippedText = text && !textStroke && textBounds.valid &&
                _state.fillSpace.components == 1 && !_state.fillSpace.gray &&
                _state.clipped && clip.valid &&
                (textBounds.right < clip.left - .01 || textBounds.left > clip.right + .01 ||
                 textBounds.top < clip.bottom - .01 || textBounds.bottom > clip.top + .01);
            bool grayTextOnPaper = _state.fillOverprint && !clippedText &&
                text && !textStroke && textBounds.valid && _pageArea > 0 &&
                _state.fillSpace.components == 1 && !_state.fillSpace.gray && !_state.fillSpace.overprintSafe;
            if (grayTextOnPaper) for (auto const& obstacle : _overprintObstacles) {
                RequireLimit(++_overprintComparisons <= 1000000, "PDF overprint relation budget exceeded.");
                // Repeated opaque black glyphs are a common synthetic-bold
                // technique. Black over existing black has the same appearance.
                if (obstacle.text && SameColor(obstacle.sourceColor, {0, 0, 0}) &&
                    SameColor(_state.fill, {0, 0, 0}) && _state.fillAlpha == 1) continue;
                bool overlaps = !obstacle.bounds.valid;
                if (_lastGlyphInk.empty()) overlaps |= Intersects(textBounds, obstacle.bounds);
                else for (auto const& glyph : _lastGlyphInk) {
                    RequireLimit(++_overprintComparisons <= 1000000, "PDF overprint relation budget exceeded.");
                    overlaps |= !glyph.valid || Intersects(glyph, obstacle.bounds);
                }
                if (overlaps) { grayTextOnPaper = false; break; }
            }
            if (!clippedText && !grayTextOnPaper &&
                ((visibleFill && _state.fillOverprint && !_state.fillSpace.overprintSafe) ||
                 (visibleStroke && _state.strokeOverprint && !_state.strokeSpace.overprintSafe)))
                throw Problem(3, "PDF device-color overprint is unsupported at operation " +
                    std::to_string(summary.operations) + ".");
            // Remember conservative bounds for every nonwhite earlier mark.
            // White paint cannot make untouched white paper colored; prior
            // obstacles remain even when a later white shape covers them.
            if ((visibleFill && !SameColor(_state.fill, {1, 1, 1})) ||
                (visibleStroke && !SameColor(_state.stroke, {1, 1, 1}))) {
                auto obstacle = text ? textBounds : _pathBounds;
                if (visibleStroke && obstacle.valid) {
                    auto m = _state.matrix;
                    double pad = std::max(1., _state.lineWidth * _state.miterLimit) *
                        std::max(std::abs(m[0]) + std::abs(m[2]), std::abs(m[1]) + std::abs(m[3]));
                    obstacle.left -= pad; obstacle.right += pad;
                    obstacle.bottom -= pad; obstacle.top += pad;
                }
                bool opaqueText = text && !visibleStroke && _state.fillAlpha == 1;
                if (opaqueText && !_lastGlyphInk.empty()) {
                    for (auto const& glyph : _lastGlyphInk)
                        _overprintObstacles.push_back({glyph, 0, false, _state.fill, {}, false, true});
                } else _overprintObstacles.push_back({obstacle, 0, false, _state.fill, {}, false, opaqueText});
                RequireLimit(_overprintObstacles.size() <= 32768, "PDF overprint obstacle budget exceeded.");
            }
            if (!preservePaint && (visibleFill && _state.fillAlpha != 1 ||
                visibleStroke && _state.strokeAlpha != 1)) summary.fractionalOutsidePhoto = true;
            if (!preservePaint && (text && visibleFill && _state.fillAlpha != 1 ||
                visibleStroke && _state.strokeAlpha != 1))
                Unsupported("Fractional PDF text or strokes are unsupported.");
            if (!_write && !preservePaint && fill && visibleFill && _state.fillAlpha != 1) {
                if (large || _state.clipped || !_pathBounds.valid)
                    Unsupported("Unbounded or clipped fractional PDF fills are unsupported.");
                _fractionalFills.push_back({{_pathBounds, summary.operations, PathRectangle(), _state.fill},
                    _state.fillAlpha, summary.paperPresent && Luminance(_paperColor) < .05});
                RequireLimit(_fractionalFills.size() <= 8192, "PDF fractional-fill budget exceeded.");
            }
            if (!_write && !preservePaint && text && textPresent && visibleFill &&
                _state.textMode == 0 && _state.fillAlpha == 1 && !_state.clipped &&
                _pageArea > 0 && SameColor(_paperColor, {1, 1, 1}) && SameColor(_state.fill, {1, 1, 1}))
                RejectKnockoutText();
            if (!_write && !preservePaint && (visibleFill || visibleStroke) && (!text || textPresent) &&
                !(fill && large && !visibleStroke)) {
                auto bounds = text ? textBounds : _pathBounds;
                std::vector<Bounds> strokeBounds;
                bool linearStroke = !text && visibleStroke && AxisAlignedStrokeBounds(strokeBounds);
                bool separateStroke = linearStroke || !text && visibleStroke &&
                    (RoundedPanelStrokeBounds(strokeBounds) || SmoothCurveStrokeBounds(strokeBounds));
                if (linearStroke && stroke && !fill && !_state.clipped &&
                    _state.strokeAlpha == 1 && Light(_state.stroke) < .38) {
                    for (auto const& segment : strokeBounds)
                        _knockoutStrokes.push_back({segment, summary.operations});
                    RequireLimit(_knockoutStrokes.size() <= 32768, "PDF stroke relation budget exceeded.");
                }
                if (visibleStroke && bounds.valid && !separateStroke) {
                    // Include square caps and the maximum permitted miter. A
                    // clipped bound is deliberately overestimated, never trimmed.
                    auto m = _state.matrix;
                    double pad = std::max(1., _state.lineWidth * _state.miterLimit) *
                        std::max(std::abs(m[0]) + std::abs(m[2]), std::abs(m[1]) + std::abs(m[3]));
                    bounds.left -= pad; bounds.right += pad;
                    bounds.bottom -= pad; bounds.top += pad;
                }
                if (visibleFill || !separateStroke)
                    RecordPlacement({bounds, summary.operations, false,
                        visibleFill ? _state.fill : _state.stroke, {}, visibleStroke && !separateStroke, text});
                for (auto const& segment : strokeBounds)
                    RecordPlacement({segment, summary.operations, false, _state.stroke, {}, true});
                if (stroke && !fill && !text) _strokeOperations.insert(summary.operations);
                RequireLimit(_placementPaint.size() <= 32768, "PDF placement paint budget exceeded.");
            }
            if (fill && visibleFill && !large && light < .38 && !preservePaint && _state.fillAlpha == 1)
                summary.darkFilledRegion = true;
            // A proved rectangular clip that contains the entire path cannot
            // alter this filled panel. Other clipped shapes still need
            // the stricter local intersection policies below.
            bool completePanel = !_state.clipped || (PathRectangle() &&
                _state.rectangularClip.valid && Contains(_state.rectangularClip, _pathBounds));
            if (stroke && visibleStroke && !text && !preservePaint && !_state.clipped && _state.strokeAlpha == 1 &&
                _state.lineWidth > 0 && _state.lineWidth *
                    std::max(std::abs(_state.matrix[0]) + std::abs(_state.matrix[2]),
                        std::abs(_state.matrix[1]) + std::abs(_state.matrix[3])) <= 2) {
                _panelOutlines.push_back({_pathBounds, summary.operations, PathRectangle(),
                    _state.stroke, RoundedPanelInterior(), true});
                RequireLimit(_panelOutlines.size() <= 8192, "PDF contrast region budget exceeded.");
            }
            if (fill && visibleFill && !large && light < .38 && !preservePaint && _state.fillAlpha == 1)
                _regions.push_back({_pathBounds, summary.operations,
                    PathRectangle() && completePanel && _state.fillAlpha == 1, _state.fill,
                    !_state.clipped ? RoundedPanelInterior() : std::vector<std::array<double, 2>>{}});
            RequireLimit(_regions.size() <= 8192, "PDF contrast region budget exceeded.");
            if (fill && visibleFill && !preservePaint) {
                if (!_state.clipped && _state.fillAlpha == 1 && PathCompoundArrow())
                    _arrowOverlayOperations.insert(summary.operations);
                auto fillBounds = _pathBounds;
                bool rectangularFill = PathRectangle() && _state.fillAlpha == 1 &&
                    (!_state.clipped || _state.rectangularClip.valid);
                if (rectangularFill && _state.clipped) {
                    fillBounds.left = std::max(fillBounds.left, _state.rectangularClip.left);
                    fillBounds.bottom = std::max(fillBounds.bottom, _state.rectangularClip.bottom);
                    fillBounds.right = std::min(fillBounds.right, _state.rectangularClip.right);
                    fillBounds.top = std::min(fillBounds.top, _state.rectangularClip.top);
                    fillBounds.valid = fillBounds.left < fillBounds.right && fillBounds.bottom < fillBounds.top;
                }
                _fills.push_back({fillBounds, summary.operations,
                    rectangularFill && completePanel, _state.fill,
                    !_state.clipped && _state.fillAlpha == 1 ? RoundedPanelInterior() :
                        std::vector<std::array<double, 2>>{}, false, false,
                    rectangularFill && _state.clipped});
                RequireLimit(_fills.size() <= 8192, "PDF contrast fill budget exceeded.");
                double shortSide = std::min(_pathBounds.right - _pathBounds.left, _pathBounds.top - _pathBounds.bottom);
                double longSide = std::max(_pathBounds.right - _pathBounds.left, _pathBounds.top - _pathBounds.bottom);
                // A rule's role follows its geometry and paint relationships.
                // Source contrast changes after normalization and cannot be a
                // classification gate without making a second pass drift.
                if (_pageArea > 0 && PathRuleRectangles() && !_state.clipped && shortSide > 0 &&
                    shortSide <= 4 && longSide >= shortSide * 8)
                    _ruleCandidates.push_back({_pathBounds, summary.operations, true, _state.fill});
                if (_pageArea > 0 && !_pathComplex && !_state.clipped && _state.fillAlpha == 1 &&
                    summary.paperPresent && Luminance(_paperColor) < .05 && light > .85 &&
                    Saturation(_state.fill) < .15 && shortSide > 0 && longSide <= 32 &&
                    shortSide * longSide <= _pageArea * .005 &&
                    (_pathPoints.size() == 3 || (_pathPoints.size() == 4 && _pathPoints.front() == _pathPoints.back())))
                    _arrowCandidates.push_back({_pathBounds, summary.operations, false, _state.fill});
            }
            if (text && textStroke && _state.strokeAlpha > 0) {
                if (textFill && !SameColor(_state.fill, _state.stroke)) textBounds.valid = false;
                else if (textBounds.valid) {
                    auto m = _state.matrix;
                    double pad = std::max(1., _state.lineWidth * _state.miterLimit) *
                        std::max(std::abs(m[0]) + std::abs(m[2]), std::abs(m[1]) + std::abs(m[3]));
                    textBounds.left -= pad; textBounds.right += pad;
                    textBounds.bottom -= pad; textBounds.top += pad;
                }
            }
            if (text && textPresent && (visibleFill || visibleStroke) && !preservePaint) {
                summary.lightText = true;
                _lightText.push_back(textBounds);
                _textColors.push_back(textFill ? _state.fill : _state.stroke);
                _textOperations.push_back(summary.operations);
                _textGlyphs.push_back(textStroke ? std::vector<Bounds>{textBounds} : _lastGlyphInk);
                RequireLimit(_lightText.size() <= 8192, "PDF contrast text budget exceeded.");
            }
            if (visibleFill && !large) {
                if (light >= .38) ++summary.lightPaints;
                else ++summary.darkPaints;
            }
            if (visibleStroke) {
                if (Light(_state.stroke) >= .38) ++summary.lightPaints;
                else ++summary.darkPaints;
            }
            if (fill && large && _state.fillAlpha == 1) {
                if (preservePaint) Unsupported("Opaque repaint of a retained photo scene is unsupported.");
                summary.paperPresent = true;
                _paperColor = _state.fill;
                _paperOperation = summary.operations;
                if (_firstPaint && light < .25) summary.darkBackground = true;
                else if (light >= .38) summary.darkBackground = false;
            }
            bool unchangedPaint = true;
            if (_write && !preservePaint) {
                if (visibleFill) {
                    auto fractional = _fractionalColors.find(summary.operations);
                    auto target = fractional != _fractionalColors.end() ? fractional->second : Recolor(_state.fill,
                        textFill || _foregroundRegions.count(summary.operations) ||
                        (PaletteForeground(_state.fill) && !_retainedRegions.count(summary.operations)) ||
                        (light < .38 && !large && !PalettePanel(_state.fill) &&
                            !_retainedRegions.count(summary.operations)), large);
                    unchangedPaint &= SameColor(target, _state.fill);
                    summary.paletteChanges |= !SameColor(target, _state.fill);
                    _output += SetColor(target, false);
                }
                if (visibleStroke) {
                    auto target = _preservedStrokes.count(summary.operations) ? _state.stroke :
                        Recolor(_state.stroke, !_backgroundStrokes.count(summary.operations), false);
                    unchangedPaint &= SameColor(target, _state.stroke);
                    summary.paletteChanges |= !SameColor(target, _state.stroke);
                    _output += SetColor(target, true);
                }
            }
            if (_write && unchangedPaint && (visibleFill || visibleStroke)) {
                unchangedVectorPaint.insert(summary.operations);
                RequireLimit(unchangedVectorPaint.size() <= 32768, "PDF unchanged-paint budget exceeded.");
            }
            if (visibleFill || visibleStroke) _firstPaint = false;
            if (text && _state.textMode >= 4) {
                _state.clipped = true;
                _state.rectangularClip = {};
            }
        }
        if (namedFill || namedStroke) {
            // Canonicalize every selection/component operation; emitted palette
            // paint temporarily changes the output space to DeviceRGB.
            _output += SetColor(namedFill ? _state.fill : _state.stroke, namedStroke);
        } else {
            for (auto const& operand : _operands) {
                _output += operand.unparse();
                _output += ' ';
            }
            _output += op + "\n";
        }
        if (_write && (text || fill || stroke)) {
            _output += SetColor(_state.fill, false);
            _output += SetColor(_state.stroke, true);
        }
        if (_write && _pageBackings.count(summary.operations)) {
            Require(fill && !stroke && _paperOperation == summary.operations && _state.fillAlpha == 1,
                "PDF composite backing lost its paper placement.");
            auto backing = _pageBackings.at(summary.operations);
            auto m = _state.matrix;
            double determinant = m[0] * m[3] - m[1] * m[2];
            Require(std::isfinite(determinant) && std::abs(determinant) > .000000000001,
                "PDF composite backing transform is singular.");
            backing.matrix = {m[3] / determinant, -m[1] / determinant, -m[2] / determinant,
                m[0] / determinant, (m[2] * m[5] - m[3] * m[4]) / determinant,
                (m[1] * m[4] - m[0] * m[5]) / determinant};
            imageBackingOffsets.push_back({_output.size(), backing});
            _output += LocalImageBacking(backing);
            summary.paletteChanges = true;
        }
        RequireLimit(_output.size() <= MaximumContentBytes, "Decoded PDF content exceeds its limit.");
        if (fill || stroke || op == "n") {
            _lastRectArea = 0;
            _lastFullRect = false;
            _pathStarted = false;
            _pathBounds = {};
            _pathPoints.clear();
            _pathComplex = false;
            _ruleSubpaths.clear();
            _linearRulePath = true;
            _curvePoints.clear();
            _smoothCurvePath = true;
            _panelControls.clear();
            _panelEndpoints.clear();
            _panelSegmentEnds.clear();
            _panelPathValid = true;
            _panelPathClosed = false;
            _panelCurveCount = 0;
        }
        _operands.clear();
    }

    void handleEOF() override
    {
        Require(_operands.empty() && _stack.empty(), "Incomplete PDF content stream.");
        if (summary.photoSceneStart && summary.hasForm)
            Unsupported("Forms inside retained photo scenes are unsupported.");
    }

    PaintSummary summary;
    std::set<std::string> usedXObjects;
    std::set<size_t> unchangedVectorPaint;
    std::vector<ImageBackingInsertion> imageBackingOffsets;

    LocalPhotoPlan RetainedLocalPhotos(RetainedImageBudget& budget) const
    {
        LocalPhotoPlan result;
        if (_pageArea <= 0 || summary.photoSceneStart || summary.hasForm || _fractionalFills.empty())
            return result;
        auto objects = _resources.getKey("/XObject");
        auto shadings = _resources.getKey("/Shading");
        if (!objects.isDictionary() || (shadings.isDictionary() && !shadings.getKeys().empty()))
            return result;
        auto contained = [](Bounds outer, Bounds inner) {
            // Coincident photo/overlay edges are intentional. Unlike text
            // contrast containment, this proof needs no inset margin.
            return outer.valid && inner.valid && inner.left >= outer.left - .000001 &&
                inner.right <= outer.right + .000001 && inner.bottom >= outer.bottom - .000001 &&
                inner.top <= outer.top + .000001;
        };
        size_t comparisons = 0;
        std::map<int, bool> opaquePhotos;
        for (auto const& image : _imagePlacements) {
            auto m = image.matrix;
            if (image.pathPending || std::abs(m[1]) > .000001 || std::abs(m[2]) > .000001 ||
                std::abs(m[0]) < .000001 || std::abs(m[3]) < .000001 ||
                (image.clipped && !image.rectangularClip.valid)) continue;
            Bounds bounds;
            for (double x : {0., 1.}) for (double y : {0., 1.})
                Include(bounds, m[0] * x + m[4], m[3] * y + m[5]);
            if (image.clipped) {
                bounds.left = std::max(bounds.left, image.rectangularClip.left);
                bounds.bottom = std::max(bounds.bottom, image.rectangularClip.bottom);
                bounds.right = std::min(bounds.right, image.rectangularClip.right);
                bounds.top = std::min(bounds.top, image.rectangularClip.top);
            }
            if (bounds.left >= bounds.right || bounds.bottom >= bounds.top) continue;
            bool overlay = false;
            for (auto const& fill : _fractionalFills) {
                RequireLimit(++comparisons <= 1000000, "PDF local photo relation budget exceeded.");
                if (fill.region.operation > image.operation && fill.region.rectangle &&
                    contained(bounds, fill.region.bounds)) overlay = true;
            }
            if (!overlay) continue;
            auto known = opaquePhotos.find(image.object);
            if (known == opaquePhotos.end()) {
                bool opaque = false;
                for (auto const& name : objects.getKeys()) {
                    RequireLimit(++comparisons <= 1000000, "PDF local photo relation budget exceeded.");
                    auto object = objects.getKey(name);
                    if (object.getObjectID() == image.object) {
                        opaque = IsOpaqueRetainedJpeg(object, budget);
                        break;
                    }
                }
                known = opaquePhotos.emplace(image.object, opaque).first;
            }
            if (!known->second) continue;
            std::set<size_t> paint{image.operation};
            bool safe = true;
            for (auto const& mark : _placementPaint) {
                RequireLimit(++comparisons <= 1000000, "PDF local photo relation budget exceeded.");
                if (mark.operation <= image.operation) continue;
                if (!mark.bounds.valid) { safe = false; break; }
                if (!Intersects(bounds, mark.bounds)) continue;
                if (!contained(bounds, mark.bounds)) { safe = false; break; }
                paint.insert(mark.operation);
            }
            // One stroke operator can paint several disconnected subpaths.
            // Preserving its intersecting segment preserves all of them, so
            // every segment of an admitted operation must stay in this photo.
            for (auto const& mark : _placementPaint) {
                RequireLimit(++comparisons <= 1000000, "PDF local photo relation budget exceeded.");
                if (mark.operation != image.operation && paint.count(mark.operation) &&
                    !contained(bounds, mark.bounds)) safe = false;
            }
            // Full-page opaque repaints are absent from placementPaint. They
            // must not silently invalidate the opaque photo backdrop proof.
            for (auto const& fill : _fills) {
                RequireLimit(++comparisons <= 1000000, "PDF local photo relation budget exceeded.");
                if (fill.operation > image.operation &&
                    (!fill.bounds.valid || Intersects(bounds, fill.bounds)) &&
                    !contained(bounds, fill.bounds)) safe = false;
            }
            if (!safe) continue;
            result.paintOperations.insert(paint.begin(), paint.end());
        }
        for (auto const& image : _imagePlacements)
            if (result.paintOperations.count(image.operation)) result.imageObjects.insert(image.object);
        // Resource preservation affects every use on this page, so do not
        // preserve a logo here and inadvertently skip darkening its other use.
        for (auto const& image : _imagePlacements)
            if (result.imageObjects.count(image.object) && !result.paintOperations.count(image.operation))
                return {};
        return result;
    }
    std::string const& output() const { return _output; }
    std::vector<Color> const& textColors() const { return _textColors; }
    bool knownTextColors() const
    {
        return std::all_of(_lightText.begin(), _lightText.end(),
            [](Bounds const& text) { return text.valid; });
    }

    std::map<int, ImageAlphaSupport> ImageAlphaSupports(RetainedImageBudget& budget) const
    {
        std::map<int, ImageAlphaSupport> result;
        auto objects = _resources.getKey("/XObject");
        if (!objects.isDictionary()) return result;
        for (auto const& name : objects.getKeys()) {
            auto object = objects.getKey(name);
            if (!object.isStream()) continue;
            auto dict = object.getDict(), mask = dict.getKey("/SMask");
            if (!dict.getKey("/Subtype").isNameAndEquals("/Image") ||
                !(RetainedJpegFilter(dict.getKey("/Filter")) || RetainedLosslessFilter(dict.getKey("/Filter"))) ||
                !mask.isStream() || !(dict.getKey("/ColorSpace").isNameAndEquals("/DeviceRGB") ||
                    dict.getKey("/ColorSpace").isNameAndEquals("/DeviceGray")) ||
                !mask.getDict().getKey("/Matte").isNull()) continue;
            auto maskInterpolate = mask.getDict().getKey("/Interpolate");
            if (!(maskInterpolate.isNull() || maskInterpolate.isBool() && !maskInterpolate.getBoolValue())) continue;
            auto width = RetainedDimension(dict, "/Width"), height = RetainedDimension(dict, "/Height");
            RequireLimit(width <= MaximumImagePixels / height, "Image pixel budget exceeded.");
            auto pixels = ValidateRetainedMask(mask, width, height,
                dict.getKey("/ColorSpace").isNameAndEquals("/DeviceGray") ? 1 : 3, budget);
            std::vector<bool> columns(width), rows(height);
            for (size_t y = 0; y < height; ++y) for (size_t x = 0; x < width; ++x) {
                if (!pixels->getBuffer()[y * width + x]) continue;
                columns[x] = rows[y] = true;
            }
            // Split only across entirely zero-alpha rows/columns, retaining two
            // samples of margin. Merge touching margins so no gap is invented.
            // The grid bounds memory and work even for fragmented/noisy masks.
            auto spans = [](std::vector<bool> const& occupied) {
                std::vector<std::pair<size_t, size_t>> ranges;
                for (size_t i = 0; i < occupied.size(); ++i) {
                    if (!occupied[i]) continue;
                    size_t first = i > 2 ? i - 2 : 0, last = std::min(occupied.size(), i + 3);
                    if (!ranges.empty() && ranges.back().second >= first) ranges.back().second = last;
                    else ranges.emplace_back(first, last);
                }
                return ranges;
            };
            auto horizontal = spans(columns), vertical = spans(rows);
            if (horizontal.empty() || vertical.empty()) continue;
            if (horizontal.size() > 64 / vertical.size()) {
                horizontal = {{horizontal.front().first, horizontal.back().second}};
                vertical = {{vertical.front().first, vertical.back().second}};
            }
            std::vector<Bounds> regions;
            for (auto [left, right] : horizontal) for (auto [top, bottom] : vertical) {
                bool occupied = false;
                for (size_t y = top; y < bottom && !occupied; ++y)
                    for (size_t x = left; x < right; ++x)
                        if (pixels->getBuffer()[y * width + x]) { occupied = true; break; }
                if (occupied) regions.push_back({double(left) / width, 1 - double(bottom) / height,
                    double(right) / width, 1 - double(top) / height, true});
            }
            result.emplace(object.getObjectID(), ImageAlphaSupport{std::move(regions), width, height, pixels});
        }
        return result;
    }

    Bounds ImageRegionBounds(ImagePlacement const& image, Bounds unit) const
    {
        Bounds bounds;
        auto m = image.matrix;
        for (double x : {unit.left, unit.right}) for (double y : {unit.bottom, unit.top})
            Include(bounds, m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);
        if (image.clipped && image.rectangularClip.valid) {
            bounds.left = std::max(bounds.left, image.rectangularClip.left);
            bounds.bottom = std::max(bounds.bottom, image.rectangularClip.bottom);
            bounds.right = std::min(bounds.right, image.rectangularClip.right);
            bounds.top = std::min(bounds.top, image.rectangularClip.top);
        }
        bounds.valid = bounds.left < bounds.right && bounds.bottom < bounds.top;
        return bounds;
    }

    bool ImageRegionIntersects(Bounds area, PaintRegion const& paint,
        std::map<int, ImageAlphaSupport> const& supports, size_t& comparisons) const
    {
        if (!paint.bounds.valid) return true;
        if (!Intersects(area, paint.bounds)) return false;
        for (auto const& image : _imagePlacements) {
            RequireLimit(++comparisons <= 1000000, "PDF image backing relation budget exceeded.");
            if (image.operation != paint.operation) continue;
            auto support = supports.find(image.object);
            if (support == supports.end()) return true;
            for (auto unit : support->second.regions) {
                RequireLimit(++comparisons <= 1000000, "PDF image backing relation budget exceeded.");
                if (Intersects(area, ImageRegionBounds(image, unit))) return true;
            }
            return false;
        }
        return true;
    }

    bool ImageOccludesPaint(ImagePlacement const& image, Bounds area, PaintRegion const& paint,
        std::map<int, ImageAlphaSupport> const& supports, size_t& samples) const
    {
        if (paint.operation >= image.operation || !paint.bounds.valid) return false;
        auto support = supports.find(image.object);
        if (support == supports.end()) return false;
        auto const& mask = support->second;
        auto m = image.matrix;
        if (std::abs(m[1]) > .000001 || std::abs(m[2]) > .000001 ||
            std::abs(m[0]) < .000001 || std::abs(m[3]) < .000001) return false;
        Bounds overlap{std::max(area.left, paint.bounds.left), std::max(area.bottom, paint.bounds.bottom),
            std::min(area.right, paint.bounds.right), std::min(area.top, paint.bounds.top), true};
        if (overlap.left >= overlap.right || overlap.bottom >= overlap.top) return true;
        // Check every mask sample touched by the covered paint, plus two samples
        // on each edge. Never infer opacity from a color or an alpha threshold.
        double x0 = (overlap.left - m[4]) / m[0], x1 = (overlap.right - m[4]) / m[0];
        double y0 = 1 - (overlap.bottom - m[5]) / m[3], y1 = 1 - (overlap.top - m[5]) / m[3];
        auto left = static_cast<long long>(std::floor(std::min(x0, x1) * mask.width)) - 2;
        auto right = static_cast<long long>(std::ceil(std::max(x0, x1) * mask.width)) + 2;
        auto top = static_cast<long long>(std::floor(std::min(y0, y1) * mask.height)) - 2;
        auto bottom = static_cast<long long>(std::ceil(std::max(y0, y1) * mask.height)) + 2;
        if (left < 0 || top < 0 || right > static_cast<long long>(mask.width) ||
            bottom > static_cast<long long>(mask.height)) return false;
        for (auto y = top; y < bottom; ++y) for (auto x = left; x < right; ++x) {
            RequireLimit(++samples <= MaximumImagePixels, "PDF image occlusion sample budget exceeded.");
            if (mask.pixels->getBuffer()[static_cast<size_t>(y) * mask.width + static_cast<size_t>(x)] != 255)
                return false;
        }
        return true;
    }

    std::set<size_t> ImageRegionText(ImagePlacement const& image,
        std::vector<Bounds> const& regions, std::set<size_t> const& overlays, size_t& comparisons) const
    {
        std::set<size_t> result, ambiguous;
        for (auto const& paint : _placementPaint) {
            RequireLimit(++comparisons <= 1000000, "PDF image annotation relation budget exceeded.");
            if (!paint.text || paint.operation <= image.operation) continue;
            bool contained = false;
            for (auto unit : regions) {
                RequireLimit(++comparisons <= 1000000, "PDF image annotation relation budget exceeded.");
                if (Contains(ImageRegionBounds(image, unit), paint.bounds)) { contained = true; break; }
            }
            for (auto const& fill : _fills) {
                RequireLimit(++comparisons <= 1000000, "PDF image annotation relation budget exceeded.");
                if (overlays.count(fill.operation) && fill.operation < paint.operation &&
                    Intersects(fill.bounds, paint.bounds)) contained = false;
            }
            if (contained) result.insert(paint.operation);
            else ambiguous.insert(paint.operation);
        }
        // A text operator can have multiple paint footprints. Preserve it only
        // when all are inside the exact image/backing composite being retained.
        for (size_t operation : ambiguous) result.erase(operation);
        return result;
    }

    std::set<size_t> OpaqueImageOverlayPaint(size_t imageOperation, Bounds imageBounds,
        size_t& comparisons) const
    {
        std::set<size_t> result;
        std::vector<PaintRegion const*> panels;
        for (auto const& fill : _fills) {
            RequireLimit(++comparisons <= 1000000, "PDF image island relation budget exceeded.");
            if (fill.operation <= imageOperation || !(fill.rectangle || fill.clippedRectangle) ||
                !Intersects(imageBounds, fill.bounds) || _shadingOperations.count(fill.operation) ||
                IsFullPage(fill.bounds) || fill.bounds.right - fill.bounds.left <= 4 ||
                fill.bounds.top - fill.bounds.bottom <= 4 ||
                !(SameColor(fill.sourceColor, Color{1, 1, 1}) || SameColor(fill.sourceColor, Panel))) continue;
            bool simple = true;
            for (auto const& paint : _placementPaint) {
                RequireLimit(++comparisons <= 1000000, "PDF image island relation budget exceeded.");
                if (paint.operation == fill.operation && (paint.stroke || paint.text)) simple = false;
            }
            if (!simple) continue;
            panels.push_back(&fill);
            result.insert(fill.operation);
        }
        for (size_t i = 0; i < _lightText.size(); ++i) {
            RequireLimit(++comparisons <= 1000000, "PDF image island relation budget exceeded.");
            if (_textOperations[i] <= imageOperation || !_lightText[i].valid ||
                !Intersects(imageBounds, _lightText[i])) continue;
            for (auto panel = panels.rbegin(); panel != panels.rend(); ++panel) {
                if ((*panel)->operation >= _textOperations[i]) continue;
                // Clipped rectangles are admitted only for this local image
                // proof; the general text-panel policy remains unchanged.
                auto coverage = **panel;
                coverage.rectangle |= coverage.clippedRectangle;
                if (!PanelContainsText(coverage, i, comparisons)) continue;
                double original = Contrast(_textColors[i], coverage.sourceColor);
                double converted = Contrast(Recolor(_textColors[i], true, false), Panel);
                if (original >= 1.5 && converted + .00001 >= std::min(4.5, original))
                    result.insert(_textOperations[i]);
                break;
            }
        }
        return result;
    }

    std::map<int, std::map<size_t, Color>> RetainedImageIslands(
        std::map<int, ImageAlphaSupport> const& supports,
        std::map<int, std::set<size_t>>& preservedText) const
    {
        std::map<int, std::map<size_t, Color>> result;
        std::set<int> ambiguous;
        size_t comparisons = 0;
        for (auto const& image : _imagePlacements) {
            auto m = image.matrix;
            if (std::abs(m[1]) > .000001 || std::abs(m[2]) > .000001 ||
                (image.clipped && !image.rectangularClip.valid)) {
                ambiguous.insert(image.object);
                continue;
            }
            auto bounds = ImageRegionBounds(image, {0, 0, 1, 1, true});
            auto overlays = OpaqueImageOverlayPaint(image.operation, bounds, comparisons);
            std::vector<std::vector<Bounds>> alternatives{{Bounds{0, 0, 1, 1, true}}};
            auto support = supports.find(image.object);
            if (support != supports.end()) alternatives.push_back(support->second.regions);
            bool admitted = false;
            for (auto const& regions : alternatives) {
                std::map<size_t, Color> fills;
                auto text = ImageRegionText(image, regions, overlays, comparisons);
                bool complete = true;
                for (auto unit : regions) {
                    auto area = ImageRegionBounds(image, unit);
                    if (!area.valid) continue;
                    bool covered = false;
                    for (auto const& fill : _fills) {
                        RequireLimit(++comparisons <= 1000000, "PDF image island relation budget exceeded.");
                        bool matches = std::abs(area.left - fill.bounds.left) <= .001 &&
                            std::abs(area.bottom - fill.bounds.bottom) <= .001 &&
                            std::abs(area.right - fill.bounds.right) <= .001 &&
                            std::abs(area.top - fill.bounds.top) <= .001;
                        if (!(fill.rectangle || fill.clippedRectangle) || fill.operation >= image.operation ||
                            _shadingOperations.count(fill.operation) || !matches) continue;
                        if (*std::min_element(fill.sourceColor.begin(), fill.sourceColor.end()) < .98 &&
                            (_pageArea <= 0 || IsFullPage(fill.bounds) ||
                                (fill.bounds.right - fill.bounds.left) * (fill.bounds.top - fill.bounds.bottom) > _pageArea * .5))
                            continue;
                        bool isolated = true;
                        for (auto const& paint : _placementPaint) {
                            RequireLimit(++comparisons <= 1000000, "PDF image island relation budget exceeded.");
                            if (paint.operation > fill.operation && paint.operation != image.operation &&
                                !overlays.count(paint.operation) && !text.count(paint.operation) &&
                                ImageRegionIntersects(area, paint, supports, comparisons)) {
                                isolated = false;
                                break;
                            }
                        }
                        if (isolated) {
                            fills.emplace(fill.operation, fill.sourceColor);
                            covered = true;
                            break;
                        }
                    }
                    if (!covered) { complete = false; break; }
                }
                if (complete && !fills.empty()) {
                    result[image.object].insert(fills.begin(), fills.end());
                    preservedText[image.object].insert(text.begin(), text.end());
                    admitted = true;
                    break;
                }
            }
            if (!admitted) ambiguous.insert(image.object);
        }
        for (int object : ambiguous) { result.erase(object); preservedText.erase(object); }
        return result;
    }

    std::map<int, std::map<size_t, ImageBacking>> ImageBackingPlacements(
        std::map<int, ImageAlphaSupport> const& supports) const
    {
        std::map<int, std::map<size_t, ImageBacking>> result;
        std::set<int> candidates, ambiguous;
        auto objects = _resources.getKey("/XObject");
        if (!objects.isDictionary() || summary.photoSceneStart) return result;
        for (auto const& name : objects.getKeys()) {
            auto object = objects.getKey(name);
            if (object.isStream() && object.getDict().getKey("/Subtype").isNameAndEquals("/Image") &&
                RetainedJpegFilter(object.getDict().getKey("/Filter")) &&
                !object.getDict().getKey("/SMask").isNull()) candidates.insert(object.getObjectID());
        }
        size_t comparisons = 0, samples = 0;
        for (auto const& image : _imagePlacements) {
            if (!candidates.count(image.object)) continue;
            auto m = image.matrix;
            bool safe = !image.pathPending && image.paperOperation &&
                std::abs(m[1]) < .000001 && std::abs(m[2]) < .000001 &&
                std::abs(m[0]) > .000001 && std::abs(m[3]) > .000001 &&
                (!image.clipped || image.rectangularClip.valid);
            auto bounds = ImageRegionBounds(image, {0, 0, 1, 1, true});
            safe &= bounds.valid;
            if (!SameColor(image.paper, Color{1, 1, 1}))
                safe &= _pageArea > 0 &&
                    (bounds.right - bounds.left) * (bounds.top - bounds.bottom) <= _pageArea * .5;
            auto overlays = OpaqueImageOverlayPaint(image.operation, bounds, comparisons);
            auto support = supports.find(image.object);
            std::vector<Bounds> regions = support == supports.end() || support->second.regions.size() == 1 ?
                std::vector<Bounds>{{0, 0, 1, 1, true}} : support->second.regions;
            auto text = ImageRegionText(image, regions, overlays, comparisons);
            auto isolated = [&](Bounds area) {
                for (auto const& paint : _placementPaint) {
                    RequireLimit(++comparisons <= 1000000, "PDF image backing relation budget exceeded.");
                    if (paint.operation <= image.paperOperation || paint.operation == image.operation ||
                        overlays.count(paint.operation) || text.count(paint.operation)) continue;
                    if (ImageRegionIntersects(area, paint, supports, comparisons) &&
                        !ImageOccludesPaint(image, area, paint, supports, samples)) return false;
                }
                return true;
            };
            auto isolatedRegions = [&]() {
                bool visible = false;
                for (auto unit : regions) {
                    auto area = ImageRegionBounds(image, unit);
                    if (!area.valid) continue;
                    visible = true;
                    if (!isolated(area)) return false;
                }
                return visible;
            };
            if (safe && !isolatedRegions()) {
                if (support == supports.end()) safe = false;
                else {
                    regions = support->second.regions;
                    text = ImageRegionText(image, regions, overlays, comparisons);
                    safe = isolatedRegions();
                }
            }
            // Every visible region needs flat known paper and unchanged paint
            // relationships. Validate all placements before retaining a resource.
            if (safe) result[image.object].emplace(image.operation, ImageBacking{regions, image.paper, text});
            else ambiguous.insert(image.object);
        }
        for (int object : ambiguous) result.erase(object);
        return result;
    }

    std::map<int, Color> RetainableImageBackdrops(std::map<int, ImageAlphaSupport> const& supports) const
    {
        std::map<int, Color> result;
        std::set<int> ambiguous;
        size_t comparisons = 0;
        for (auto const& placement : _imagePlacements) {
            auto m = placement.matrix;
            auto support = supports.find(placement.object);
            auto regions = support == supports.end() ? std::vector<Bounds>{{0, 0, 1, 1, true}} :
                support->second.regions;
            bool flat = placement.paperOperation &&
                (!placement.clipped || placement.rectangularClip.valid) &&
                Luminance(placement.paper) < .05 && std::abs(m[1]) < .000001 &&
                std::abs(m[2]) < .000001 && std::abs(m[0]) > .000001 && std::abs(m[3]) > .000001;
            for (auto const& paint : _placementPaint) {
                RequireLimit(++comparisons <= 1000000, "PDF image backdrop relation budget exceeded.");
                if (paint.operation > placement.paperOperation && paint.operation < placement.operation)
                    for (auto region : regions)
                        if (ImageRegionIntersects(ImageRegionBounds(placement, region), paint, supports, comparisons))
                            flat = false;
            }
            auto prior = result.find(placement.object);
            if (!flat || prior != result.end() && !SameColor(prior->second, placement.paper))
                ambiguous.insert(placement.object);
            else result.emplace(placement.object, placement.paper);
        }
        for (int object : ambiguous) result.erase(object);
        return result;
    }

    std::map<int, std::set<size_t>> RetainedAnnotatedImages() const
    {
        std::map<int, std::set<size_t>> result;
        if (summary.photoSceneStart) return result;
        std::set<int> ambiguous;
        std::set<size_t> nonvector;
        for (auto const& image : _imagePlacements) nonvector.insert(image.operation);
        for (auto const& form : _formPlacements) nonvector.insert(form.operation);
        nonvector.insert(_shadingOperations.begin(), _shadingOperations.end());
        for (auto const& fill : _fractionalFills) nonvector.insert(fill.region.operation);
        size_t comparisons = 0;
        for (auto const& image : _imagePlacements) {
            auto m = image.matrix;
            if (!image.paperOperation || Luminance(image.paper) >= .05 ||
                std::abs(m[1]) > .000001 || std::abs(m[2]) > .000001 ||
                (image.clipped && !image.rectangularClip.valid)) {
                ambiguous.insert(image.object); continue;
            }
            Bounds bounds;
            for (double x : {0., 1.}) for (double y : {0., 1.})
                Include(bounds, m[0] * x + m[4], m[3] * y + m[5]);
            if (image.clipped) {
                bounds.left = std::max(bounds.left, image.rectangularClip.left);
                bounds.bottom = std::max(bounds.bottom, image.rectangularClip.bottom);
                bounds.right = std::min(bounds.right, image.rectangularClip.right);
                bounds.top = std::min(bounds.top, image.rectangularClip.top);
            }
            bool crossingAnnotation = false;
            for (auto const& paint : _placementPaint) {
                RequireLimit(++comparisons <= 1000000, "PDF annotated-image relation budget exceeded.");
                if (paint.operation > image.operation && !nonvector.count(paint.operation) &&
                    paint.bounds.valid && Intersects(bounds, paint.bounds) &&
                    !Contains(bounds, paint.bounds)) crossingAnnotation = true;
            }
            // White labels and stroked arrows need the same preservation proof
            // as colored labels. The checks below validate every connected paint
            // footprint and its contrast outside the retained image.
            if (!crossingAnnotation) { ambiguous.insert(image.object); continue; }
            std::set<size_t> preserved;
            bool safe = true;
            for (auto const& paint : _placementPaint) {
                RequireLimit(++comparisons <= 1000000, "PDF annotated-image relation budget exceeded.");
                if (paint.operation <= image.operation) continue;
                if (!paint.bounds.valid) { safe = false; break; }
                if (!Intersects(bounds, paint.bounds)) continue;
                if (!paint.bounds.valid || nonvector.count(paint.operation)) { safe = false; break; }
                if (!Contains(bounds, paint.bounds) &&
                    (paint.stroke && !_strokeOperations.count(paint.operation) ||
                     Luminance(paint.sourceColor) <= Luminance(image.paper) ||
                     Contrast(paint.sourceColor, Paper) + .000001 < Contrast(paint.sourceColor, image.paper))) {
                    safe = false; break;
                }
                preserved.insert(paint.operation);
            }
            // A preserved annotation may extend beyond the screenshot. Other
            // paint there must not change its original backdrop relationship.
            bool expanded = true;
            while (safe && expanded) {
                expanded = false;
                for (auto const& paint : _placementPaint) {
                    if (!preserved.count(paint.operation)) continue;
                    for (auto const& other : _placementPaint) {
                    RequireLimit(++comparisons <= 1000000, "PDF annotated-image relation budget exceeded.");
                    if (other.operation == image.operation || preserved.count(other.operation)) continue;
                    if (!other.bounds.valid) { safe = false; break; }
                    if (!Intersects(paint.bounds, other.bounds)) continue;
                    Bounds overlap{std::max(paint.bounds.left, other.bounds.left),
                        std::max(paint.bounds.bottom, other.bounds.bottom),
                        std::min(paint.bounds.right, other.bounds.right),
                        std::min(paint.bounds.top, other.bounds.top), true};
                    if (!Contains(bounds, overlap)) {
                        if (other.operation <= image.operation || nonvector.count(other.operation) ||
                            Luminance(other.sourceColor) <= Luminance(image.paper) ||
                            Contrast(other.sourceColor, Paper) + .000001 < Contrast(other.sourceColor, image.paper)) {
                            safe = false; break;
                        }
                        preserved.insert(other.operation);
                        expanded = true;
                    }
                    }
                }
            }
            // An operation can paint both fill and stroke with different colors.
            // Check every footprint even if another part admitted that operation.
            for (auto const& paint : _placementPaint)
                if (preserved.count(paint.operation) && !Contains(bounds, paint.bounds) &&
                    (paint.stroke && !_strokeOperations.count(paint.operation) ||
                     Luminance(paint.sourceColor) <= Luminance(image.paper) ||
                     Contrast(paint.sourceColor, Paper) + .000001 < Contrast(paint.sourceColor, image.paper)))
                    safe = false;
            if (!safe) ambiguous.insert(image.object);
            else result[image.object].insert(preserved.begin(), preserved.end());
        }
        for (int object : ambiguous) result.erase(object);
        return result;
    }

    std::map<int, AnnotatedImageComposite> AnnotatedImageComposites() const
    {
        std::map<int, AnnotatedImageComposite> result;
        if (summary.photoSceneStart || summary.hasForm || _pageArea <= 0) return result;
        auto objects = _resources.getKey("/XObject");
        if (!objects.isDictionary()) return result;
        std::set<int> candidates, ambiguous, opaque;
        for (auto const& name : objects.getKeys()) {
            auto object = objects.getKey(name);
            if (object.isStream() && object.getDict().getKey("/Subtype").isNameAndEquals("/Image") &&
                (RetainedJpegFilter(object.getDict().getKey("/Filter")) ||
                    RetainedLosslessFilter(object.getDict().getKey("/Filter"))) &&
                object.getDict().getKey("/Mask").isNull()) {
                candidates.insert(object.getObjectID());
                if (object.getDict().getKey("/SMask").isNull()) opaque.insert(object.getObjectID());
            }
        }
        std::set<size_t> nonvector = _shadingOperations;
        for (auto const& image : _imagePlacements) nonvector.insert(image.operation);
        for (auto const& fill : _fractionalFills) nonvector.insert(fill.region.operation);
        size_t comparisons = 0;
        for (auto const& image : _imagePlacements) {
            if (!candidates.count(image.object)) continue;
            auto m = image.matrix;
            if (_paperOperation > image.operation ||
                std::abs(m[1]) > .000001 || std::abs(m[2]) > .000001 ||
                (image.clipped && !image.rectangularClip.valid)) {
                ambiguous.insert(image.object); continue;
            }
            auto bounds = ImageRegionBounds(image, {0, 0, 1, 1, true}), area = bounds;
            std::set<size_t> paint;
            std::set<int> images{image.object};
            bool safe = bounds.valid, expanded = true, hasVectorPaint = false;
            // Retain connected annotations as one composite. A single PDF
            // operation may have several fill/stroke footprints; include all.
            while (safe && expanded) {
                expanded = false;
                for (auto region : _placementPaint) {
                    RequireLimit(++comparisons <= 1000000, "PDF image composite relation budget exceeded.");
                    if (!ClipPlacement(region)) continue;
                    if (region.operation <= image.operation) continue;
                    if (!region.bounds.valid) { safe = false; break; }
                    if (!paint.count(region.operation) && !Intersects(area, region.bounds)) continue;
                    if (nonvector.count(region.operation)) {
                        auto other = std::find_if(_imagePlacements.begin(), _imagePlacements.end(),
                            [&](auto const& placed) { return placed.operation == region.operation; });
                        if (other == _imagePlacements.end() || !candidates.count(other->object) ||
                            std::abs(other->matrix[1]) > .000001 || std::abs(other->matrix[2]) > .000001 ||
                            (other->clipped && !other->rectangularClip.valid)) { safe = false; break; }
                        images.insert(other->object);
                    }
                    hasVectorPaint |= !nonvector.count(region.operation);
                    expanded |= paint.insert(region.operation).second;
                    auto prior = area;
                    Include(area, region.bounds.left, region.bounds.bottom);
                    Include(area, region.bounds.right, region.bounds.top);
                    expanded |= prior.left != area.left || prior.bottom != area.bottom ||
                        prior.right != area.right || prior.top != area.top;
                }
            }
            if (!safe || (!hasVectorPaint && images.size() == 1) ||
                (area.right - area.left) * (area.top - area.bottom) > _pageArea * .8) {
                ambiguous.insert(image.object); continue;
            }
            for (auto const& other : _imagePlacements)
                if (other.object != image.object && images.count(other.object) && !paint.count(other.operation))
                    safe = false;
            size_t backdrop = image.paperOperation;
            PaintRegion const* localPaper = nullptr;
            bool extendedPaper = false;
            Bounds extendedArea;
            for (auto const& fill : _fills) {
                RequireLimit(++comparisons <= 1000000, "PDF image composite relation budget exceeded.");
                if (fill.operation <= backdrop || fill.operation >= image.operation ||
                    !(fill.rectangle || fill.clippedRectangle) || _shadingOperations.count(fill.operation) ||
                    !SameColor(fill.sourceColor, Color{1, 1, 1}) || !fill.bounds.valid) continue;
                bool covers = area.left >= fill.bounds.left - .001 && area.bottom >= fill.bounds.bottom - .001 &&
                    area.right <= fill.bounds.right + .001 && area.top <= fill.bounds.top + .001;
                auto expandedArea = area;
                if (!covers) {
                    // An image can slightly overhang a local white panel onto
                    // implicit white paper. Extend only across a proved clear
                    // original backdrop; no earlier visible paint may be hidden.
                    if (image.paperOperation || !SameColor(image.paper, Color{1, 1, 1}) ||
                        !Intersects(fill.bounds, bounds)) continue;
                    Include(expandedArea, fill.bounds.left, fill.bounds.bottom);
                    Include(expandedArea, fill.bounds.right, fill.bounds.top);
                    bool clear = true;
                    for (auto prior : _placementPaint) {
                        RequireLimit(++comparisons <= 1000000, "PDF image composite relation budget exceeded.");
                        if (prior.operation >= fill.operation || !ClipPlacement(prior)) continue;
                        if (!prior.bounds.valid || Intersects(expandedArea, prior.bounds)) { clear = false; break; }
                    }
                    if (!clear) continue;
                }
                backdrop = fill.operation;
                localPaper = &fill;
                extendedPaper = !covers;
                extendedArea = expandedArea;
            }
            if (!localPaper && !SameColor(image.paper, Color{1, 1, 1})) {
                ambiguous.insert(image.object); continue;
            }
            if (localPaper) area = extendedPaper ? extendedArea : localPaper->bounds;
            else {
                area.left = std::max(_pageBox[0], area.left - 1);
                area.bottom = std::max(_pageBox[1], area.bottom - 1);
                area.right = std::min(_pageBox[2], area.right + 1);
                area.top = std::min(_pageBox[3], area.top + 1);
            }
            // A local opaque white panel can also contain vector marks drawn
            // before the image. Preserve that complete original-color scene,
            // including every footprint of each admitted operation.
            if (localPaper) {
                std::set<size_t> earlier;
                auto within = [&](Bounds value) {
                    return value.valid && value.left >= area.left && value.right <= area.right &&
                        value.bottom >= area.bottom && value.top <= area.top;
                };
                for (auto region : _placementPaint) {
                    RequireLimit(++comparisons <= 1000000, "PDF image composite relation budget exceeded.");
                    if (!ClipPlacement(region) || region.operation <= backdrop ||
                        region.operation >= image.operation) continue;
                    if (!region.bounds.valid) { safe = false; break; }
                    if (!Intersects(area, region.bounds)) continue;
                    if (nonvector.count(region.operation) || !within(region.bounds)) {
                        safe = false; break;
                    }
                    earlier.insert(region.operation);
                }
                for (auto region : _placementPaint) {
                    RequireLimit(++comparisons <= 1000000, "PDF image composite relation budget exceeded.");
                    if (!ClipPlacement(region) || !earlier.count(region.operation)) continue;
                    if (!within(region.bounds)) { safe = false; break; }
                }
                if (safe) paint.insert(earlier.begin(), earlier.end());
            }
            std::set<size_t> hiddenPaint;
            for (auto region : _placementPaint) {
                    RequireLimit(++comparisons <= 1000000, "PDF image composite relation budget exceeded.");
                    if (!ClipPlacement(region)) continue;
                if (region.operation <= backdrop || region.operation == image.operation ||
                    paint.count(region.operation)) continue;
                if (!region.bounds.valid) { safe = false; break; }
                auto extent = region.bounds;
                bool clippedFill = false;
                if (region.operation < image.operation && !region.stroke && !region.text) {
                    for (auto const& fill : _fills) {
                        RequireLimit(++comparisons <= 1000000, "PDF image composite relation budget exceeded.");
                        if (fill.operation == region.operation && (fill.rectangle || fill.clippedRectangle)) {
                            extent = fill.bounds;
                            clippedFill = true;
                            break;
                        }
                    }
                }
                // For opaque images, coincident edges cover earlier filled
                // rectangles too. Use their proved clipped extent, never a text
                // or stroke hull, and do not require the glyph inset margin.
                bool covered = region.operation < image.operation && opaque.count(image.object) &&
                    (Contains(bounds, region.bounds) || (clippedFill && extent.valid &&
                    extent.left >= bounds.left && extent.right <= bounds.right &&
                    extent.bottom >= bounds.bottom && extent.top <= bounds.top));
                if (covered && clippedFill) hiddenPaint.insert(region.operation);
                if (Intersects(area, extent) && !covered) {
                    safe = false; break;
                }
            }
            if (!safe || (area.right - area.left) * (area.top - area.bottom) > _pageArea * .8) {
                ambiguous.insert(image.object); continue;
            }
            for (auto const& region : _placementPaint)
                if (region.stroke || region.text) hiddenPaint.erase(region.operation);
            auto& composite = result[image.object];
            composite.paint.insert(paint.begin(), paint.end());
            composite.paint.insert(hiddenPaint.begin(), hiddenPaint.end());
            composite.images.insert(images.begin(), images.end());
            if (localPaper) composite.fills.emplace(backdrop, localPaper->sourceColor);
            if (!localPaper || extendedPaper) {
                auto& backing = composite.backings[extendedPaper ? 0 : backdrop];
                backing.color = {1, 1, 1};
                backing.pageCoordinates = true;
                backing.rectangles.push_back(area);
            }
        }
        for (int object : ambiguous) result.erase(object);
        return result;
    }

    std::map<size_t, Color> ResolveFractionalFills() const
    {
        std::map<size_t, Color> result;
        size_t comparisons = 0;
        auto rules = ResolveLightRules();
        std::set<size_t> fractional;
        for (auto const& fill : _fractionalFills) fractional.insert(fill.region.operation);
        for (auto const& fill : _fractionalFills) {
            auto const& region = fill.region;
            if (!fill.darkPaper || !std::isfinite(fill.alpha) || fill.alpha < .4 || fill.alpha > .8)
                Unsupported("PDF fractional fill needs a verified dark paper backdrop and bounded opacity.");
            for (auto const& image : _imagePlacements) {
                RequireLimit(++comparisons <= 1000000, "PDF fractional-fill relation budget exceeded.");
                Bounds bounds;
                auto m = image.matrix;
                for (double x : {0., 1.}) for (double y : {0., 1.})
                    Include(bounds, m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);
                if (Intersects(bounds, region.bounds))
                    Unsupported("PDF fractional fill overlaps an image backdrop.");
            }
            for (auto const& form : _formPlacements) {
                RequireLimit(++comparisons <= 1000000, "PDF fractional-fill relation budget exceeded.");
                if (Intersects(form.bounds, region.bounds))
                    Unsupported("PDF fractional fill overlaps a form backdrop.");
            }
            for (auto const& prior : _fills) {
                RequireLimit(++comparisons <= 1000000, "PDF fractional-fill relation budget exceeded.");
                if (prior.operation >= region.operation || IsFullPage(prior.bounds) ||
                    !Intersects(prior.bounds, region.bounds) || rules.count(prior.operation)) continue;
                if (!fractional.count(prior.operation))
                    Unsupported("PDF fractional fill has an ambiguous painted backdrop.");
                Bounds overlap{std::max(prior.bounds.left, region.bounds.left),
                    std::max(prior.bounds.bottom, region.bounds.bottom),
                    std::min(prior.bounds.right, region.bounds.right),
                    std::min(prior.bounds.top, region.bounds.top), true};
                for (size_t i = 0; i < _lightText.size(); ++i) {
                    RequireLimit(++comparisons <= 1000000, "PDF fractional-fill relation budget exceeded.");
                    if (!_lightText[i].valid)
                        Unsupported("PDF text crosses overlapping translucent panels.");
                    for (auto const& glyph : _textGlyphs[i]) {
                        RequireLimit(++comparisons <= 1000000, "PDF fractional-fill relation budget exceeded.");
                        if (Intersects(glyph, overlap))
                            Unsupported("PDF text crosses overlapping translucent panels.");
                    }
                }
            }
            bool accepted = false;
            std::vector<double> targets{.135, .10, .075, .06, .045, .03, .02, .01};
            double existing = Luminance(Composite(region.sourceColor, Paper, fill.alpha));
            auto matched = std::find_if(targets.begin(), targets.end(), [&](double value) {
                return std::abs(existing - value) <= .000002;
            });
            // Prefer a current palette value only after rerunning the same
            // contrast proof. Searching brighter alternatives first would mix
            // white into a previously darkened hue and drift on every retry.
            if (matched != targets.end()) std::rotate(targets.begin(), matched, matched + 1);
            for (double luminance : targets) {
                auto target = ToneComposite(region.sourceColor, fill.alpha, luminance);
                auto background = Composite(target, Paper, fill.alpha);
                bool readable = true;
                for (size_t i = 0; i < _lightText.size(); ++i) {
                    RequireLimit(++comparisons <= 1000000, "PDF fractional-fill relation budget exceeded.");
                    if (!_lightText[i].valid)
                        Unsupported("PDF fractional-fill text bounds are unknown.");
                    if (!Intersects(_lightText[i], region.bounds)) continue;
                    auto foreground = Recolor(_textColors[i], true, false);
                    if (_textOperations[i] < region.operation)
                        foreground = Composite(target, foreground, fill.alpha);
                    if (Contrast(foreground, background) < 4.6) { readable = false; break; }
                }
                for (auto const& rule : _ruleCandidates) {
                    RequireLimit(++comparisons <= 1000000, "PDF fractional-fill relation budget exceeded.");
                    if (!rules.count(rule.operation) || !Intersects(rule.bounds, region.bounds)) continue;
                    auto foreground = Recolor(rule.sourceColor, true, false);
                    if (rule.operation < region.operation)
                        foreground = Composite(target, foreground, fill.alpha);
                    if (Contrast(foreground, background) < 4.6) { readable = false; break; }
                }
                if (readable) {
                    result.emplace(region.operation, target);
                    accepted = true;
                    break;
                }
            }
            if (!accepted) Unsupported("PDF translucent panel cannot retain readable text contrast.");
        }
        return result;
    }

    static Bounds PlaceFormText(Bounds text, std::array<double, 6> const& m)
    {
        if (!text.valid) return {};
        Bounds placed;
        for (double x : {text.left, text.right}) for (double y : {text.bottom, text.top}) {
            double px = m[0] * x + m[2] * y + m[4], py = m[1] * x + m[3] * y + m[5];
            RequireLimit(std::isfinite(px) && std::isfinite(py) && std::abs(px) <= 200000000 &&
                std::abs(py) <= 200000000, "PDF form text placement exceeds its limit.");
            Include(placed, px, py);
        }
        return placed;
    }

    std::vector<Bounds> FormTextBounds(std::map<int, FormContrast> const& forms) const
    {
        auto result = _lightText;
        for (auto const& placement : _formPlacements) {
            auto found = forms.find(placement.object);
            Require(found != forms.end(), "PDF form text inventory is missing.");
            RequireLimit(result.size() + found->second.textBounds.size() <= 8192,
                "PDF form text bounds budget exceeded.");
            for (auto const& text : found->second.textBounds)
                result.push_back(PlaceFormText(text, placement.matrix));
        }
        return result;
    }

    void ValidateFormContrast(std::map<int, FormContrast> const& forms) const
    {
        size_t comparisons = 0;
        for (auto const& placement : _formPlacements) {
            auto found = forms.find(placement.object);
            Require(found != forms.end(), "PDF form contrast inventory is missing.");
            if (found->second.darkFill)
                for (auto const& text : _lightText) {
                    RequireLimit(++comparisons <= 1000000, "PDF form contrast budget exceeded.");
                    if (!text.valid || Intersects(placement.bounds, text))
                        Unsupported("Overlapping form fills and page text need contrast-aware recoloring.");
                }
            if (found->second.text)
                for (auto const& region : _regions) {
                    RequireLimit(++comparisons <= 1000000, "PDF form contrast budget exceeded.");
                    bool touchesText = !region.bounds.valid || found->second.textBounds.empty();
                    for (auto const& text : found->second.textBounds) {
                        RequireLimit(++comparisons <= 1000000, "PDF form contrast budget exceeded.");
                        auto placed = PlaceFormText(text, placement.matrix);
                        touchesText |= !placed.valid || Intersects(placed, region.bounds);
                    }
                    if (!touchesText) continue;
                    bool readablePanel = (region.operation < placement.operation ||
                        _arrowOverlayOperations.count(region.operation)) &&
                        PalettePanel(region.sourceColor) && found->second.knownTextColors &&
                        !found->second.textColors.empty();
                    for (auto const& color : found->second.textColors) {
                        RequireLimit(++comparisons <= 1000000, "PDF form contrast budget exceeded.");
                        if (Contrast(Recolor(color, true, false),
                            Recolor(region.sourceColor, false, false)) < 4.5) readablePanel = false;
                    }
                    if (readablePanel) continue;
                    if (!region.bounds.valid || Intersects(placement.bounds, region.bounds))
                        Unsupported("Overlapping page fills and form text need contrast-aware recoloring.");
                }
            for (auto const& other : _formPlacements) {
                if (other.operation <= placement.operation) continue;
                RequireLimit(++comparisons <= 1000000, "PDF form contrast budget exceeded.");
                auto another = forms.find(other.object);
                Require(another != forms.end(), "PDF form contrast inventory is missing.");
                if ((found->second.darkFill && another->second.text ||
                    found->second.text && another->second.darkFill) &&
                    Intersects(placement.bounds, other.bounds))
                    Unsupported("Overlapping PDF forms need contrast-aware recoloring.");
            }
        }
    }

    std::set<size_t> ResolveLightRules() const
    {
        std::set<size_t> rules;
        size_t comparisons = 0;
        for (auto const& rule : _ruleCandidates) rules.insert(rule.operation);
        std::set<size_t> fractional;
        for (auto const& fill : _fractionalFills) fractional.insert(fill.region.operation);
        // Adjacent white segments may overlap at their join. Admit the entire
        // connected group only when no other visible paint intersects it.
        bool removed;
        do {
            removed = false;
            for (auto const& rule : _ruleCandidates) {
                if (!rules.count(rule.operation)) continue;
                for (auto const& paint : _placementPaint) {
                    RequireLimit(++comparisons <= 1'000'000, "PDF light-rule relation budget exceeded.");
                    // Translucent fills are checked against the rule's ink by
                    // ResolveFractionalFills before any rewrite is admitted.
                    bool sameInkStroke = _strokeOperations.count(paint.operation) &&
                        SameColor(Recolor(rule.sourceColor, true, false),
                            Recolor(paint.sourceColor, true, false));
                    if (!rules.count(paint.operation) && !SameColor(rule.sourceColor, paint.sourceColor) &&
                        !fractional.count(paint.operation) && !sameInkStroke &&
                        (!paint.bounds.valid || Intersects(rule.bounds, paint.bounds))) {
                        rules.erase(rule.operation);
                        removed = true;
                        break;
                    }
                }
            }
        } while (removed);
        for (auto const& arrow : _arrowCandidates) {
            bool clear = true;
            for (auto const& text : _lightText) {
                RequireLimit(++comparisons <= 1000000, "PDF foreground-shape relation budget exceeded.");
                if (!text.valid || Intersects(arrow.bounds, text)) { clear = false; break; }
            }
            if (clear) rules.insert(arrow.operation);
        }
        return rules;
    }

    void ValidateSoftPanelPlacements(std::map<int, DiagramSupport> const& images,
        std::set<int> const& panels) const
    {
        auto foreground = ResolveLightRules();
        size_t comparisons = 0, pixels = 0;
        for (auto const& placement : _imagePlacements) {
            if (!panels.count(placement.object)) continue;
            auto const& image = images.at(placement.object);
            auto m = placement.matrix;
            if (!placement.paperOperation || Luminance(placement.paper) >= .05 ||
                std::abs(m[1]) > .000001 || std::abs(m[2]) > .000001 ||
                std::abs(m[0]) < .000001 || std::abs(m[3]) < .000001 ||
                (placement.clipped && !placement.rectangularClip.valid))
                Unsupported("Soft image panels need a verified flat dark paper backdrop.");
            Bounds bounds{std::min(m[4], m[4] + m[0]), std::min(m[5], m[5] + m[3]),
                std::max(m[4], m[4] + m[0]), std::max(m[5], m[5] + m[3]), true};
            for (auto const& paint : _placementPaint) {
                RequireLimit(++comparisons <= 1000000, "PDF soft-panel relation budget exceeded.");
                if (paint.operation <= placement.paperOperation || paint.operation >= placement.operation) continue;
                if (!paint.bounds.valid) Unsupported("Soft image panel has unknown prior paint.");
                if (!Intersects(paint.bounds, bounds)) continue;
                bool lightInk = Light(paint.sourceColor) > .85 && Saturation(paint.sourceColor) < .15 &&
                    (_strokeOperations.count(paint.operation) || foreground.count(paint.operation));
                if (lightInk) continue;
                double u0 = (paint.bounds.left - m[4]) / m[0], u1 = (paint.bounds.right - m[4]) / m[0];
                double v0 = 1 - (paint.bounds.bottom - m[5]) / m[3], v1 = 1 - (paint.bounds.top - m[5]) / m[3];
                int left = std::max(0, static_cast<int>(std::floor(Clamp(std::min(u0, u1)) * image.width)) - 1);
                int right = std::min(image.width, static_cast<int>(std::ceil(Clamp(std::max(u0, u1)) * image.width)) + 1);
                int top = std::max(0, static_cast<int>(std::floor(Clamp(std::min(v0, v1)) * image.height)) - 1);
                int bottom = std::min(image.height, static_cast<int>(std::ceil(Clamp(std::max(v0, v1)) * image.height)) + 1);
                for (int y = top; y < bottom; ++y) for (int x = left; x < right; ++x) {
                    RequireLimit(++pixels <= MaximumTotalPixels, "PDF soft-panel overlap budget exceeded.");
                    unsigned alpha = static_cast<unsigned char>(image.alpha[static_cast<size_t>(y) * image.width + x]);
                    if (alpha != 0 && alpha != 255)
                        Unsupported("Soft image panel edge overlaps unvalidated prior paint.");
                }
            }
        }
    }

    void ValidateImageText(std::set<int> const& recolored,
        std::map<int, DiagramSupport> const& diagrams) const
    {
        size_t comparisons = 0;
        size_t testedPixels = 0;
        for (auto const& placement : _imagePlacements) {
            if (!recolored.count(placement.object)) continue;
            Bounds bounds;
            auto m = placement.matrix;
            for (double x : {0., 1.}) for (double y : {0., 1.})
                Include(bounds, m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);
            for (size_t index = 0; index < _lightText.size(); ++index) {
                RequireLimit(++comparisons <= 1'000'000, "PDF image/text relation budget exceeded.");
                if (_textOperations[index] <= placement.operation ||
                    (_lightText[index].valid && !Intersects(bounds, _lightText[index]))) continue;
                size_t cover = 0;
                for (auto const& fill : _fills) {
                    RequireLimit(++comparisons <= 1'000'000, "PDF image/text relation budget exceeded.");
                    if (fill.operation > placement.operation && fill.operation < _textOperations[index] &&
                        fill.rectangle && Contains(fill.bounds, _lightText[index])) {
                        // Admitted axial stops map to luminance <= .135;
                        // interpolation cannot exceed that upper bound.
                        if (!_shadingOperations.count(fill.operation) ||
                            (Luminance(Recolor(_textColors[index], true, false)) + .05) / .185 >= 4.5)
                            cover = fill.operation;
                    }
                }
                if (cover) {
                    for (auto const& paint : _placementPaint) {
                        RequireLimit(++comparisons <= 1'000'000, "PDF image/text relation budget exceeded.");
                        if (paint.operation > cover && paint.operation < _textOperations[index] &&
                            (!paint.bounds.valid || Intersects(paint.bounds, _lightText[index]))) {
                            cover = 0;
                            break;
                        }
                    }
                }
                if (cover) continue;
                auto diagram = diagrams.find(placement.object);
                auto text = _lightText[index];
                if (diagram == diagrams.end() || !text.valid ||
                    (placement.clipped && !placement.rectangularClip.valid) ||
                    std::abs(m[1]) > .000001 || std::abs(m[2]) > .000001 ||
                    std::abs(m[0]) < .000001 || std::abs(m[3]) < .000001)
                    Unsupported("Text over a recolored image needs local image contrast validation.");
                text.left = std::max(text.left, bounds.left);
                text.bottom = std::max(text.bottom, bounds.bottom);
                text.right = std::min(text.right, bounds.right);
                text.top = std::min(text.top, bounds.top);
                if (placement.clipped) {
                    text.left = std::max(text.left, placement.rectangularClip.left);
                    text.bottom = std::max(text.bottom, placement.rectangularClip.bottom);
                    text.right = std::min(text.right, placement.rectangularClip.right);
                    text.top = std::min(text.top, placement.rectangularClip.top);
                }
                if (text.left >= text.right || text.bottom >= text.top) continue;
                for (auto const& paint : _placementPaint) {
                    RequireLimit(++comparisons <= 1'000'000, "PDF image/text relation budget exceeded.");
                    if (paint.operation > placement.operation && paint.operation < _textOperations[index] &&
                        (!paint.bounds.valid || Intersects(paint.bounds, text)))
                        Unsupported("Intervening paint prevents image/text contrast validation.");
                }
                auto const& image = diagram->second;
                double u0 = (text.left - m[4]) / m[0], u1 = (text.right - m[4]) / m[0];
                double v0 = 1 - (text.bottom - m[5]) / m[3], v1 = 1 - (text.top - m[5]) / m[3];
                int left = std::max(0, static_cast<int>(std::floor(Clamp(std::min(u0, u1)) * image.width)) - 1);
                int right = std::min(image.width, static_cast<int>(std::ceil(Clamp(std::max(u0, u1)) * image.width)) + 1);
                int top = std::max(0, static_cast<int>(std::floor(Clamp(std::min(v0, v1)) * image.height)) - 1);
                int bottom = std::min(image.height, static_cast<int>(std::ceil(Clamp(std::max(v0, v1)) * image.height)) + 1);
                std::vector<std::array<int, 4>> footprints;
                auto glyphs = _textGlyphs[index];
                if (glyphs.empty()) glyphs.push_back(text);
                for (auto glyph : glyphs) {
                    if (!glyph.valid) { footprints = {{left, top, right, bottom}}; break; }
                    double gx0 = (glyph.left - m[4]) / m[0], gx1 = (glyph.right - m[4]) / m[0];
                    double gy0 = 1 - (glyph.bottom - m[5]) / m[3], gy1 = 1 - (glyph.top - m[5]) / m[3];
                    footprints.push_back({
                        std::max(left, static_cast<int>(std::floor(Clamp(std::min(gx0, gx1)) * image.width)) - 1),
                        std::max(top, static_cast<int>(std::floor(Clamp(std::min(gy0, gy1)) * image.height)) - 1),
                        std::min(right, static_cast<int>(std::ceil(Clamp(std::max(gx0, gx1)) * image.width)) + 1),
                        std::min(bottom, static_cast<int>(std::ceil(Clamp(std::max(gy0, gy1)) * image.height)) + 1)});
                }
                double ink = Luminance(Recolor(_textColors[index], true, false));
                size_t contactPixels = 0, footprintPixels = 0;
                for (int y = top; y < bottom; ++y) {
                    // Union the padded glyph intervals on each row. Spaces and
                    // inter-glyph gaps are not painted text; do not reject a
                    // diagram border there or count overlapping boxes twice.
                    std::vector<std::pair<int, int>> intervals, merged;
                    for (auto box : footprints) {
                        RequireLimit(++comparisons <= 1'000'000, "PDF glyph/image relation budget exceeded.");
                        if (y >= box[1] && y < box[3] && box[0] < box[2])
                            intervals.emplace_back(box[0], box[2]);
                    }
                    std::sort(intervals.begin(), intervals.end());
                    for (auto interval : intervals) {
                        if (!merged.empty() && merged.back().second >= interval.first)
                            merged.back().second = std::max(merged.back().second, interval.second);
                        else merged.push_back(interval);
                    }
                    for (auto [start, end] : merged) for (int x = start; x < end; ++x) {
                    RequireLimit(++testedPixels <= MaximumTotalPixels, "PDF image/text pixel budget exceeded.");
                    size_t pixel = static_cast<size_t>(y) * image.width + x;
                    ++footprintPixels;
                    if (!image.alpha.empty() && static_cast<unsigned char>(image.alpha[pixel]) != 255)
                        Unsupported("Text over a transparent image requires composite contrast validation.");
                    Color background{};
                    for (size_t c = 0; c < 3; ++c)
                        background[c] = static_cast<unsigned char>(image.pixels[pixel * 3 + c]) / 255.0;
                    double paper = Luminance(background);
                    // Requiring a uniformly darker background also covers any
                    // interpolation between pixels inside the padded footprint.
                    // A conservative font box can include an existing diagram
                    // stroke touching a label. Jointly recolored neutral dark
                    // ink is not paper; keep that original foreground contact.
                    unsigned gray = static_cast<unsigned char>(image.sourceGray[pixel]);
                    bool neutralText =
                        Saturation(_textColors[index]) < .10 &&
                        *std::max_element(_textColors[index].begin(), _textColors[index].end()) <= .4;
                    // This exception is only a sparse existing contact between
                    // near-black foregrounds that both map near the new ink.
                    // A gray panel is not foreground, even if it is neutral.
                    bool sharedInk = gray <= 64 && neutralText &&
                        Luminance(_textColors[index]) <= .06 && paper >= ink * .65;
                    contactPixels += sharedInk;
                    double requiredContrast = 4.5;
                    if (neutralText) {
                        double originalInk = Luminance(_textColors[index]);
                        double originalPaper = Luminance({gray / 255., gray / 255., gray / 255.});
                        requiredContrast = std::min(requiredContrast,
                            (std::max(originalInk, originalPaper) + .05) /
                            (std::min(originalInk, originalPaper) + .05));
                    }
                    if (!sharedInk && (ink <= paper || (ink + .05) / (paper + .05) < requiredContrast))
                        Unsupported("Recolored image does not provide readable local text contrast.");
                    }
                }
                if (contactPixels * 10 > footprintPixels)
                    Unsupported("Image foreground covers too much of the overlaid text footprint.");
            }
        }
    }

    void ValidateRetainedPanelText(std::set<int> const& panels,
        std::set<size_t> const& unchangedPaint, std::set<int> const& retainedImages,
        std::set<int> const& backedImages) const
    {
        std::set<size_t> independentImages;
        for (auto const& placement : _imagePlacements) {
            // CloneResources has already validated the image and its masks. An
            // opaque image hides the earlier image wherever it paints. Retained
            // samples and masks preserve their original composite unless a new
            // backing is inserted; those placements still need the normal guard.
            if (placement.opaque ||
                retainedImages.count(placement.object) && !backedImages.count(placement.object))
                independentImages.insert(placement.operation);
        }
        size_t comparisons = 0;
        for (auto const& placement : _imagePlacements) {
            if (!panels.count(placement.object)) continue;
            Bounds bounds;
            auto m = placement.matrix;
            for (double x : {0., 1.}) for (double y : {0., 1.})
                Include(bounds, m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);
            for (auto paint : _placementPaint) {
                RequireLimit(++comparisons <= 1000000, "PDF retained-image paint budget exceeded.");
                // The retained image and this vector operation both keep their
                // source colors, so their existing local contrast cannot worsen.
                // This proof comes from both writer targets, not palette membership.
                if (unchangedPaint.count(paint.operation) || independentImages.count(paint.operation) ||
                    !ClipPlacement(paint)) continue;
                if (paint.operation > placement.operation &&
                    (!paint.bounds.valid || Intersects(bounds, paint.bounds)))
                    throw Problem(3, "Paint over retained image " + std::to_string(placement.object) +
                        " at operation " + std::to_string(paint.operation) + " needs local contrast validation.");
            }
        }
    }

    void ValidateSparsePlacements(std::map<int, MaskSupport> const& masks) const
    {
        size_t comparisons = 0, testedPixels = 0;
        for (auto const& placement : _imagePlacements) {
            auto found = masks.find(placement.object);
            if (found == masks.end()) continue;
            auto const& image = found->second;
            auto m = placement.matrix;
            if (std::abs(m[1]) > .000001 || std::abs(m[2]) > .000001 ||
                std::abs(m[0]) < .000001 || std::abs(m[3]) < .000001)
                Unsupported("Sparse annotation transform needs placement-aware recoloring.");
            Bounds footprint{std::min(m[4], m[4] + m[0]), std::min(m[5], m[5] + m[3]),
                std::max(m[4], m[4] + m[0]), std::max(m[5], m[5] + m[3]), true};
            for (auto const& paint : _placementPaint) {
                if (paint.operation >= placement.operation) break;
                RequireLimit(++comparisons <= 1'000'000, "PDF annotation relation budget exceeded.");
                if (!paint.bounds.valid)
                    Unsupported("Sparse annotation has an unbounded painted backdrop.");
                if (!Intersects(footprint, paint.bounds)) continue;
                double u0 = (paint.bounds.left - m[4]) / m[0];
                double u1 = (paint.bounds.right - m[4]) / m[0];
                // PDF image samples start at the top of its unit square.
                double v0 = 1 - (paint.bounds.bottom - m[5]) / m[3];
                double v1 = 1 - (paint.bounds.top - m[5]) / m[3];
                int left = std::max(0, static_cast<int>(std::floor(Clamp(std::min(u0, u1)) * image.width)) - 1);
                int right = std::min(image.width, static_cast<int>(std::ceil(Clamp(std::max(u0, u1)) * image.width)) + 1);
                int top = std::max(0, static_cast<int>(std::floor(Clamp(std::min(v0, v1)) * image.height)) - 1);
                int bottom = std::min(image.height, static_cast<int>(std::ceil(Clamp(std::max(v0, v1)) * image.height)) + 1);
                for (int y = top; y < bottom; ++y) for (int x = left; x < right; ++x) {
                    RequireLimit(++testedPixels <= MaximumTotalPixels,
                        "PDF annotation overlap pixel budget exceeded.");
                    if (static_cast<unsigned char>(image.alpha[static_cast<size_t>(y) * image.width + x]) != 0)
                        Unsupported("Sparse annotation overlaps earlier page marks through its mask.");
                }
            }
        }
    }

    ContrastPlan ResolveContrast() const
    {
        ContrastPlan plan;
        auto& retained = plan.fills;
        if (_regions.empty() || _lightText.empty()) return plan;
        for (auto const& region : _regions)
            if (!region.bounds.valid)
                Unsupported("Unbounded dark fill and light text need contrast-aware recoloring.");
        size_t comparisons = 0;
        auto preservedOutlines = PreservedPanelOutlines(comparisons);
        OccludedTextPlan occluded;
        for (auto const& text : _lightText) {
            if (!text.valid)
                Unsupported("Light text with a dark filled region needs measurable bounds for contrast-aware recoloring.");
            size_t containing = 0;
            auto textColor = _textColors.at(&text - _lightText.data());
            auto unchangedRelationship = [&](PaintRegion const& region) {
                return SameColor(textColor, region.sourceColor) ||
                    (PalettePanel(region.sourceColor) &&
                    Contrast(Recolor(textColor, true, false), Recolor(region.sourceColor, false, false)) + .00001 >=
                    std::min(4.5, Contrast(textColor, region.sourceColor)));
            };
            bool needsBackground = false;
            for (auto const& region : _regions) {
                RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                if (region.operation > _textOperations.at(&text - _lightText.data()) &&
                    OpaquePanelShape(region) && Intersects(text, region.bounds))
                    occluded.fills[region.operation].insert(&text - _lightText.data());
                // Palette backgrounds stay dark while text maps to light ink.
                // Do not expose source glyphs hidden against that background.
                if (PalettePanel(region.sourceColor) && Contrast(textColor, region.sourceColor) < 1.5 &&
                    Intersects(text, region.bounds))
                    Unsupported("Ambiguous dark fill and light text need contrast-aware recoloring.");
                if (Intersects(text, region.bounds) && !unchangedRelationship(region)) needsBackground = true;
            }
            if (!needsBackground) {
                for (auto const& region : _regions) {
                    RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                    if (PalettePanel(region.sourceColor) && Intersects(text, region.bounds) &&
                        CanCrossPanel(region, &text - _lightText.data(), comparisons, plan.strokes,
                            preservedOutlines, occluded))
                        retained.insert(region.operation);
                }
                continue;
            }
            // A bright badge inside a dark card is also an opaque background.
            // Select it before evaluating the older dark paints beneath it.
            for (auto fill = _fills.rbegin(); fill != _fills.rend(); ++fill) {
                RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                if (fill->operation < _textOperations.at(&text - _lightText.data()) &&
                    !IsFullPage(fill->bounds) && !SameColor(textColor, fill->sourceColor) &&
                    PanelContainsText(*fill, &text - _lightText.data(), comparisons)) {
                    containing = fill->operation;
                    break;
                }
            }
            // The last fully covering opaque panel determines the background.
            // Earlier panels are hidden at every glyph, including nested cards.
            for (auto entry = _regions.rbegin(); entry != _regions.rend(); ++entry) {
                auto const& region = *entry;
                RequireLimit(++comparisons <= 1'000'000,
                    "PDF contrast relation budget exceeded.");
                if (!Intersects(text, region.bounds)) continue;
                if (containing && region.operation <= containing) continue;
                if (region.operation > _textOperations.at(&text - _lightText.data()) && OpaquePanelShape(region)) {
                    // A later opaque card hides earlier ink. Its final role
                    // depends on later labels, so validate the finished plan.
                    occluded.fills[region.operation].insert(&text - _lightText.data());
                    continue;
                }
                if (std::abs(textColor[0] - region.sourceColor[0]) < .000001 &&
                    std::abs(textColor[1] - region.sourceColor[1]) < .000001 &&
                    std::abs(textColor[2] - region.sourceColor[2]) < .000001)
                    continue; // Same-color underlines/ink retain their original relationship.
                // A recognized palette panel already follows the background
                // branch during rewriting. Its edge may cross a conservative
                // text box without changing that text/background relationship.
                if (PalettePanel(region.sourceColor) &&
                    Contrast(Recolor(textColor, true, false),
                        Recolor(region.sourceColor, false, false)) + .00001 >=
                    std::min(4.5, Contrast(textColor, region.sourceColor)))
                    continue;
                if (PanelContainsText(region, &text - _lightText.data(), comparisons) &&
                    region.operation < _textOperations.at(&text - _lightText.data())) {
                    containing = region.operation;
                    break;
                } else if (CanCrossPanel(region, &text - _lightText.data(), comparisons, plan.strokes,
                    preservedOutlines, occluded)) {
                    retained.insert(region.operation);
                } else {
                    throw Problem(3, "Ambiguous dark fill and light text need contrast-aware recoloring: text " +
                        std::to_string(_textOperations.at(&text - _lightText.data())) + ", fill " +
                        std::to_string(region.operation) + ".");
                }
            }
            if (containing) {
                for (auto const& panel : _regions) {
                    RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                    if (panel.operation <= containing && PanelContainsText(panel, &text - _lightText.data(), comparisons))
                        retained.insert(panel.operation);
                }
                for (auto const& fill : _placementPaint) {
                    RequireLimit(++comparisons <= 1'000'000,
                        "PDF contrast relation budget exceeded.");
                    if (fill.text && SameColor(fill.sourceColor, textColor) &&
                        SameColor(Recolor(fill.sourceColor, true, false), Recolor(textColor, true, false))) continue;
                    auto outline = preservedOutlines.find(fill.operation);
                    if (fill.stroke && outline != preservedOutlines.end() && outline->second == containing) continue;
                    if ((fill.operation > containing || fill.operation == containing && fill.stroke) &&
                        fill.operation < _textOperations.at(&text - _lightText.data())) {
                        auto const& glyphs = _textGlyphs.at(&text - _lightText.data());
                        bool intersects = !fill.bounds.valid || (glyphs.empty() && Intersects(text, fill.bounds));
                        for (auto const& glyph : glyphs) {
                            RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                            if (Intersects(glyph, fill.bounds)) { intersects = true; break; }
                        }
                        if (intersects)
                            Unsupported("Covered dark panel and light text need contrast-aware recoloring.");
                    }
                }
                retained.insert(containing);
            }
        }
        // A different text run may have established this fill as a background.
        // Recheck the complete plan so low-contrast source text cannot become
        // visible merely because that other run caused the panel to stay dark.
        for (auto const& fill : _fills) {
            if (!retained.count(fill.operation)) continue;
            for (size_t i = 0; i < _lightText.size(); ++i) {
                RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                if (fill.operation < _textOperations[i] && Contrast(fill.sourceColor, _textColors[i]) < 1.5 &&
                    Intersects(fill.bounds, _lightText[i])) {
                    // An opaque badge can fully hide an older card beneath the
                    // glyphs. Only a later, retained, legible backing discharges
                    // this check; ambiguous or same-color overlays still fail.
                    bool covered = false;
                    for (auto const& backing : _fills) {
                        RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                        if (backing.operation > fill.operation && backing.operation < _textOperations[i] &&
                            retained.count(backing.operation) && Contrast(backing.sourceColor, _textColors[i]) >= 1.5 &&
                            PanelContainsText(backing, i, comparisons)) {
                            covered = true;
                            break;
                        }
                    }
                    if (!covered)
                        Unsupported("Ambiguous dark fill and light text need contrast-aware recoloring.");
                }
            }
        }
        for (auto const& outline : preservedOutlines) {
            bool background = retained.count(outline.second);
            for (auto const& region : _regions) {
                RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                if (region.operation == outline.second && PalettePanel(region.sourceColor)) background = true;
            }
            if (background) plan.preservedStrokes.insert(outline.first);
        }
        for (auto const& fill : _fills) {
            auto deferred = occluded.fills.find(fill.operation);
            if (deferred == occluded.fills.end()) continue;
            if (!retained.count(fill.operation) && !PalettePanel(fill.sourceColor))
                Unsupported("Ambiguous dark fill and light text need contrast-aware recoloring.");
            for (auto index : deferred->second) {
                RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                // Prove the exposed ink's original backdrop before accepting
                // a later opaque card that hides part of that ink.
                if (!occluded.provedBackdrops.count(index) &&
                    !CanRemainOnPaper(index, comparisons, preservedOutlines, occluded))
                    Unsupported("Ambiguous dark fill and light text need contrast-aware recoloring.");
                double original = Contrast(_textColors[index], fill.sourceColor);
                if (original < 1.5 || Contrast(Recolor(_textColors[index], true, false),
                    Recolor(fill.sourceColor, false, false)) + .00001 < std::min(4.5, original))
                    Unsupported("Ambiguous dark fill and light text need contrast-aware recoloring.");
            }
        }
        for (auto operation : occluded.strokes)
            if (!plan.preservedStrokes.count(operation))
                Unsupported("Ambiguous dark fill and light text need contrast-aware recoloring.");
        return plan;
    }

  private:
    void RejectKnockoutText()
    {
        // White glyphs can erase underlines before a final dark glyph run.
        // Mapping that eraser to ink merges the offset glyphs into blobs.
        // Ordinary white labels on filled panels do not establish this motif.
        for (auto const& stroke : _knockoutStrokes) {
            RequireLimit(++_overprintComparisons <= 1000000, "PDF overprint relation budget exceeded.");
            bool overlaps = false;
            for (auto const& glyph : _lastGlyphInk) {
                RequireLimit(++_overprintComparisons <= 1000000, "PDF overprint relation budget exceeded.");
                overlaps |= Intersects(glyph, stroke.bounds);
            }
            if (!overlaps) continue;
            bool covered = false;
            for (auto const& fill : _fills) {
                RequireLimit(++_overprintComparisons <= 1000000, "PDF overprint relation budget exceeded.");
                if (fill.operation > stroke.operation && fill.rectangle && Contains(fill.bounds, stroke.bounds)) {
                    covered = true;
                    break;
                }
            }
            if (!covered) Unsupported("PDF knockout text over prior strokes is unsupported.");
        }
    }

    void RecordPlacement(PaintRegion paint)
    {
        if (_state.clipped) paint.rectangularClip = _state.rectangularClip;
        _placementPaint.push_back(std::move(paint));
    }

    static bool ClipPlacement(PaintRegion& paint)
    {
        // Only local composite proofs use this visible extent. Keep intrinsic
        // text/stroke bounds for the other contrast policies.
        auto clip = paint.rectangularClip;
        if (!paint.bounds.valid || !clip.valid) return true;
        paint.bounds.left = std::max(paint.bounds.left, clip.left);
        paint.bounds.right = std::min(paint.bounds.right, clip.right);
        paint.bounds.bottom = std::max(paint.bounds.bottom, clip.bottom);
        paint.bounds.top = std::min(paint.bounds.top, clip.top);
        return paint.bounds.left < paint.bounds.right && paint.bounds.bottom < paint.bounds.top;
    }

    void RecordPanelPath(std::string const& op)
    {
        if (!_panelPathValid) return;
        if (op == "h") { _panelPathClosed = true; return; }
        if (_panelPathClosed || op == "re" || op == "v" || op == "y" ||
            (op == "m" && !_panelControls.empty()) ||
            (op != "m" && _panelControls.empty()) ||
            _operands.size() != (op == "c" ? 6 : 2) || _panelControls.size() > 60) {
            _panelPathValid = false;
            return;
        }
        std::vector<std::array<double, 2>> points;
        for (size_t i = 0; i < _operands.size(); i += 2) {
            if (!_operands[i].isNumber() || !_operands[i + 1].isNumber()) {
                _panelPathValid = false;
                return;
            }
            auto p = Transform(_operands[i].getNumericValue(), _operands[i + 1].getNumericValue());
            if (!std::isfinite(p[0]) || !std::isfinite(p[1]) ||
                std::abs(p[0]) > 200000000 || std::abs(p[1]) > 200000000) {
                _panelPathValid = false;
                return;
            }
            points.push_back(p);
        }
        if (op != "m") {
            auto start = _panelControls.back(), end = points.back();
            auto aligned = [](auto a, auto b) {
                return std::abs(a[0] - b[0]) < .000001 || std::abs(a[1] - b[1]) < .000001;
            };
            if (op == "l" && !aligned(start, end)) _panelPathValid = false;
            if (op == "c") {
                // A corner may be divided into several monotone cubics. The
                // endpoint polygon and conservative curve inset below establish
                // containment; individual tangents need not be axis aligned.
                bool straight = aligned(start, end);
                for (auto p : points) {
                    if (p[0] < std::min(start[0], end[0]) - .000001 ||
                        p[0] > std::max(start[0], end[0]) + .000001 ||
                        p[1] < std::min(start[1], end[1]) - .000001 ||
                        p[1] > std::max(start[1], end[1]) + .000001) _panelPathValid = false;
                }
                if (!straight) {
                    auto a = points[0], b = points[1];
                    double tx = a[0] - start[0], ty = a[1] - start[1];
                    double ux = end[0] - b[0], uy = end[1] - b[1];
                    if (std::hypot(tx, ty) < .000001 || std::hypot(ux, uy) < .000001)
                        _panelPathValid = false;
                    ++_panelCurveCount;
                }
            }
        }
        _panelControls.insert(_panelControls.end(), points.begin(), points.end());
        _panelEndpoints.push_back(points.back());
        _panelSegmentEnds.push_back(_panelControls.size() - 1);
    }

    std::vector<std::array<double, 2>> RoundedPanelInterior() const
    {
        if (!_panelPathValid || _panelCurveCount < 4 || _panelCurveCount > 16 || _panelEndpoints.size() < 5 ||
            (!_panelPathClosed && _panelEndpoints.front() != _panelEndpoints.back())) return {};
        if (std::abs(_panelEndpoints.front()[0] - _panelEndpoints.back()[0]) > .000001 &&
            std::abs(_panelEndpoints.front()[1] - _panelEndpoints.back()[1]) > .000001) return {};
        auto segments = SubdividedPanelSegments();
        if (_panelControls.back() != _panelControls.front())
            segments.push_back({_panelControls.back(), _panelControls.front()});
        std::vector<std::array<double, 2>> polygon{_panelControls.front()};
        for (auto const& segment : segments) polygon.push_back(segment.back());
        polygon.erase(std::unique(polygon.begin(), polygon.end()), polygon.end());
        if (polygon.front() == polygon.back()) polygon.pop_back();
        // A repeated lap can have a convex endpoint polygon but cancel its
        // interior under even-odd filling. Admit only a single boundary walk.
        for (size_t i = 0; i < polygon.size(); ++i)
            for (size_t j = i + 1; j < polygon.size(); ++j)
                if (std::abs(polygon[i][0] - polygon[j][0]) < .000001 &&
                    std::abs(polygon[i][1] - polygon[j][1]) < .000001) return {};
        double area = 0, winding = 0;
        std::array<double, 2> center{};
        for (auto p : polygon) { center[0] += p[0]; center[1] += p[1]; }
        center[0] /= polygon.size(); center[1] /= polygon.size();
        for (size_t i = 0; i < polygon.size(); ++i) {
            auto a = polygon[i], b = polygon[(i + 1) % polygon.size()];
            a[0] -= center[0]; a[1] -= center[1];
            b[0] -= center[0]; b[1] -= center[1];
            double cross = a[0] * b[1] - a[1] * b[0];
            area += cross;
            winding += std::atan2(cross, a[0] * b[0] + a[1] * b[1]);
        }
        if (std::abs(area) < .000001 || std::abs(std::abs(winding) - 2 * std::acos(-1.0)) > .000001)
            return {};
        // Establish a convex, single-lap chord polygon. Curve controls may have
        // small serialized tangent kinks; they are accounted for below rather
        // than accepted by increasing this geometric tolerance.
        for (size_t i = 0; i < polygon.size(); ++i) {
            auto a = polygon[i], b = polygon[(i + 1) % polygon.size()];
            for (auto p : polygon)
                if (((b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])) *
                    (area > 0 ? 1 : -1) < -.000001) return {};
        }
        auto result = polygon;
        for (auto const& segment : segments) {
            auto a = segment.front(), b = segment.back();
            double dx = b[0] - a[0], dy = b[1] - a[1];
            double length = std::hypot(dx, dy);
            if (length < .000001) {
                if (std::all_of(segment.begin(), segment.end(), [&](auto p) { return p == a; })) continue;
                return {};
            }
            auto distance = [&](std::array<double, 2> p) {
                return (dx * (p[1] - a[1]) - dy * (p[0] - a[0])) * (area > 0 ? 1 : -1) / length;
            };
            double inset = 0;
            for (auto p : segment) inset = std::max(inset, distance(p));
            inset += .00001;
            // A Bezier lies in its control hull. Both it and the straight
            // chord stay outside this inset half-plane throughout their linear
            // homotopy. Points in every inset half-plane therefore keep the
            // chord polygon's winding of one, for either PDF fill rule. This
            // proves containment without modifying or assuming convex curves.
            std::vector<std::array<double, 2>> clipped;
            for (size_t i = 0; i < result.size(); ++i) {
                auto p = result[i], q = result[(i + 1) % result.size()];
                double dp = distance(p) - inset, dq = distance(q) - inset;
                if (dp >= 0) clipped.push_back(p);
                if ((dp >= 0) != (dq >= 0)) {
                    double t = dp / (dp - dq);
                    clipped.push_back({p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1])});
                }
            }
            result = std::move(clipped);
            if (result.size() < 3) return {};
        }
        // Clipping can leave repeated vertices at a collapsed point or line.
        // Such a list cannot establish a covered interior, regardless of count.
        double interiorArea = 0;
        auto origin = result.front();
        for (size_t i = 1; i + 1 < result.size(); ++i)
            interiorArea += (result[i][0] - origin[0]) * (result[i + 1][1] - origin[1]) -
                (result[i][1] - origin[1]) * (result[i + 1][0] - origin[0]);
        if (!std::isfinite(interiorArea) || interiorArea * (area > 0 ? 1 : -1) <= .000001) return {};
        if (area < 0) std::reverse(result.begin(), result.end());
        return result;
    }

    std::vector<std::vector<std::array<double, 2>>> SubdividedPanelSegments() const
    {
        // De Casteljau preserves the curve exactly; each subcurve stays in its
        // control hull. RoundedPanelInterior accounts for its chord deviation.
        // Eight pieces per cubic bound both work and conservative stroke error.
        using Point = std::array<double, 2>;
        std::vector<std::vector<Point>> result;
        auto midpoint = [](Point a, Point b) -> Point {
            return {(a[0] + b[0]) / 2, (a[1] + b[1]) / 2};
        };
        auto split = [&](auto&& self, std::vector<Point> const& p, unsigned depth) -> void {
            if (p.size() != 4 || depth == 0) { result.push_back(p); return; }
            auto a = midpoint(p[0], p[1]), b = midpoint(p[1], p[2]), c = midpoint(p[2], p[3]);
            auto d = midpoint(a, b), e = midpoint(b, c), f = midpoint(d, e);
            self(self, {p[0], a, d, f}, depth - 1);
            self(self, {f, e, c, p[3]}, depth - 1);
        };
        for (size_t i = 1; i < _panelSegmentEnds.size(); ++i) {
            std::vector<Point> segment(_panelControls.begin() + _panelSegmentEnds[i - 1],
                _panelControls.begin() + _panelSegmentEnds[i] + 1);
            split(split, segment, 3);
        }
        return result;
    }

    bool RoundedPanelStrokeBounds(std::vector<Bounds>& result) const
    {
        if (_state.lineWidth <= 0 || RoundedPanelInterior().empty()) return false;
        auto m = _state.matrix;
        if (std::abs(m[1]) > .000001 || std::abs(m[2]) > .000001) return false;
        auto segments = SubdividedPanelSegments();
        if (_panelControls.back() != _panelControls.front())
            segments.push_back({_panelControls.back(), _panelControls.front()});
        // The fill proof permits subdivided corners. Independently bound every
        // nondegenerate join's turn to 90 degrees before using the narrower
        // stroke hull; an acute miter must retain generic conservative bounds.
        std::vector<std::pair<std::array<double, 2>, std::array<double, 2>>> tangents;
        for (auto const& segment : segments) {
            std::array<double, 2> first{}, last{};
            for (size_t i = 1; i < segment.size(); ++i) {
                first = {segment[i][0] - segment.front()[0], segment[i][1] - segment.front()[1]};
                if (first[0] != 0 || first[1] != 0) break;
            }
            for (size_t i = segment.size() - 1; i > 0; --i) {
                last = {segment.back()[0] - segment[i - 1][0], segment.back()[1] - segment[i - 1][1]};
                if (last[0] != 0 || last[1] != 0) break;
            }
            if (first[0] == 0 && first[1] == 0 && last[0] == 0 && last[1] == 0) continue;
            if (std::hypot(first[0], first[1]) <= .000001 || std::hypot(last[0], last[1]) <= .000001) return false;
            tangents.emplace_back(first, last);
        }
        if (tangents.empty()) return false;
        for (size_t i = 0; i < tangents.size(); ++i) {
            auto a = tangents[i].second, b = tangents[(i + 1) % tangents.size()].first;
            if (a[0] * b[0] + a[1] * b[1] < 0) return false;
        }
        // Half-width times sqrt(2) covers caps and the proved right-angle miters.
        double pad = _state.lineWidth * .707107 *
            std::max(std::abs(m[0]) + std::abs(m[2]), std::abs(m[1]) + std::abs(m[3]));
        for (auto const& segment : segments) {
            Bounds bounds;
            for (auto p : segment) Include(bounds, p[0], p[1]);
            bounds.left -= pad; bounds.right += pad;
            bounds.bottom -= pad; bounds.top += pad;
            result.push_back(bounds);
        }
        return true;
    }

    bool PanelContainsInk(PaintRegion const& panel, Bounds const& ink, size_t& comparisons) const
    {
        RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
        if (!Contains(panel.bounds, ink)) return false;
        if (panel.rectangle) return true;
        if (panel.interior.empty()) return false;
        for (size_t i = 0; i < panel.interior.size(); ++i) {
            RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
            auto a = panel.interior[i], b = panel.interior[(i + 1) % panel.interior.size()];
            for (double x : {ink.left, ink.right})
                for (double y : {ink.bottom, ink.top})
                    if ((b[0] - a[0]) * (y - a[1]) - (b[1] - a[1]) * (x - a[0]) <
                        -.0000001 * std::hypot(b[0] - a[0], b[1] - a[1]))
                        return false;
        }
        return true;
    }

    bool PanelContainsText(PaintRegion const& panel, size_t textIndex, size_t& comparisons) const
    {
        auto const& glyphs = _textGlyphs.at(textIndex);
        if (glyphs.empty()) return PanelContainsInk(panel, _lightText.at(textIndex), comparisons);
        return std::all_of(glyphs.begin(), glyphs.end(), [&](auto const& ink) {
            return PanelContainsInk(panel, ink, comparisons);
        });
    }

    bool MatchingPanelOutline(PaintRegion const& panel, PaintRegion const& outline) const
    {
        return outline.operation >= panel.operation && outline.rectangle == panel.rectangle &&
            outline.interior == panel.interior &&
            std::abs(outline.bounds.left - panel.bounds.left) < .000001 &&
            std::abs(outline.bounds.right - panel.bounds.right) < .000001 &&
            std::abs(outline.bounds.bottom - panel.bounds.bottom) < .000001 &&
            std::abs(outline.bounds.top - panel.bounds.top) < .000001;
    }

    bool OpaquePanelShape(PaintRegion const& panel) const
    {
        // Rectangle/interior proofs are recorded only for opaque unclipped
        // fills. Exclude rules and page repaints from this occlusion policy.
        return panel.bounds.valid && (panel.rectangle || !panel.interior.empty()) &&
            panel.bounds.right - panel.bounds.left > 4 && panel.bounds.top - panel.bounds.bottom > 4 &&
            !IsFullPage(panel.bounds);
    }

    std::map<size_t, size_t> PreservedPanelOutlines(size_t& comparisons) const
    {
        std::map<size_t, size_t> result;
        if (_pageArea <= 0 || !summary.paperPresent || summary.photoSceneStart ||
            summary.hasForm || !_preservedPaint.empty()) return result;
        for (auto const& panel : _regions) {
            if (_paperOperation >= panel.operation || (!panel.rectangle && panel.interior.empty())) continue;
            for (auto const& outline : _panelOutlines) {
                RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                if (SameColor(outline.sourceColor, panel.sourceColor) || !MatchingPanelOutline(panel, outline)) continue;
                auto color = outline.sourceColor;
                // Keep a distinct border only if its original color remains
                // at least as legible against both adjacent backgrounds.
                if (Contrast(color, Paper) + .00001 < std::min(3., Contrast(color, _paperColor)) ||
                    Contrast(color, Recolor(panel.sourceColor, false, false)) + .00001 <
                        std::min(3., Contrast(color, panel.sourceColor))) continue;
                std::vector<Bounds> segments;
                for (auto const& paint : _placementPaint) {
                    RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                    if (paint.operation == outline.operation && paint.stroke) segments.push_back(paint.bounds);
                }
                bool safe = !segments.empty(), touchesText = false;
                for (auto const& segment : segments) {
                    if (!segment.valid) { safe = false; break; }
                    // Validate the entire stroke operation, including text
                    // elsewhere along the border, before retaining any of it.
                    for (auto const& paint : _placementPaint) {
                        RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                        if (paint.operation <= _paperOperation || paint.text ||
                            (paint.operation == panel.operation && !paint.stroke) ||
                            (paint.operation == outline.operation && paint.stroke)) continue;
                        if (!paint.bounds.valid || Intersects(segment, paint.bounds)) { safe = false; break; }
                    }
                    if (!safe) break;
                    for (size_t i = 0; i < _lightText.size(); ++i) {
                        if (_textOperations[i] <= _paperOperation) continue;
                        if (!_lightText[i].valid || _textGlyphs[i].empty()) { safe = false; break; }
                        for (auto const& ink : _textGlyphs[i]) {
                            RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                            if (!ink.valid) { safe = false; break; }
                            if (!Intersects(segment, ink)) continue;
                            touchesText = true;
                            double original = Contrast(_textColors[i], color);
                            if (original < 1.5 || Contrast(Recolor(_textColors[i], true, false), color) + .00001 <
                                std::min(4.5, original)) { safe = false; break; }
                        }
                        if (!safe) break;
                    }
                    if (!safe) break;
                }
                if (safe && touchesText) result.emplace(outline.operation, panel.operation);
            }
        }
        return result;
    }

    bool PanelContainsInkCorner(PaintRegion const& panel, Bounds ink, size_t& comparisons) const
    {
        if (!ink.valid) return false;
        // This is an overlap witness, not full glyph containment. The usual
        // quarter-point inset would miss a real, shallow boundary crossing.
        constexpr double margin = .001;
        for (double x : {ink.left, ink.right}) for (double y : {ink.bottom, ink.top}) {
            RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
            if (x <= panel.bounds.left + margin || x >= panel.bounds.right - margin ||
                y <= panel.bounds.bottom + margin || y >= panel.bounds.top - margin) continue;
            if (panel.rectangle) return true;
            bool inside = !panel.interior.empty();
            for (size_t i = 0; i < panel.interior.size(); ++i) {
                RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                auto a = panel.interior[i], b = panel.interior[(i + 1) % panel.interior.size()];
                if ((b[0] - a[0]) * (y - a[1]) - (b[1] - a[1]) * (x - a[0]) <=
                    margin * std::hypot(b[0] - a[0], b[1] - a[1])) { inside = false; break; }
            }
            if (inside) return true;
        }
        return false;
    }

    bool CanRemainOnPaper(size_t textIndex, size_t& comparisons,
        std::map<size_t, size_t> const& preservedOutlines, OccludedTextPlan& occluded) const
    {
        auto operation = _textOperations.at(textIndex);
        auto color = _textColors.at(textIndex);
        auto const& glyphs = _textGlyphs.at(textIndex);
        if (_pageArea <= 0 || summary.photoSceneStart || !_paperOperation ||
            _paperOperation >= operation || glyphs.empty() ||
            Contrast(color, _paperColor) < 1.5 || Contrast(Recolor(color, true, false), Paper) < 4.5)
            return false;
        OccludedTextPlan pending;
        for (auto const& ink : glyphs) {
            if (!ink.valid) return false;
            for (auto const& paint : _placementPaint) {
                RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                if (paint.operation <= _paperOperation || paint.operation == operation) continue;
                // Fill-only text with the same source and target ink keeps its
                // original overlap, including repeated or overprinted labels.
                // Mixed fill/stroke text still needs the full backdrop proof.
                if (paint.text && !paint.stroke && SameColor(paint.sourceColor, color) &&
                    SameColor(Recolor(paint.sourceColor, true, false), Recolor(color, true, false))) continue;
                if (!paint.bounds.valid) return false;
                if (!Intersects(ink, paint.bounds)) continue;
                if (paint.operation > operation) {
                    bool opaqueImage = false;
                    for (auto const& image : _imagePlacements) {
                        RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                        if (image.operation != paint.operation || image.pathPending ||
                            (image.clipped && !image.rectangularClip.valid) ||
                            image.matrix[1] != 0 || image.matrix[2] != 0 ||
                            image.matrix[0] == 0 || image.matrix[3] == 0) continue;
                        auto resources = _resources;
                        auto objects = resources.getKey("/XObject");
                        for (auto const& name : objects.getKeys()) {
                            RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                            auto object = objects.getKey(name);
                            if (object.getObjectID() != image.object) continue;
                            auto dict = object.getDict();
                            auto stencil = dict.getKey("/ImageMask"), embeddedAlpha = dict.getKey("/SMaskInData");
                            opaqueImage = dict.getKey("/SMask").isNull() && dict.getKey("/Mask").isNull() &&
                                (stencil.isNull() || stencil.isBool() && !stencil.getBoolValue()) &&
                                (embeddedAlpha.isNull() || embeddedAlpha.isInteger() && embeddedAlpha.getIntValue() == 0);
                            break;
                        }
                        break;
                    }
                    // At this point photo scenes and fractional placement are
                    // excluded. An unmasked later image hides the earlier ink
                    // wherever it paints; the remaining ink is still on paper.
                    // Global image validation preserves its coverage and rejects
                    // unsupported encodings independently of this contrast proof.
                    if (opaqueImage) continue;
                }
                // These outlines already have a whole-operation contrast and
                // collision proof. Their final unchanged color is checked below.
                if (paint.stroke && preservedOutlines.count(paint.operation)) {
                    pending.strokes.insert(paint.operation);
                    continue;
                }
                bool futurePanel = false;
                if (paint.operation > operation && !paint.text && !paint.stroke)
                    for (auto const& fill : _fills) {
                        RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                        if (fill.operation == paint.operation && OpaquePanelShape(fill)) {
                            pending.fills[fill.operation].insert(textIndex);
                            futurePanel = true;
                            break;
                        }
                    }
                if (!futurePanel) {
                    // An intervening image (or other bounded paint) need not be
                    // opaque when a later opaque rectangle hides its entire
                    // intersection with this glyph. Prove exact coverage, with
                    // no expanded bounds, then defer that rectangle's final
                    // background/contrast validation through the existing plan.
                    Bounds overlap{std::max(ink.left, paint.bounds.left),
                        std::max(ink.bottom, paint.bounds.bottom),
                        std::min(ink.right, paint.bounds.right),
                        std::min(ink.top, paint.bounds.top), true};
                    for (auto const& fill : _fills) {
                        RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                        if (fill.operation <= operation || fill.operation <= paint.operation ||
                            !fill.rectangle || !OpaquePanelShape(fill)) continue;
                        if (overlap.left >= fill.bounds.left && overlap.right <= fill.bounds.right &&
                            overlap.bottom >= fill.bounds.bottom && overlap.top <= fill.bounds.top) {
                            pending.fills[fill.operation].insert(textIndex);
                            futurePanel = true;
                            break;
                        }
                    }
                }
                if (!futurePanel) return false;
            }
        }
        for (auto const& fill : pending.fills)
            occluded.fills[fill.first].insert(fill.second.begin(), fill.second.end());
        occluded.strokes.insert(pending.strokes.begin(), pending.strokes.end());
        occluded.provedBackdrops.insert(textIndex);
        return true;
    }

    bool CanCrossPanel(PaintRegion const& panel, size_t textIndex, size_t& comparisons,
        std::set<size_t>& backgroundStrokes, std::map<size_t, size_t> const& preservedOutlines,
        OccludedTextPlan& occluded) const
    {
        auto operation = _textOperations.at(textIndex);
        auto color = _textColors.at(textIndex);
        auto foreground = Recolor(color, true, false);
        // A real code comment can extend beyond its card. Admit the unchanged
        // geometry only when both final backgrounds give readable ink and the
        // visible outside region is proved to be page paper, not an image or
        // intervening paint. Do not reveal same-color hidden source text.
        if (_pageArea <= 0 || summary.photoSceneStart || _paperOperation >= panel.operation ||
            panel.operation >= operation || (!panel.rectangle && panel.interior.empty()) ||
            Contrast(color, _paperColor) < 1.5 || Contrast(color, panel.sourceColor) < 1.5 ||
            Contrast(foreground, Paper) < 4.5 ||
            Contrast(foreground, Recolor(panel.sourceColor, false, false)) < 4.5) return false;
        std::set<size_t> outlines;
        for (auto const& outline : _panelOutlines) {
            RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
            auto preserved = preservedOutlines.find(outline.operation);
            if (outline.operation < operation && MatchingPanelOutline(panel, outline) &&
                (SameColor(outline.sourceColor, panel.sourceColor) ||
                    (preserved != preservedOutlines.end() && preserved->second == panel.operation)))
                outlines.insert(outline.operation);
        }
        auto const& glyphs = _textGlyphs.at(textIndex);
        bool inside = false, outside = false;
        OccludedTextPlan pending;
        for (auto const& ink : glyphs) {
            bool covered = PanelContainsInk(panel, ink, comparisons);
            inside |= covered;
            // One glyph can straddle a panel without any whole glyph inside.
            // A small corner neighborhood proves possible interior coverage;
            // both backgrounds and every other paint under the full ink box
            // must still pass the same contrast and collision checks below.
            if (!inside) inside = PanelContainsInkCorner(panel, ink, comparisons);
            outside |= !covered;
            for (auto const& paint : _placementPaint) {
                RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                if (paint.operation == operation || paint.operation <= _paperOperation ||
                    (paint.text && SameColor(paint.sourceColor, color) &&
                        SameColor(Recolor(paint.sourceColor, true, false), foreground)) ||
                    (paint.operation == panel.operation && !paint.stroke) ||
                    (paint.stroke && outlines.count(paint.operation)) ||
                    (covered && paint.operation < panel.operation)) continue;
                if (!paint.bounds.valid) return false;
                if (!Intersects(ink, paint.bounds)) continue;
                if (paint.operation > operation && !paint.text) {
                    if (paint.stroke && preservedOutlines.count(paint.operation)) {
                        pending.strokes.insert(paint.operation);
                        continue;
                    }
                    bool futurePanel = false;
                    if (!paint.stroke) for (auto const& fill : _fills) {
                        RequireLimit(++comparisons <= 1'000'000, "PDF contrast relation budget exceeded.");
                        if (fill.operation == paint.operation && OpaquePanelShape(fill)) {
                            pending.fills[fill.operation].insert(textIndex);
                            futurePanel = true;
                            break;
                        }
                    }
                    if (futurePanel) continue;
                }
                return false;
            }
        }
        if (!inside || !outside) return false;
        for (auto outline : outlines)
            if (!preservedOutlines.count(outline)) backgroundStrokes.insert(outline);
        for (auto const& fill : pending.fills)
            occluded.fills[fill.first].insert(fill.second.begin(), fill.second.end());
        occluded.strokes.insert(pending.strokes.begin(), pending.strokes.end());
        occluded.provedBackdrops.insert(textIndex);
        return true;
    }

    bool HasGlyphBytes(QPDFObjectHandle value, bool array) const
    {
        if (!array) {
            Require(value.isString(), "Invalid PDF text operand.");
            return !value.getStringValue().empty();
        }
        Require(value.isArray(), "Invalid PDF text array.");
        for (int i = 0; i < value.getArrayNItems(); ++i) {
            auto item = value.getArrayItem(i);
            if (item.isString() && !item.getStringValue().empty()) return true;
            Require(item.isNumber() || item.isString(), "Invalid PDF text array item.");
        }
        return false;
    }

    std::array<double, 2> Transform(double x, double y) const
    {
        auto m = _state.matrix;
        return {m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]};
    }

    void PathPoint(double x, double y)
    {
        auto point = Transform(x, y);
        Include(_pathBounds, point[0], point[1]);
        if (_smoothCurvePath) {
            if (_curvePoints.size() < 193) _curvePoints.push_back(point);
            else _smoothCurvePath = false;
        }
        if (_pathPoints.size() < 5) _pathPoints.push_back(point);
        else _pathComplex = true;
        if (_linearRulePath && !_ruleSubpaths.empty()) {
            auto& points = _ruleSubpaths.back();
            // Exact consecutive duplicates add no extent beyond the padded
            // adjacent edge. Keep the emitted path and other path proofs intact;
            // an entirely degenerate subpath still falls back to generic bounds.
            if (points.empty() || points.back() != point) {
                if (points.size() < 5) points.push_back(point);
                else _linearRulePath = false;
            }
        }
    }

    void BeginRuleSubpath()
    {
        if (_ruleSubpaths.size() < 32) _ruleSubpaths.emplace_back();
        else _linearRulePath = false;
    }

    void MoveText(double x, double y)
    {
        auto line = _state.lineMatrix;
        line[4] += x * line[0] + y * line[2];
        line[5] += x * line[1] + y * line[3];
        _state.lineMatrix = line;
        _state.textMatrix = line;
        // Text showing advances only the text matrix. A line-relative move
        // restores an exact position even after an approximate font advance.
        _state.textPositionKnown = true;
    }

    Bounds MeasureText(QPDFObjectHandle value, bool array)
    {
        Bounds bounds;
        _lastGlyphInk.clear();
        auto found = _fonts.find(_state.fontName);
        if (found == _fonts.end() || _state.fontSize <= 0 || !_state.textPositionKnown)
            return bounds;
        auto const& font = found->second;
        if (font.approximateAdvance && (array || _state.charSpacing < 0 ||
            _state.wordSpacing < 0 || _state.horizontalScale <= 0)) {
            _state.textPositionKnown = false;
            return bounds;
        }
        double cursor = 0;
        size_t glyphs = 0;
        auto includeInk = [&](double x, double y, Bounds* glyph = nullptr) {
            auto t = _state.textMatrix;
            auto point = Transform(t[0] * x + t[2] * y + t[4],
                t[1] * x + t[3] * y + t[5]);
            RequireLimit(std::isfinite(point[0]) && std::isfinite(point[1]) &&
                std::abs(point[0]) <= 200000000 && std::abs(point[1]) <= 200000000,
                "PDF transformed text bounds exceed the resource limit.");
            Include(bounds, point[0], point[1]);
            if (glyph) Include(*glyph, point[0], point[1]);
        };
        auto segment = [&](QPDFObjectHandle item) {
            if (item.isNumber()) {
                cursor -= item.getNumericValue() * _state.fontSize * _state.horizontalScale / 1000;
            } else {
                Require(item.isString(), "Invalid PDF text operand.");
                auto bytes = item.getStringValue();
                Require(!font.cid || bytes.size() % 2 == 0, "Odd-length PDF CID text.");
                for (size_t i = 0; i < bytes.size(); i += font.cid ? 2 : 1) {
                    unsigned code = static_cast<unsigned char>(bytes[i]);
                    if (font.cid) code = (code << 8) | static_cast<unsigned char>(bytes[i + 1]);
                    auto box = font.glyphBounds ? font.glyphBounds->ForCode(code) : TrueTypeGlyphBox{};
                    if (!box.known && font.cffGlyphBounds)
                        box = font.cffGlyphBounds->ForCode(code, font.Width(code));
                    if (!box.known && font.systemGlyphBounds)
                        box = font.systemGlyphBounds->ForCode(code, font.Width(code));
                    if (!box.known) box = {true, false, font.left, font.bottom, font.right, font.top};
                    if (!box.empty) {
                        Bounds glyph;
                        for (double x : {box.left, box.right})
                            for (double y : {box.bottom, box.top})
                                includeInk(cursor + x * _state.fontSize * _state.horizontalScale,
                                    y * _state.fontSize + _state.rise, &glyph);
                        _lastGlyphInk.push_back(glyph);
                        RequireLimit(++_measuredGlyphs <= 100000, "PDF glyph footprint budget exceeded.");
                    }
                    cursor += (font.Width(code) * _state.fontSize / 1000 + _state.charSpacing +
                        (!font.cid && code == 32 ? _state.wordSpacing : 0)) * _state.horizontalScale;
                    ++glyphs;
                }
            }
            Require(std::isfinite(cursor) && std::abs(cursor) <= 200000000,
                "PDF text advance exceeds the contrast policy.");
        };
        if (array) {
            Require(value.isArray(), "Invalid PDF text array.");
            for (int i = 0; i < value.getArrayNItems(); ++i) segment(value.getArrayItem(i));
        } else segment(value);
        // A run of verified empty glyphs has a known empty ink extent.
        if (glyphs && !bounds.valid) includeInk(0, _state.rise);
        auto& t = _state.textMatrix;
        t[4] += cursor * t[0];
        t[5] += cursor * t[1];
        if (glyphs && font.approximateAdvance) _state.textPositionKnown = false;
        return bounds;
    }

    bool PathRectangle() const
    {
        return !_pathComplex && RectanglePoints(_pathPoints, _pathBounds);
    }

    bool SmoothCurveStrokeBounds(std::vector<Bounds>& result) const
    {
        // A closed chain of cubic curves with continuous, nonzero tangents has
        // no sharp miter. Each curve stays inside its control-point hull.
        if (!_smoothCurvePath || _curvePoints.size() < 7 ||
            (_curvePoints.size() - 1) % 3 != 0 || _state.lineWidth <= 0 ||
            _curvePoints.front() != _curvePoints.back()) return false;
        size_t end = _curvePoints.size() - 1;
        auto m = _state.matrix;
        if (std::abs(m[0] * m[3] - m[1] * m[2]) < .000000001) return false;
        for (size_t i = 0; i < end; i += 3) {
            auto const& p = _curvePoints[i];
            auto const& before = _curvePoints[i ? i - 1 : end - 1];
            auto const& after = _curvePoints[i + 1];
            double ax = p[0] - before[0], ay = p[1] - before[1];
            double bx = after[0] - p[0], by = after[1] - p[1];
            // Test tangents in the stroking coordinate system. A strongly
            // sheared transform can disguise an acute join in page space.
            double ux = m[3] * ax - m[2] * ay, uy = m[0] * ay - m[1] * ax;
            double vx = m[3] * bx - m[2] * by, vy = m[0] * by - m[1] * bx;
            ax = ux; ay = uy; bx = vx; by = vy;
            double lengths = std::hypot(ax, ay) * std::hypot(bx, by);
            if (lengths <= .000000001 || ax * bx + ay * by <= 0 ||
                std::abs(ax * by - ay * bx) > lengths * .000001) return false;
        }
        double pad = _state.lineWidth * .500001 *
            std::max(std::abs(m[0]) + std::abs(m[2]), std::abs(m[1]) + std::abs(m[3]));
        for (size_t i = 0; i < end; i += 3) {
            Bounds bounds;
            for (size_t j = i; j <= i + 3; ++j)
                Include(bounds, _curvePoints[j][0], _curvePoints[j][1]);
            bounds.left -= pad; bounds.right += pad;
            bounds.bottom -= pad; bounds.top += pad;
            result.push_back(bounds);
        }
        return true;
    }

    bool AxisAlignedStrokeBounds(std::vector<Bounds>& result) const
    {
        if (!_linearRulePath || _ruleSubpaths.empty() || _state.lineWidth <= 0) return false;
        auto m = _state.matrix;
        double pad = _state.lineWidth * .5 *
            std::max(std::abs(m[0]) + std::abs(m[2]), std::abs(m[1]) + std::abs(m[3]));
        // Orthogonal segments have no acute miter. Half-width inflation covers
        // their square/round caps and right-angle joins without treating a
        // rectangular border's entire interior as painted.
        for (auto const& points : _ruleSubpaths) {
            if (points.size() < 2) { result.clear(); return false; }
            size_t segments = points.size() == 2 ? 1 : points.size();
            for (size_t i = 0; i < segments; ++i) {
                auto const& a = points[i];
                auto const& b = points[(i + 1) % points.size()];
                if (points.size() > 2 && std::abs(a[0] - b[0]) > .000001 && std::abs(a[1] - b[1]) > .000001) {
                    result.clear(); return false;
                }
                bool aligned = (std::abs(a[0] - b[0]) <= .000001 ||
                    std::abs(a[1] - b[1]) <= .000001) &&
                    std::abs(m[1]) <= .000001 && std::abs(m[2]) <= .000001;
                double extent = points.size() == 2 && !aligned ? pad * std::sqrt(2.) : pad;
                result.push_back({std::min(a[0], b[0]) - extent, std::min(a[1], b[1]) - extent,
                    std::max(a[0], b[0]) + extent, std::max(a[1], b[1]) + extent, true});
            }
        }
        return true;
    }

    bool PathCompoundArrow() const
    {
        if (!_linearRulePath || _ruleSubpaths.size() != 2) return false;
        auto shaft = _ruleSubpaths[0];
        auto head = _ruleSubpaths[1];
        if (shaft.size() == 5 && shaft.front() == shaft.back()) shaft.pop_back();
        if (head.size() == 4 && head.front() == head.back()) head.pop_back();
        if (shaft.size() != 4 || head.size() != 3) return false;
        double ax = shaft[1][0] - shaft[0][0], ay = shaft[1][1] - shaft[0][1];
        double bx = shaft[2][0] - shaft[1][0], by = shaft[2][1] - shaft[1][1];
        double first = std::hypot(ax, ay), second = std::hypot(bx, by);
        double width = std::min(first, second), length = std::max(first, second);
        if (width <= 0 || width > 4 || length < 8 * width ||
            std::abs(ax * bx + ay * by) > first * second * .01 ||
            std::abs(shaft[3][0] + ax - shaft[2][0]) > .03 ||
            std::abs(shaft[3][1] + ay - shaft[2][1]) > .03) return false;
        Bounds tip;
        for (auto const& point : head) Include(tip, point[0], point[1]);
        if (tip.right - tip.left > 16 || tip.top - tip.bottom > 16) return false;
        auto endA = first >= second ? shaft[0] : shaft[1];
        auto endB = first >= second ? shaft[1] : shaft[2];
        double cx = (tip.left + tip.right) * .5, cy = (tip.bottom + tip.top) * .5;
        return std::min(std::hypot(cx - endA[0], cy - endA[1]),
            std::hypot(cx - endB[0], cy - endB[1])) <= 16;
    }

    bool PathRuleRectangles() const
    {
        if (!_linearRulePath || _ruleSubpaths.empty()) return false;
        for (auto const& points : _ruleSubpaths) {
            Bounds bounds;
            for (auto const& point : points) Include(bounds, point[0], point[1]);
            if (!RectanglePoints(points, bounds)) return false;
        }
        return true;
    }

    static bool RectanglePoints(std::vector<std::array<double, 2>> const& points, Bounds bounds)
    {
        if ((points.size() != 4 && points.size() != 5) || !bounds.valid) return false;
        constexpr double tolerance = .01;
        if (points.size() == 5 &&
            (std::abs(points[0][0] - points[4][0]) > tolerance ||
             std::abs(points[0][1] - points[4][1]) > tolerance)) return false;
        std::set<unsigned> corners;
        for (auto const& point : points) {
            if (std::min(std::abs(point[0] - bounds.left),
                    std::abs(point[0] - bounds.right)) > tolerance ||
                std::min(std::abs(point[1] - bounds.bottom),
                    std::abs(point[1] - bounds.top)) > tolerance) return false;
            corners.insert((std::abs(point[0] - bounds.right) <= tolerance ? 1u : 0u) |
                (std::abs(point[1] - bounds.top) <= tolerance ? 2u : 0u));
        }
        for (size_t i = 0; i < 4; ++i) {
            auto const& from = points[i];
            auto const& to = points[(i + 1) % 4];
            bool horizontal = std::abs(from[1] - to[1]) <= tolerance &&
                std::abs(from[0] - to[0]) > tolerance;
            bool vertical = std::abs(from[0] - to[0]) <= tolerance &&
                std::abs(from[1] - to[1]) > tolerance;
            if (!horizontal && !vertical) return false;
        }
        return corners.size() == 4 && bounds.right - bounds.left > tolerance &&
            bounds.top - bounds.bottom > tolerance;
    }

    bool IsFullPage(Bounds bounds) const
    {
        return _pageArea > 0 && bounds.valid &&
            std::abs(bounds.left - _pageBox[0]) < .01 &&
            std::abs(bounds.bottom - _pageBox[1]) < .01 &&
            std::abs(bounds.right - _pageBox[2]) < .01 &&
            std::abs(bounds.top - _pageBox[3]) < .01;
    }

    double _pageArea;
    bool _write;
    QPDFObjectHandle _resources;
    std::array<double, 4> _pageBox;
    std::set<size_t> _retainedRegions;
    std::set<size_t> _backgroundStrokes;
    std::set<size_t> _preservedStrokes;
    std::map<size_t, ImageBacking> _imageBackings;
    std::map<size_t, ImageBacking> _pageBackings;
    std::vector<PaintRegion> _panelOutlines;
    std::set<size_t> _preservedPaint;
    std::set<size_t> _foregroundRegions;
    std::map<size_t, Color> _fractionalColors;
    std::vector<FractionalFill> _fractionalFills;
    Color _paperColor{1, 1, 1};
    size_t _paperOperation = 0;
    std::vector<PaintRegion> _ruleCandidates;
    std::vector<PaintRegion> _arrowCandidates;
    State _state;
    std::map<std::string, PaintColorSpace> _paintSpaces;
    std::vector<PaintRegion> _overprintObstacles;
    std::vector<PaintRegion> _knockoutStrokes;
    size_t _overprintComparisons = 0;
    std::vector<State> _stack;
    std::vector<QPDFObjectHandle> _operands;
    std::string _output;
    double _lastRectArea = 0;
    bool _lastFullRect = false;
    bool _pathStarted = false;
    bool _firstPaint = true;
    Bounds _pathBounds;
    std::vector<std::array<double, 2>> _pathPoints;
    bool _pathComplex = false;
    std::vector<PaintRegion> _regions;
    std::vector<PaintRegion> _placementPaint;
    std::set<size_t> _strokeOperations;
    std::set<size_t> _shadingOperations;
    std::set<size_t> _arrowOverlayOperations;
    std::vector<ImagePlacement> _imagePlacements;
    std::vector<std::vector<std::array<double, 2>>> _ruleSubpaths;
    bool _linearRulePath = true;
    std::vector<std::array<double, 2>> _curvePoints;
    std::vector<std::array<double, 2>> _panelControls;
    std::vector<std::array<double, 2>> _panelEndpoints;
    std::vector<size_t> _panelSegmentEnds;
    bool _panelPathValid = true;
    bool _panelPathClosed = false;
    unsigned _panelCurveCount = 0;
    bool _smoothCurvePath = true;
    std::vector<FormPlacement> _formPlacements;
    std::vector<PaintRegion> _fills;
    std::vector<Bounds> _lightText;
    std::vector<Bounds> _lastGlyphInk;
    std::vector<std::vector<Bounds>> _textGlyphs;
    size_t _measuredGlyphs = 0;
    std::vector<Color> _textColors;
    std::vector<size_t> _textOperations;
    std::map<std::string, FontMetrics> _fonts;
};

struct Counts {
    std::map<int, Color> imageBackdrops;
    std::set<int> imageIslands;
    std::set<int> coloredImageIslands;
    std::set<int> annotatedImages;
    std::set<int> compositeImages;
    std::set<int> localPhotoObjects;
    std::set<int> retainedImageObjects;
    std::set<int> imageBackingCandidates;
    std::set<int> backedImages;
    std::set<int> retainedPanelObjects;
    std::set<int> softPanelObjects;
    std::map<int, FormContrast> formContrast;
    std::map<int, MaskSupport> sparseMasks;
    std::map<int, DiagramSupport> diagramImages;
    std::set<int> recoloredImageObjects;
    unsigned changedPages = 0;
    unsigned alreadyDarkPages = 0;
    unsigned recoloredImages = 0;
    unsigned preservedImages = 0;
    size_t decodedPixels = 0;
    size_t decodedBytes = 0;
    size_t operations = 0;
    size_t resourceClones = 0;
    bool resourcePaletteChanges = false;
};

void AccountDecoded(Counts& counts, size_t bytes)
{
    RequireLimit(bytes <= MaximumDecodedBytes &&
        counts.decodedBytes <= MaximumTotalDecodedBytes - bytes,
        "PDF decoded-stream budget exceeded.");
    counts.decodedBytes += bytes;
}

void AccountOperations(Counts& counts, size_t operations)
{
    RequireLimit(operations <= MaximumTotalOperations &&
        counts.operations <= MaximumTotalOperations - operations,
        "PDF aggregate operation budget exceeded.");
    counts.operations += operations;
}

void AccountRetainedImage(Counts& counts, RetainedImageBudget const& budget)
{
    AccountDecoded(counts, budget.decodedBytes);
    RequireLimit(budget.decodedPixels <= MaximumTotalPixels &&
        counts.decodedPixels <= MaximumTotalPixels - budget.decodedPixels,
        "Document retained-image pixel budget exceeded.");
    counts.decodedPixels += budget.decodedPixels;
}

int Positive(QPDFObjectHandle dictionary, std::string const& key)
{
    auto value = dictionary.getKey(key);
    Require(value.isInteger(), "Invalid image dimensions or precision.");
    auto raw = value.getIntValue();
    Require(raw <= std::numeric_limits<int>::max(), "PDF integer exceeds its limit.");
    int number = static_cast<int>(raw);
    Require(number > 0, "Invalid image dimensions or precision.");
    return number;
}

std::string ImagePixels(QPDFObjectHandle image, int& width, int& height, int& channels, Counts& counts)
{
    auto dict = image.getDict();
    width = Positive(dict, "/Width");
    height = Positive(dict, "/Height");
    RequireLimit(static_cast<size_t>(width) * height <= MaximumImagePixels, "Image pixel budget exceeded.");
    auto space = dict.getKey("/ColorSpace");
    if (space.isArray() || dict.hasKey("/Decode") || Positive(dict, "/BitsPerComponent") == 1) {
        RetainedImageBudget budget;
        auto expanded = ReadRetainedRgb(image, budget);
        AccountRetainedImage(counts, budget);
        Require(expanded.width == static_cast<size_t>(width) && expanded.height == static_cast<size_t>(height),
            "Expanded PDF image dimensions changed.");
        if (expanded.blackMatte)
            Unsupported("Recoloring premultiplied PDF image samples is unsupported.");
        channels = 3;
        return expanded.rgb;
    }
    counts.decodedPixels += static_cast<size_t>(width) * height;
    RequireLimit(counts.decodedPixels <= MaximumTotalPixels, "Document image pixel budget exceeded.");
    if (Positive(dict, "/BitsPerComponent") != 8) Unsupported("Only 8-bit PDF images can be classified.");
    channels = space.isNameAndEquals("/DeviceRGB") ? 3 : space.isNameAndEquals("/DeviceGray") ? 1 : 0;
    if (channels == 0 || dict.hasKey("/Decode") || dict.hasKey("/Mask") || dict.hasKey("/ImageMask"))
        Unsupported("PDF image color or mask is outside the supported policy.");
    auto data = image.getStreamData(qpdf_dl_all);
    AccountDecoded(counts, data->getSize());
    Require(data->getSize() == static_cast<size_t>(width) * height * channels,
        "Decoded PDF image size does not match its inventory.");
    return std::string(reinterpret_cast<char const*>(data->getBuffer()), data->getSize());
}

bool IsLineArt(std::string const& pixels, int channels, std::string const& mask)
{
    size_t sampled = 0;
    size_t neutral = 0;
    size_t light = 0;
    size_t dark = 0;
    size_t paperWhite = 0;
    size_t middle = 0;
    std::set<unsigned> palette;
    size_t stride = std::max<size_t>(1, (pixels.size() / channels) / 65536);
    for (size_t pixel = 0; pixel < pixels.size() / channels; pixel += stride) {
        if (!mask.empty() && static_cast<unsigned char>(mask[pixel]) < 16) continue;
        auto at = [&](int component) { return static_cast<unsigned char>(pixels[pixel * channels + component]); };
        unsigned r = at(0), g = channels == 1 ? r : at(1), b = channels == 1 ? r : at(2);
        unsigned minimum = std::min({r, g, b}), maximum = std::max({r, g, b});
        ++sampled;
        if (maximum - minimum < 25) ++neutral;
        if (minimum > 210) ++light;
        if (maximum < 90) ++dark;
        if (minimum >= 248) ++paperWhite;
        if (maximum > 80 && maximum < 210) ++middle;
        palette.insert(((r / 32) << 6) | ((g / 32) << 3) | (b / 32));
    }
    // A white background plus dark marks is a much narrower signal than
    // "mostly neutral": it excludes pale photographs and blank scans.
    bool paperDiagram = sampled && neutral * 100 >= sampled * 96 && light * 100 >= sampled * 78 &&
        paperWhite * 100 >= sampled * 84 && middle * 100 <= sampled * 6 &&
        dark * 100 >= sampled * 1 && palette.size() <= 48;
    bool transparentInk = !mask.empty() && sampled && neutral * 100 >= sampled * 98 &&
        dark * 100 >= sampled * 90 && palette.size() <= 8;
    return paperDiagram || transparentInk;
}

std::string RecolorPixels(std::string pixels, int channels, bool foreground = false)
{
    std::string result;
    result.reserve((pixels.size() / channels) * 3);
    for (size_t pixel = 0; pixel < pixels.size() / channels; ++pixel) {
        auto at = [&](int component) { return static_cast<unsigned char>(pixels[pixel * channels + component]) / 255.0; };
        Color source{at(0), channels == 1 ? at(0) : at(1), channels == 1 ? at(0) : at(2)};
        double light = Light(source);
        Color target{};
        if (foreground) {
            target = Recolor(source, true, false);
        } else if (Saturation(source) < .10) {
            for (size_t c = 0; c < 3; ++c) target[c] = Paper[c] * light + Ink[c] * (1 - light);
        } else {
            target = Recolor(source, light < .70, light >= .70);
        }
        for (double value : target) result.push_back(static_cast<char>(std::lround(Clamp(value) * 255)));
    }
    return result;
}

bool PreservesImageInkContrast(std::string const& source, size_t channels,
    std::string const& output, size_t width, size_t height)
{
    // A region classifier can mistake colored lettering for a panel, or leave
    // a pale screenshot label behind while lightening its text. Abrupt palette
    // decisions can also amplify JPEG noise or erase white button lettering.
    // Detect these ambiguities before accepting the recolored image.
    static auto const linear = [] {
        std::array<double, 256> values{};
        for (size_t i = 0; i < values.size(); ++i) {
            double c = double(i) / 255;
            values[i] = c <= .04045 ? c / 12.92 : std::pow((c + .055) / 1.055, 2.4);
        }
        return values;
    }();
    auto luminance = [&](std::string const& samples, size_t count, size_t pixel) {
        auto at = [&](size_t channel) {
            return linear[static_cast<unsigned char>(samples[pixel * count + (count == 1 ? 0 : channel)])];
        };
        return .2126 * at(0) + .7152 * at(1) + .0722 * at(2);
    };
    auto contrast = [](double a, double b) { return (std::max(a, b) + .05) / (std::min(a, b) + .05); };
    auto white = [&](size_t pixel) {
        for (size_t c = 0; c < channels; ++c)
            if (static_cast<unsigned char>(source[pixel * channels + c]) < 230) return false;
        return true;
    };
    auto darkColored = [&](size_t pixel, double light) {
        if (channels != 3 || light >= .15) return false;
        auto at = [&](size_t c) { return static_cast<unsigned char>(source[pixel * channels + c]); };
        return std::max({at(0), at(1), at(2)}) - std::min({at(0), at(1), at(2)}) >= 30;
    };
    auto paleColored = [&](size_t pixel, double light) {
        if (channels != 3 || light <= .5) return false;
        auto at = [&](size_t c) { return static_cast<unsigned char>(source[pixel * channels + c]); };
        auto low = std::min({at(0), at(1), at(2)});
        return low >= 180 && std::max({at(0), at(1), at(2)}) - low >= 12;
    };
    auto colored = [&](size_t pixel) {
        if (channels != 3) return false;
        auto at = [&](size_t c) { return static_cast<unsigned char>(source[pixel * channels + c]); };
        return std::max({at(0), at(1), at(2)}) - std::min({at(0), at(1), at(2)}) >= 30;
    };
    auto palePair = [&](size_t first, size_t second) {
        if (channels != 3) return false;
        bool chromatic = false;
        for (size_t pixel : {first, second}) {
            auto at = [&](size_t c) { return static_cast<unsigned char>(source[pixel * channels + c]); };
            auto low = std::min({at(0), at(1), at(2)});
            if (low < 180) return false;
            chromatic |= std::max({at(0), at(1), at(2)}) - low >= 12;
        }
        return chromatic;
    };
    // Small labels must not be diluted by thousands of successful edges in
    // the rest of a screenshot. This is a bounded ambiguity test, not OCR or
    // a proof that every remaining image label is readable.
    size_t columns = (width + 63) / 64;
    std::vector<size_t> lostLabelEdges(columns * ((height + 63) / 64));
    std::vector<size_t> inventedEdges(lostLabelEdges.size()), lostWhiteColorEdges(lostLabelEdges.size());
    std::vector<size_t> coloredPixels(lostLabelEdges.size()), tilePixels(lostLabelEdges.size());
    // Require color around the failing edge itself, not merely an unrelated
    // panel in the same 64-pixel tile. Rolling 24-pixel windows bound both
    // work and storage without a full-image integral table.
    std::vector<unsigned char> colorColumns(width);
    for (size_t y = 0; y < std::min(height, size_t(12)); ++y)
        for (size_t x = 0; x < width; ++x)
            if (colored(y * width + x)) ++colorColumns[x];
    size_t strongEdges = 0, weakEdges = 0;
    for (size_t y = 0; y < height; ++y) {
        if (y > 0) for (size_t x = 0; x < width; ++x) {
            if (y >= 13 && colored((y - 13) * width + x)) --colorColumns[x];
            if (y + 11 < height && colored((y + 11) * width + x)) ++colorColumns[x];
        }
        size_t localColored = 0;
        for (size_t x = 0; x < std::min(width, size_t(12)); ++x) localColored += colorColumns[x];
        size_t localRows = std::min(height, y + 12) - (y > 12 ? y - 12 : 0);
        for (size_t x = 0; x < width; ++x) {
            if (x > 0) {
                if (x >= 13) localColored -= colorColumns[x - 13];
                if (x + 11 < width) localColored += colorColumns[x + 11];
            }
            size_t localColumns = std::min(width, x + 12) - (x > 12 ? x - 12 : 0);
            size_t pixel = y * width + x;
            size_t tile = (y / 64) * columns + x / 64;
            ++tilePixels[tile];
            if (colored(pixel)) ++coloredPixels[tile];
            double before = luminance(source, channels, pixel), after = luminance(output, 3, pixel);
            for (size_t next : {x + 1 < width ? pixel + 1 : pixel, y + 1 < height ? pixel + width : pixel}) {
                if (next == pixel) continue;
                double other = luminance(source, channels, next);
                double otherAfter = luminance(output, 3, next);
                double sourceContrast = contrast(before, other), targetContrast = contrast(after, otherAfter);
                bool smooth = true;
                for (size_t c = 0; c < channels; ++c)
                    if (std::abs(int(static_cast<unsigned char>(source[pixel * channels + c])) -
                        int(static_cast<unsigned char>(source[next * channels + c]))) > 8) smooth = false;
                if (smooth && targetContrast >= 3 && palePair(pixel, next)) ++inventedEdges[tile];
                bool weak = targetContrast < 3;
                if (sourceContrast >= 3 && weak && localColored * 4 >= localRows * localColumns &&
                    ((white(pixel) && colored(next)) || (colored(pixel) && white(next))))
                    ++lostWhiteColorEdges[tile];
                if (sourceContrast < 4.5) continue;
                if ((darkColored(pixel, before) && white(next)) || (white(pixel) && darkColored(next, other))) {
                    ++strongEdges;
                    if (weak) ++weakEdges;
                }
                if (weak && ((before < .25 && paleColored(next, other) && otherAfter > .5) ||
                    (paleColored(pixel, before) && after > .5 && other < .25)))
                    ++lostLabelEdges[tile];
            }
        }
    }
    // Sparse compression ringing is not evidence of a lost ink role. Require
    // a substantial set and loss of at least half its strong colored edges.
    if (strongEdges >= 24 && weakEdges * 2 >= strongEdges) return false;
    // JPEG fringes can keep strong edges around an otherwise erased letter.
    // Count substantial local losses against a still-pale surface rather than
    // requiring most of those fringes to fail as well.
    for (size_t tile = 0; tile < lostLabelEdges.size(); ++tile)
        if (lostLabelEdges[tile] >= 24 || inventedEdges[tile] >= 24 ||
            (lostWhiteColorEdges[tile] >= 24 && coloredPixels[tile] * 4 >= tilePixels[tile]))
            return false;
    return true;
}

QPDFObjectHandle CloneImage(QPDFObjectHandle image, Counts& counts, unsigned depth)
{
    RetainedImageBudget retainedBudget;
    if (depth == 0 && counts.compositeImages.count(image.getObjectID())) {
        ValidateRetainedImage(image, retainedBudget);
        AccountRetainedImage(counts, retainedBudget);
        ++counts.preservedImages;
        counts.retainedImageObjects.insert(image.getObjectID());
        return image;
    }
    if (depth == 0 && counts.localPhotoObjects.count(image.getObjectID())) {
        ValidateRetainedImage(image, retainedBudget);
        AccountRetainedImage(counts, retainedBudget);
        ++counts.preservedImages;
        counts.retainedImageObjects.insert(image.getObjectID());
        return image;
    }
    if (depth == 0 && counts.annotatedImages.count(image.getObjectID())) {
        if (image.getDict().getKey("/SMask").isNull() && image.getDict().getKey("/Mask").isNull()) {
            ValidateRetainedImage(image, retainedBudget);
            AccountRetainedImage(counts, retainedBudget);
            ++counts.preservedImages;
            counts.retainedImageObjects.insert(image.getObjectID());
            return image;
        }
        auto samples = ReadRetainedRgb(image, retainedBudget);
        bool opaque = std::all_of(samples.alpha.begin(), samples.alpha.end(),
            [](char value) { return static_cast<unsigned char>(value) == 255; });
        AccountRetainedImage(counts, retainedBudget);
        retainedBudget = {};
        if (opaque) {
            ++counts.preservedImages;
            counts.retainedImageObjects.insert(image.getObjectID());
            return image;
        }
        counts.annotatedImages.erase(image.getObjectID());
    }
    if (depth == 0 && counts.imageIslands.count(image.getObjectID()) &&
        !image.getDict().getKey("/SMask").isNull()) {
        ValidateRetainedImage(image, retainedBudget);
        AccountRetainedImage(counts, retainedBudget);
        ++counts.preservedImages;
        counts.retainedImageObjects.insert(image.getObjectID());
        return image;
    }
    bool retainedJpeg = IsOpaqueRetainedJpeg(image, retainedBudget);
    bool retained = retainedJpeg || IsRetainedLightInk(image, retainedBudget);
    AccountRetainedImage(counts, retainedBudget);
    if (retained) {
        ++counts.preservedImages;
        counts.retainedImageObjects.insert(image.getObjectID());
        // JPEG samples stay unchanged, so recolored text cannot assume that
        // their backdrop became dark. Require a proved original-color composite
        // whenever later text overlaps a retained JPEG.
        if (retainedJpeg) counts.retainedPanelObjects.insert(image.getObjectID());
        return image;
    }
    int width = 0, height = 0, channels = 0;
    auto pixels = ImagePixels(image, width, height, channels, counts);
    auto dict = image.getDict();
    auto mask = dict.getKey("/SMask");
    std::string maskPixels;
    bool maskAllOpaque = false;
    bool normalizeBlackMatte = false;
    if (!mask.isNull()) {
        Require(mask.isStream(), "Invalid PDF soft mask.");
        auto maskDict = mask.getDict();
        if (!(maskDict.getKey("/ColorSpace").isNameAndEquals("/DeviceGray") &&
            Positive(maskDict, "/BitsPerComponent") == 8 &&
            Positive(maskDict, "/Width") == width && Positive(maskDict, "/Height") == height))
            Unsupported("Unsupported PDF soft mask.");
        RetainedImageBudget maskBudget;
        auto maskData = ValidateRetainedMask(mask, width, height, channels, maskBudget);
        AccountRetainedImage(counts, maskBudget);
        if (!maskDict.getKey("/Matte").isNull()) {
            // For exact zero samples with an exact black matte, unmatting is
            // zero at every nonzero alpha. Alpha-zero samples never contribute.
            // Other premultiplied images need a separate precision/fidelity proof.
            auto space = dict.getKey("/ColorSpace");
            if (dict.hasKey("/Decode") ||
                !(space.isNameAndEquals("/DeviceRGB") || space.isNameAndEquals("/DeviceGray")) ||
                !std::all_of(pixels.begin(), pixels.end(), [](char value) { return value == 0; }))
                Unsupported("Recoloring premultiplied PDF image samples is unsupported.");
            normalizeBlackMatte = true;
        }
        Require(maskData->getSize() == static_cast<size_t>(width) * height, "PDF soft mask size changed.");
        maskPixels.assign(reinterpret_cast<char const*>(maskData->getBuffer()), maskData->getSize());
        maskAllOpaque = std::all_of(maskPixels.begin(), maskPixels.end(),
            [](char alpha) { return static_cast<unsigned char>(alpha) == 255; });
    }
    auto softPanel = DetectSoftNeutralPanel(pixels, static_cast<size_t>(channels),
        static_cast<size_t>(width), static_cast<size_t>(height), maskPixels);
    bool targetPanel = softPanel == SoftPanelPalette::Dark;
    bool recolor = softPanel != SoftPanelPalette::None || IsLineArt(pixels, channels, maskPixels);
    if (softPanel != SoftPanelPalette::None) {
        if (depth != 0) Unsupported("Soft image panels inside forms are unsupported.");
        counts.softPanelObjects.insert(image.getObjectID());
    }
    bool foreground = false;
    std::string diagramPixels;
    if (!recolor) {
        auto maskPolicy = pdf_dark_image::MaskPolicy::Unsupported;
        if (!mask.isNull() && !maskAllOpaque) {
            maskPolicy = pdf_dark_image::Evaluate(pixels, channels, width, height, maskPixels);
            if (maskPolicy == pdf_dark_image::MaskPolicy::Unsupported && depth == 0) {
                auto backdrop = counts.imageBackdrops.find(image.getObjectID());
                if (backdrop != counts.imageBackdrops.end()) {
                    RetainedImageBudget budget;
                    bool safe = CanRetainOnDarkPaper(image, backdrop->second, Paper, budget);
                    AccountRetainedImage(counts, budget);
                    if (safe) {
                        ++counts.preservedImages;
                        counts.retainedImageObjects.insert(image.getObjectID());
                        counts.retainedPanelObjects.insert(image.getObjectID());
                        return image;
                    }
                }
                if (counts.imageBackingCandidates.count(image.getObjectID()) ||
                    counts.coloredImageIslands.count(image.getObjectID())) {
                    RetainedImageBudget budget;
                    ValidateRetainedImage(image, budget);
                    AccountRetainedImage(counts, budget);
                    ++counts.preservedImages;
                    counts.retainedImageObjects.insert(image.getObjectID());
                    if (counts.imageBackingCandidates.count(image.getObjectID()))
                        counts.backedImages.insert(image.getObjectID());
                    return image;
                }
            }
            if (maskPolicy == pdf_dark_image::MaskPolicy::Unsupported || depth != 0)
                Unsupported("Ambiguous transparent image cannot be placed on dark paper safely.");
            foreground = maskPolicy == pdf_dark_image::MaskPolicy::RecolorInk;
            if (foreground) counts.sparseMasks.emplace(image.getObjectID(), MaskSupport{width, height, maskPixels});
        }
        // Newly admitted panels retain their original mask and use the same
        // placement/text guards as other recolored images. Do not expand form
        // admission before inherited backgrounds can be verified.
        if (!foreground && depth == 0 && (mask.isNull() || maskAllOpaque ||
            maskPolicy == pdf_dark_image::MaskPolicy::PreservePanel))
            pdf_dark_diagram::TryRecolor(pixels, channels, width, height, maskPixels, diagramPixels);
        if (!foreground && diagramPixels.empty()) {
            ++counts.preservedImages;
            counts.retainedImageObjects.insert(image.getObjectID());
            counts.retainedPanelObjects.insert(image.getObjectID());
            return image;
        }
    }
    if (depth == 0 && mask.isNull() && !diagramPixels.empty() &&
        !PreservesImageInkContrast(pixels, static_cast<size_t>(channels), diagramPixels,
            static_cast<size_t>(width), static_cast<size_t>(height))) {
        // The initial IsOpaqueRetainedJpeg probe validates every JPEG before
        // deciding whether it is a photograph. Reuse that completed validation
        // for this unchanged object instead of scanning and charging it twice.
        if (!RetainedJpegFilter(dict.getKey("/Filter"))) {
            RetainedImageBudget budget;
            ValidateRetainedImage(image, budget);
            AccountRetainedImage(counts, budget);
        }
        ++counts.preservedImages;
        counts.retainedImageObjects.insert(image.getObjectID());
        counts.retainedPanelObjects.insert(image.getObjectID());
        return image;
    }
    auto outputPixels = targetPanel ? pixels :
        diagramPixels.empty() ? RecolorPixels(pixels, channels, foreground) : std::move(diagramPixels);
    {
        std::string sourceGray(static_cast<size_t>(width) * height, static_cast<char>(255));
        for (size_t pixel = 0; pixel < sourceGray.size(); ++pixel) {
            unsigned low = 255, high = 0, sum = 0;
            for (int c = 0; c < channels; ++c) {
                unsigned value = static_cast<unsigned char>(pixels[pixel * channels + c]);
                low = std::min(low, value); high = std::max(high, value);
                sum += value;
            }
            if (high - low < 25) sourceGray[pixel] = static_cast<char>(sum / channels);
        }
        counts.diagramImages.emplace(image.getObjectID(), DiagramSupport{
            width, height, outputPixels, maskPixels, std::move(sourceGray)});
    }
    if (targetPanel || channels == 3 && outputPixels == pixels) {
        ++counts.preservedImages;
        counts.retainedImageObjects.insert(image.getObjectID());
        counts.recoloredImageObjects.insert(image.getObjectID()); // Still validate overlaid text.
        return image;
    }
    auto copy = image.copyStream();
    auto copyDict = copy.getDict();
    copyDict.replaceKey("/ColorSpace", QPDFObjectHandle::newName("/DeviceRGB"));
    copyDict.replaceKey("/BitsPerComponent", QPDFObjectHandle::newInteger(8));
    copyDict.removeKey("/Decode");
    if (normalizeBlackMatte) {
        auto normalizedMask = mask.copyStream();
        normalizedMask.getDict().removeKey("/Matte");
        copyDict.replaceKey("/SMask", normalizedMask);
    }
    copy.replaceStreamData(std::move(outputPixels),
        QPDFObjectHandle::newNull(), QPDFObjectHandle::newNull());
    Require(copy.getObjectID() != image.getObjectID(), "PDF image clone did not separate resources.");
    ++counts.recoloredImages;
    counts.recoloredImageObjects.insert(image.getObjectID());
    return copy;
}

QPDFObjectHandle CloneResources(QPDFObjectHandle resources, Counts& counts, QPDF& pdf, unsigned depth,
    bool preserveImages = false, bool allowFractional = false,
    std::set<std::string> const* usedXObjects = nullptr);

void ValidateFonts(QPDFObjectHandle resources)
{
    auto fonts = resources.getKey("/Font");
    if (fonts.isNull()) return;
    Require(fonts.isDictionary(), "Invalid PDF font resources.");
    for (auto const& name : fonts.getKeys()) {
        auto font = fonts.getKey(name);
        Require(font.isDictionary(), "Invalid PDF font resource.");
        auto subtype = font.getKey("/Subtype");
        if (subtype.isNameAndEquals("/Type3"))
            Unsupported("Type 3 fonts can paint their own glyph colors and are unsupported.");
        if (subtype.isNameAndEquals("/Type0")) {
            auto descendants = font.getKey("/DescendantFonts");
            if (!descendants.isArray() || descendants.getArrayNItems() != 1)
                Unsupported("Type 0 font descendants are unsupported.");
            auto descendant = descendants.getArrayItem(0);
            if (!descendant.isDictionary() ||
                !(descendant.getKey("/Subtype").isNameAndEquals("/CIDFontType0") ||
                  descendant.getKey("/Subtype").isNameAndEquals("/CIDFontType2")))
                Unsupported("Unexpected Type 0 descendant font is unsupported.");
        } else if (!(subtype.isNameAndEquals("/Type1") ||
                     subtype.isNameAndEquals("/MMType1") ||
                     subtype.isNameAndEquals("/TrueType"))) {
            Unsupported("Unexpected PDF font subtype is unsupported.");
        }
    }
}

QPDFObjectHandle CloneForm(QPDFObjectHandle form, QPDFObjectHandle inherited, Counts& counts, QPDF& pdf, unsigned depth)
{
    RequireLimit(depth < MaximumDepth, "Nested PDF forms exceed the limit.");
    auto copy = form.copyStream();
    auto dict = copy.getDict();
    if (dict.hasKey("/Group")) Unsupported("Form transparency group is outside the dark-mode policy.");
    auto own = dict.getKey("/Resources");
    auto resources = own.isNull() ? inherited : own;
    auto content = ReadBoundedPdfStream(form, MaximumContentBytes, "Form content exceeds the limit.");
    RequireLimit(content->getSize() <= MaximumContentBytes, "Form content exceeds the limit.");
    AccountDecoded(counts, content->getSize());
    ContentRewrite inspection(0, false, resources, {}, true);
    QPDFObjectHandle::parseContentStream(form, &inspection);
    // A form may inherit the page dictionary, which includes the form itself.
    // Keep unused entries intact; only follow actual Do operations for rewriting.
    dict.replaceKey("/Resources", CloneResources(resources, counts, pdf, depth + 1,
        false, false, &inspection.usedXObjects));
    inspection.ValidateFormContrast(counts.formContrast);
    auto retained = inspection.ResolveContrast();
    ContentRewrite rewrite(0, true, resources, {}, true, retained.fills, {}, {}, {}, retained.strokes);
    QPDFObjectHandle::parseContentStream(form, &rewrite);
    AccountOperations(counts, rewrite.summary.operations);
    AccountDecoded(counts, inspection.summary.decodedFontBytes);
    AccountDecoded(counts, rewrite.summary.decodedFontBytes);
    FormContrast contrast{rewrite.summary.darkFilledRegion, rewrite.summary.lightText,
        inspection.textColors(), inspection.knownTextColors(), inspection.FormTextBounds(counts.formContrast)};
    auto children = resources.getKey("/XObject");
    if (children.isDictionary())
        for (auto const& key : children.getKeys()) {
            if (!inspection.usedXObjects.count(key)) continue;
            auto child = counts.formContrast.find(children.getKey(key).getObjectID());
            if (child != counts.formContrast.end()) {
                contrast.darkFill |= child->second.darkFill;
                contrast.text |= child->second.text;
                contrast.knownTextColors &= child->second.knownTextColors;
                RequireLimit(contrast.textColors.size() + child->second.textColors.size() <= 8192,
                    "PDF form text-color budget exceeded.");
                contrast.textColors.insert(contrast.textColors.end(),
                    child->second.textColors.begin(), child->second.textColors.end());
            }
        }
    counts.formContrast[form.getObjectID()] = contrast;
    counts.resourcePaletteChanges |= rewrite.summary.paletteChanges;
    copy.replaceStreamData(rewrite.output(), QPDFObjectHandle::newNull(), QPDFObjectHandle::newNull());
    return copy;
}

QPDFObjectHandle CloneResources(QPDFObjectHandle resources, Counts& counts, QPDF& pdf, unsigned depth,
    bool preserveImages, bool allowFractional, std::set<std::string> const* usedXObjects)
{
    RequireLimit(depth < MaximumDepth, "PDF resources exceed the nesting limit.");
    Require(resources.isDictionary(), "Invalid PDF resources.");
    ValidateFonts(resources);
    for (auto key : resources.getKeys())
        if (key == "/Pattern") {
            auto value = resources.getKey(key);
            if (!value.isDictionary() || !value.getKeys().empty())
                Unsupported("PDF resource features are outside the current dark-mode policy.");
        }
    // Resolve only selected named spaces in ContentRewrite; unused resources
    // cannot affect page colors.
    auto colorSpaces = resources.getKey("/ColorSpace");
    Require(colorSpaces.isNull() || colorSpaces.isDictionary(), "Invalid PDF color-space resources.");
    if (colorSpaces.isDictionary())
        for (auto const* key : {"/DefaultGray", "/DefaultRGB", "/DefaultCMYK"})
            if (!colorSpaces.getKey(key).isNull())
                Unsupported("PDF default color-space substitution is unsupported.");
    auto states = resources.getKey("/ExtGState");
    if (!states.isNull()) {
        Require(states.isDictionary(), "Invalid PDF graphics states.");
        for (auto const& name : states.getKeys()) {
            auto state = states.getKey(name);
            Require(state.isDictionary(), "Invalid PDF graphics state.");
            ReadGraphicsStrokeStyle(state);
            for (auto const& key : state.getKeys()) {
                auto value = state.getKey(key);
                if (key == "/Type" && value.isNameAndEquals("/ExtGState") ||
                    key == "/BM" && value.isNameAndEquals("/Normal") ||
                    key == "/SMask" && value.isNameAndEquals("/None") ||
                    key == "/AIS" && value.isBool() && !value.getBoolValue() ||
                    (key == "/OP" || key == "/op") && value.isBool() ||
                    key == "/SA" && value.isBool() ||
                    key == "/LC" || key == "/LJ" || key == "/LW" || key == "/ML") continue;
                if (key == "/SM" && value.isNumber() && std::isfinite(value.getNumericValue()) &&
                    value.getNumericValue() >= 0 && value.getNumericValue() <= 1) continue;
                if (key == "/OPM" && value.isInteger() &&
                    (value.getIntValue() == 0 || value.getIntValue() == 1) &&
                    state.getKey("/OP").isBool() && !state.getKey("/OP").getBoolValue() &&
                    state.getKey("/op").isBool() && !state.getKey("/op").getBoolValue()) continue;
                if (key != "/CA" && key != "/ca") Unsupported("PDF graphics-state feature is unsupported.");
                auto alpha = state.getKey(key);
                Require(alpha.isNumber() && std::isfinite(alpha.getNumericValue()) &&
                    alpha.getNumericValue() >= 0 && alpha.getNumericValue() <= 1,
                    "Invalid PDF transparency value.");
                if (!preserveImages && !allowFractional && alpha.getNumericValue() != 0 && alpha.getNumericValue() != 1)
                    Unsupported("Fractional PDF transparency is unsupported.");
            }
        }
    }
    auto copy = resources.shallowCopy();
    auto shadings = resources.getKey("/Shading");
    if (!shadings.isNull()) {
        Require(shadings.isDictionary(), "Invalid PDF shading resources.");
        auto clonedShadings = shadings.shallowCopy();
        for (auto const& key : shadings.getKeys()) {
            RequireLimit(++counts.resourceClones <= MaximumObjects, "PDF resource clone budget exceeded.");
            clonedShadings.replaceKey(key, CloneAxialShading(shadings.getKey(key), counts.resourcePaletteChanges));
        }
        copy.replaceKey("/Shading", clonedShadings);
    }
    auto xobjects = resources.getKey("/XObject");
    if (xobjects.isNull()) return copy;
    Require(xobjects.isDictionary(), "Invalid PDF XObject inventory.");
    auto cloned = xobjects.shallowCopy();
    for (auto const& key : xobjects.getKeys()) {
        if (usedXObjects && !usedXObjects->count(key)) continue;
        RequireLimit(++counts.resourceClones <= MaximumObjects,
            "PDF resource clone budget exceeded.");
        auto object = xobjects.getKey(key);
        Require(object.isStream(), "Invalid PDF XObject.");
        auto subtype = object.getDict().getKey("/Subtype");
        if (subtype.isNameAndEquals("/Form"))
            cloned.replaceKey(key, CloneForm(object, resources, counts, pdf, depth + 1));
        else if (subtype.isNameAndEquals("/Image")) {
            if (preserveImages) {
                RetainedImageBudget budget;
                ValidateRetainedImage(object, budget);
                AccountRetainedImage(counts, budget);
                ++counts.preservedImages;
                counts.retainedImageObjects.insert(object.getObjectID());
            } else cloned.replaceKey(key, CloneImage(object, counts, depth));
        }
        else Unsupported("Unsupported PDF XObject subtype.");
    }
    copy.replaceKey("/XObject", cloned);
    return copy;
}

struct ImageDimensionRecovery {
    std::array<size_t, 2> imageBefore;
    std::array<size_t, 2> maskBefore;
    std::array<size_t, 2> intrinsic;
    bool operator==(ImageDimensionRecovery const&) const = default;
};

struct DrawingCoordinateRecovery {
    size_t offset;
    std::string original;
    std::string replacement;
    std::string operation;
    size_t operand;
    bool operator==(DrawingCoordinateRecovery const&) const = default;
};

bool RecoverableZeroCoordinate(std::string const& token)
{
    // Reader agreement has been independently checked for this bounded grammar.
    // It is not PDF scientific notation, arbitrary prefix parsing, or subtraction.
    auto dot = token.find('.'), minus = token.find('-');
    if (dot < 1 || dot > 3 || minus == std::string::npos || minus <= dot + 1 ||
        minus > dot + 4 || token.size() <= minus + 1 || token.size() > minus + 5) return false;
    for (size_t i = 0; i < token.size(); ++i) {
        if (i == dot || i == minus) continue;
        if (i < minus ? token[i] != '0' : token[i] < '0' || token[i] > '9') return false;
    }
    return true;
}

bool HasRecoverableCoordinate(std::string const& content)
{
    for (size_t minus = content.find('-'); minus != std::string::npos; minus = content.find('-', minus + 1)) {
        size_t first = minus, last = minus + 1;
        while (first > 0 && minus - first < 8 && (content[first - 1] == '0' || content[first - 1] == '.')) --first;
        while (last < content.size() && last - minus <= 5 && content[last] >= '0' && content[last] <= '9') ++last;
        if (RecoverableZeroCoordinate(content.substr(first, last - first))) return true;
    }
    return false;
}

std::vector<DrawingCoordinateRecovery> RecoverDrawingCoordinates(QPDFObjectHandle stream, Counts& counts,
    bool inspectBoundary = false, std::vector<size_t> const& boundaries = {})
{
    auto data = stream.getStreamData(qpdf_dl_all);
    RequireLimit(data->getSize() <= MaximumContentBytes, "Coordinate recovery content exceeds its limit.");
    AccountDecoded(counts, data->getSize());
    std::string content(reinterpret_cast<char const*>(data->getBuffer()), data->getSize());
    if (!inspectBoundary && !HasRecoverableCoordinate(content)) return {};
    BufferInputSource input("PDF coordinate recovery", data.get());
    QPDFTokenizer tokenizer;
    tokenizer.allowEOF();
    using T = QPDFTokenizer;
    struct Operand { bool number; size_t offset; std::string repair; };
    std::vector<Operand> operands;
    std::vector<T::token_type_e> nesting;
    std::vector<DrawingCoordinateRecovery> recovered;
    size_t operations = 0, tokens = 0;
    while (true) {
        auto token = tokenizer.readToken(input, "PDF drawing coordinate", true, MaximumContentBytes);
        RequireLimit(++tokens <= MaximumOperations * 8, "Coordinate recovery token budget exceeded.");
        auto type = token.getType();
        auto raw = token.getRawValue();
        if (type == T::tt_eof) break;
        Require(type != T::tt_bad, "Malformed token prevents PDF coordinate recovery.");
        if (type == T::tt_string && !raw.empty() && raw[0] == '(') {
            auto start = static_cast<size_t>(input.getLastOffset());
            auto boundary = std::upper_bound(boundaries.begin(), boundaries.end(), start);
            if (boundary != boundaries.end() && *boundary < start + raw.size())
                Unsupported("PDF text spans content streams and cannot be preserved unambiguously. Re-export the source PDF.");
        }
        // Stream boundaries can separate a damaged number into different tokens.
        // Never infer the unsplit zero interpretation from their raw byte join.
        // Some readers tolerate trailing signs differently, so numeric-looking
        // tokens must either be valid PDF numbers or match the proved grammar.
        if ((type == T::tt_word || type == T::tt_real || type == T::tt_integer) && !raw.empty() &&
            (raw[0] == '+' || raw[0] == '-' || raw[0] == '.' || (raw[0] >= '0' && raw[0] <= '9')) &&
            !RecoverableZeroCoordinate(raw)) {
            size_t first = raw[0] == '+' || raw[0] == '-' ? 1 : 0;
            bool digit = false, dot = false, valid = true;
            for (size_t i = first; i < raw.size(); ++i) {
                if (raw[i] >= '0' && raw[i] <= '9') digit = true;
                else if (raw[i] == '.' && !dot) dot = true;
                else valid = false;
            }
            if (!valid || !digit)
                Unsupported("Malformed PDF numeric tokens cannot be recovered unambiguously. Re-export the source PDF.");
        }
        if (type == T::tt_array_open || type == T::tt_dict_open) {
            if (nesting.empty()) operands.push_back({false, 0, {}});
            RequireLimit(nesting.size() < MaximumDepth, "Coordinate recovery nesting exceeds its limit.");
            nesting.push_back(type == T::tt_array_open ? T::tt_array_close : T::tt_dict_close);
        } else if (type == T::tt_array_close || type == T::tt_dict_close) {
            Require(!nesting.empty() && nesting.back() == type, "Invalid PDF coordinate recovery object nesting.");
            nesting.pop_back();
        } else if (!nesting.empty()) {
            if ((type == T::tt_word || type == T::tt_real || type == T::tt_integer) && RecoverableZeroCoordinate(raw))
                Unsupported("Malformed coordinates inside PDF arrays or dictionaries cannot be recovered. Re-export the source PDF.");
            continue;
        } else if (type == T::tt_word && !RecoverableZeroCoordinate(raw)) {
            if (raw == "BI" || raw == "ID" || raw == "EI")
                Unsupported("Inline PDF images are unsupported.");
            ++operations;
            if (std::any_of(operands.begin(), operands.end(), [](auto const& value) { return !value.repair.empty(); })) {
                size_t arity = raw == "m" || raw == "l" ? 2 :
                    raw == "c" || raw == "cm" ? 6 : raw == "v" || raw == "y" || raw == "re" ? 4 : 0;
                if (!arity || operands.size() != arity ||
                    !std::all_of(operands.begin(), operands.end(), [](auto const& value) { return value.number; }))
                    Unsupported("Malformed coordinates cannot be recovered in this PDF operation. Re-export the source PDF.");
                for (size_t i = 0; i < operands.size(); ++i) {
                    auto const& value = operands[i];
                    if (value.repair.empty()) continue;
                    RequireLimit(recovered.size() < 4096, "PDF coordinate recovery budget exceeded.");
                    recovered.push_back({value.offset, value.repair,
                        value.repair.substr(0, value.repair.find('-')), raw, i});
                }
            }
            operands.clear();
        } else {
            bool repair = (type == T::tt_word || type == T::tt_real || type == T::tt_integer) &&
                RecoverableZeroCoordinate(raw);
            operands.push_back({repair || type == T::tt_integer || type == T::tt_real,
                static_cast<size_t>(input.getLastOffset()), repair ? raw : std::string{}});
        }
        RequireLimit(operands.size() <= 256, "Coordinate recovery operand budget exceeded.");
    }
    AccountOperations(counts, operations);
    if (recovered.empty()) {
        if (std::any_of(operands.begin(), operands.end(), [](auto const& value) { return !value.repair.empty(); }))
            Unsupported("Incomplete drawing coordinates cannot be recovered. Re-export the source PDF.");
        return {};
    }
    Require(nesting.empty() && operands.empty(), "Incomplete PDF stream prevents coordinate recovery.");
    for (auto item = recovered.rbegin(); item != recovered.rend(); ++item) {
        Require(item->offset <= content.size() &&
            content.compare(item->offset, item->original.size(), item->original) == 0,
            "PDF coordinate recovery offset is invalid.");
        content.replace(item->offset, item->original.size(), item->replacement);
    }
    stream.replaceStreamData(content, QPDFObjectHandle::newNull(), QPDFObjectHandle::newNull());
    return recovered;
}

bool RecoverCoordinateStream(QPDFObjectHandle stream,
    std::map<QPDFObjGen, std::vector<DrawingCoordinateRecovery>>& recoveries, Counts& counts)
{
    auto id = stream.getObjGen();
    if (recoveries.count(id)) return true;
    auto recovered = RecoverDrawingCoordinates(stream, counts);
    if (recovered.empty()) return false;
    size_t total = recovered.size();
    for (auto const& item : recoveries) total += item.second.size();
    RequireLimit(total <= 4096, "Document coordinate recovery budget exceeded.");
    recoveries.emplace(id, std::move(recovered));
    return true;
}

std::vector<DrawingCoordinateRecovery> RecoverPageCoordinates(QPDFObjectHandle page, Counts& counts)
{
    auto streams = page.getPageContents();
    if (streams.empty()) return {};
    RequireLimit(streams.size() <= MaximumObjects, "Page coordinate recovery stream budget exceeded.");
    bool candidate = false;
    size_t total = 0;
    size_t joinedSize = 0;
    bool needNewline = false;
    std::vector<size_t> boundaries;
    for (auto stream : streams) {
        auto data = stream.getStreamData(qpdf_dl_all);
        RequireLimit(data->getSize() <= MaximumContentBytes, "Coordinate recovery content exceeds its limit.");
        AccountDecoded(counts, data->getSize());
        total += data->getSize();
        std::string content(reinterpret_cast<char const*>(data->getBuffer()), data->getSize());
        if (joinedSize || needNewline) boundaries.push_back(joinedSize);
        if (needNewline) ++joinedSize;
        joinedSize += content.size();
        needNewline = content.empty() || content.back() != '\n';
        candidate = candidate || HasRecoverableCoordinate(content);
    }
    // Every multi-stream page needs the lexical boundary check, even when no
    // damaged coordinate is present: later paint rewriting also joins streams.
    if (!candidate && streams.size() == 1) return {};
    RequireLimit(total <= MaximumContentBytes && joinedSize <= MaximumContentBytes,
        "Joined coordinate recovery content exceeds its limit.");
    // QPDF owns concatenation semantics. Work on a detached page and stream:
    // neither another page's shared operands nor passive attachment bytes may
    // change when this particular page's operator context establishes a repair.
    QPDFPageObjectHelper helper(page);
    auto copy = helper.shallowCopyPage();
    copy.coalesceContentStreams();
    auto joined = copy.getPageContents();
    Require(joined.size() == 1, "Page coordinate recovery could not join its content streams.");
    auto recoveredStream = joined.front().copyStream();
    auto recovered = RecoverDrawingCoordinates(recoveredStream, counts, streams.size() > 1, boundaries);
    if (!recovered.empty()) page.replaceKey("/Contents", recoveredStream);
    return recovered;
}

std::optional<ImageDimensionRecovery> RecoverImageDimensions(QPDFObjectHandle image,
    RetainedImageBudget& budget)
{
    auto dict = image.getDict();
    auto mask = dict.getKey("/SMask");
    // Intrinsic JPEG dimensions plus an independent, exactly sized alpha plane
    // are required. A bare JPEG header cannot prove a damaged mask's row layout.
    if (!RetainedJpegFilter(dict.getKey("/Filter")) || !mask.isStream() ||
        !dict.getKey("/ColorSpace").isNameAndEquals("/DeviceRGB") ||
        !dict.getKey("/BitsPerComponent").isInteger() ||
        dict.getKey("/BitsPerComponent").getIntValue() != 8) return std::nullopt;
    auto md = mask.getDict();
    ImageDimensionRecovery recovery{{RetainedDimension(dict, "/Width"), RetainedDimension(dict, "/Height")},
        {RetainedDimension(md, "/Width"), RetainedDimension(md, "/Height")},
        RetainedJpegGeometry(image, budget)};
    if (recovery.imageBefore == recovery.intrinsic && recovery.maskBefore == recovery.intrinsic)
        return std::nullopt;
    auto area = [](auto const& size) { return size[0] * size[1]; };
    RequireLimit(area(recovery.imageBefore) <= MaximumImagePixels &&
        area(recovery.maskBefore) <= MaximumImagePixels, "Image recovery pixel budget exceeded.");
    if (!dict.getKey("/DecodeParms").isNull() || !md.getKey("/DecodeParms").isNull() ||
        !md.getKey("/Matte").isNull() ||
        (recovery.imageBefore != recovery.intrinsic && recovery.maskBefore != recovery.imageBefore &&
            recovery.maskBefore != recovery.intrinsic) ||
        (recovery.maskBefore != recovery.intrinsic && area(recovery.maskBefore) == area(recovery.intrinsic)))
        Unsupported("Image dimensions disagree and cannot be recovered unambiguously. Re-export the source PDF.");
    // Copy the mask: another image may legitimately share the original mask at
    // its original dimensions. Never rewrite that object's dictionary in place.
    auto normalizedMask = mask.copyStream();
    auto normalizedImage = image.copyStream();
    for (auto object : {normalizedImage, normalizedMask}) {
        object.getDict().replaceKey("/Width", QPDFObjectHandle::newInteger(recovery.intrinsic[0]));
        object.getDict().replaceKey("/Height", QPDFObjectHandle::newInteger(recovery.intrinsic[1]));
    }
    normalizedImage.getDict().replaceKey("/SMask", normalizedMask);
    // This validates every image/mask attribute, the exact alpha sample count,
    // and independently decodes JPEG samples at the proposed dimensions.
    ValidateRetainedImage(normalizedImage, budget);
    auto rgb = RetainedDecoded(normalizedImage, budget, MaximumDecodedBytes);
    Require(rgb->getSize() == area(recovery.intrinsic) * 3,
        "Image dimensions disagree with the decoded JPEG. Re-export the source PDF.");
    dict.replaceKey("/Width", normalizedImage.getDict().getKey("/Width"));
    dict.replaceKey("/Height", normalizedImage.getDict().getKey("/Height"));
    dict.replaceKey("/SMask", normalizedMask);
    return recovery;
}

bool RecoverResourceDimensions(QPDFObjectHandle resources, unsigned depth,
    std::map<QPDFObjGen, ImageDimensionRecovery>& recoveries, std::set<QPDFObjGen>& visited,
    std::map<QPDFObjGen, std::vector<DrawingCoordinateRecovery>>& coordinates, Counts& counts)
{
    RequireLimit(depth < MaximumDepth, "Image recovery resource nesting exceeds the limit.");
    auto objects = resources.getKey("/XObject");
    if (!objects.isDictionary()) return false;
    bool changed = false;
    for (auto const& key : objects.getKeys()) {
        auto object = objects.getKey(key);
        if (!object.isStream()) continue;
        auto id = object.getObjGen();
        if (recoveries.count(id)) { changed = true; continue; }
        if (!visited.insert(id).second) continue;
        RequireLimit(visited.size() <= MaximumObjects, "Image recovery object budget exceeded.");
        auto dict = object.getDict();
        if (dict.getKey("/Subtype").isNameAndEquals("/Image")) {
            RetainedImageBudget budget;
            auto recovery = RecoverImageDimensions(object, budget);
            AccountRetainedImage(counts, budget);
            if (recovery) { recoveries.emplace(id, *recovery); changed = true; }
        } else if (dict.getKey("/Subtype").isNameAndEquals("/Form")) {
            changed = RecoverCoordinateStream(object, coordinates, counts) || changed;
            auto nested = dict.getKey("/Resources");
            if (nested.isDictionary())
                changed = RecoverResourceDimensions(nested, depth + 1, recoveries, visited, coordinates, counts) || changed;
        }
    }
    return changed;
}

struct Plan {
    unsigned pageCount = 0;
    Counts counts;
    std::vector<bool> changed;
    std::vector<bool> photoScenes;
    std::vector<std::set<int>> retainedImageObjects;
    std::vector<std::string> expectedContents;
    std::vector<size_t> insertedPrefixBytes;
    std::vector<std::vector<ImageBackingInsertion>> imageBackingOffsets;
    std::map<QPDFObjGen, ImageDimensionRecovery> imageDimensionRecoveries;
    std::map<QPDFObjGen, std::vector<DrawingCoordinateRecovery>> coordinateRecoveries;
    std::map<size_t, std::vector<DrawingCoordinateRecovery>> pageCoordinateRecoveries;
};

class SemanticTokens final : public QPDFObjectHandle::ParserCallbacks {
  public:
    void handleObject(QPDFObjectHandle object) override
    {
        if (!object.isOperator()) {
            _operands.push_back(object);
            Require(_operands.size() <= 256, "Output PDF operation has too many operands.");
            return;
        }
        auto op = object.getOperatorValue();
        if (op == "Do") {
            Require(_operands.size() == 1 && _operands[0].isName(), "Invalid output PDF XObject name.");
            usedXObjects.insert(_operands[0].getName());
        }
        if (op != "rg" && op != "RG" && op != "g" && op != "G" &&
            op != "k" && op != "K" && op != "cs" && op != "CS" &&
            op != "sc" && op != "SC" && op != "scn" && op != "SCN") {
            for (auto const& operand : _operands) {
                normalized += operand.unparse();
                normalized += ' ';
            }
            normalized += op + "\n";
        }
        _operands.clear();
        Require(normalized.size() <= MaximumContentBytes, "Output PDF content exceeds its limit.");
    }
    void handleEOF() override { Require(_operands.empty(), "Incomplete output PDF content."); }
    std::string normalized;
    std::set<std::string> usedXObjects;

  private:
    std::vector<QPDFObjectHandle> _operands;
};

void VerifyResources(QPDFObjectHandle before, QPDFObjectHandle after, unsigned depth,
    bool photoScene = false, std::set<int> const& retainedImages = {},
    std::set<std::string> const* usedXObjects = nullptr)
{
    Require(depth < MaximumDepth, "Output PDF resources exceed nesting limit.");
    Require(before.isDictionary() && after.isDictionary(), "Output PDF lost its resources.");
    auto originalFonts = before.getKey("/Font");
    auto resultFonts = after.getKey("/Font");
    Require(originalFonts.isNull() == resultFonts.isNull(), "Output PDF font resources changed.");
    if (!originalFonts.isNull()) {
        Require(originalFonts.isDictionary() && resultFonts.isDictionary() &&
            originalFonts.getKeys() == resultFonts.getKeys(), "Output PDF font names changed.");
        for (auto const& key : originalFonts.getKeys()) {
            auto a = originalFonts.getKey(key);
            auto b = resultFonts.getKey(key);
            Require(a.isDictionary() && b.isDictionary() &&
                a.getKey("/Subtype").unparse() == b.getKey("/Subtype").unparse() &&
                a.getKey("/BaseFont").unparse() == b.getKey("/BaseFont").unparse(),
                "Output PDF font identity changed.");
        }
    }
    auto originalShadings = before.getKey("/Shading");
    auto resultShadings = after.getKey("/Shading");
    Require(originalShadings.isNull() == resultShadings.isNull(), "Output PDF shading resources changed.");
    if (!originalShadings.isNull()) {
        Require(originalShadings.isDictionary() && resultShadings.isDictionary() &&
            originalShadings.getKeys() == resultShadings.getKeys(), "Output PDF shading names changed.");
        for (auto const& key : originalShadings.getKeys())
            VerifyAxialShading(originalShadings.getKey(key), resultShadings.getKey(key));
    }
    auto originalObjects = before.getKey("/XObject");
    auto resultObjects = after.getKey("/XObject");
    Require(originalObjects.isNull() == resultObjects.isNull(), "Output PDF XObjects changed.");
    if (originalObjects.isNull()) return;
    Require(originalObjects.isDictionary() && resultObjects.isDictionary() &&
        originalObjects.getKeys() == resultObjects.getKeys(), "Output PDF XObject names changed.");
    for (auto const& key : originalObjects.getKeys()) {
        auto a = originalObjects.getKey(key);
        auto b = resultObjects.getKey(key);
        if (usedXObjects && !usedXObjects->count(key)) {
            CompareRetainedValue(a, b, 0);
            continue;
        }
        Require(a.isStream() && b.isStream(), "Output PDF XObject type changed.");
        auto da = a.getDict(), db = b.getDict();
        Require(da.getKey("/Subtype").unparse() == db.getKey("/Subtype").unparse(),
            "Output PDF XObject subtype changed.");
        if (da.getKey("/Subtype").isNameAndEquals("/Image")) {
            RetainedImageBudget retainedBudget;
            if (photoScene || retainedImages.count(a.getObjectID()) ||
                IsOpaqueRetainedJpeg(a, retainedBudget) || IsRetainedLightInk(a, retainedBudget))
                VerifyRetainedImage(a, b);
            Require(da.getKey("/Width").unparse() == db.getKey("/Width").unparse() &&
                da.getKey("/Height").unparse() == db.getKey("/Height").unparse() &&
                da.getKey("/SMask").isNull() == db.getKey("/SMask").isNull(),
                "Output PDF image geometry or mask changed.");
            if (!da.getKey("/SMask").isNull()) {
                auto originalMask = da.getKey("/SMask"), resultMask = db.getKey("/SMask");
                auto originalMatte = originalMask.getDict().getKey("/Matte");
                if (!originalMatte.isNull() && resultMask.getDict().getKey("/Matte").isNull()) {
                    RetainedImageBudget budget;
                    auto samples = ReadRetainedRgb(a, budget);
                    Require(samples.blackMatte &&
                        std::all_of(samples.rgb.begin(), samples.rgb.end(), [](char value) { return value == 0; }),
                        "Output PDF matte normalization changed source semantics.");
                    originalMask = originalMask.copyStream();
                    originalMask.getDict().removeKey("/Matte");
                }
                CompareRetainedValue(originalMask, resultMask, 0);
                auto ma = da.getKey("/SMask").getStreamData(qpdf_dl_all);
                auto mb = db.getKey("/SMask").getStreamData(qpdf_dl_all);
                Require(ma->getSize() == mb->getSize() &&
                    std::equal(ma->getBuffer(), ma->getBuffer() + ma->getSize(), mb->getBuffer()),
                    "Output PDF soft mask changed.");
            }
        } else {
            SemanticTokens at, bt;
            QPDFObjectHandle::parseContentStream(a, &at);
            QPDFObjectHandle::parseContentStream(b, &bt);
            Require(at.normalized == bt.normalized, "Output PDF form geometry or text changed.");
            auto ra = da.getKey("/Resources");
            auto rb = db.getKey("/Resources");
            // Missing form resources inherit the surrounding dictionary. Verify
            // the synthesized copy as well, without following unused self entries.
            VerifyResources(ra.isNull() ? before : ra, rb, depth + 1,
                false, retainedImages, &at.usedXObjects);
        }
    }
}

void VerifyOutput(std::string const& source, std::shared_ptr<Buffer> const& output, Plan const& plan)
{
    Require(output && output->getSize() > 0 && output->getSize() <= MaximumOutputBytes,
        "Output PDF exceeds its size limit.");
    QPDF original;
    QPDF candidate;
    original.setAttemptRecovery(false);
    candidate.setAttemptRecovery(false);
    original.processMemoryFile("original PDF", source.data(), source.size());
    auto originalStructure = InspectPdfStructure(original);
    // Re-establish precisely the recorded recovery on an independent source
    // parse. Ordinary resource/stream/structure equality then applies without
    // blanket exceptions for dimensions, masks or image bytes.
    RetainedImageBudget recoveryBudget;
    for (auto const& [id, expected] : plan.imageDimensionRecoveries) {
        auto actual = RecoverImageDimensions(original.getObject(id), recoveryBudget);
        Require(actual && *actual == expected, "Output PDF image recovery record changed.");
    }
    Counts coordinateBudget;
    for (auto const& [id, expected] : plan.coordinateRecoveries) {
        auto actual = RecoverDrawingCoordinates(original.getObject(id), coordinateBudget);
        Require(actual == expected, "Output PDF coordinate recovery record changed.");
    }
    auto recoveryPages = original.getAllPages();
    for (auto const& [index, expected] : plan.pageCoordinateRecoveries) {
        Require(index < recoveryPages.size(), "Output PDF coordinate recovery page changed.");
        auto actual = RecoverPageCoordinates(recoveryPages[index], coordinateBudget);
        Require(actual == expected, "Output PDF joined coordinate recovery record changed.");
    }
    candidate.processMemoryFile("candidate PDF", reinterpret_cast<char const*>(output->getBuffer()),
        output->getSize());
    auto beforePages = original.getAllPages();
    auto afterPages = candidate.getAllPages();
    std::map<size_t, std::string> markerContents;
    Require(beforePages.size() == afterPages.size(), "Output PDF page count changed.");
    for (size_t index = 0; index < beforePages.size(); ++index) {
        auto originalStreams = beforePages[index].getPageContents();
        auto outputStreams = afterPages[index].getPageContents();
        if (plan.changed[index]) {
            Require(outputStreams.size() == 1, "Output PDF changed-page stream count is invalid.");
            auto data = outputStreams[0].getStreamData(qpdf_dl_all);
            auto const& expected = plan.expectedContents[index];
            size_t matched = 0;
            while (matched < data->getSize() && matched < expected.size() &&
                data->getBuffer()[matched] == static_cast<unsigned char>(expected[matched])) ++matched;
            if (matched != data->getSize() || matched != expected.size())
                throw Problem(4, "Output PDF changed its generated page stream on page " +
                    std::to_string(index + 1) + " at byte " + std::to_string(matched) +
                    " (expected " + std::to_string(expected.size()) + ", actual " +
                    std::to_string(data->getSize()) + ").");
        } else {
            Require(originalStreams.size() == outputStreams.size(),
                "Output PDF changed an already-dark page stream count.");
            for (size_t stream = 0; stream < originalStreams.size(); ++stream) {
                auto a = originalStreams[stream].getStreamData(qpdf_dl_all);
                auto b = outputStreams[stream].getStreamData(qpdf_dl_all);
                Require(a->getSize() == b->getSize() &&
                    std::equal(a->getBuffer(), a->getBuffer() + a->getSize(), b->getBuffer()),
                    "Output PDF changed an already-dark page stream.");
            }
        }
        QPDFPageObjectHelper before(beforePages[index]), after(afterPages[index]);
        for (auto const& key : {"/MediaBox", "/CropBox", "/Rotate", "/UserUnit"}) {
            Require(before.getAttribute(key, false).unparse() == after.getAttribute(key, false).unparse(),
                "Output PDF page geometry changed.");
        }
        SemanticTokens a, b;
        before.parsePageContents(&a);
        if (plan.changed[index]) {
            // Exact decoded-stream equality above proves this is the prefix we inserted.
            // Normalize only the remainder; arbitrary source marked-content is retained.
            auto remainder = plan.expectedContents[index].substr(plan.insertedPrefixBytes[index]);
            // Offsets come only from proved image placements. Exact stream
            // equality above and these literal checks allow only the planned
            // local rectangles; every original drawing/text token is compared.
            for (auto offset = plan.imageBackingOffsets[index].rbegin();
                offset != plan.imageBackingOffsets[index].rend(); ++offset) {
                auto inserted = LocalImageBacking(offset->backing);
                Require(offset->offset <= remainder.size() &&
                    remainder.compare(offset->offset, inserted.size(), inserted) == 0,
                    "Output PDF image backing changed.");
                remainder.erase(offset->offset, inserted.size());
            }
            // Preserve every original marked-content group. Only the exact,
            // verified generated backdrops above are excluded from the snapshot.
            markerContents.emplace(index, remainder);
            QPDFObjectHandle::parseContentStream(QPDFObjectHandle::newStream(&candidate, remainder), &b);
        } else {
            after.parsePageContents(&b);
        }
        Require(a.normalized == b.normalized, "Output PDF changed text, drawing or page operations.");
        VerifyResources(before.getAttribute("/Resources", false),
            after.getAttribute("/Resources", false), 0, plan.photoScenes[index], plan.retainedImageObjects[index]);
    }
    RequirePreservedPdfStructure(candidate, originalStructure, &markerContents);
    Require(!candidate.anyWarnings(), "Output PDF parser reported warnings.");
}

std::vector<QPDFObjectHandle> ReadSourceObjects(QPDF& pdf)
{
    Require(!pdf.anyWarnings(), "PDF parser reported warnings.");
    auto xref = pdf.getXRefTable();
    RequireLimit(xref.size() <= MaximumObjects, "PDF object budget exceeded.");
    std::set<QPDFObjGen> unused;
    for (auto const& [id, entry] : xref)
        if (entry.getType() == 1 && entry.getOffset() == 0) unused.insert(id);
    if (unused.empty()) return pdf.getAllObjects();

    // Some exporters leave deleted objects marked in-use at offset zero. Prove
    // that no reference from the entire trailer can reach any such entry before
    // allowing qpdf to normalize those entries to null. Never infer this from
    // a missing page resource or from the warning text alone.
    std::vector<QPDFObjectHandle> pending{pdf.getTrailer()};
    std::set<QPDFObjGen> visited;
    size_t references = 1;
    auto enqueue = [&](QPDFObjectHandle value) {
        RequireLimit(++references <= 1000000, "PDF reference graph budget exceeded.");
        pending.push_back(value);
    };
    while (!pending.empty()) {
        auto object = pending.back(); pending.pop_back();
        if (object.isIndirect()) {
            auto id = object.getObjGen();
            Require(!unused.count(id), "Referenced PDF object has an invalid zero offset.");
            if (!visited.insert(id).second) continue;
        }
        if (object.isStream()) object = object.getDict();
        if (object.isDictionary()) {
            for (auto const& key : object.getKeys()) enqueue(object.getKey(key));
        } else if (object.isArray()) {
            for (int index = 0; index < object.getArrayNItems(); ++index)
                enqueue(object.getArrayItem(index));
        }
    }
    Require(!pdf.anyWarnings(), "Referenced PDF objects reported parser warnings.");
    auto all = pdf.getAllObjects();
    std::set<std::string> expected;
    for (auto id : unused) expected.insert("object " + id.unparse(' '));
    for (auto const& warning : pdf.getWarnings()) {
        Require(warning.getErrorCode() == qpdf_e_damaged_pdf &&
            warning.getFilePosition() == 0 && expected.erase(warning.getObject()) == 1 &&
            warning.getMessageDetail() ==
                "object has offset 0 - a common error handled correctly by qpdf and most other applications",
            "PDF parser reported an unsupported source warning.");
    }
    return all;
}

Plan Inspect(QPDF& pdf, bool convert)
{
    if (pdf.isEncrypted()) Unsupported("Encrypted PDFs are unsupported.");
    auto all = ReadSourceObjects(pdf);
    RequireLimit(all.size() <= MaximumObjects, "PDF object budget exceeded.");
    for (auto object : all) {
        if (!object.isDictionary()) continue;
        if (object.getKey("/Type").isNameAndEquals("/Sig") ||
            object.getKey("/FT").isNameAndEquals("/Sig"))
            Unsupported("Signed PDFs are unsupported.");
    }
    auto root = pdf.getRoot();
    ValidatePassiveAcroForm(pdf);
    InspectPdfStructure(pdf);
    if (!root.getKey("/OutputIntents").isNull())
        Unsupported("PDF output intents are unsupported.");
    auto pages = pdf.getAllPages();
    Require(!pages.empty(), "PDF has no pages.");
    RequireLimit(pages.size() <= MaximumPages, "PDF page budget exceeded.");
    Plan plan;
    plan.pageCount = static_cast<unsigned>(pages.size());
    size_t pageNumber = 0;
    for (auto page : pages) {
        ++pageNumber;
        try {
            auto group = page.getKey("/Group");
            if (!(group.isNull() || group.isDictionary() &&
                group.getKey("/S").isNameAndEquals("/Transparency") &&
                group.getKey("/CS").isNameAndEquals("/DeviceRGB") &&
                !group.hasKey("/I") && !group.hasKey("/K")))
                Unsupported("PDF page transparency group is unsupported.");
            for (auto const& key : {"/AA", "/PresSteps", "/B", "/VP", "/PieceInfo", "/SeparationInfo"}) {
                if (!page.getKey(key).isNull()) Unsupported("Interactive PDF page features are unsupported.");
            }
            QPDFPageObjectHelper helper(page);
            auto resources = helper.getAttribute("/Resources", false);
            Require(resources.isDictionary(), "PDF page resources are missing.");
            std::set<QPDFObjGen> recoveryVisited;
            bool recoveredDimensions = RecoverResourceDimensions(resources, 0,
                plan.imageDimensionRecoveries, recoveryVisited, plan.coordinateRecoveries, plan.counts);
            auto pageRecovery = RecoverPageCoordinates(page, plan.counts);
            bool recoveredCoordinates = !pageRecovery.empty();
            if (recoveredCoordinates) plan.pageCoordinateRecoveries.emplace(pageNumber - 1, std::move(pageRecovery));
            size_t coordinateCount = 0;
            for (auto const& item : plan.coordinateRecoveries) coordinateCount += item.second.size();
            for (auto const& item : plan.pageCoordinateRecoveries) coordinateCount += item.second.size();
            RequireLimit(coordinateCount <= 4096, "Document coordinate recovery budget exceeded.");
            ValidateFonts(resources);
            auto box = helper.getAttribute("/MediaBox", false);
            Require(box.isArray() && box.getArrayNItems() == 4, "Invalid PDF page box.");
            double width = box.getArrayItem(2).getNumericValue() - box.getArrayItem(0).getNumericValue();
            double height = box.getArrayItem(3).getNumericValue() - box.getArrayItem(1).getNumericValue();
            Require(width > 0 && height > 0, "Invalid PDF page dimensions.");
            RequireLimit(width * height <= 200000000, "PDF page geometry exceeds the limit.");
            std::array<double, 4> pageBox{box.getArrayItem(0).getNumericValue(),
                box.getArrayItem(1).getNumericValue(), box.getArrayItem(2).getNumericValue(),
                box.getArrayItem(3).getNumericValue()};
            ContentRewrite initial(width * height, false, resources, pageBox);
            for (auto stream : page.getPageContents()) {
                auto decoded = stream.getStreamData(qpdf_dl_all);
                RequireLimit(decoded->getSize() <= MaximumContentBytes,
                    "PDF page content stream exceeds the limit.");
                AccountDecoded(plan.counts, decoded->getSize());
            }
            helper.parsePageContents(&initial);
            AccountOperations(plan.counts, initial.summary.operations);
            AccountDecoded(plan.counts, initial.summary.decodedFontBytes);
            AccountRetainedImage(plan.counts, initial.summary.retainedImageProbe);
            RetainedImageBudget localPhotoBudget;
            auto localPhotos = initial.RetainedLocalPhotos(localPhotoBudget);
            AccountRetainedImage(plan.counts, localPhotoBudget);
            std::unique_ptr<ContentRewrite> localInspection;
            if (!localPhotos.paintOperations.empty()) {
                localInspection = std::make_unique<ContentRewrite>(width * height, false, resources,
                    pageBox, false, std::set<size_t>{}, std::set<size_t>{}, std::map<size_t, Color>{},
                    localPhotos.paintOperations);
                helper.parsePageContents(localInspection.get());
                AccountOperations(plan.counts, localInspection->summary.operations);
                AccountDecoded(plan.counts, localInspection->summary.decodedFontBytes);
                AccountRetainedImage(plan.counts, localInspection->summary.retainedImageProbe);
            }
            auto& inspection = localInspection ? *localInspection : initial;
            bool photoScene = inspection.summary.photoSceneStart != 0;
            plan.photoScenes.push_back(photoScene);
            if (photoScene && inspection.summary.fractionalOutsidePhoto)
                Unsupported("Fractional paint before a retained photo is unsupported.");
            plan.counts.formContrast.clear();
            plan.counts.resourcePaletteChanges = false;
            auto priorRecoloredImages = plan.counts.recoloredImages;
            auto retained = inspection.ResolveContrast();
            auto fractional = inspection.ResolveFractionalFills();
            plan.counts.diagramImages.clear();
            plan.counts.recoloredImageObjects.clear();
            plan.counts.sparseMasks.clear();
            plan.counts.retainedImageObjects.clear();
            plan.counts.localPhotoObjects = localPhotos.imageObjects;
            plan.counts.retainedPanelObjects.clear();
            plan.counts.softPanelObjects.clear();
            RetainedImageBudget supportBudget;
            auto supports = inspection.ImageAlphaSupports(supportBudget);
            AccountRetainedImage(plan.counts, supportBudget);
            plan.counts.imageBackdrops = inspection.RetainableImageBackdrops(supports);
            std::map<int, std::set<size_t>> islandText;
            auto islands = inspection.RetainedImageIslands(supports, islandText);
            auto backings = inspection.ImageBackingPlacements(supports);
            std::map<size_t, ImageBacking> imageBackings;
            plan.counts.imageIslands.clear();
            plan.counts.coloredImageIslands.clear();
            plan.counts.imageBackingCandidates.clear();
            plan.counts.backedImages.clear();
            for (auto const& island : islands) {
                bool white = std::all_of(island.second.begin(), island.second.end(), [](auto const& paint) {
                    return *std::min_element(paint.second.begin(), paint.second.end()) >= .98;
                });
                if (white) plan.counts.imageIslands.insert(island.first);
                else plan.counts.coloredImageIslands.insert(island.first);
            }
            for (auto const& backing : backings) {
                if (islands.count(backing.first)) continue;
                plan.counts.imageBackingCandidates.insert(backing.first);
            }
            auto annotated = inspection.RetainedAnnotatedImages();
            auto composites = inspection.AnnotatedImageComposites();
            plan.counts.annotatedImages.clear();
            plan.counts.compositeImages.clear();
            auto preservedPaint = localPhotos.paintOperations;
            for (auto const& scene : annotated) {
                plan.counts.annotatedImages.insert(scene.first);
            }
            auto cloneAndValidate = [&]() {
                auto cloned = CloneResources(resources, plan.counts, pdf, 0, photoScene,
                    !fractional.empty() || !localPhotos.paintOperations.empty());
                inspection.ValidateSparsePlacements(plan.counts.sparseMasks);
                inspection.ValidateSoftPanelPlacements(plan.counts.diagramImages, plan.counts.softPanelObjects);
                inspection.ValidateImageText(plan.counts.recoloredImageObjects, plan.counts.diagramImages);
                if (!plan.counts.retainedPanelObjects.empty()) {
                    ContentRewrite preview(width * height, true, resources, pageBox, false, retained.fills,
                        inspection.ResolveLightRules(), fractional, preservedPaint, retained.strokes, {},
                        retained.preservedStrokes);
                    helper.parsePageContents(&preview);
                    AccountOperations(plan.counts, preview.summary.operations);
                    AccountDecoded(plan.counts, preview.summary.decodedFontBytes);
                    AccountRetainedImage(plan.counts, preview.summary.retainedImageProbe);
                    inspection.ValidateRetainedPanelText(plan.counts.retainedPanelObjects,
                        preview.unchangedVectorPaint, plan.counts.retainedImageObjects, plan.counts.backedImages);
                }
                inspection.ValidateFormContrast(plan.counts.formContrast);
                return cloned;
            };
            auto initialCounts = plan.counts;
            QPDFObjectHandle cloned;
            try {
                cloned = cloneAndValidate();
            } catch (Problem const& problem) {
                if (problem.code != 3 || composites.empty()) throw;
                // Prefer the normal dark palette. If its contrast/transparency
                // proof fails, retry with the independently proved original
                // composites. Account both attempts against the work budgets.
                initialCounts.decodedBytes = plan.counts.decodedBytes;
                initialCounts.decodedPixels = plan.counts.decodedPixels;
                initialCounts.operations = plan.counts.operations;
                initialCounts.resourceClones = plan.counts.resourceClones;
                plan.counts = std::move(initialCounts);
                for (auto const& [object, composite] : composites)
                    plan.counts.compositeImages.insert(composite.images.begin(), composite.images.end());
                cloned = cloneAndValidate();
            }
            for (int object : plan.counts.backedImages) {
                auto const& operations = backings.at(object);
                imageBackings.insert(operations.begin(), operations.end());
                for (auto const& [operation, backing] : operations)
                    preservedPaint.insert(backing.preservedText.begin(), backing.preservedText.end());
            }
            for (auto const& scene : annotated)
                if (plan.counts.annotatedImages.count(scene.first))
                    preservedPaint.insert(scene.second.begin(), scene.second.end());
            std::map<size_t, ImageBacking> pageBackings;
            for (auto const& [object, composite] : composites) {
                if (!plan.counts.compositeImages.count(object)) continue;
                preservedPaint.insert(composite.paint.begin(), composite.paint.end());
                fractional.insert(composite.fills.begin(), composite.fills.end());
                for (auto const& [operation, backing] : composite.backings) {
                    auto& merged = pageBackings[operation];
                    merged.color = backing.color;
                    merged.pageCoordinates = true;
                    merged.rectangles.insert(merged.rectangles.end(), backing.rectangles.begin(), backing.rectangles.end());
                }
            }
            for (auto const& island : islands)
                if (plan.counts.retainedImageObjects.count(island.first)) {
                    fractional.insert(island.second.begin(), island.second.end());
                    auto const& text = islandText[island.first];
                    preservedPaint.insert(text.begin(), text.end());
                }
            plan.retainedImageObjects.push_back(plan.counts.retainedImageObjects);
            ContentRewrite rewritten(width * height, true, resources, pageBox, false, retained.fills,
                inspection.ResolveLightRules(), fractional, preservedPaint, retained.strokes, imageBackings,
                retained.preservedStrokes, pageBackings);
            helper.parsePageContents(&rewritten);
            AccountOperations(plan.counts, rewritten.summary.operations);
            AccountDecoded(plan.counts, rewritten.summary.decodedFontBytes);
            AccountRetainedImage(plan.counts, rewritten.summary.retainedImageProbe);
            bool changed = recoveredDimensions || recoveredCoordinates || !inspection.summary.paperPresent || rewritten.summary.paletteChanges ||
                plan.counts.resourcePaletteChanges || plan.counts.recoloredImages != priorRecoloredImages;
            plan.changed.push_back(changed);
            plan.imageBackingOffsets.push_back(rewritten.imageBackingOffsets);
            if (!changed) ++plan.counts.alreadyDarkPages;
            else ++plan.counts.changedPages;
            if (!changed || !convert) {
                plan.expectedContents.emplace_back();
                plan.insertedPrefixBytes.push_back(0);
                continue;
            }
            auto background = SetColor(Paper, false) + Number(box.getArrayItem(0).getNumericValue()) + " " +
                Number(box.getArrayItem(1).getNumericValue()) + " " + Number(width) + " " + Number(height) + " re f\n";
            auto prefix = inspection.summary.paperPresent ? std::string{} :
                "/Artifact BMC\nq\n" + background + "Q\nEMC\n";
            auto content = prefix + rewritten.output();
            plan.expectedContents.push_back(content);
            plan.insertedPrefixBytes.push_back(prefix.size());
            auto stream = pdf.newStream(content);
            page.replaceKey("/Resources", cloned);
            page.replaceKey("/Contents", stream);
        } catch (Problem const& problem) {
            throw Problem(problem.code, "Page " + std::to_string(pageNumber) + ": " + problem.what());
        }
    }
    Require(!pdf.anyWarnings(), "PDF parser reported warnings.");
    return plan;
}

void VerifyRepeatableOutput(std::shared_ptr<Buffer> const& output)
{
    RequireLimit(output->getSize() <= MaximumSourceBytes,
        "PDF reading copy exceeds the repeat-input limit.");
    // A successful reading copy must itself be a valid no-op input. Check the
    // serialized bytes with the same bounded planner; do not trust a marker or
    // keep applying lossy palette changes until the document happens to settle.
    try {
        QPDF repeat;
        repeat.setAttemptRecovery(false);
        repeat.processMemoryFile("PDF repeat validation",
            reinterpret_cast<char const*>(output->getBuffer()), output->getSize());
        auto repeated = Inspect(repeat, false);
        if (repeated.counts.changedPages)
            Unsupported("PDF reading palette does not reach a stable repeat conversion.");
    } catch (Problem const& problem) {
        if (problem.code == 5) throw;
        Unsupported("PDF reading palette does not reach a stable repeat conversion.");
    }
}

std::string ReadInput()
{
    Require(_setmode(_fileno(stdin), _O_BINARY) != -1, "Binary stdin is required.");
    auto length = _filelengthi64(_fileno(stdin));
    Require(length > 0, "Empty PDF source.");
    RequireLimit(static_cast<uint64_t>(length) <= MaximumSourceBytes, "Seekable PDF source exceeds its size limit.");
    Require(_lseeki64(_fileno(stdin), 0, SEEK_SET) == 0, "PDF source handle is not seekable.");
    std::string input(static_cast<size_t>(length), '\0');
    Require(fread(input.data(), 1, input.size(), stdin) == input.size(), "Could not read the PDF source.");
    Require(input.compare(0, 5, "%PDF-") == 0, "Source is not a PDF.");
    return input;
}

void Put32(uint32_t value)
{
    std::array<unsigned char, 4> bytes{
        static_cast<unsigned char>(value), static_cast<unsigned char>(value >> 8),
        static_cast<unsigned char>(value >> 16), static_cast<unsigned char>(value >> 24)};
    Require(fwrite(bytes.data(), 1, bytes.size(), stdout) == bytes.size(), "Could not write response header.");
}

void WriteResult(Plan const& plan, size_t sourceBytes, std::shared_ptr<Buffer> output)
{
    size_t recoveredCoordinates = 0;
    for (auto const& item : plan.coordinateRecoveries) recoveredCoordinates += item.second.size();
    for (auto const& item : plan.pageCoordinateRecoveries) recoveredCoordinates += item.second.size();
    std::string json = "{\"policy\":\"pdf-dark-9\",\"pageCount\":" + std::to_string(plan.pageCount) +
        ",\"changedPages\":" + std::to_string(plan.counts.changedPages) +
        ",\"alreadyDarkPages\":" + std::to_string(plan.counts.alreadyDarkPages) +
        ",\"recoloredImages\":" + std::to_string(plan.counts.recoloredImages) +
        ",\"preservedImages\":" + std::to_string(plan.counts.preservedImages) +
        ",\"recoveredImages\":" + std::to_string(plan.imageDimensionRecoveries.size()) +
        ",\"recoveredCoordinates\":" + std::to_string(recoveredCoordinates) +
        ",\"sourceBytes\":" + std::to_string(sourceBytes) + "}";
    RequireLimit(json.size() <= 65536 && (!output || output->getSize() <= MaximumOutputBytes),
        "PDF response exceeds its limit.");
    Require(_setmode(_fileno(stdout), _O_BINARY) != -1, "Binary stdout is required.");
    Put32(0x31445043);
    Put32(1);
    Put32(static_cast<uint32_t>(json.size()));
    Put32(output ? static_cast<uint32_t>(output->getSize()) : 0);
    Require(fwrite(json.data(), 1, json.size(), stdout) == json.size(), "Could not write response.");
    if (output)
        Require(fwrite(output->getBuffer(), 1, output->getSize(), stdout) == output->getSize(), "Could not write PDF result.");
    Require(fflush(stdout) == 0, "Could not finish PDF response.");
}
}

int main(int argc, char** argv)
{
    if (argc != 2 || (std::string(argv[1]) != "--inspect" && std::string(argv[1]) != "--convert")) {
        std::cerr << "Expected --inspect or --convert.\n";
        return 2;
    }
    try {
        auto source = ReadInput();
        QPDF pdf;
        pdf.setAttemptRecovery(false);
        pdf.processMemoryFile("PDF source", source.data(), source.size());
        bool convert = std::string(argv[1]) == "--convert";
        // Inspection and export share output/fidelity admission. Inspection
        // discards the verified bytes rather than approving a later bad export.
        auto plan = Inspect(pdf, true);
        std::shared_ptr<Buffer> output;
        if (plan.counts.changedPages) {
            QPDFWriter writer(pdf);
            writer.setOutputMemory();
            writer.write();
            output = writer.getBufferSharedPointer();
            VerifyOutput(source, output, plan);
            VerifyRepeatableOutput(output);
        }
        if (!convert) output.reset();
        WriteResult(plan, source.size(), output);
        return 0;
    } catch (Problem const& error) {
        std::cerr << error.what() << '\n';
        return error.code;
    } catch (QPDFExc const& error) {
        std::cerr << error.what() << '\n';
        return error.getErrorCode() == qpdf_e_password ? 3 : 4;
    } catch (std::exception const& error) {
        std::cerr << error.what() << '\n';
        return 6;
    }
}
