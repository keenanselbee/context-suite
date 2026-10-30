#pragma once

// Included inside PdfDark.cpp's anonymous namespace, after Require/RequireLimit.
// Unknown glyphs retain the caller's font-wide fallback; empty glyphs have no ink.
struct TrueTypeGlyphBox {
    bool known = false;
    bool empty = false;
    double left = 0, bottom = 0, right = 0, top = 0;
};

class TrueTypeGlyphBounds {
  public:
    size_t DecodedBytes() const { return _bytes.size() + _cidMap.size() * 2; }

    TrueTypeGlyphBox ForCode(unsigned code) const
    {
        unsigned gid = 0;
        if (_cid) {
            if (code > 65535 || (_identityMap && code >= _glyphCount) ||
                (!_identityMap && code >= _cidMap.size())) return {};
            gid = _identityMap ? code : _cidMap[code];
        } else {
            if (code > 255 || _simpleMap[code] < 0) return {};
            gid = static_cast<unsigned>(_simpleMap[code]);
        }
        if (gid >= _glyphCount) return {};
        auto cached = _boxes.find(gid);
        if (cached != _boxes.end()) return cached->second;
        size_t components = 0;
        auto box = ReadGlyph(gid, 0, components);
        _boxes.emplace(gid, box);
        return box;
    }

  private:
    struct Table { size_t offset = 0, length = 0; };
    std::string _bytes;
    Table _glyf;
    std::vector<size_t> _loca;
    std::array<int, 256> _simpleMap{};
    std::vector<unsigned> _cidMap;
    unsigned _glyphCount = 0;
    unsigned _units = 0;
    bool _cid = false;
    bool _identityMap = false;
    mutable std::map<unsigned, TrueTypeGlyphBox> _boxes;
    mutable size_t _decodedPoints = 0;
    mutable size_t _decodedComponents = 0;

    void Span(size_t offset, size_t length) const
    {
        Require(offset <= _bytes.size() && length <= _bytes.size() - offset,
            "Malformed embedded TrueType table offset or length.");
    }

    unsigned Byte(size_t offset) const
    {
        Span(offset, 1);
        return static_cast<unsigned char>(_bytes[offset]);
    }

    unsigned U16(size_t offset) const { return (Byte(offset) << 8) | Byte(offset + 1); }
    int I16(size_t offset) const { return static_cast<int16_t>(U16(offset)); }
    uint32_t U32(size_t offset) const { return (U16(offset) << 16) | U16(offset + 2); }

    unsigned Cmap4(Table table, unsigned unicode) const
    {
        size_t base = table.offset;
        Require(table.length >= 16 && U16(base) == 4, "Malformed TrueType cmap format 4.");
        unsigned length = U16(base + 2), segments = U16(base + 6) / 2;
        Require(length <= table.length && length >= 16 && segments >= 1 && segments <= 8192 &&
                U16(base + 6) == segments * 2 && 16ull + 8ull * segments <= length,
            "Malformed TrueType cmap format 4 segments.");
        size_t endCodes = base + 14;
        size_t startCodes = endCodes + 2 * segments + 2;
        size_t deltas = startCodes + 2 * segments;
        size_t offsets = deltas + 2 * segments;
        Require(U16(endCodes + 2 * (segments - 1)) == 65535,
            "TrueType cmap format 4 lacks its terminal segment.");
        unsigned previousEnd = 0;
        unsigned mapped = 0;
        for (unsigned index = 0; index < segments; ++index) {
            unsigned end = U16(endCodes + 2 * index);
            unsigned start = U16(startCodes + 2 * index);
            Require(start <= end && (index == 0 || start > previousEnd),
                "Malformed TrueType cmap format 4 ordering.");
            previousEnd = end;
            unsigned rangeOffset = U16(offsets + 2 * index);
            if (rangeOffset) {
                size_t last = offsets + 2 * index + rangeOffset + 2 * (end - start);
                Require(last >= base && last <= base + length && base + length - last >= 2,
                    "Malformed TrueType cmap format 4 glyph range.");
            }
            if (unicode < start || unicode > end) continue;
            if (!rangeOffset) {
                mapped = (unicode + U16(deltas + 2 * index)) & 65535u;
                continue;
            }
            size_t position = offsets + 2 * index + rangeOffset + 2 * (unicode - start);
            Require(position >= base && position + 2 <= base + length,
                "Malformed TrueType cmap format 4 glyph offset.");
            unsigned gid = U16(position);
            mapped = gid ? (gid + U16(deltas + 2 * index)) & 65535u : 0;
        }
        return mapped;
    }

