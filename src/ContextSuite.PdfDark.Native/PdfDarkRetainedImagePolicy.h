#pragma once

// Included inside PdfDark.cpp's anonymous namespace, after its resource bounds
// and Problem helpers. These checks admit unchanged image bytes only; they never
// decode JPEG samples into replacement pixels or rewrite color profiles.
struct RetainedImageBudget {
    size_t decodedBytes = 0;
    size_t decodedPixels = 0;
};

void AccountRetainedBytes(RetainedImageBudget& budget, size_t bytes)
{
    RequireLimit(bytes <= MaximumDecodedBytes &&
        budget.decodedBytes <= MaximumTotalDecodedBytes - bytes,
        "Retained PDF image streams exceed the decoded-byte budget.");
    budget.decodedBytes += bytes;
}

void AccountRetainedPixels(RetainedImageBudget& budget, size_t pixels)
{
    RequireLimit(pixels <= MaximumImagePixels &&
        budget.decodedPixels <= MaximumTotalPixels - pixels,
        "Retained PDF images exceed the pixel budget.");
    budget.decodedPixels += pixels;
}

bool RetainedKeys(QPDFObjectHandle dictionary, std::set<std::string> const& allowed)
{
    Require(dictionary.isDictionary(), "Invalid retained PDF image dictionary.");
    for (auto const& key : dictionary.getKeys())
        if (!allowed.count(key)) return false;
    return true;
}

size_t RetainedDimension(QPDFObjectHandle dictionary, char const* key)
{
    auto value = dictionary.getKey(key);
    Require(value.isInteger() && value.getIntValue() > 0, "Invalid retained PDF image dimension.");
    auto result = static_cast<size_t>(value.getIntValue());
    RequireLimit(result <= 16384, "Retained PDF image dimension exceeds the limit.");
    return result;
}

std::shared_ptr<Buffer> RetainedDecoded(QPDFObjectHandle stream, RetainedImageBudget& budget,
    size_t maximum)
{
    Require(stream.isStream(), "Invalid retained PDF stream.");
    auto data = ReadBoundedPdfStream(stream, maximum, "Retained PDF stream exceeds its size limit.");
    Require(data != nullptr, "Retained PDF stream cannot be decoded.");
    RequireLimit(data->getSize() <= maximum, "Retained PDF stream exceeds its size limit.");
    AccountRetainedBytes(budget, data->getSize());
    return data;
}

void ValidateRetainedIcc(QPDFObjectHandle profile, RetainedImageBudget& budget)
{
    Require(profile.isStream(), "Invalid retained PDF ICC profile.");
    auto dict = profile.getDict();
    auto filter = dict.getKey("/Filter"), alternate = dict.getKey("/Alternate");
    if (!RetainedKeys(dict, {"/Length", "/Filter", "/N", "/Alternate", "/Range"}) ||
        !(filter.isNull() || filter.isNameAndEquals("/FlateDecode")) ||
        !dict.getKey("/N").isInteger() || dict.getKey("/N").getIntValue() != 3 ||
        !(alternate.isNull() || alternate.isNameAndEquals("/DeviceRGB")))
        Unsupported("Retained PDF ICC profile has unsupported features.");
    auto range = dict.getKey("/Range");
    if (!range.isNull()) {
        Require(range.isArray() && range.getArrayNItems() == 6, "Invalid PDF RGB ICC range.");
        for (int i = 0; i < 6; ++i)
            if (!range.getArrayItem(i).isNumber() || range.getArrayItem(i).getNumericValue() != i % 2)
                Unsupported("PDF RGB ICC range is unsupported.");
    }
    auto bytes = RetainedDecoded(profile, budget, 4ull * 1024 * 1024);
    Require(bytes->getSize() >= 128, "Retained PDF ICC profile is truncated.");
    auto data = bytes->getBuffer();
    size_t declared = static_cast<size_t>(data[0]) << 24 |
        static_cast<size_t>(data[1]) << 16 | static_cast<size_t>(data[2]) << 8 | data[3];
    Require(declared == bytes->getSize() &&
        std::equal(data + 16, data + 20, "RGB ") &&
        std::equal(data + 36, data + 40, "acsp"),
        "Retained PDF ICC profile identity is invalid.");
}

bool RetainedIccMapsWhite(QPDFObjectHandle profile)
{
    // ICCBased is safe to retain over a photo regardless of its transform. For
    // white ink over newly darkened paper, admit only a bounded RGB matrix/TRC
    // profile whose device white maps to PCS D50 white. LUT profiles can give
    // arbitrary output for an RGB value of 255,255,255.
    auto bytes = profile.getStreamData(qpdf_dl_all);
    if (!bytes || bytes->getSize() < 132) return false;
    auto data = bytes->getBuffer();
    auto size = bytes->getSize();
    auto signature = [&](size_t offset) {
        return std::string(reinterpret_cast<char const*>(data + offset), 4);
    };
    auto word = [&](size_t offset) -> size_t {
        return static_cast<size_t>(data[offset]) << 24 |
            static_cast<size_t>(data[offset + 1]) << 16 |
            static_cast<size_t>(data[offset + 2]) << 8 | data[offset + 3];
    };
    if (signature(12) != "mntr" || signature(20) != "XYZ ") return false;
    size_t count = word(128);
    if (count < 7 || count > 64 || 132 + count * 12 > size) return false;
    std::map<std::string, std::pair<size_t, size_t>> tags;
    static std::set<std::string> const allowed{"desc", "dscm", "cprt", "wtpt",
        "rXYZ", "gXYZ", "bXYZ", "rTRC", "gTRC", "bTRC", "chad", "chrm"};
    for (size_t i = 0; i < count; ++i) {
        size_t at = 132 + i * 12;
        auto name = signature(at);
        size_t offset = word(at + 4), length = word(at + 8);
        if (!allowed.count(name) || tags.count(name) || offset > size ||
            length > size - offset) return false;
        tags[name] = {offset, length};
    }
    for (auto const& name : {"wtpt", "rXYZ", "gXYZ", "bXYZ", "rTRC", "gTRC", "bTRC"})
        if (!tags.count(name)) return false;
    auto xyz = [&](char const* name) -> std::array<double, 3> {
        auto [offset, length] = tags.at(name);
        if (length < 20 || signature(offset) != "XYZ ") return {-1, -1, -1};
        std::array<double, 3> values{};
        for (size_t c = 0; c < 3; ++c)
            values[c] = static_cast<int32_t>(word(offset + 8 + c * 4)) / 65536.0;
        return values;
    };
    auto red = xyz("rXYZ"), green = xyz("gXYZ"), blue = xyz("bXYZ");
    auto white = xyz("wtpt");
    if (white[1] < .9 || white[1] > 1.1) return false;
    std::array<double, 3> const d50{.9642, 1.0, .8249};
    for (size_t c = 0; c < 3; ++c)
        if (std::abs(red[c] + green[c] + blue[c] - d50[c]) > .035) return false;
    for (auto const& name : {"rTRC", "gTRC", "bTRC"}) {
        auto [offset, length] = tags.at(name);
        if (length < 12) return false;
        if (signature(offset) == "curv") {
            size_t samples = word(offset + 8);
            if (samples > 4096 || 12 + samples * 2 > length) return false;
            if (samples == 1 && (data[offset + 12] == 0 && data[offset + 13] == 0))
                return false;
            if (samples > 1 &&
                (static_cast<unsigned>(data[offset + 10 + samples * 2]) << 8 |
                    data[offset + 11 + samples * 2]) < 65000)
                return false;
        } else if (signature(offset) == "para") {
            unsigned type = static_cast<unsigned>(data[offset + 8]) << 8 | data[offset + 9];
            static std::array<size_t, 5> const parameters{1, 3, 4, 5, 7};
            if (type > 4 || length < 12 + parameters[type] * 4) return false;
            auto fixed = [&](size_t index) {
                return static_cast<int32_t>(word(offset + 12 + index * 4)) / 65536.0;
            };
            double gamma = fixed(0), result = 0;
            if (gamma <= 0 || gamma > 16) return false;
            if (type == 0) result = 1;
            else {
                if (fixed(1) <= 0 || type >= 3 && fixed(4) > 1) return false;
                double base = fixed(1) + fixed(2);
                if (base < 0 || base > 16) return false;
                result = std::pow(base, gamma) + (type == 2 ? fixed(3) :
                    type == 4 ? fixed(5) : 0);
            }
            if (!std::isfinite(result) || std::abs(result - 1) > .035) return false;
        } else return false;
    }
    return true;
}


// A bounded monochrome ICC transform. Keep the embedded profile rather than
// treating its alternate DeviceGray space as the actual source color.
struct GrayIccCurve {
    std::vector<double> samples;
    double gamma = 1;
    double Convert(double value) const
    {
        double linear = value;
        if (samples.empty()) linear = std::pow(value, gamma);
        else {
            double position = value * (samples.size() - 1);
            size_t lower = std::min(samples.size() - 2, static_cast<size_t>(position));
            linear = samples[lower] + (samples[lower + 1] - samples[lower]) * (position - lower);
        }
        return linear <= .0031308 ? 12.92 * linear : 1.055 * std::pow(linear, 1 / 2.4) - .055;
    }
};

