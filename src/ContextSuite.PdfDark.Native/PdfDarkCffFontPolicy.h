#pragma once

// Included after PdfDarkSystemFontPolicy.h inside PdfDark.cpp's anonymous
// namespace. Include <d2d1.h> and <dwrite_3.h> at file scope. DirectWrite owns
// Type 2 execution; this reader only resolves explicitly named PDF glyphs.
class CffOutlineSink final : public IDWriteGeometrySink {
  public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** value) override
    {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (id != __uuidof(IUnknown) && id != __uuidof(IDWriteGeometrySink))
            return E_NOINTERFACE;
        *value = static_cast<IDWriteGeometrySink*>(this);
        AddRef();
        return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++_references; }
    ULONG STDMETHODCALLTYPE Release() override
    {
        ULONG remaining = --_references;
        if (!remaining) delete this;
        return remaining;
    }
    void STDMETHODCALLTYPE SetFillMode(D2D1_FILL_MODE) override {}
    void STDMETHODCALLTYPE SetSegmentFlags(D2D1_PATH_SEGMENT) override {}
    void STDMETHODCALLTYPE BeginFigure(D2D1_POINT_2F start, D2D1_FIGURE_BEGIN) override
    {
        if (_figure || !Reserve(1)) _valid = false;
        _figure = true;
        _start = start;
    }
    void STDMETHODCALLTYPE AddLines(D2D1_POINT_2F const* points, UINT32 count) override
    {
        if (!_figure || !Reserve(count)) { _valid = false; return; }
        if (count) Include(_start);
        for (UINT32 index = 0; index < count && _valid; ++index) Include(points[index]);
    }
    void STDMETHODCALLTYPE AddBeziers(D2D1_BEZIER_SEGMENT const* curves, UINT32 count) override
    {
        if (!_figure || count > 65536 / 3 || !Reserve(count * 3)) {
            _valid = false;
            return;
        }
        if (count) Include(_start);
        for (UINT32 index = 0; index < count && _valid; ++index) {
            // Every cubic lies inside its control-point hull, including
            // off-curve extrema. No flattening tolerance can shrink this bound.
            Include(curves[index].point1);
            Include(curves[index].point2);
            Include(curves[index].point3);
        }
    }
    void STDMETHODCALLTYPE EndFigure(D2D1_FIGURE_END) override
    {
        if (!_figure) _valid = false;
        _figure = false;
    }
    HRESULT STDMETHODCALLTYPE Close() override { return _valid && !_figure ? S_OK : E_FAIL; }

    size_t Points() const { return _points; }
    TrueTypeGlyphBox Box() const
    {
        if (!_valid || _figure) return {};
        auto result = _box;
        result.known = true;
        result.empty = !_ink;
        if (_ink) {
            // Outline coordinates are FLOATs. Expand by more than their maximum
            // rounding error within the admitted 64-em coordinate range.
            constexpr double padding = .00002;
            result.left -= padding; result.bottom -= padding;
            result.right += padding; result.top += padding;
        }
        return result;
    }

  private:
    ULONG _references = 1;
    size_t _points = 0;
    bool _valid = true, _figure = false, _ink = false;
    D2D1_POINT_2F _start{};
    TrueTypeGlyphBox _box;

    bool Reserve(size_t count)
    {
        if (!_valid || count > 65536 - _points) return false;
        _points += count;
        return true;
    }
    void Include(D2D1_POINT_2F point)
    {
        double x = point.x, y = -static_cast<double>(point.y);
        if (!std::isfinite(x) || !std::isfinite(y) || std::abs(x) > 64 || std::abs(y) > 64) {
            _valid = false;
            return;
        }
        if (!_ink) {
            _box = {true, false, x, y, x, y};
            _ink = true;
        } else {
            _box.left = std::min(_box.left, x); _box.right = std::max(_box.right, x);
            _box.bottom = std::min(_box.bottom, y); _box.top = std::max(_box.top, y);
        }
    }
};

class CffFontGlyphBounds {
  public:
    ~CffFontGlyphBounds()
    {
        _face.reset();
        if (_registered) _factory->UnregisterFontFileLoader(_loader.get());
    }
    size_t DecodedBytes() const { return _bytes.size() * 2; }