    unsigned Cmap12(Table table, unsigned unicode) const
    {
        size_t base = table.offset;
        Require(table.length >= 16 && U16(base) == 12 && U16(base + 2) == 0,
            "Malformed TrueType cmap format 12.");
        uint32_t length = U32(base + 4), groups = U32(base + 12);
        RequireLimit(groups <= 65536, "TrueType cmap group budget exceeded.");
        Require(length <= table.length && length >= 16 &&
                16ull + static_cast<uint64_t>(groups) * 12 <= length,
            "Malformed TrueType cmap format 12 groups.");
        uint32_t previousEnd = 0;
        unsigned mapped = 0;
        for (uint32_t index = 0; index < groups; ++index) {
            size_t item = base + 16 + static_cast<size_t>(index) * 12;
            uint32_t start = U32(item), end = U32(item + 4), first = U32(item + 8);
            Require(start <= end && end <= 0x10ffff &&
                    (index == 0 || start > previousEnd) &&
                    static_cast<uint64_t>(first) + end - start <= 65535,
                "Malformed TrueType cmap format 12 ordering or glyph range.");
            previousEnd = end;
            if (unicode >= start && unicode <= end) mapped = first + unicode - start;
        }
        return mapped;
    }

    static int WinAnsi(unsigned code)
    {
        static constexpr std::array<int, 32> extra{0x20ac, -1, 0x201a, 0x0192,
            0x201e, 0x2026, 0x2020, 0x2021, 0x02c6, 0x2030, 0x0160, 0x2039,
            0x0152, -1, 0x017d, -1, -1, 0x2018, 0x2019, 0x201c,
            0x201d, 0x2022, 0x2013, 0x2014, 0x02dc, 0x2122, 0x0161,
            0x203a, 0x0153, -1, 0x017e, 0x0178};
        return code >= 128 && code < 160 ? extra[code - 128] : static_cast<int>(code);
    }

    bool ReadCmap(Table table)
    {
        Require(table.length >= 4, "Malformed embedded TrueType cmap.");
        unsigned records = U16(table.offset + 2);
        RequireLimit(records <= 128, "TrueType cmap record budget exceeded.");
        Require(4ull + 8ull * records <= table.length, "Malformed TrueType cmap records.");
        std::optional<Table> chosen;
        int priority = -1;
        for (unsigned index = 0; index < records; ++index) {
            size_t item = table.offset + 4 + 8 * index;
            unsigned platform = U16(item), encoding = U16(item + 2);
            uint32_t offset = U32(item + 4);
            Require(offset <= table.length && table.length - offset >= 2,
                "Malformed TrueType cmap subtable offset.");
            unsigned format = U16(table.offset + offset);
            int rank = platform == 3 && encoding == 1 && format == 4 ? 3 :
                platform == 3 && encoding == 10 && format == 12 ? 2 :
                platform == 0 && format == 4 ? 1 : -1;
            if (rank > priority) {
                priority = rank;
                chosen = Table{table.offset + offset, table.length - offset};
            }
        }
        if (!chosen) return false;
        for (unsigned code = 0; code < 256; ++code) {
            int unicode = WinAnsi(code);
            _simpleMap[code] = unicode < 0 ? -1 : static_cast<int>(
                U16(chosen->offset) == 4 ? Cmap4(*chosen, static_cast<unsigned>(unicode)) :
                    Cmap12(*chosen, static_cast<unsigned>(unicode)));
            if (_simpleMap[code] >= static_cast<int>(_glyphCount))
                Require(false, "TrueType cmap refers past the glyph inventory.");
        }
        return true;
    }