GrayIccCurve ReadGrayIcc(QPDFObjectHandle profile, RetainedImageBudget& budget)
{
    Require(profile.isStream(), "Invalid PDF gray ICC profile.");
    auto dict = profile.getDict(), filter = dict.getKey("/Filter");
    if (!RetainedKeys(dict, {"/Length", "/Filter", "/N", "/Alternate"}) ||
        !(filter.isNull() || filter.isNameAndEquals("/FlateDecode")) ||
        !dict.getKey("/N").isInteger() || dict.getKey("/N").getIntValue() != 1 ||
        !(dict.getKey("/Alternate").isNull() || dict.getKey("/Alternate").isNameAndEquals("/DeviceGray")))
        Unsupported("PDF gray ICC profile has unsupported features.");
    auto bytes = ReadBoundedPdfStream(profile, 4ull * 1024 * 1024,
        "Retained PDF stream exceeds its size limit.");
    AccountRetainedBytes(budget, bytes->getSize());
    auto data = bytes->getBuffer();
    size_t size = bytes->getSize();
    Require(size >= 132, "PDF gray ICC profile is truncated.");
    auto word = [&](size_t at) -> size_t {
        return static_cast<size_t>(data[at]) << 24 | static_cast<size_t>(data[at + 1]) << 16 |
            static_cast<size_t>(data[at + 2]) << 8 | data[at + 3];
    };
    auto signature = [&](size_t at) {
        return std::string(reinterpret_cast<char const*>(data + at), 4);
    };
    Require(word(0) == size && signature(36) == "acsp", "Invalid PDF gray ICC identity.");
    if ((data[8] != 2 && data[8] != 4) || signature(12) != "mntr" ||
        signature(16) != "GRAY" || signature(20) != "XYZ ")
        Unsupported("PDF gray ICC profile is outside the monitor curve policy.");
    size_t count = word(128);
    RequireLimit(count <= 64, "PDF gray ICC tag budget exceeded.");
    Require(132 + count * 12 <= size, "PDF gray ICC tags are truncated.");
    std::map<std::string, std::pair<size_t, size_t>> tags;
    for (size_t i = 0; i < count; ++i) {
        size_t at = 132 + i * 12, offset = word(at + 4), length = word(at + 8);
        auto name = signature(at);
        if ((name != "desc" && name != "dscm" && name != "cprt" && name != "wtpt" && name != "kTRC" && name != "bkpt") ||
            tags.count(name) || offset < 132 + count * 12 || offset > size || length > size - offset)
            Unsupported("PDF gray ICC transform tag is unsupported.");
        tags[name] = {offset, length};
    }
    if (!tags.count("wtpt") || !tags.count("kTRC")) Unsupported("PDF gray ICC curve or white point is missing.");
    if (tags.count("bkpt")) {
        auto [black, length] = tags.at("bkpt");
        // A zero PCS black point adds no compensation to the relative curve.
        // Nonzero points require a separately evaluated black-point policy.
        Require(length == 20 && signature(black) == "XYZ " &&
            std::all_of(data + black + 4, data + black + 20,
                [](unsigned char value) { return value == 0; }),
            "PDF gray ICC black point is unsupported.");
    }
    auto [white, whiteLength] = tags.at("wtpt");
    Require(whiteLength >= 20 && signature(white) == "XYZ ", "Invalid PDF gray ICC white point.");
    std::array<double, 3> d50{.9642, 1, .8249};
    std::array<double, 3> d65{.95047, 1, 1.08883};
    bool whiteD50 = true, whiteD65 = true;
    for (size_t c = 0; c < 3; ++c) {
        double value = static_cast<int32_t>(word(white + 8 + c * 4)) / 65536.0;
        whiteD50 &= std::abs(value - d50[c]) <= .0003;
        whiteD65 &= std::abs(value - d65[c]) <= .0003;
    }
    // Legacy v2 monitor profiles may record the actual D65 media white.
    // The gray TRC still describes the achromatic, relative PCS channel.
    if (!whiteD50 && !(data[8] == 2 && whiteD65))
        Unsupported("PDF gray ICC white point is unsupported.");
    auto [curve, length] = tags.at("kTRC");
    Require(length >= 12, "PDF gray ICC curve is truncated.");
    if (signature(curve) != "curv") Unsupported("PDF gray ICC curve type is unsupported.");
    size_t samples = word(curve + 8);
    RequireLimit(samples <= 4096, "PDF gray ICC curve budget exceeded.");
    Require(12 + 2 * samples <= length, "PDF gray ICC samples are truncated.");
    GrayIccCurve result;
    auto sample = [&](size_t i) {
        return (static_cast<unsigned>(data[curve + 12 + i * 2]) << 8) | data[curve + 13 + i * 2];
    };
    if (samples == 1) {
        result.gamma = sample(0) / 256.0;
        Require(result.gamma > 0 && result.gamma <= 16, "Invalid PDF gray ICC gamma.");
    } else if (samples > 1) {
        for (size_t i = 0; i < samples; ++i) {
            result.samples.push_back(sample(i) / 65535.0);
            Require(i == 0 || result.samples[i] >= result.samples[i - 1], "PDF gray ICC curve is not monotone.");
        }
        Require(result.samples.front() == 0 && result.samples.back() == 1,
            "PDF gray ICC curve endpoints are unsupported.");
    }
    return result;
}

// Admit the common calibrated RGB space with sRGB primaries and D65 white.
// Arbitrary matrices, black-point compensation, and other spaces stay closed.
std::array<double, 3> ReadCalRgb(QPDFObjectHandle space)
{
    Require(space.isArray() && space.getArrayNItems() == 2 &&
        space.getArrayItem(0).isNameAndEquals("/CalRGB"), "Invalid PDF calibrated RGB space.");
    auto dict = space.getArrayItem(1);
    Require(dict.isDictionary() && RetainedKeys(dict, {"/WhitePoint", "/Gamma", "/Matrix"}),
        "Unsupported PDF calibrated RGB features.");
    auto check = [&](char const* key, std::vector<double> const& expected) {
        auto values = dict.getKey(key);
        Require(values.isArray() && values.getArrayNItems() == expected.size(), "Invalid PDF calibrated RGB array.");
        for (size_t i = 0; i < expected.size(); ++i) {
            auto value = values.getArrayItem(static_cast<int>(i));
            Require(value.isNumber() && std::isfinite(value.getNumericValue()) &&
                std::abs(value.getNumericValue() - expected[i]) <= .0002, "Unsupported PDF calibrated RGB primaries.");
        }
    };
    check("/WhitePoint", {.9505, 1, 1.089});
    check("/Matrix", {.4124, .2126, .0193, .3576, .7152, .1192, .1805, .0722, .9505});
    std::array<double, 3> gamma{1, 1, 1};
    auto values = dict.getKey("/Gamma");
    if (!values.isNull()) {
        Require(values.isArray() && values.getArrayNItems() == 3, "Invalid PDF calibrated RGB gamma.");
        for (int i = 0; i < 3; ++i) {
            auto value = values.getArrayItem(i);
            Require(value.isNumber() && std::isfinite(value.getNumericValue()) &&
                value.getNumericValue() > 0 && value.getNumericValue() <= 16, "Invalid PDF calibrated RGB gamma.");
            gamma[i] = value.getNumericValue();
        }
    }
    return gamma;
}

struct RetainedColorSpace {
    size_t channels = 0;
    int indexedMaximum = -1;
    std::string palette;
};

RetainedColorSpace ValidateRetainedColorSpace(QPDFObjectHandle colorSpace,
    RetainedImageBudget& budget, bool allowGray)
{
    if (colorSpace.isNameAndEquals("/DeviceRGB")) return {3, -1, {}};
    if (allowGray && colorSpace.isNameAndEquals("/DeviceGray")) return {1, -1, {}};
    if (!colorSpace.isArray())
        Unsupported("Retained PDF image color space is unsupported.");
    if (colorSpace.getArrayNItems() == 2 && colorSpace.getArrayItem(0).isNameAndEquals("/ICCBased")) {
        auto profile = colorSpace.getArrayItem(1);
        if (profile.isStream() && profile.getDict().getKey("/N").isInteger() &&
            profile.getDict().getKey("/N").getIntValue() == 1) {
            ReadGrayIcc(profile, budget);
            return {1, -1, {}};
        }
        ValidateRetainedIcc(profile, budget);
        return {3, -1, {}};
    }
    if (colorSpace.getArrayNItems() == 2 && colorSpace.getArrayItem(0).isNameAndEquals("/CalRGB")) {
        ReadCalRgb(colorSpace);
        return {3, -1, {}};
    }
    if (colorSpace.getArrayNItems() != 4 ||
        !colorSpace.getArrayItem(0).isNameAndEquals("/Indexed"))
        Unsupported("Retained PDF image color space is unsupported.");
    auto base = colorSpace.getArrayItem(1);
    if (base.isArray() && base.getArrayNItems() == 2 && base.getArrayItem(0).isNameAndEquals("/CalRGB")) {
        ReadCalRgb(base);
    } else if (!base.isNameAndEquals("/DeviceRGB")) {
        if (!base.isArray() || base.getArrayNItems() != 2 ||
            !base.getArrayItem(0).isNameAndEquals("/ICCBased"))
            Unsupported("Retained PDF indexed base color space is unsupported.");
        ValidateRetainedIcc(base.getArrayItem(1), budget);
    }
    auto high = colorSpace.getArrayItem(2);
    Require(high.isInteger() && high.getIntValue() >= 0,
        "Invalid retained PDF indexed palette maximum.");
    RequireLimit(high.getIntValue() <= 255, "Retained PDF indexed palette exceeds the limit.");
    auto lookup = colorSpace.getArrayItem(3);
    std::string palette;
    if (lookup.isString()) {
        palette = lookup.getStringValue();
        AccountRetainedBytes(budget, palette.size());
    } else if (lookup.isStream()) {
        auto lookupFilter = lookup.getDict().getKey("/Filter");
        if (!RetainedKeys(lookup.getDict(), {"/Length", "/Filter"}) ||
            !(lookupFilter.isNull() || lookupFilter.isNameAndEquals("/FlateDecode")))
            Unsupported("Retained PDF indexed lookup has unsupported features.");
        auto bytes = RetainedDecoded(lookup, budget, 768);
        palette.assign(reinterpret_cast<char const*>(bytes->getBuffer()), bytes->getSize());
    } else Require(false, "Invalid retained PDF indexed lookup.");
    Require(palette.size() == static_cast<size_t>(high.getIntValue() + 1) * 3,
        "Retained PDF indexed palette length is invalid.");
    return {1, static_cast<int>(high.getIntValue()), palette};
}

