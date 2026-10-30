#pragma once

// Included inside PdfDark.cpp's anonymous namespace, after the palette helpers.

double ShadingNumber(QPDFObjectHandle value, char const* message)
{
    Require(value.isNumber(), message);
    double result = value.getNumericValue();
    Require(std::isfinite(result), message);
    return result;
}

std::vector<double> ShadingNumbers(QPDFObjectHandle value, int count, char const* message)
{
    Require(value.isArray() && value.getArrayNItems() == count, message);
    std::vector<double> result;
    result.reserve(count);
    for (int index = 0; index < count; ++index)
        result.push_back(ShadingNumber(value.getArrayItem(index), message));
    return result;
}

void ShadingKeys(QPDFObjectHandle dictionary, std::set<std::string> const& allowed)
{
    Require(dictionary.isDictionary(), "Invalid PDF axial shading dictionary.");
    for (auto const& key : dictionary.getKeys())
        if (!allowed.contains(key)) Unsupported("PDF shading feature is outside the axial background policy.");
}

void ShadingUnitRange(QPDFObjectHandle dictionary)
{
    auto range = dictionary.getKey("/Range");
    if (range.isNull()) return;
    auto values = ShadingNumbers(range, 6, "Invalid PDF RGB shading function range.");
    for (size_t index = 0; index < values.size(); ++index)
        Require(values[index] == (index % 2 == 0 ? 0.0 : 1.0),
            "PDF RGB shading function range is outside the supported policy.");
}

std::array<double, 2> ShadingDomain(QPDFObjectHandle dictionary)
{
    auto values = ShadingNumbers(dictionary.getKey("/Domain"), 2,
        "Invalid PDF shading function domain.");
    Require(values[0] < values[1] && values[0] >= -1000000 && values[1] <= 1000000,
        "PDF shading function domain is outside the supported policy.");
    return {values[0], values[1]};
}

Color ShadingStop(QPDFObjectHandle value)
{
    auto values = ShadingNumbers(value, 3, "Invalid PDF RGB shading stop.");
    Color source{values[0], values[1], values[2]};
    for (double channel : source)
        Require(channel >= 0 && channel <= 1, "PDF RGB shading stop is outside its range.");
    if (PalettePanel(source)) return source;
    if (Luminance(source) < .70)
        Unsupported("PDF shading is not a light background or target-palette panel.");
    return source;
}

QPDFObjectHandle ShadingStopArray(Color color)
{
    auto result = QPDFObjectHandle::newArray();
    for (double channel : color)
        result.appendItem(QPDFObjectHandle::newReal(channel, 8));
    return result;
}

QPDFObjectHandle CloneShadingFunction(QPDFObjectHandle function, bool& changed, unsigned depth)
{
    RequireLimit(depth <= 4, "PDF shading function nesting exceeds its limit.");
    Require(function.isDictionary(), "Invalid PDF shading function.");
    auto type = function.getKey("/FunctionType");
    Require(type.isInteger(), "Invalid PDF shading function type.");
    auto domain = ShadingDomain(function);
    ShadingUnitRange(function);
    if (type.getIntValue() == 2) {
        ShadingKeys(function, {"/FunctionType", "/Domain", "/Range", "/C0", "/C1", "/N"});
        Require(domain[0] == 0 && domain[1] == 1,
            "PDF exponential shading domain is outside the supported policy.");
        double exponent = ShadingNumber(function.getKey("/N"), "Invalid PDF shading exponent.");
        Require(exponent >= .125 && exponent <= 8,
            "PDF shading exponent is outside the supported policy.");
        Color first = ShadingStop(function.getKey("/C0"));
        Color last = ShadingStop(function.getKey("/C1"));
        Color newFirst = PalettePanel(first) ? first : Recolor(first, false, false);
        Color newLast = PalettePanel(last) ? last : Recolor(last, false, false);
        if (SameColor(first, newFirst) && SameColor(last, newLast)) return function;
        auto copy = function.shallowCopy();
        copy.replaceKey("/C0", ShadingStopArray(newFirst));
        copy.replaceKey("/C1", ShadingStopArray(newLast));
        changed = true;
        return copy;
    }
    if (type.getIntValue() == 3) {
        ShadingKeys(function, {"/FunctionType", "/Domain", "/Range", "/Functions", "/Bounds", "/Encode"});
        auto children = function.getKey("/Functions");
        Require(children.isArray(), "Invalid PDF stitched shading functions.");
        int count = children.getArrayNItems();
        RequireLimit(count >= 1 && count <= 8, "PDF stitched shading function count exceeds its limit.");
        auto bounds = ShadingNumbers(function.getKey("/Bounds"), count - 1,
            "Invalid PDF stitched shading bounds.");
        double previous = domain[0];
        for (double bound : bounds) {
            Require(bound > previous && bound < domain[1], "PDF stitched shading bounds are not ordered.");
            previous = bound;
        }
        auto encode = ShadingNumbers(function.getKey("/Encode"), count * 2,
            "Invalid PDF stitched shading encoding.");
        auto result = QPDFObjectHandle::newArray();
        bool anyChanged = false;
        for (int index = 0; index < count; ++index) {
            // The supported subset maps each interval over its child's full unit domain.
            Require((encode[index * 2] == 0 && encode[index * 2 + 1] == 1) ||
                    (encode[index * 2] == 1 && encode[index * 2 + 1] == 0),
                "PDF stitched shading encoding is outside the supported policy.");
            auto child = children.getArrayItem(index);
            Require(child.isDictionary() && child.getKey("/FunctionType").isInteger() &&
                    child.getKey("/FunctionType").getIntValue() == 2,
                "PDF nested shading function is outside the supported policy.");
            bool childChanged = false;
            result.appendItem(CloneShadingFunction(child, childChanged, depth + 1));
            anyChanged |= childChanged;
        }
        if (!anyChanged) return function;
        auto copy = function.shallowCopy();
        copy.replaceKey("/Functions", result);
        changed = true;
        return copy;
    }
    Unsupported("PDF shading function type is outside the background policy.");
}

