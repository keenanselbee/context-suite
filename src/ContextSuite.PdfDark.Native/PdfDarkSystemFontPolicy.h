#pragma once

// Included inside PdfDark.cpp's anonymous namespace. Include <dwrite.h> and
// <optional> at file scope and link dwrite.lib in the native project.
template <typename T> struct SystemFontRelease {
    void operator()(T* value) const noexcept { if (value) value->Release(); }
};

template <typename T>
using SystemFontPtr = std::unique_ptr<T, SystemFontRelease<T>>;

class SystemFontGlyphBounds {
  public:
    size_t DecodedBytes() const { return 0; }

    TrueTypeGlyphBox ForCode(unsigned code, double pdfWidth) const
    {
        if (code > 255 || !std::isfinite(pdfWidth) || pdfWidth < 0 || pdfWidth > 2000)
            return {};
        auto found = _cache.find(code);
        if (found == _cache.end()) {
            auto glyph = ReadGlyph(code);
            found = _cache.emplace(code, glyph).first;
        }
        auto const& glyph = found->second;
        if (!glyph.box.known || std::abs(glyph.advance * 1000 - pdfWidth) > 1)
            return {};
        auto box = glyph.box;
        // PDF descriptors have integer thousandth-em bounds and can disagree
        // slightly with the installed face. Allow only this bounded discrepancy,
        // then expand the actual ink bounds so no painted area is omitted.
        constexpr double descriptorTolerance = .002;
        if (!box.empty && (box.left < _descriptor[0] - descriptorTolerance ||
                           box.bottom < _descriptor[1] - descriptorTolerance ||
                           box.right > _descriptor[2] + descriptorTolerance ||
                           box.top > _descriptor[3] + descriptorTolerance))
            return {};
        if (!box.empty) {
            box.left -= descriptorTolerance;
            box.bottom -= descriptorTolerance;
            box.right += descriptorTolerance;
            box.top += descriptorTolerance;
        }
        return box;
    }

  private:
    struct Glyph {
        TrueTypeGlyphBox box;
        double advance = 0;
    };
    SystemFontPtr<IDWriteFontFace> _face;
    std::array<double, 4> _descriptor{};
    unsigned _units = 0;
    mutable std::map<unsigned, Glyph> _cache;

    static int WinAnsi(unsigned code)
    {
        static constexpr std::array<int, 32> extra{0x20ac, -1, 0x201a, 0x0192,
            0x201e, 0x2026, 0x2020, 0x2021, 0x02c6, 0x2030, 0x0160, 0x2039,
            0x0152, -1, 0x017d, -1, -1, 0x2018, 0x2019, 0x201c,
            0x201d, 0x2022, 0x2013, 0x2014, 0x02dc, 0x2122, 0x0161,
            0x203a, 0x0153, -1, 0x017e, 0x0178};
        return code >= 128 && code < 160 ? extra[code - 128] : static_cast<int>(code);
    }

    Glyph ReadGlyph(unsigned code) const
    {
        int unicode = WinAnsi(code);
        if (unicode < 0) return {};
        UINT32 point = static_cast<UINT32>(unicode);
        UINT16 index = 0;
        if (FAILED(_face->GetGlyphIndices(&point, 1, &index)) || index == 0) return {};
        DWRITE_GLYPH_METRICS metrics{};
        if (FAILED(_face->GetDesignGlyphMetrics(&index, 1, &metrics, FALSE))) return {};
        double units = static_cast<double>(_units);
        double left = metrics.leftSideBearing / units;
        double right = (static_cast<double>(metrics.advanceWidth) - metrics.rightSideBearing) / units;
        double top = (static_cast<double>(metrics.verticalOriginY) - metrics.topSideBearing) / units;
        double bottom = (static_cast<double>(metrics.verticalOriginY) - metrics.advanceHeight +
            metrics.bottomSideBearing) / units;
        if (!std::isfinite(left) || !std::isfinite(bottom) || !std::isfinite(right) ||
            !std::isfinite(top) || left > right || bottom > top)
            return {};
        bool empty = left == right || bottom == top;
        return {{true, empty, left, bottom, right, top}, metrics.advanceWidth / units};
    }

    friend std::optional<SystemFontGlyphBounds> ReadSystemTrueTypeGlyphBounds(QPDFObjectHandle);
};