void ValidateRetainedDecodeParms(QPDFObjectHandle image, size_t width, size_t height,
    size_t channels, RetainedImageBudget& budget)
{
    auto parameters = image.getDict().getKey("/DecodeParms");
    if (parameters.isNull()) return;
    if (!RetainedKeys(parameters, {"/BitsPerComponent", "/Colors", "/Columns", "/Predictor"}))
        Unsupported("Retained PDF image predictor has unsupported features.");
    auto matches = [&](char const* key, int expected) {
        auto value = parameters.getKey(key);
        return value.isNull() || value.isInteger() && value.getIntValue() == expected;
    };
    auto predictor = parameters.getKey("/Predictor");
    int mode = predictor.isNull() ? 1 : predictor.isInteger() &&
        predictor.getIntValue() >= 1 && predictor.getIntValue() <= 15 ?
        static_cast<int>(predictor.getIntValue()) : 0;
    if (!matches("/BitsPerComponent", 8) || !matches("/Colors", static_cast<int>(channels)) ||
        !matches("/Columns", static_cast<int>(width)) || !(mode == 1 || mode >= 10))
        Unsupported("Retained PDF image predictor is unsupported.");
    // QPDF decodes PNG row predictors before the exact sample-length check.
    // Omitted Colors/Columns mean one, not the enclosing image's dimensions.
    if (mode >= 10 && (parameters.getKey("/Colors").isNull() && channels != 1 ||
        parameters.getKey("/Columns").isNull() && width != 1))
        Unsupported("Retained PDF image predictor defaults do not match its image.");
    if (mode >= 10) {
        // Check row framing before QPDF removes predictors: its tolerant decoder
        // can otherwise accept an invalid row filter without rejecting the image.
        QPDF wrapper;
        wrapper.emptyPDF();
        auto encoded = wrapper.newStream(image.getRawStreamData());
        auto filter = image.getDict().getKey("/Filter");
        if (filter.isNameAndEquals("/FlateDecode"))
            encoded.getDict().replaceKey("/Filter", QPDFObjectHandle::newName("/FlateDecode"));
        else if (filter.isArray())
            encoded.getDict().replaceKey("/Filter",
                QPDFObjectHandle::parse("[/ASCII85Decode /FlateDecode]"));
        else Unsupported("Retained PDF PNG predictor requires a lossless compression filter.");
        auto rows = RetainedDecoded(encoded, budget, MaximumDecodedBytes + 16384);
        size_t stride = width * channels + 1;
        Require(rows->getSize() == stride * height, "Retained PDF predictor row length is invalid.");
        for (size_t offset = 0; offset < rows->getSize(); offset += stride)
            Require(rows->getBuffer()[offset] <= 4, "Retained PDF predictor row filter is invalid.");
    }
}

bool RetainedLosslessFilter(QPDFObjectHandle filter)
{
    return filter.isNull() || filter.isNameAndEquals("/FlateDecode") ||
        (filter.isArray() && filter.getArrayNItems() == 2 &&
         filter.getArrayItem(0).isNameAndEquals("/ASCII85Decode") &&
         filter.getArrayItem(1).isNameAndEquals("/FlateDecode"));
}

bool RetainedJpegFilter(QPDFObjectHandle filter)
{
    return filter.isNameAndEquals("/DCTDecode") ||
        (filter.isArray() && filter.getArrayNItems() == 2 &&
         filter.getArrayItem(0).isNameAndEquals("/ASCII85Decode") &&
         filter.getArrayItem(1).isNameAndEquals("/DCTDecode"));
}

std::shared_ptr<Buffer> RetainedJpegBytes(QPDFObjectHandle image)
{
    auto bytes = image.getRawStreamData();
    Require(bytes != nullptr, "Retained PDF JPEG stream is missing.");
    RequireLimit(bytes->getSize() <= MaximumSourceBytes, "Retained PDF JPEG exceeds the source limit.");
    if (image.getDict().getKey("/Filter").isArray()) {
        // Decode only the lossless wrapper in a separate document. JPEG bytes
        // are never decoded or re-encoded by the preservation comparison.
        QPDF wrapper;
        wrapper.emptyPDF();
        auto stream = wrapper.newStream(bytes);
        stream.getDict().replaceKey("/Filter", QPDFObjectHandle::newName("/ASCII85Decode"));
        bytes = stream.getStreamData(qpdf_dl_generalized);
        RequireLimit(bytes->getSize() <= MaximumSourceBytes,
            "Retained PDF JPEG exceeds the source limit.");
    }
    return bytes;
}

std::shared_ptr<Buffer> ValidateRetainedMask(QPDFObjectHandle mask, size_t width, size_t height,
    size_t sourceChannels, RetainedImageBudget& budget)
{
    Require(mask.isStream(), "Invalid retained PDF soft mask.");
    auto dict = mask.getDict();
    if (!RetainedKeys(dict, {"/Length", "/Type", "/Subtype", "/Name", "/Width", "/Height",
        "/BitsPerComponent", "/ColorSpace", "/Filter", "/DecodeParms", "/Matte",
        "/Interpolate", "/Decode"}) ||
        !RetainedLosslessFilter(dict.getKey("/Filter")) ||
        !dict.getKey("/ColorSpace").isNameAndEquals("/DeviceGray"))
        Unsupported("Retained PDF soft mask has unsupported features.");
    auto type = dict.getKey("/Type"), subtype = dict.getKey("/Subtype");
    Require((type.isNull() || type.isNameAndEquals("/XObject")) &&
        (subtype.isNull() || subtype.isNameAndEquals("/Image")) &&
        (dict.getKey("/Name").isNull() || dict.getKey("/Name").isName()) &&
        (dict.getKey("/Interpolate").isNull() || dict.getKey("/Interpolate").isBool()) &&
        dict.getKey("/BitsPerComponent").isInteger() &&
        dict.getKey("/BitsPerComponent").getIntValue() == 8 &&
        RetainedDimension(dict, "/Width") == width &&
        RetainedDimension(dict, "/Height") == height,
        "Retained PDF soft mask geometry is invalid.");
    ValidateRetainedDecodeParms(mask, width, height, 1, budget);
    auto decode = dict.getKey("/Decode");
    if (!decode.isNull() && !(decode.isArray() && decode.getArrayNItems() == 2 &&
        decode.getArrayItem(0).isNumber() && decode.getArrayItem(0).getNumericValue() == 0 &&
        decode.getArrayItem(1).isNumber() && decode.getArrayItem(1).getNumericValue() == 1))
        Unsupported("Retained PDF soft mask decode array is unsupported.");
    auto matte = dict.getKey("/Matte");
    if (!matte.isNull()) {
        if (!matte.isArray() || matte.getArrayNItems() != static_cast<int>(sourceChannels))
            Unsupported("Retained PDF soft mask matte is unsupported.");
        for (int i = 0; i < matte.getArrayNItems(); ++i) {
            auto value = matte.getArrayItem(i);
            if (!value.isNumber() || value.getNumericValue() != 0)
                Unsupported("Retained PDF soft mask matte is unsupported.");
        }
    }
    auto pixels = RetainedDecoded(mask, budget, MaximumImagePixels);
    Require(pixels->getSize() == width * height, "Retained PDF soft mask length is invalid.");
    return pixels;
}