    TrueTypeGlyphBox ReadGlyph(unsigned gid, unsigned depth, size_t& components) const
    {
        RequireLimit(depth <= 16 && ++components <= 1024,
            "TrueType composite glyph budget exceeded.");
        RequireLimit(++_decodedComponents <= 100000,
            "TrueType aggregate component budget exceeded.");
        Require(gid < _glyphCount, "Invalid TrueType component glyph reference.");
        size_t start = _glyf.offset + _loca[gid], end = _glyf.offset + _loca[gid + 1];
        if (start == end) return {true, true};
        Require(end >= start && end - start >= 10, "Malformed embedded TrueType glyph header.");
        int contours = I16(start);
        int xMin = I16(start + 2), yMin = I16(start + 4);
        int xMax = I16(start + 6), yMax = I16(start + 8);
        Require(xMin <= xMax && yMin <= yMax, "Invalid TrueType glyph bounding box.");
        if (contours < 0) {
            Require(contours == -1, "Invalid TrueType composite contour count.");
            size_t cursor = start + 10;
            unsigned flags = 0;
            bool hasInk = false;
            bool hasInstructions = false;
            double unit = static_cast<double>(_units);
            TrueTypeGlyphBox bounds{true, false, xMin / unit, yMin / unit, xMax / unit, yMax / unit};
            do {
                Require(end - cursor >= 4, "Truncated TrueType component header.");
                flags = U16(cursor);
                hasInstructions |= (flags & 256) != 0;
                unsigned child = U16(cursor + 2);
                cursor += 4;
                Require(!(flags & 0xe010), "Invalid TrueType component flags.");
                if (!(flags & 2)) return {}; // Point attachment needs outline-point indices.
                bool words = flags & 1;
                Require(end - cursor >= (words ? 4u : 2u), "Truncated TrueType component offset.");
                double x = words ? I16(cursor) : static_cast<int8_t>(Byte(cursor));
                double y = words ? I16(cursor + 2) : static_cast<int8_t>(Byte(cursor + 1));
                cursor += words ? 4 : 2;
                double a = 1, b = 0, c = 0, d = 1;
                unsigned transforms = !!(flags & 8) + !!(flags & 64) + !!(flags & 128);
                Require(transforms <= 1 && (flags & 0x1800) != 0x1800,
                    "Ambiguous TrueType component transform.");
                size_t transformBytes = flags & 8 ? 2 : flags & 64 ? 4 : flags & 128 ? 8 : 0;
                Require(end - cursor >= transformBytes, "Truncated TrueType component transform.");
                if (flags & 8) a = d = I16(cursor) / 16384.;
                else if (flags & 64) { a = I16(cursor) / 16384.; d = I16(cursor + 2) / 16384.; }
                else if (flags & 128) {
                    a = I16(cursor) / 16384.; b = I16(cursor + 2) / 16384.;
                    c = I16(cursor + 4) / 16384.; d = I16(cursor + 6) / 16384.;
                }
                cursor += transformBytes;
                if (transformBytes && (x || y) && !(flags & 0x1800)) return {};
                if (flags & 0x800) {
                    double tx = a * x + c * y;
                    y = b * x + d * y; x = tx;
                }
                auto box = ReadGlyph(child, depth + 1, components);
                if (!box.known) return {};
                if (!box.empty) {
                    hasInk = true;
                    for (double px : {box.left, box.right}) for (double py : {box.bottom, box.top}) {
                        double tx = a * px + c * py + x / unit;
                        double ty = b * px + d * py + y / unit;
                        RequireLimit(std::abs(tx) <= 64 && std::abs(ty) <= 64,
                            "TrueType composite glyph bound exceeds its limit.");
                        bounds.left = std::min(bounds.left, tx); bounds.right = std::max(bounds.right, tx);
                        bounds.bottom = std::min(bounds.bottom, ty); bounds.top = std::max(bounds.top, ty);
                    }
                }
            } while (flags & 32);
            if (hasInstructions) {
                Require(end - cursor >= 2, "Truncated TrueType composite instructions.");
                unsigned instructions = U16(cursor); cursor += 2;
                Require(instructions <= end - cursor, "Truncated TrueType composite instruction bytes.");
            }
            for (double value : {bounds.left, bounds.bottom, bounds.right, bounds.top})
                RequireLimit(std::abs(value) <= 64, "TrueType composite glyph bound exceeds its limit.");
            bounds.empty = !hasInk;
            return bounds;
        }
        if (contours == 0) return {true, true};
        size_t cursor = start + 10;
        Require(static_cast<size_t>(contours) <= (end - cursor) / 2,
            "Malformed TrueType contour endpoints.");
        unsigned previous = 0, points = 0;
        for (int contour = 0; contour < contours; ++contour) {
            unsigned last = U16(cursor);
            Require(contour == 0 || last > previous, "Unordered TrueType contour endpoints.");
            previous = last;
            points = last + 1;
            cursor += 2;
        }
        RequireLimit(points <= 65535, "TrueType glyph point budget exceeded.");
        RequireLimit(points <= 2000000 - _decodedPoints,
            "TrueType aggregate point budget exceeded.");
        _decodedPoints += points;
        Require(cursor <= end && end - cursor >= 2, "Malformed TrueType glyph instructions.");
        unsigned instructions = U16(cursor);
        cursor += 2;
        Require(instructions <= end - cursor, "Malformed TrueType glyph instruction length.");
        cursor += instructions;
        std::vector<unsigned char> flags;
        flags.reserve(points);
        while (flags.size() < points) {
            Require(cursor < end, "Truncated TrueType glyph flags.");
            unsigned char flag = static_cast<unsigned char>(_bytes[cursor++]);
            Require(!(flag & 0x80), "Invalid TrueType glyph flag.");
            unsigned repeat = 0;
            if (flag & 0x08) {
                Require(cursor < end, "Truncated TrueType glyph flag repeat.");
                repeat = static_cast<unsigned char>(_bytes[cursor++]);
            }
            Require(repeat + 1 <= points - flags.size(), "TrueType glyph flag repeat exceeds points.");
            flags.insert(flags.end(), repeat + 1, flag);
        }
        int lowX = xMin, highX = xMax, lowY = yMin, highY = yMax;
        int coordinate = 0;
        for (unsigned char flag : flags) {
            int delta = 0;
            if (flag & 0x02) {
                Require(cursor < end, "Truncated TrueType x coordinate.");
                delta = static_cast<unsigned char>(_bytes[cursor++]);
                if (!(flag & 0x10)) delta = -delta;
            } else if (!(flag & 0x10)) {
                Require(end - cursor >= 2, "Truncated TrueType x coordinate.");
                delta = I16(cursor); cursor += 2;
            }
            coordinate += delta;
            lowX = std::min(lowX, coordinate); highX = std::max(highX, coordinate);
        }
        coordinate = 0;
        for (unsigned char flag : flags) {
            int delta = 0;
            if (flag & 0x04) {
                Require(cursor < end, "Truncated TrueType y coordinate.");
                delta = static_cast<unsigned char>(_bytes[cursor++]);
                if (!(flag & 0x20)) delta = -delta;
            } else if (!(flag & 0x20)) {
                Require(end - cursor >= 2, "Truncated TrueType y coordinate.");
                delta = I16(cursor); cursor += 2;
            }
            coordinate += delta;
            lowY = std::min(lowY, coordinate); highY = std::max(highY, coordinate);
        }
        for (int value : {lowX, lowY, highX, highY})
            RequireLimit(std::abs(static_cast<double>(value) / _units) <= 64,
                "TrueType glyph bound exceeds the resource limit.");
        double unit = static_cast<double>(_units);
        return {true, false, lowX / unit, lowY / unit, highX / unit, highY / unit};
    }