    TrueTypeGlyphBox ForCode(unsigned code, double pdfWidth) const
    {
        if (code > 255 || _codeToGlyph[code] < 0 || !std::isfinite(pdfWidth) ||
            pdfWidth < 0 || pdfWidth > 2000) return {};
        unsigned glyph = static_cast<unsigned>(_codeToGlyph[code]);
        auto found = _cache.find(glyph);
        if (found != _cache.end()) return found->second;
        RequireLimit(_outlinePoints <= 2000000 - 65536,
            "Embedded CFF outline point budget exceeded.");
        SystemFontPtr<CffOutlineSink> sink(new CffOutlineSink);
        UINT16 glyphIndex = static_cast<UINT16>(glyph);
        FLOAT advance = 0;
        HRESULT status = _face->GetGlyphRunOutline(1, &glyphIndex, &advance, nullptr,
            1, FALSE, FALSE, sink.get());
        _outlinePoints += sink->Points();
        auto box = SUCCEEDED(status) && SUCCEEDED(sink->Close()) ? sink->Box() : TrueTypeGlyphBox{};
        _cache.emplace(glyph, box);
        return box;
    }

  private:
    struct Span { size_t start = 0, length = 0; };
    struct Index { std::vector<Span> entries; size_t end = 0; };
    std::string _bytes;
    std::array<int, 256> _codeToGlyph{};
    SystemFontPtr<IDWriteFactory5> _factory;
    SystemFontPtr<IDWriteInMemoryFontFileLoader> _loader;
    SystemFontPtr<IDWriteFontFace> _face;
    bool _registered = false;
    mutable size_t _outlinePoints = 0;
    mutable std::map<unsigned, TrueTypeGlyphBox> _cache;