std::array<size_t, 2> RetainedJpegGeometry(QPDFObjectHandle image, RetainedImageBudget& budget)
{
    auto bytes = RetainedJpegBytes(image);
    AccountRetainedBytes(budget, bytes->getSize());
    auto data = bytes->getBuffer();
    auto length = bytes->getSize();
    // Some PDF writers count trailing stream whitespace in the JPEG length.
    // Retain those bytes, but allow only a small whitespace suffix after EOI.
    size_t padding = 0;
    while (length > 2 && padding < 16 && (data[length - 1] == 0x20 ||
        data[length - 1] == 0x0a || data[length - 1] == 0x0d ||
        data[length - 1] == 0x09 || data[length - 1] == 0x0c)) { --length; ++padding; }
    Require(length >= 16 && data[0] == 0xff && data[1] == 0xd8 &&
        data[length - 2] == 0xff && data[length - 1] == 0xd9,
        "Retained PDF JPEG framing is invalid.");
    size_t at = 2, markers = 0;
    bool foundFrame = false, foundScan = false;
    std::array<size_t, 2> geometry{};
    while (at + 4 < length && ++markers <= 512) {
        Require(data[at++] == 0xff, "Retained PDF JPEG marker is invalid.");
        while (at < length && data[at] == 0xff) ++at;
        Require(at + 2 < length, "Retained PDF JPEG marker is truncated.");
        unsigned marker = data[at++];
        Require(marker != 0 && marker != 0xd8 && marker != 0xd9 &&
            marker != 0x01 && (marker < 0xd0 || marker > 0xd7),
            "Retained PDF JPEG marker is unsupported.");
        size_t segment = static_cast<size_t>(data[at]) << 8 | data[at + 1];
        Require(segment >= 2 && segment <= length - at,
            "Retained PDF JPEG segment length is invalid.");
        if (marker == 0xc0 || marker == 0xc1 || marker == 0xc2) {
            Require(!foundFrame && segment >= 11 && data[at + 2] == 8 &&
                (data[at + 7] == 1 || data[at + 7] == 3),
                "Retained PDF JPEG frame is invalid.");
            auto color = ValidateRetainedColorSpace(image.getDict().getKey("/ColorSpace"), budget, true);
            Require(data[at + 7] == color.channels && segment == 8 + 3 * color.channels,
                "Retained PDF JPEG channels do not match its color space.");
            geometry = {static_cast<size_t>(data[at + 5]) << 8 | data[at + 6],
                static_cast<size_t>(data[at + 3]) << 8 | data[at + 4]};
            Require(geometry[0] > 0 && geometry[1] > 0, "Retained PDF JPEG dimensions are invalid.");
            RequireLimit(geometry[0] <= 16384 && geometry[1] <= 16384 &&
                geometry[0] <= MaximumImagePixels / geometry[1], "Retained PDF JPEG pixel budget exceeded.");
            foundFrame = true;
        } else if (marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc)
            Unsupported("Retained PDF JPEG frame type is unsupported.");
        if (marker == 0xda) {
            Require(foundFrame && segment >= 6, "Retained PDF JPEG scan is invalid.");
            foundScan = true;
            break;
        }
        at += segment;
    }
    RequireLimit(markers <= 512, "Retained PDF JPEG marker budget exceeded.");
    Require(foundFrame && foundScan, "Retained PDF JPEG has no complete frame and scan.");
    return geometry;
}

void ValidateRetainedJpegData(QPDFObjectHandle image, size_t width, size_t height,
    RetainedImageBudget& budget)
{
    Require(RetainedJpegGeometry(image, budget) == std::array<size_t, 2>{width, height},
        "Retained PDF JPEG frame does not match its image geometry.");
}

void ValidateRetainedImage(QPDFObjectHandle image, RetainedImageBudget& budget)
{
    Require(image.isStream(), "Invalid retained PDF image.");
    auto dict = image.getDict();
    if (!RetainedKeys(dict, {"/Length", "/Type", "/Subtype", "/Name", "/Width", "/Height",
        "/BitsPerComponent", "/ColorSpace", "/Filter", "/DecodeParms", "/Decode",
        "/SMask", "/Interpolate", "/Intent"}))
        Unsupported("Retained PDF image has unsupported active features.");
    auto intent = dict.getKey("/Intent");
    if (!intent.isNull() && !intent.isNameAndEquals("/RelativeColorimetric"))
        Unsupported("Retained PDF image rendering intent is unsupported.");
    auto type = dict.getKey("/Type"), subtype = dict.getKey("/Subtype");
    Require((type.isNull() || type.isNameAndEquals("/XObject")) &&
        subtype.isNameAndEquals("/Image") &&
        (dict.getKey("/Name").isNull() || dict.getKey("/Name").isName()) &&
        (dict.getKey("/Interpolate").isNull() || dict.getKey("/Interpolate").isBool()) &&
        dict.getKey("/BitsPerComponent").isInteger() &&
        (dict.getKey("/BitsPerComponent").getIntValue() == 8 ||
         dict.getKey("/BitsPerComponent").getIntValue() == 1),
        "Retained PDF image attributes are invalid.");
    auto width = RetainedDimension(dict, "/Width");
    auto height = RetainedDimension(dict, "/Height");
    RequireLimit(width <= MaximumImagePixels / height,
        "Retained PDF image pixel count exceeds the limit.");
    AccountRetainedPixels(budget, width * height);
    auto filter = dict.getKey("/Filter");
    bool jpeg = RetainedJpegFilter(filter);
    bool packedGray = dict.getKey("/BitsPerComponent").getIntValue() == 1;
    if (packedGray && (jpeg || !dict.getKey("/ColorSpace").isNameAndEquals("/DeviceGray") ||
        !dict.getKey("/DecodeParms").isNull()))
        Unsupported("Packed PDF images require lossless DeviceGray without predictors.");
    if (!jpeg && !RetainedLosslessFilter(filter))
        Unsupported("Retained PDF image compression is unsupported.");
    auto color = ValidateRetainedColorSpace(dict.getKey("/ColorSpace"), budget, true);
    auto decode = dict.getKey("/Decode");
    if (!decode.isNull()) {
        // For an 8-bit Indexed source this maps the source byte to exactly the
        // same palette index. Other Decode arrays can invert or rescale colors.
        if ((!packedGray && color.indexedMaximum < 0) || !decode.isArray() || decode.getArrayNItems() != 2 ||
            !decode.getArrayItem(0).isNumber() ||
            decode.getArrayItem(0).getNumericValue() != 0 ||
            !decode.getArrayItem(1).isNumber() ||
            decode.getArrayItem(1).getNumericValue() != (packedGray ? 1 : 255))
            Unsupported("Retained PDF image decode array is unsupported.");
    }
    auto mask = dict.getKey("/SMask");
    if (jpeg) {
        if (!dict.getKey("/DecodeParms").isNull() || (color.channels != 1 && color.channels != 3) || color.indexedMaximum >= 0)
            Unsupported("Retained PDF JPEG color or transparency is unsupported.");
        ValidateRetainedJpegData(image, width, height, budget);
        if (!mask.isNull()) ValidateRetainedMask(mask, width, height, color.channels, budget);
        return;
    }
    ValidateRetainedDecodeParms(image, width, height, color.channels, budget);
    auto pixels = RetainedDecoded(image, budget, MaximumDecodedBytes);
    Require(pixels->getSize() == (packedGray ? ((width + 7) / 8) * height : width * height * color.channels),
        "Retained PDF image sample length is invalid.");
    if (color.indexedMaximum >= 0)
        for (size_t i = 0; i < pixels->getSize(); ++i)
            Require(pixels->getBuffer()[i] <= color.indexedMaximum,
                "Retained PDF indexed pixel exceeds its palette.");
    if (!mask.isNull()) ValidateRetainedMask(mask, width, height,
        color.indexedMaximum >= 0 ? 3 : color.channels, budget);
}

bool IsRetainedLightInk(QPDFObjectHandle image, RetainedImageBudget& budget)
{
    if (!image.isStream() || image.getDict().getKey("/SMask").isNull() ||
        !image.getDict().getKey("/Filter").isNameAndEquals("/FlateDecode"))
        return false;
    ValidateRetainedImage(image, budget);
    auto dict = image.getDict();
    if (dict.getKey("/BitsPerComponent").getIntValue() != 8) return false;
    auto width = RetainedDimension(dict, "/Width");
    auto height = RetainedDimension(dict, "/Height");
    auto mask = dict.getKey("/SMask");
    auto alpha = mask.getStreamData(qpdf_dl_all);
    auto pixels = image.getStreamData(qpdf_dl_all);
    auto color = dict.getKey("/ColorSpace");
    bool indexed = color.isArray() && color.getArrayItem(0).isNameAndEquals("/Indexed");
    bool gray = color.isNameAndEquals("/DeviceGray");
    size_t channels = indexed || gray ? 1 : 3;
    std::string palette;
    if (indexed) {
        auto lookup = color.getArrayItem(3);
        if (lookup.isString()) palette = lookup.getStringValue();
        else {
            auto bytes = lookup.getStreamData(qpdf_dl_all);
            palette.assign(reinterpret_cast<char const*>(bytes->getBuffer()), bytes->getSize());
        }
    }
    bool matte = !mask.getDict().getKey("/Matte").isNull();
    if (indexed) {
        auto base = color.getArrayItem(1);
        if (base.isArray() && (!base.getArrayItem(0).isNameAndEquals("/ICCBased") ||
            !RetainedIccMapsWhite(base.getArrayItem(1)))) return false;
    } else if (color.isArray()) return false;
    size_t visible = 0;
    for (size_t i = 0; i < width * height; ++i) {
        unsigned opacity = alpha->getBuffer()[i];
        if (opacity == 0) continue;
        ++visible;
        unsigned minimum = 255;
        bool paletteInk = !matte && color.isNameAndEquals("/DeviceRGB");
        constexpr std::array<unsigned, 3> targetInk{244, 235, 216};
        for (size_t c = 0; c < 3; ++c) {
            unsigned sample = indexed ? static_cast<unsigned char>(palette[
                static_cast<size_t>(pixels->getBuffer()[i]) * 3 + c]) :
                pixels->getBuffer()[i * channels + (gray ? 0 : c)];
            if (matte) sample = std::min(255u, (sample * 255u + opacity / 2u) / opacity);
            minimum = std::min(minimum, sample);
            paletteInk &= sample == targetInk[c];
        }
        // Antialiasing can encode a dark sample at alpha 1-3 even when the
        // composited mark is indistinguishable from white mask coverage. The
        // retained result may differ from ideal white ink by at most three
        // channel levels there; stronger dark marks remain unsupported.
        if (!paletteInk && minimum < 240 && opacity * (255 - minimum) > 3 * 255)
            return false;
    }
    return visible >= width * height / 100 && visible > 0;
}