    friend std::optional<TrueTypeGlyphBounds> ReadEmbeddedTrueTypeGlyphBounds(QPDFObjectHandle);
};

std::optional<TrueTypeGlyphBounds> ReadEmbeddedTrueTypeGlyphBounds(QPDFObjectHandle font)
{
    bool cid = font.getKey("/Subtype").isNameAndEquals("/Type0");
    QPDFObjectHandle descendant = font;
    if (cid) {
        if (!font.getKey("/Encoding").isNameAndEquals("/Identity-H")) return std::nullopt;
        auto descendants = font.getKey("/DescendantFonts");
        if (!descendants.isArray() || descendants.getArrayNItems() != 1) return std::nullopt;
        descendant = descendants.getArrayItem(0);
        if (!descendant.isDictionary() ||
            !descendant.getKey("/Subtype").isNameAndEquals("/CIDFontType2")) return std::nullopt;
    } else {
        if (!font.getKey("/Subtype").isNameAndEquals("/TrueType") ||
            !font.getKey("/Encoding").isNameAndEquals("/WinAnsiEncoding")) return std::nullopt;
    }
    auto descriptor = descendant.getKey("/FontDescriptor");
    if (!descriptor.isDictionary()) return std::nullopt;
    if (!cid) {
        auto flags = descriptor.getKey("/Flags");
        if (!flags.isInteger() || !(flags.getIntValue() & 32) || (flags.getIntValue() & 4))
            return std::nullopt;
    }
    auto embedded = descriptor.getKey("/FontFile2");
    if (!embedded.isStream()) return std::nullopt;
    auto buffer = embedded.getStreamData(qpdf_dl_all);
    RequireLimit(buffer->getSize() <= 32ull * 1024 * 1024,
        "Embedded TrueType font exceeds the glyph-bound budget.");
    Require(buffer->getSize() >= 12, "Truncated embedded TrueType font.");
    TrueTypeGlyphBounds result;
    result._bytes.assign(reinterpret_cast<char const*>(buffer->getBuffer()), buffer->getSize());
    result._cid = cid;
    if (result.U32(0) != 0x00010000) return std::nullopt;
    unsigned tables = result.U16(4);
    RequireLimit(tables <= 128, "Embedded TrueType table budget exceeded.");
    Require(tables >= 4 && 12ull + 16ull * tables <= result._bytes.size(),
        "Malformed embedded TrueType table directory.");
    std::map<std::string, TrueTypeGlyphBounds::Table> directory;
    for (unsigned index = 0; index < tables; ++index) {
        size_t entry = 12 + 16 * index;
        std::string tag = result._bytes.substr(entry, 4);
        size_t offset = result.U32(entry + 8), length = result.U32(entry + 12);
        result.Span(offset, length);
        Require(directory.emplace(tag, TrueTypeGlyphBounds::Table{offset, length}).second,
            "Duplicate embedded TrueType table.");
    }
    std::vector<TrueTypeGlyphBounds::Table> extents;
    for (auto const& entry : directory)
        if (entry.second.length) extents.push_back(entry.second);
    std::sort(extents.begin(), extents.end(), [](auto left, auto right) {
        return left.offset < right.offset;
    });
    for (size_t index = 1; index < extents.size(); ++index)
        Require(extents[index - 1].length <= extents[index].offset - extents[index - 1].offset,
            "Overlapping embedded TrueType tables.");
    auto required = [&](char const* tag) {
        auto found = directory.find(tag);
        Require(found != directory.end(), "Embedded TrueType required table is missing.");
        return found->second;
    };
    if (directory.count("gvar")) return std::nullopt; // Instance bounds need variation deltas.
    auto head = required("head"), maxp = required("maxp"), loca = required("loca");
    result._glyf = required("glyf");
    Require(head.length >= 54 && maxp.length >= 6 &&
            result.U32(head.offset + 12) == 0x5f0f3cf5 &&
            result.U32(maxp.offset) == 0x00010000,
        "Malformed embedded TrueType header.");
    result._units = result.U16(head.offset + 18);
    Require(result._units >= 16 && result._units <= 16384,
        "Invalid embedded TrueType units per em.");
    result._glyphCount = result.U16(maxp.offset + 4);
    Require(result._glyphCount >= 1 && result._glyphCount <= 65535,
        "Invalid embedded TrueType glyph count.");
    int locaFormat = result.I16(head.offset + 50);
    Require(locaFormat == 0 || locaFormat == 1, "Unsupported TrueType loca format.");
    size_t entrySize = locaFormat == 0 ? 2 : 4;
    Require(loca.length >= (static_cast<size_t>(result._glyphCount) + 1) * entrySize,
        "Truncated embedded TrueType loca table.");
    result._loca.reserve(static_cast<size_t>(result._glyphCount) + 1);
    size_t prior = 0;
    for (unsigned index = 0; index <= result._glyphCount; ++index) {
        size_t offset = loca.offset + static_cast<size_t>(index) * entrySize;
        size_t value = locaFormat == 0 ? result.U16(offset) * 2ull : result.U32(offset);
        Require(value >= prior && value <= result._glyf.length,
            "Malformed or unordered embedded TrueType loca offsets.");
        result._loca.push_back(value);
        prior = value;
    }
    if (cid) {
        auto map = descendant.getKey("/CIDToGIDMap");
        if (map.isNameAndEquals("/Identity")) {
            result._identityMap = true;
            return result;
        }
        if (!map.isStream()) return std::nullopt;
        auto bytes = map.getStreamData(qpdf_dl_all);
        RequireLimit(bytes->getSize() <= 131072,
            "PDF CID-to-glyph map exceeds its limit.");
        Require(bytes->getSize() % 2 == 0, "Malformed PDF CID-to-glyph map.");
        result._cidMap.reserve(bytes->getSize() / 2);
        for (size_t index = 0; index < bytes->getSize(); index += 2) {
            unsigned gid = (static_cast<unsigned>(static_cast<unsigned char>(bytes->getBuffer()[index])) << 8) |
                static_cast<unsigned>(static_cast<unsigned char>(bytes->getBuffer()[index + 1]));
            Require(gid < result._glyphCount, "PDF CID-to-glyph map exceeds font glyphs.");
            result._cidMap.push_back(gid);
        }
    } else {
        if (!result.ReadCmap(required("cmap"))) return std::nullopt;
    }
    return result;
}