std::array<double, 4> ReadAxialShadingBox(QPDFObjectHandle shading)
{
    ShadingKeys(shading, {"/ShadingType", "/ColorSpace", "/Coords", "/Domain",
        "/Function", "/Extend", "/BBox", "/AntiAlias"});
    Require(shading.getKey("/ShadingType").isInteger() &&
            shading.getKey("/ShadingType").getIntValue() == 2 &&
            shading.getKey("/ColorSpace").isNameAndEquals("/DeviceRGB"),
        "PDF shading is outside the axial RGB background policy.");
    auto antialias = shading.getKey("/AntiAlias");
    Require(antialias.isNull() || (antialias.isBool() && !antialias.getBoolValue()),
        "PDF shading antialias setting is outside the supported policy.");
    auto box = ShadingNumbers(shading.getKey("/BBox"), 4, "Invalid PDF shading bounding box.");
    auto coords = ShadingNumbers(shading.getKey("/Coords"), 4, "Invalid PDF axial shading coordinates.");
    for (double value : box) RequireLimit(std::abs(value) <= 1000000, "PDF shading geometry exceeds its limit.");
    for (double value : coords) RequireLimit(std::abs(value) <= 1000000, "PDF shading geometry exceeds its limit.");
    Require(box[0] < box[2] && box[1] < box[3] &&
            std::hypot(coords[2] - coords[0], coords[3] - coords[1]) > .000001,
        "Degenerate PDF axial shading geometry.");
    auto domain = shading.getKey("/Domain");
    if (!domain.isNull()) {
        auto values = ShadingNumbers(domain, 2, "Invalid PDF axial shading domain.");
        Require(values[0] < values[1] && values[0] >= -1000000 && values[1] <= 1000000,
            "PDF axial shading domain is outside the supported policy.");
    }
    auto extend = shading.getKey("/Extend");
    Require(extend.isArray() && extend.getArrayNItems() == 2 &&
            extend.getArrayItem(0).isBool() && extend.getArrayItem(1).isBool(),
        "Invalid PDF axial shading extension.");
    bool ignored = false;
    CloneShadingFunction(shading.getKey("/Function"), ignored, 0);
    return {box[0], box[1], box[2], box[3]};
}

QPDFObjectHandle CloneAxialShading(QPDFObjectHandle shading, bool& changed)
{
    ReadAxialShadingBox(shading);
    bool functionChanged = false;
    auto function = CloneShadingFunction(shading.getKey("/Function"), functionChanged, 0);
    if (!functionChanged) return shading;
    auto copy = shading.shallowCopy();
    copy.replaceKey("/Function", function);
    changed = true;
    return copy;
}

void VerifyShadingValue(QPDFObjectHandle expected, QPDFObjectHandle actual,
    unsigned depth, bool numericTolerance)
{
    RequireLimit(depth <= 4, "Output PDF shading nesting exceeds its limit.");
    if (expected.isNumber() && actual.isNumber()) {
        double delta = std::abs(ShadingNumber(expected, "Invalid expected shading number.") -
            ShadingNumber(actual, "Invalid output shading number."));
        Require(delta <= (numericTolerance ? .00000002 : 0),
            "Output PDF shading number changed.");
        return;
    }
    if (expected.isArray() && actual.isArray()) {
        Require(expected.getArrayNItems() == actual.getArrayNItems(),
            "Output PDF shading array length changed.");
        for (int index = 0; index < expected.getArrayNItems(); ++index)
            VerifyShadingValue(expected.getArrayItem(index), actual.getArrayItem(index),
                depth + 1, numericTolerance);
        return;
    }
    Require(expected.unparse() == actual.unparse(), "Output PDF shading field changed.");
}

void VerifyShadingFunction(QPDFObjectHandle expected, QPDFObjectHandle actual, unsigned depth)
{
    RequireLimit(depth <= 4, "Output PDF shading function nesting exceeds its limit.");
    Require(expected.isDictionary() && actual.isDictionary() &&
            expected.getKeys() == actual.getKeys(),
        "Output PDF shading function fields changed.");
    for (auto const& key : expected.getKeys()) {
        auto left = expected.getKey(key);
        auto right = actual.getKey(key);
        if (key == "/Functions") {
            Require(left.isArray() && right.isArray() &&
                    left.getArrayNItems() == right.getArrayNItems(),
                "Output PDF stitched shading functions changed.");
            for (int index = 0; index < left.getArrayNItems(); ++index)
                VerifyShadingFunction(left.getArrayItem(index), right.getArrayItem(index), depth + 1);
        } else {
            VerifyShadingValue(left, right, depth + 1, key == "/C0" || key == "/C1");
        }
    }
}

void VerifyAxialShading(QPDFObjectHandle original, QPDFObjectHandle output)
{
    bool changed = false;
    auto expected = CloneAxialShading(original, changed);
    ReadAxialShadingBox(output);
    Require(expected.isDictionary() && output.isDictionary() &&
            expected.getKeys() == output.getKeys(),
        "Output PDF axial shading fields changed.");
    for (auto const& key : expected.getKeys()) {
        if (key == "/Function")
            VerifyShadingFunction(expected.getKey(key), output.getKey(key), 0);
        else
            VerifyShadingValue(expected.getKey(key), output.getKey(key), 0, false);
    }
}