struct RetainedRgbImage {
    size_t width = 0;
    size_t height = 0;
    std::string rgb; // Three 8-bit sRGB channels per pixel, unpremultiplied for classification.
    std::string alpha; // Empty when there is no soft mask; never changes the source mask.
    bool blackMatte = false;
};

struct RetainedIccCurve {
    std::vector<double> samples;
    double gamma = 1;
    int parametricType = -1;
    std::array<double, 7> parameters{};

    double Evaluate(double x) const
    {
        double result = 0;
        auto const& p = parameters;
        if (parametricType == 0) result = std::pow(x, p[0]);
        else if (parametricType == 1 || parametricType == 2)
            result = x >= -p[2] / p[1] ? std::pow(p[1] * x + p[2], p[0]) +
                (parametricType == 2 ? p[3] : 0) : (parametricType == 2 ? p[3] : 0);
        else if (parametricType >= 3)
            result = x >= p[4] ? std::pow(p[1] * x + p[2], p[0]) +
                (parametricType == 4 ? p[5] : 0) : p[3] * x + (parametricType == 4 ? p[6] : 0);
        else if (samples.empty()) result = std::pow(x, gamma);
        else {
            double position = x * (samples.size() - 1);
            size_t lower = std::min(samples.size() - 2, static_cast<size_t>(position));
            result = samples[lower] + (samples[lower + 1] - samples[lower]) * (position - lower);
        }
        if (!std::isfinite(result) || result < -.001 || result > 1.001)
            Unsupported("PDF ICC paint transfer curve is invalid at its operand.");
        return Clamp(result);
    }
};

struct RetainedIccTransform {
    std::array<std::array<double, 3>, 3> xyz{};
    std::array<std::array<double, 256>, 3> linear{};
    std::array<RetainedIccCurve, 3> curves;

    Color Convert(Color sample) const
    {
        Color input{};
        for (size_t c = 0; c < 3; ++c) input[c] = curves[c].Evaluate(Clamp(sample[c]));
        return ConvertLinear(input);
    }

    Color ConvertLinear(Color input) const
    {
        std::array<double, 3> connection{};
        for (size_t row = 0; row < 3; ++row)
            for (size_t c = 0; c < 3; ++c) connection[row] += xyz[c][row] * input[c];
        // Inverse of the D50-adapted sRGB colorant matrix. ICC matrix profiles
        // express their colorants in the D50 profile connection space.
        std::array<double, 3> result{
            3.1338561 * connection[0] - 1.6168667 * connection[1] - .4906146 * connection[2],
            -.9787684 * connection[0] + 1.9161415 * connection[1] + .0334540 * connection[2],
            .0719453 * connection[0] - .2289914 * connection[1] + 1.4052427 * connection[2],
        };
        Color output{};
        for (size_t c = 0; c < 3; ++c) {
            double value = std::clamp(result[c], 0.0, 1.0);
            value = value <= .0031308 ? 12.92 * value : 1.055 * std::pow(value, 1 / 2.4) - .055;
            output[c] = Clamp(value);
        }
        return output;
    }

    std::array<unsigned char, 3> Convert(std::array<unsigned char, 3> sample) const
    {
        Color input{};
        for (size_t c = 0; c < 3; ++c) input[c] = linear[c][sample[c]];
        auto color = ConvertLinear(input);
        std::array<unsigned char, 3> output{};
        for (size_t c = 0; c < 3; ++c)
            output[c] = static_cast<unsigned char>(std::lround(color[c] * 255));
        return output;
    }
};