    unsigned Byte(size_t offset) const
    {
        Require(offset < _bytes.size(), "Truncated embedded CFF font.");
        return static_cast<unsigned char>(_bytes[offset]);
    }
    uint32_t Unsigned(size_t offset, unsigned count) const
    {
        Require(count <= 4 && offset <= _bytes.size() && count <= _bytes.size() - offset,
            "Truncated embedded CFF integer.");
        uint32_t value = 0;
        for (unsigned index = 0; index < count; ++index) value = (value << 8) | Byte(offset + index);
        return value;
    }
    Index ReadIndex(size_t start) const
    {
        unsigned count = Unsigned(start, 2);
        Index result;
        if (!count) { result.end = start + 2; return result; }
        unsigned size = Byte(start + 2);
        Require(size >= 1 && size <= 4, "Invalid embedded CFF INDEX offset size.");
        size_t offsets = start + 3;
        Require(offsets <= _bytes.size() &&
            (static_cast<size_t>(count) + 1) * size <= _bytes.size() - offsets,
            "Truncated embedded CFF INDEX offsets.");
        size_t data = offsets + (static_cast<size_t>(count) + 1) * size;
        size_t previous = Unsigned(offsets, size);
        Require(previous == 1, "Invalid embedded CFF INDEX initial offset.");
        result.entries.reserve(count);
        for (unsigned index = 1; index <= count; ++index) {
            size_t next = Unsigned(offsets + static_cast<size_t>(index) * size, size);
            Require(next >= previous && next >= 1 && next - 1 <= _bytes.size() - data,
                "Invalid embedded CFF INDEX extent.");
            result.entries.push_back({data + previous - 1, next - previous});
            previous = next;
        }
        result.end = data + previous - 1;
        return result;
    }
    double Number(size_t& cursor, size_t end) const
    {
        auto next = [&]() {
            Require(cursor < end, "Truncated embedded CFF DICT operand.");
            return Byte(cursor++);
        };
        unsigned first = next();
        if (first >= 32 && first <= 246) return static_cast<int>(first) - 139;
        if (first >= 247 && first <= 250) return (first - 247) * 256 + next() + 108;
        if (first >= 251 && first <= 254) return -static_cast<int>((first - 251) * 256 + next() + 108);
        if (first == 28 || first == 29) {
            unsigned count = first == 28 ? 2 : 4;
            Require(count <= end - cursor, "Truncated embedded CFF DICT integer.");
            uint32_t value = Unsigned(cursor, count);
            cursor += count;
            return count == 2 ? static_cast<int16_t>(value) : static_cast<int32_t>(value);
        }
        Require(first == 30, "Invalid embedded CFF DICT number.");
        double value = 0;
        int fraction = 0, exponent = 0;
        bool minus = false, decimal = false, inExponent = false, exponentMinus = false;
        bool digit = false, exponentDigit = false;
        for (unsigned encoded = 0; encoded < 32; ++encoded) {
            unsigned pair = next();
            for (unsigned nibble : {pair >> 4, pair & 15}) {
                if (nibble == 15) {
                    Require(digit && (!inExponent || exponentDigit), "Invalid embedded CFF real.");
                    double result = (minus ? -value : value) *
                        std::pow(10.0, (exponentMinus ? -exponent : exponent) - fraction);
                    Require(std::isfinite(result), "Nonfinite embedded CFF real.");
                    return result;
                }
                if (nibble <= 9) {
                    if (inExponent) {
                        Require(exponent <= 100, "Embedded CFF real exponent exceeds its limit.");
                        exponent = exponent * 10 + static_cast<int>(nibble);
                        exponentDigit = true;
                    } else {
                        value = value * 10 + nibble;
                        if (decimal) ++fraction;
                        digit = true;
                    }
                } else if (nibble == 10 && !decimal && !inExponent) decimal = true;
                else if ((nibble == 11 || nibble == 12) && !inExponent && digit) {
                    inExponent = true; exponentMinus = nibble == 12;
                } else if (nibble == 14 && !minus && !digit && !decimal && !inExponent) minus = true;
                else Require(false, "Invalid embedded CFF real nibble.");
            }
        }
        RequireLimit(false, "Embedded CFF real operand exceeds its limit.");
        return 0;
    }
    std::map<unsigned, std::vector<double>> ReadDictionary(Span span) const
    {
        RequireLimit(span.length <= 65536, "Embedded CFF DICT exceeds its limit.");
        std::map<unsigned, std::vector<double>> result;
        std::vector<double> operands;
        size_t cursor = span.start, end = span.start + span.length;
        while (cursor < end) {
            unsigned first = Byte(cursor);
            if (first == 28 || first == 29 || first == 30 || (first >= 32 && first <= 254)) {
                RequireLimit(operands.size() < 48, "Embedded CFF DICT operand budget exceeded.");
                operands.push_back(Number(cursor, end));
            } else {
                Require(first <= 21, "Invalid embedded CFF DICT operator.");
                ++cursor;
                unsigned operation = first;
                if (first == 12) {
                    Require(cursor < end, "Truncated embedded CFF escaped operator.");
                    operation = 0x100 + Byte(cursor++);
                }
                Require(result.emplace(operation, std::move(operands)).second,
                    "Duplicate embedded CFF DICT operator.");
                operands.clear();
            }
        }
        Require(operands.empty(), "Unterminated embedded CFF DICT operand.");
        return result;
    }

    friend std::shared_ptr<CffFontGlyphBounds> ReadEmbeddedCffGlyphBounds(QPDFObjectHandle);
};