std::optional<SystemFontGlyphBounds> ReadSystemTrueTypeGlyphBounds(QPDFObjectHandle font)
{
    if (!font.isDictionary() || !font.getKey("/Subtype").isNameAndEquals("/TrueType") ||
        !font.getKey("/Encoding").isNameAndEquals("/WinAnsiEncoding")) return std::nullopt;
    auto descriptor = font.getKey("/FontDescriptor");
    if (!descriptor.isDictionary() || !descriptor.getKey("/FontFile").isNull() ||
        !descriptor.getKey("/FontFile2").isNull() ||
        !descriptor.getKey("/FontFile3").isNull()) return std::nullopt;
    auto flags = descriptor.getKey("/Flags");
    if (!flags.isInteger() || !(flags.getIntValue() & 32) || (flags.getIntValue() & 4))
        return std::nullopt;
    auto box = descriptor.getKey("/FontBBox");
    if (!box.isArray() || box.getArrayNItems() != 4) return std::nullopt;
    SystemFontGlyphBounds result;
    for (int index = 0; index < 4; ++index) {
        auto value = box.getArrayItem(index);
        if (!value.isNumber()) return std::nullopt;
        double extent = value.getNumericValue() / 1000;
        if (!std::isfinite(extent) || std::abs(extent) > 64) return std::nullopt;
        result._descriptor[static_cast<size_t>(index)] = extent;
    }
    if (result._descriptor[0] > result._descriptor[2] ||
        result._descriptor[1] > result._descriptor[3]) return std::nullopt;
    auto base = font.getKey("/BaseFont");
    if (!base.isName()) return std::nullopt;
    std::string name = base.getName();
    if (name.size() < 2 || name.size() > 128 || name[0] != '/') return std::nullopt;
    for (size_t index = 1; index < name.size(); ++index)
        if (static_cast<unsigned char>(name[index]) < 33 ||
            static_cast<unsigned char>(name[index]) > 126) return std::nullopt;
    std::wstring target(name.begin() + 1, name.end());

    IDWriteFactory* factoryRaw = nullptr;
    if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(&factoryRaw)))) return std::nullopt;
    SystemFontPtr<IDWriteFactory> factory(factoryRaw);
    IDWriteFontCollection* collectionRaw = nullptr;
    if (FAILED(factory->GetSystemFontCollection(&collectionRaw, FALSE))) return std::nullopt;
    SystemFontPtr<IDWriteFontCollection> collection(collectionRaw);
    UINT32 families = collection->GetFontFamilyCount();
    RequireLimit(families <= 4096, "DirectWrite font collection exceeds the policy budget.");
    SystemFontPtr<IDWriteFont> selected;
    unsigned matches = 0;
    unsigned scanned = 0;
    for (UINT32 familyIndex = 0; familyIndex < families; ++familyIndex) {
        IDWriteFontFamily* familyRaw = nullptr;
        if (FAILED(collection->GetFontFamily(familyIndex, &familyRaw))) return std::nullopt;
        SystemFontPtr<IDWriteFontFamily> family(familyRaw);
        UINT32 fonts = family->GetFontCount();
        RequireLimit(fonts <= 128 && scanned <= 32768 - fonts,
            "DirectWrite font inventory exceeds the policy budget.");
        scanned += fonts;
        for (UINT32 fontIndex = 0; fontIndex < fonts; ++fontIndex) {
            IDWriteFont* fontRaw = nullptr;
            if (FAILED(family->GetFont(fontIndex, &fontRaw))) return std::nullopt;
            SystemFontPtr<IDWriteFont> candidate(fontRaw);
            if (candidate->GetSimulations() != DWRITE_FONT_SIMULATIONS_NONE) continue;
            BOOL exists = FALSE;
            IDWriteLocalizedStrings* namesRaw = nullptr;
            HRESULT named = candidate->GetInformationalStrings(
                DWRITE_INFORMATIONAL_STRING_POSTSCRIPT_NAME, &namesRaw, &exists);
            SystemFontPtr<IDWriteLocalizedStrings> names(namesRaw);
            if (FAILED(named)) continue;
            if (!exists || !names) continue;
            UINT32 count = names->GetCount();
            RequireLimit(count <= 8, "DirectWrite font name inventory exceeds its limit.");
            bool exact = false;
            for (UINT32 index = 0; index < count; ++index) {
                UINT32 length = 0;
                if (FAILED(names->GetStringLength(index, &length))) continue;
                if (length != target.size() || length > 128) continue;
                std::array<wchar_t, 129> value{};
                if (SUCCEEDED(names->GetString(index, value.data(), length + 1)) &&
                    std::wstring(value.data(), length) == target) exact = true;
            }
            if (!exact) continue;
            if (++matches > 1) return std::nullopt;
            selected = std::move(candidate);
        }
    }
    if (!selected) return std::nullopt;
    IDWriteFontFace* faceRaw = nullptr;
    if (FAILED(selected->CreateFontFace(&faceRaw))) return std::nullopt;
    result._face.reset(faceRaw);
    if (result._face->GetType() != DWRITE_FONT_FACE_TYPE_TRUETYPE) return std::nullopt;
    DWRITE_FONT_METRICS metrics{};
    result._face->GetMetrics(&metrics);
    result._units = metrics.designUnitsPerEm;
    if (result._units < 16 || result._units > 16384) return std::nullopt;
    return result;
}