RetainedIccTransform ReadRetainedIccTransform(QPDFObjectHandle profile,
    RetainedImageBudget& budget)
{
    ValidateRetainedIcc(profile, budget);
    auto bytes = profile.getStreamData(qpdf_dl_all);
    auto data = bytes->getBuffer();
    auto size = bytes->getSize();
    auto signature = [&](size_t offset) {
        return std::string(reinterpret_cast<char const*>(data + offset), 4);
    };
    auto word = [&](size_t offset) -> size_t {
        return static_cast<size_t>(data[offset]) << 24 |
            static_cast<size_t>(data[offset + 1]) << 16 |
            static_cast<size_t>(data[offset + 2]) << 8 | data[offset + 3];
    };
    if (size < 132 || (data[8] != 2 && data[8] != 4) || signature(12) != "mntr" || signature(20) != "XYZ ")
        Unsupported("PDF ICC image profile is not a matrix monitor profile.");
    size_t count = word(128);
    RequireLimit(count <= 64, "PDF ICC image profile has too many tags.");
    Require(132 + count * 12 <= size, "PDF ICC image profile tags are truncated.");
    std::map<std::string, std::pair<size_t, size_t>> tags;
    static std::set<std::string> const allowed{"desc", "dscm", "cprt", "wtpt",
        "rXYZ", "gXYZ", "bXYZ", "rTRC", "gTRC", "bTRC", "chad", "chrm",
        // These observed display-profile tags carry metadata or video-card
        // calibration, not an ICC device-to-PCS transform.
        "aarg", "aabg", "aagg", "vcgt", "ndin", "mmod",
        // Descriptive device/viewing/measurement tags do not replace the
        // selected matrix/TRC device-to-PCS transform.
        "dmnd", "dmdd", "vued", "view", "lumi", "meas", "tech", "bkpt"};
    for (size_t i = 0; i < count; ++i) {
        size_t at = 132 + i * 12;
        auto name = signature(at);
        size_t offset = word(at + 4), length = word(at + 8);
        if (!allowed.count(name) || tags.count(name) || offset < 132 + count * 12 || offset > size ||
            length > size - offset)
            Unsupported("PDF ICC image profile has an unsupported transform tag.");
        tags[name] = {offset, length};
    }
    for (auto const& name : {"wtpt", "rXYZ", "gXYZ", "bXYZ", "rTRC", "gTRC", "bTRC"})
        if (!tags.count(name)) Unsupported("PDF ICC image profile lacks a matrix/TRC tag.");
    if (tags.count("bkpt")) {
        auto [offset, length] = tags.at("bkpt");
        Require(length == 20 && signature(offset) == "XYZ " &&
            std::all_of(data + offset + 4, data + offset + 20,
                [](unsigned char value) { return value == 0; }),
            "PDF RGB ICC black point is unsupported.");
    }
    auto xyz = [&](char const* name) -> std::array<double, 3> {
        auto [offset, length] = tags.at(name);
        if (length < 20 || signature(offset) != "XYZ ")
            Unsupported("PDF ICC image profile has an invalid XYZ tag.");
        std::array<double, 3> values{};
        for (size_t c = 0; c < 3; ++c)
            values[c] = static_cast<int32_t>(word(offset + 8 + c * 4)) / 65536.0;
        return values;
    };
    RetainedIccTransform transform;
    transform.xyz = {xyz("rXYZ"), xyz("gXYZ"), xyz("bXYZ")};
    for (auto const& colorant : transform.xyz)
        for (double value : colorant)
            if (std::abs(value) > 4)
                Unsupported("PDF ICC image profile matrix exceeds its bounds.");
    auto const& m = transform.xyz;
    double matrixDeterminant = m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) -
        m[1][0] * (m[0][1] * m[2][2] - m[0][2] * m[2][1]) +
        m[2][0] * (m[0][1] * m[1][2] - m[0][2] * m[1][1]);
    if (std::abs(matrixDeterminant) < .001 || std::abs(matrixDeterminant) > 10)
        Unsupported("PDF ICC image profile matrix is degenerate.");
    std::array<double, 3> const d50{.9642, 1.0, .8249};
    std::array<double, 3> const d65{.95047, 1.0, 1.08883};
    auto white = xyz("wtpt");
    bool rawD50 = true, rawD65 = true;
    for (size_t c = 0; c < 3; ++c) {
        rawD50 &= std::abs(white[c] - d50[c]) <= .035;
        rawD65 &= std::abs(white[c] - d65[c]) <= .035;
        double sum = transform.xyz[0][c] + transform.xyz[1][c] + transform.xyz[2][c];
        if (std::abs(sum - d50[c]) > .035)
            Unsupported("PDF ICC image profile white point is unsupported.");
    }
    bool legacySrgb = !rawD50 && !tags.count("chad");
    if (legacySrgb) {
        // Older v2 sRGB profiles can omit chad while recording D65 media white
        // and already-adapted D50 colorants (ICC's sRGB profile guidance).
        // Prove that specific encoding, including every TRC sample below;
        // an arbitrary no-chad display profile is still ambiguous.
        if (data[8] != 2) Unsupported("PDF legacy sRGB profile version is unsupported.");
        static std::array<std::array<double, 3>, 3> const srgb{
            std::array<double, 3>{.4360747, .2225045, .0139322},
            std::array<double, 3>{.3850649, .7168786, .0971045},
            std::array<double, 3>{.1430804, .0606169, .7141733},
        };
        for (size_t row = 0; row < 3; ++row) {
            double illuminant = static_cast<int32_t>(word(68 + row * 4)) / 65536.0;
            if (std::abs(white[row] - d65[row]) > .0003 ||
                std::abs(illuminant - d50[row]) > .00003)
                Unsupported("PDF legacy sRGB white point is unsupported.");
            for (size_t c = 0; c < 3; ++c)
                if (std::abs(transform.xyz[c][row] - srgb[c][row]) > .0002)
                    Unsupported("PDF legacy sRGB colorants are unsupported.");
        }
    }
    if (!rawD50 && !legacySrgb) {
        // A few legacy v2 display profiles record their unadapted D65 media
        // white despite storing D50-adapted PCS colorants. ICC v4 requires
        // the media-white tag itself to be D50, so never extend that case.
        if (data[8] != 2 || !rawD65 || !tags.count("chad"))
            Unsupported("PDF ICC image profile white point is unsupported.");
        auto [offset, length] = tags.at("chad");
        if (length != 44 || signature(offset) != "sf32" ||
            std::any_of(data + offset + 4, data + offset + 8,
                [](unsigned char byte) { return byte != 0; }))
            Unsupported("PDF ICC image profile chromatic adaptation is unsupported.");
        std::array<std::array<double, 3>, 3> adaptation{};
        for (size_t row = 0; row < 3; ++row) {
            for (size_t column = 0; column < 3; ++column) {
                double value = static_cast<int32_t>(word(offset + 8 +
                    4 * (row * 3 + column))) / 65536.0;
                if (!std::isfinite(value) || std::abs(value) > 4)
                    Unsupported("PDF ICC image profile chromatic adaptation is unsupported.");
                adaptation[row][column] = value;
            }
        }
        double determinant =
            adaptation[0][0] * (adaptation[1][1] * adaptation[2][2] -
                adaptation[1][2] * adaptation[2][1]) -
            adaptation[0][1] * (adaptation[1][0] * adaptation[2][2] -
                adaptation[1][2] * adaptation[2][0]) +
            adaptation[0][2] * (adaptation[1][0] * adaptation[2][1] -
                adaptation[1][1] * adaptation[2][0]);
        if (std::abs(determinant) < .1 || std::abs(determinant) > 10)
            Unsupported("PDF ICC image profile chromatic adaptation is unsupported.");
        for (size_t row = 0; row < 3; ++row) {
            double adapted = 0;
            for (size_t column = 0; column < 3; ++column)
                adapted += adaptation[row][column] * white[column];
            if (std::abs(adapted - d50[row]) > .035)
                Unsupported("PDF ICC image profile chromatic adaptation is unsupported.");
        }
        // The profile colorants above already live in D50 PCS coordinates.
        // chad only establishes that the recorded D65 media white is valid;
        // applying it again would distort every converted pixel.
    }
    for (size_t c = 0; c < 3; ++c) {
        auto name = std::array<char const*, 3>{"rTRC", "gTRC", "bTRC"}[c];
        auto [offset, length] = tags.at(name);
        if (length < 12) Unsupported("PDF ICC image profile TRC is truncated.");
        if (signature(offset) == "curv") {
            size_t samples = word(offset + 8);
            if (samples > 4096 || 12 + samples * 2 > length)
                Unsupported("PDF ICC image profile curve is unsupported.");
            if (legacySrgb && samples < 256)
                Unsupported("PDF legacy sRGB requires a sampled standard transfer curve.");
            std::vector<double> table;
            for (size_t i = 0; i < samples; ++i) {
                unsigned value = static_cast<unsigned>(data[offset + 12 + i * 2]) << 8 |
                    data[offset + 13 + i * 2];
                table.push_back(value / 65535.0);
                if (i && table[i] < table[i - 1])
                    Unsupported("PDF ICC image profile curve is not monotone.");
                if (legacySrgb) {
                    double x = static_cast<double>(i) / (samples - 1);
                    double expected = x <= .04045 ? x / 12.92 : std::pow((x + .055) / 1.055, 2.4);
                    if (std::abs(table[i] - expected) > .00002)
                        Unsupported("PDF legacy sRGB transfer curve is unsupported.");
                }
            }
            if (samples > 1 && (table.front() > .035 || table.back() < .965))
                Unsupported("PDF ICC image profile curve endpoints are unsupported.");
            double gamma = samples == 1 ?
                (static_cast<unsigned>(data[offset + 12]) << 8 | data[offset + 13]) / 256.0 : 1;
            if (gamma <= 0 || gamma > 16)
                Unsupported("PDF ICC image profile gamma is unsupported.");
            for (size_t value = 0; value < 256; ++value) {
                double x = value / 255.0;
                if (samples == 0) transform.linear[c][value] = x;
                else if (samples == 1) transform.linear[c][value] = std::pow(x, gamma);
                else {
                    double position = x * (samples - 1);
                    size_t lower = std::min(samples - 2, static_cast<size_t>(position));
                    transform.linear[c][value] = table[lower] +
                        (table[lower + 1] - table[lower]) * (position - lower);
                }
            }
            transform.curves[c].gamma = gamma;
            if (samples > 1) transform.curves[c].samples = std::move(table);
        } else if (signature(offset) == "para") {
            if (legacySrgb)
                Unsupported("PDF legacy sRGB requires a sampled standard transfer curve.");
            unsigned type = static_cast<unsigned>(data[offset + 8]) << 8 | data[offset + 9];
            static std::array<size_t, 5> const parameters{1, 3, 4, 5, 7};
            if (type > 4 || length < 12 + parameters[type] * 4)
                Unsupported("PDF ICC image profile parametric TRC is unsupported.");
            std::array<double, 7> p{};
            for (size_t i = 0; i < parameters[type]; ++i)
                p[i] = static_cast<int32_t>(word(offset + 12 + i * 4)) / 65536.0;
            if (p[0] <= 0 || p[0] > 16 || type > 0 && p[1] <= 0)
                Unsupported("PDF ICC image profile parametric TRC is invalid.");
            transform.curves[c].parametricType = static_cast<int>(type);
            transform.curves[c].parameters = p;
            for (size_t value = 0; value < 256; ++value) {
                double x = value / 255.0, y = 0;
                if (type == 0) y = std::pow(x, p[0]);
                else if (type == 1 || type == 2)
                    y = x >= -p[2] / p[1] ? std::pow(p[1] * x + p[2], p[0]) +
                        (type == 2 ? p[3] : 0) : (type == 2 ? p[3] : 0);
                else
                    y = x >= p[4] ? std::pow(p[1] * x + p[2], p[0]) +
                        (type == 4 ? p[5] : 0) : p[3] * x + (type == 4 ? p[6] : 0);
                if (!std::isfinite(y) || y < -.001 || y > 1.001 ||
                    value && y + .0001 < transform.linear[c][value - 1])
                    Unsupported("PDF ICC image profile parametric TRC is invalid.");
                transform.linear[c][value] = std::clamp(y, 0.0, 1.0);
            }
            if (transform.linear[c][0] > .035 || transform.linear[c][255] < .965)
                Unsupported("PDF ICC image profile parametric TRC endpoints are unsupported.");
        } else Unsupported("PDF ICC image profile TRC format is unsupported.");
    }
    return transform;
}