std::shared_ptr<CffFontGlyphBounds> ReadEmbeddedCffGlyphBounds(QPDFObjectHandle font)
{
    if (!font.isDictionary() || !font.getKey("/Subtype").isNameAndEquals("/Type1")) return {};
    auto descriptor = font.getKey("/FontDescriptor");
    if (!descriptor.isDictionary()) return {};
    auto embedded = descriptor.getKey("/FontFile3");
    if (!embedded.isStream() ||
        !embedded.getDict().getKey("/Subtype").isNameAndEquals("/Type1C")) return {};
    auto encoding = font.getKey("/Encoding");
    bool macRoman = encoding.isNameAndEquals("/MacRomanEncoding") ||
        (encoding.isDictionary() && encoding.getKey("/BaseEncoding").isNameAndEquals("/MacRomanEncoding"));
    if (!encoding.isDictionary() && !macRoman) return {};
    auto differences = encoding.isDictionary() ? encoding.getKey("/Differences") : QPDFObjectHandle::newNull();
    if (!differences.isNull() && (!differences.isArray() || differences.getArrayNItems() > 512)) return {};
    auto result = std::make_shared<CffFontGlyphBounds>();
    auto buffer = ReadBoundedPdfStream(embedded, 8ull * 1024 * 1024,
        "Embedded CFF font exceeds the glyph-bound budget.");
    result->_bytes = buffer->move();
    result->_codeToGlyph.fill(-1);
    if (result->Byte(0) != 1 || result->Byte(1) != 0) return {};
    unsigned header = result->Byte(2), offSize = result->Byte(3);
    Require(header >= 4 && header <= result->_bytes.size() && offSize >= 1 && offSize <= 4,
        "Invalid embedded CFF header.");
    auto names = result->ReadIndex(header);
    auto top = result->ReadIndex(names.end);
    if (names.entries.size() != 1 || top.entries.size() != 1) return {};
    auto strings = result->ReadIndex(top.end);
    auto subroutines = result->ReadIndex(strings.end);
    (void)subroutines; // Validate the fixed INDEX layout before handing bytes to DirectWrite.
    auto dict = result->ReadDictionary(top.entries[0]);
    // CID, synthetic, and non-Type2 programs have different mapping semantics.
    for (unsigned key : {0x100u + 30, 0x100u + 36, 0x100u + 37, 0x100u + 20, 0x100u + 21})
        if (dict.count(key)) return {};
    auto paintType = dict.find(0x100 + 5);
    if (paintType != dict.end() && paintType->second != std::vector<double>{0}) return {};
    auto charstringType = dict.find(0x100 + 6);
    if (charstringType != dict.end() && charstringType->second != std::vector<double>{2}) return {};
    auto matrix = dict.find(0x100 + 7);
    if (matrix != dict.end()) {
        std::array<double, 6> expected{.001, 0, 0, .001, 0, 0};
        if (matrix->second.size() != expected.size()) return {};
        for (size_t index = 0; index < expected.size(); ++index)
            if (std::abs(matrix->second[index] - expected[index]) > 1e-12) return {};
    }
    auto offset = [&](unsigned key, size_t defaultValue) {
        auto found = dict.find(key);
        if (found == dict.end()) return defaultValue;
        auto const& operands = found->second;
        Require(operands.size() == 1 && operands[0] >= 0 &&
            operands[0] <= result->_bytes.size() && operands[0] == std::floor(operands[0]),
            "Invalid embedded CFF table offset.");
        return static_cast<size_t>(operands[0]);
    };
    if (!dict.count(17)) return {};
    auto charstrings = result->ReadIndex(offset(17, 0));
    RequireLimit(charstrings.entries.size() <= 4096, "Embedded CFF glyph count exceeds its limit.");
    if (charstrings.entries.empty()) return {};
    size_t charset = offset(15, 0);
    // ISOAdobe uses consecutive standard SIDs. Expert charsets and standard
    // names outside the explicitly recognized ASCII subset retain broad bounds.
    if (charset == 1 || charset == 2) return {};
    bool isoAdobe = charset == 0;
    if (isoAdobe && charstrings.entries.size() > 229) return {};
    unsigned format = isoAdobe ? 0 : result->Byte(charset++);
    if (format > 2) return {};
    std::map<std::string, unsigned> namedGlyphs;
    std::set<std::string> customNames;
    std::set<unsigned> seenSids;
    seenSids.insert(0);
    size_t glyph = 1;
    bool unknownStandardName = false;
    auto standardName = [](unsigned sid) -> std::string {
        if (sid == 1) return "space";
        if (sid >= 34 && sid <= 59) return std::string(1, static_cast<char>('A' + sid - 34));
        if (sid >= 66 && sid <= 91) return std::string(1, static_cast<char>('a' + sid - 66));
        static constexpr std::array<char const*, 10> digits{
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"};
        if (sid >= 17 && sid <= 26) return digits[sid - 17];
        return {};
    };
    auto addGlyph = [&](unsigned sid) {
        Require(sid > 0 && sid <= 64999 && seenSids.insert(sid).second,
            "Invalid or duplicate embedded CFF charset SID.");
        std::string name;
        if (sid < 391) {
            name = standardName(sid);
            if (name.empty()) unknownStandardName = true;
        } else {
            size_t stringIndex = sid - 391;
            Require(stringIndex < strings.entries.size(), "Embedded CFF charset string is missing.");
            auto span = strings.entries[stringIndex];
            RequireLimit(span.length <= 127, "Embedded CFF glyph name exceeds its limit.");
            name = result->_bytes.substr(span.start, span.length);
            Require(name != ".notdef", "Duplicate embedded CFF notdef glyph name.");
            customNames.insert(name);
        }
        if (!name.empty()) {
            Require(namedGlyphs.emplace(name, static_cast<unsigned>(glyph)).second,
                "Duplicate embedded CFF glyph name.");
        }
        ++glyph;
    };
    while (glyph < charstrings.entries.size()) {
        if (isoAdobe) { addGlyph(static_cast<unsigned>(glyph)); continue; }
        unsigned first = result->Unsigned(charset, 2); charset += 2;
        unsigned extra = 0;
        if (format) { extra = result->Unsigned(charset, format); charset += format; }
        Require(extra < charstrings.entries.size() - glyph && extra <= 64999 - std::min(first, 64999u),
            "Invalid embedded CFF charset range.");
        for (unsigned index = 0; index <= extra; ++index) addGlyph(first + index);
    }
    // A custom name can alias an unrecognized standard SID. Do not narrow those
    // custom glyphs unless every standard name in this charset is recognized.
    if (unknownStandardName)
        for (auto const& name : customNames) namedGlyphs.erase(name);
    int code = -1;
    std::array<bool, 256> assigned{};
    bool mapped = false;
    if (macRoman) {
        for (unsigned character = 32; character <= 122; ++character) {
            unsigned sid = character == 32 ? 1 :
                character >= 48 && character <= 57 ? character - 48 + 17 :
                character >= 65 && character <= 90 ? character - 65 + 34 :
                character >= 97 && character <= 122 ? character - 97 + 66 : 0;
            if (!sid) continue;
            auto found = namedGlyphs.find(standardName(sid));
            if (found != namedGlyphs.end()) {
                result->_codeToGlyph[character] = static_cast<int>(found->second);
                mapped = true;
            }
        }
    }
    int differenceCount = differences.isArray() ? differences.getArrayNItems() : 0;
    for (int index = 0; index < differenceCount; ++index) {
        auto item = differences.getArrayItem(index);
        if (item.isInteger()) {
            auto value = item.getIntValue();
            Require(value >= 0 && value <= 255, "Invalid PDF CFF encoding code.");
            code = static_cast<int>(value);
        } else {
            Require(item.isName() && code >= 0 && code <= 255 && !assigned[static_cast<size_t>(code)],
                "Invalid or duplicate PDF CFF encoding difference.");
            assigned[static_cast<size_t>(code)] = true;
            result->_codeToGlyph[static_cast<size_t>(code)] = -1;
            std::string name = item.getName().substr(1);
            auto found = namedGlyphs.find(name);
            if (found != namedGlyphs.end()) {
                result->_codeToGlyph[static_cast<size_t>(code)] = static_cast<int>(found->second);
                mapped = true;
            }
            ++code;
        }
    }
    if (!mapped) return {};
    IDWriteFactory5* factory = nullptr;
    if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_ISOLATED, __uuidof(IDWriteFactory5),
        reinterpret_cast<IUnknown**>(&factory)))) return {};
    result->_factory.reset(factory);
    IDWriteInMemoryFontFileLoader* loader = nullptr;
    if (FAILED(factory->CreateInMemoryFontFileLoader(&loader))) return {};
    result->_loader.reset(loader);
    if (FAILED(factory->RegisterFontFileLoader(loader))) return {};
    result->_registered = true;
    IDWriteFontFile* fileRaw = nullptr;
    if (FAILED(loader->CreateInMemoryFontFileReference(factory, result->_bytes.data(),
        static_cast<UINT32>(result->_bytes.size()), nullptr, &fileRaw))) return {};
    SystemFontPtr<IDWriteFontFile> file(fileRaw);
    IDWriteFontFace* face = nullptr;
    if (FAILED(factory->CreateFontFace(DWRITE_FONT_FACE_TYPE_RAW_CFF, 1, &fileRaw,
        0, DWRITE_FONT_SIMULATIONS_NONE, &face))) return {};
    result->_face.reset(face);
    return result;
}