RetainedRgbImage ReadRetainedRgb(QPDFObjectHandle image, RetainedImageBudget& budget)
{
    ValidateRetainedImage(image, budget);
    auto dict = image.getDict();
    if (!RetainedLosslessFilter(dict.getKey("/Filter")) && !RetainedJpegFilter(dict.getKey("/Filter")))
        Unsupported("PDF image RGB expansion requires supported compression.");
    RetainedRgbImage result;
    result.width = RetainedDimension(dict, "/Width");
    result.height = RetainedDimension(dict, "/Height");
    auto space = dict.getKey("/ColorSpace");
    bool indexed = space.isArray() && space.getArrayItem(0).isNameAndEquals("/Indexed");
    bool gray = space.isNameAndEquals("/DeviceGray");
    bool packedGray = dict.getKey("/BitsPerComponent").getIntValue() == 1;
    auto baseSpace = indexed ? space.getArrayItem(1) : space;
    bool calibrated = baseSpace.isArray() && baseSpace.getArrayItem(0).isNameAndEquals("/CalRGB");
    QPDFObjectHandle profile = QPDFObjectHandle::newNull();
    if (indexed) {
        auto base = space.getArrayItem(1);
        if (base.isArray() && !calibrated) profile = base.getArrayItem(1);
    } else if (space.isArray() && !calibrated) profile = space.getArrayItem(1);
    std::string palette;
    if (indexed) {
        auto lookup = space.getArrayItem(3);
        if (lookup.isString()) palette = lookup.getStringValue();
        else {
            auto bytes = lookup.getStreamData(qpdf_dl_all);
            palette.assign(reinterpret_cast<char const*>(bytes->getBuffer()), bytes->getSize());
        }
    }
    RetainedIccTransform transform;
    bool colorManaged = !profile.isNull();
    bool grayProfile = colorManaged && profile.getDict().getKey("/N").getIntValue() == 1;
    GrayIccCurve grayCurve;
    std::array<double, 3> gamma{};
    if (grayProfile) { grayCurve = ReadGrayIcc(profile, budget); gray = true; }
    else if (colorManaged) transform = ReadRetainedIccTransform(profile, budget);
    if (calibrated) gamma = ReadCalRgb(baseSpace);
    auto mask = dict.getKey("/SMask");
    if (!mask.isNull()) {
        auto alpha = mask.getStreamData(qpdf_dl_all);
        result.alpha.assign(reinterpret_cast<char const*>(alpha->getBuffer()), alpha->getSize());
        result.blackMatte = !mask.getDict().getKey("/Matte").isNull();
    }
    auto pixels = RetainedDecoded(image, budget, MaximumDecodedBytes);
    Require(pixels->getSize() == (packedGray ? ((result.width + 7) / 8) * result.height :
        result.width * result.height * (indexed || gray ? 1 : 3)),
        "Expanded PDF image sample length is invalid.");
    RequireLimit(result.width * result.height <= MaximumDecodedBytes / 3,
        "PDF image RGB expansion exceeds its decoded-byte limit.");
    AccountRetainedBytes(budget, result.width * result.height * 3);
    result.rgb.reserve(result.width * result.height * 3);
    for (size_t i = 0; i < result.width * result.height; ++i) {
        std::array<unsigned char, 3> sample{};
        for (size_t c = 0; c < 3; ++c) {
            if (indexed) sample[c] = static_cast<unsigned char>(palette[
                static_cast<size_t>(pixels->getBuffer()[i]) * 3 + c]);
            else if (packedGray) sample[c] =
                (pixels->getBuffer()[(i / result.width) * ((result.width + 7) / 8) +
                    (i % result.width) / 8] >> (7 - (i % result.width) % 8) & 1) ? 255 : 0;
            else sample[c] = pixels->getBuffer()[i * (gray ? 1 : 3) + (gray ? 0 : c)];
        }
        if (result.blackMatte) {
            unsigned opacity = static_cast<unsigned char>(result.alpha[i]);
            for (auto& component : sample)
                component = opacity ? static_cast<unsigned char>(std::min(255u,
                    (static_cast<unsigned>(component) * 255u + opacity / 2u) / opacity)) : 0;
        }
        if (grayProfile) {
            auto value = static_cast<unsigned char>(std::lround(Clamp(grayCurve.Convert(sample[0] / 255.0)) * 255));
            sample = {value, value, value};
        } else if (colorManaged) sample = transform.Convert(sample);
        else if (calibrated) for (size_t c = 0; c < 3; ++c) {
            GrayIccCurve curve; curve.gamma = gamma[c];
            sample[c] = static_cast<unsigned char>(std::lround(Clamp(curve.Convert(sample[c] / 255.0)) * 255));
        }
        for (auto component : sample) result.rgb.push_back(static_cast<char>(component));
    }
    return result;
}

bool IsOpaqueRetainedJpeg(QPDFObjectHandle image, RetainedImageBudget& budget)
{
    if (!image.isStream() || !RetainedJpegFilter(image.getDict().getKey("/Filter")))
        return false;
    ValidateRetainedImage(image, budget);
    auto mask = image.getDict().getKey("/SMask");
    if (!mask.isNull()) {
        auto alpha = mask.getStreamData(qpdf_dl_all);
        if (!std::all_of(alpha->getBuffer(), alpha->getBuffer() + alpha->getSize(),
            [](unsigned char value) { return value == 255; })) return false;
    }
    // A lossless wrapper does not determine whether a JPEG is a photograph.
    // Give ordinary RGB diagrams the same existing appearance classification
    // before admitting a photograph scene or preserving its paint colors.
    if (image.getDict().getKey("/ColorSpace").isNameAndEquals("/DeviceRGB") ||
        image.getDict().getKey("/ColorSpace").isNameAndEquals("/DeviceGray")) {
        auto expanded = ReadRetainedRgb(image, budget);
        std::string recolored;
        if (IsLineArt(expanded.rgb, 3, {}) || pdf_dark_diagram::TryRecolor(expanded.rgb, 3,
            static_cast<int>(expanded.width), static_cast<int>(expanded.height), {}, recolored)) return false;
    }
    return true;
}

enum class SoftPanelPalette { None, Light, Dark };

SoftPanelPalette DetectSoftNeutralPanel(std::string const& pixels, size_t channels,
    size_t width, size_t height, std::string const& mask)
{
    // The only admitted motif is an empty, softly masked, near-neutral frame.
    // Its labels must be separately painted and checked against the new panel.
    if ((channels != 1 && channels != 3) || width == 0 || height == 0 ||
        width > 4ull * 1024 * 1024 / height) return SoftPanelPalette::None;
    size_t count = width * height;
    if (pixels.size() != count * channels || mask.size() != count)
        return SoftPanelPalette::None;

    auto alpha = [&](size_t pixel) {
        return static_cast<unsigned char>(mask[pixel]);
    };
    auto nearContour = [&](size_t pixel) {
        size_t row = pixel / width, column = pixel % width;
        size_t top = row > 2 ? row - 2 : 0;
        size_t left = column > 2 ? column - 2 : 0;
        for (size_t y = top; y <= std::min(height - 1, row + 2); ++y)
            for (size_t x = left; x <= std::min(width - 1, column + 2); ++x)
                if (alpha(y * width + x) < 250) return true;
        return false;
    };
    auto classify = [&](bool darkPalette) {
        size_t visible = 0, high = 0, bright = 0, dark = 0, fringe = 0;
        std::array<size_t, 16> bins{};
        for (size_t pixel = 0; pixel < count; ++pixel) {
            unsigned opacity = alpha(pixel);
            if (!opacity) continue;
            ++visible;
            std::array<unsigned, 3> rgb{};
            for (size_t c = 0; c < 3; ++c)
                rgb[c] = static_cast<unsigned char>(pixels[pixel * channels +
                    (channels == 1 ? 0 : c)]);
            unsigned gray = 0;
            if (darkPalette) {
                // Output gray is an exact projection on the existing stable
                // Paper-to-Ink ramp. An arbitrary dark photo cannot match it.
                double numerator = 0, denominator = 0;
                for (size_t c = 0; c < 3; ++c) {
                    double span = (Paper[c] - Ink[c]) * 255;
                    numerator += (static_cast<double>(rgb[c]) - Ink[c] * 255) * span;
                    denominator += span * span;
                }
                if (denominator <= 0) return false;
                double coordinate = numerator / denominator;
                if (coordinate < -1.0 / 255 || coordinate > 1 + 1.0 / 255)
                    return false;
                gray = static_cast<unsigned>(std::lround(
                    std::clamp(coordinate, 0.0, 1.0) * 255));
                for (size_t c = 0; c < 3; ++c) {
                    double mapped = Paper[c] * gray + Ink[c] * (255 - gray);
                    if (std::abs(static_cast<double>(rgb[c]) - std::lround(mapped)) > 1)
                        return false;
                }
            } else {
                unsigned minimum = std::min({rgb[0], rgb[1], rgb[2]});
                unsigned maximum = std::max({rgb[0], rgb[1], rgb[2]});
                if (maximum - minimum > 1) {
                    // A handful of edge antialiasing pixels in the observed
                    // gray-panel corpus carry small chroma. Interior accents
                    // and larger colored regions are never admitted.
                    if (maximum - minimum > 20 || !nearContour(pixel)) return false;
                    ++fringe;
                }
                gray = (rgb[0] + rgb[1] + rgb[2]) / 3;
            }
            if (opacity < 250) continue;
            ++high;
            bright += gray >= 174;
            bool borderInk = gray < 115;
            dark += borderInk;
            if (gray < 174 && !nearContour(pixel)) return false;
            ++bins[gray / 16];
        }
        if (!visible || !high || fringe * 1000 > visible ||
            high * 2 < count || bright * 100 < high * 95 ||
            dark < 64 || dark * 200 < high) return false;
        std::partial_sort(bins.begin(), bins.begin() + 8, bins.end(),
            std::greater<>{});
        size_t common = 0;
        for (size_t i = 0; i < 8; ++i) common += bins[i];
        return common * 100 >= high * 98;
    };
    if (classify(false)) return SoftPanelPalette::Light;
    if (channels == 3 && classify(true)) return SoftPanelPalette::Dark;
    return SoftPanelPalette::None;
}

bool CanRetainOnDarkPaper(QPDFObjectHandle image, Color sourcePaper,
    Color targetPaper, RetainedImageBudget& budget)
{
    if (!image.isStream() || image.getDict().getKey("/SMask").isNull() ||
        !image.getDict().getKey("/Filter").isNameAndEquals("/FlateDecode"))
        return false;
    auto darkFlat = [](Color color) {
        return std::all_of(color.begin(), color.end(), [](double channel) {
            return std::isfinite(channel) && channel >= 0 && channel <= .30;
        }) && Luminance(color) <= .20;
    };
    if (!darkFlat(sourcePaper) || !darkFlat(targetPaper)) return false;
    auto dict = image.getDict();
    auto width = RetainedDimension(dict, "/Width");
    auto height = RetainedDimension(dict, "/Height");
    // The proof compares every horizontal and vertical sample edge. Limit
    // this separate pass even when the general decoded-image budget is larger.
    if (width > 4ull * 1024 * 1024 / height) return false;
    auto expanded = ReadRetainedRgb(image, budget);
    if (expanded.alpha.empty() || expanded.blackMatte) return false;
    // Every placement has already proved a flat, unoccluded paper backdrop.
    // If that backdrop is unchanged, retaining these validated samples and
    // alpha preserves appearance exactly, including previously recolored
    // diagrams. Do not require them to resemble their former white panels.
    // The caller still rejects text painted over the retained image.
    if (SameColor(sourcePaper, targetPaper)) return true;
    // A close change between two proved dark, flat backdrops can preserve
    // already-dark colored diagrams without treating them as white panels.
    // Keep this separate from the established bright neutral-panel proof.
    bool closeDarkBackdrop = true;
    for (size_t c = 0; c < 3; ++c)
        closeDarkBackdrop &= std::abs(sourcePaper[c] - targetPaper[c]) * 255 <= 12;
    size_t count = expanded.width * expanded.height;
    size_t high = 0, bright = 0, dark = 0, neutral = 0;
    std::array<size_t, 4096> bins{};
    auto channel = [&](size_t pixel, size_t c) {
        return static_cast<unsigned char>(expanded.rgb[pixel * 3 + c]);
    };
    auto alpha = [&](size_t pixel) {
        return static_cast<unsigned char>(expanded.alpha[pixel]);
    };
    for (size_t pixel = 0; pixel < count; ++pixel) {
        for (size_t c = 0; c < 3; ++c) {
            double change = (255 - alpha(pixel)) *
                std::abs(sourcePaper[c] - targetPaper[c]);
            if (closeDarkBackdrop && change > 12.001) return false;
        }
        if (alpha(pixel) < 250) continue;
        ++high;
        if (closeDarkBackdrop) continue;
        unsigned r = channel(pixel, 0), g = channel(pixel, 1), b = channel(pixel, 2);
        unsigned minimum = std::min({r, g, b}), maximum = std::max({r, g, b});
        neutral += maximum - minimum <= 20;
        double light = (.2126 * r + .7152 * g + .0722 * b) / 255;
        bright += light >= .68;
        dark += light < .45;
        ++bins[((r / 16) << 8) | ((g / 16) << 4) | b / 16];
    }
    if (high * 5 < count) return false;
    if (!closeDarkBackdrop && (bright * 5 < high * 4 ||
        dark < 64 || dark * 200 < high || neutral * 50 < high * 49))
        return false;
    if (!closeDarkBackdrop) {
        std::partial_sort(bins.begin(), bins.begin() + 8, bins.end(), std::greater<>{});
        size_t topEight = 0;
        for (size_t i = 0; i < 8; ++i) topEight += bins[i];
        if (topEight * 20 < high * 19) return false;
    }
    auto compositedEdge = [&](size_t first, size_t second, Color paper) {
        double maximum = 0;
        for (size_t c = 0; c < 3; ++c) {
            double a = (alpha(first) * channel(first, c) +
                (255 - alpha(first)) * paper[c] * 255) / 255;
            double b = (alpha(second) * channel(second, c) +
                (255 - alpha(second)) * paper[c] * 255) / 255;
            maximum = std::max(maximum, std::abs(a - b));
        }
        return maximum;
    };
    size_t interiorEdges = 0;
    for (size_t row = 0; row < expanded.height; ++row) {
        for (size_t column = 0; column < expanded.width; ++column) {
            size_t pixel = row * expanded.width + column;
            for (size_t neighbor : {column + 1 < expanded.width ? pixel + 1 : count,
                    row + 1 < expanded.height ? pixel + expanded.width : count}) {
                if (neighbor == count) continue;
                double before = compositedEdge(pixel, neighbor, sourcePaper);
                double after = compositedEdge(pixel, neighbor, targetPaper);
                if (before >= 40 && after * 2 < before ||
                    after >= 40 && before * 2 < after)
                    return false;
                // Small backdrop shifts may weaken a thin contour without
                // removing it. Refuse near-disappearance, and require stronger
                // source edges to retain at least two thirds of their contrast.
                if (closeDarkBackdrop && (before >= 12 && after < 6 ||
                    after >= 12 && before < 6 ||
                    before >= 19 && after * 3 < before * 2 ||
                    after >= 19 && before * 3 < after * 2))
                    return false;
                interiorEdges += before >= 40 && after >= 40 &&
                    alpha(pixel) >= 128 && alpha(neighbor) >= 128;
            }
        }
    }
    return interiorEdges >= std::max<size_t>(64, high / 50);
}

void CompareRetainedValue(QPDFObjectHandle before, QPDFObjectHandle after, unsigned depth)
{
    RequireLimit(depth <= 8, "Retained PDF image metadata exceeds the nesting limit.");
    Require(before.getTypeCode() == after.getTypeCode(),
        "Output PDF retained image metadata type changed.");
    if (before.isStream()) {
        auto a = before.getDict(), b = after.getDict();
        auto keysA = a.getKeys(), keysB = b.getKeys();
        keysA.erase("/Length");
        keysB.erase("/Length");
        bool jpeg = RetainedJpegFilter(a.getKey("/Filter"));
        if (jpeg) {
            Require(RetainedJpegFilter(b.getKey("/Filter")),
                "Output PDF retained JPEG compression changed unsafely.");
            keysA.erase("/Filter");
            keysB.erase("/Filter");
        } else if (depth != 0 || (RetainedLosslessFilter(a.getKey("/Filter")) &&
            RetainedLosslessFilter(b.getKey("/Filter")))) {
            // QPDF may normalize lossless compression while writing. Images
            // and auxiliary streams were validated on both sides; compare
            // decoded bytes and every semantic key.
            Require(RetainedLosslessFilter(a.getKey("/Filter")) &&
                RetainedLosslessFilter(b.getKey("/Filter")),
                "Output PDF retained auxiliary stream compression changed unsafely.");
            keysA.erase("/Filter");
            keysB.erase("/Filter");
            keysA.erase("/DecodeParms");
            keysB.erase("/DecodeParms");
        }
        Require(keysA == keysB, "Output PDF retained image stream metadata changed.");
        for (auto const& key : keysA) CompareRetainedValue(a.getKey(key), b.getKey(key), depth + 1);
        auto bytesA = jpeg ? RetainedJpegBytes(before) : before.getStreamData(qpdf_dl_all);
        auto bytesB = jpeg ? RetainedJpegBytes(after) : after.getStreamData(qpdf_dl_all);
        Require(bytesA && bytesB && bytesA->getSize() == bytesB->getSize() &&
            std::equal(bytesA->getBuffer(), bytesA->getBuffer() + bytesA->getSize(),
                bytesB->getBuffer()),
            "Output PDF retained image or nested stream bytes changed.");
    } else if (before.isDictionary()) {
        Require(before.getKeys() == after.getKeys(),
            "Output PDF retained image dictionary keys changed.");
        for (auto const& key : before.getKeys())
            CompareRetainedValue(before.getKey(key), after.getKey(key), depth + 1);
    } else if (before.isArray()) {
        Require(before.getArrayNItems() == after.getArrayNItems(),
            "Output PDF retained image array length changed.");
        for (int i = 0; i < before.getArrayNItems(); ++i)
            CompareRetainedValue(before.getArrayItem(i), after.getArrayItem(i), depth + 1);
    } else if (before.isNumber())
        Require(before.getNumericValue() == after.getNumericValue(),
            "Output PDF retained image numeric metadata changed.");
    else if (before.isString())
        Require(before.getStringValue() == after.getStringValue(),
            "Output PDF retained image string metadata changed.");
    else Require(before.unparse() == after.unparse(),
        "Output PDF retained image metadata changed.");
}

void VerifyRetainedImage(QPDFObjectHandle before, QPDFObjectHandle after)
{
    RetainedImageBudget first, second;
    ValidateRetainedImage(before, first);
    ValidateRetainedImage(after, second);
    CompareRetainedValue(before, after, 0);
}
