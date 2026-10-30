#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <qpdf/QPDF.hh>
#include <qpdf/QPDFObjectHandle.hh>
#include <qpdf/Pipeline.hh>
#include <algorithm>
#include <array>
#include <bit>
#include <charconv>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
constexpr uint32_t Magic = 0x31564943; // CIV1
constexpr uint64_t MaximumInput = 128 * 1024 * 1024;
constexpr uint64_t MaximumPixels = 16000000;
constexpr uint64_t MaximumTotalPixels = 128000000;
constexpr int MaximumPages = 4096;
static_assert(std::endian::native == std::endian::little);

void Require(bool value) { if (!value) throw std::runtime_error("Invalid candidate."); }

struct Page {
    uint32_t index{}, width{}, height{}, bits{}, components{}, alpha{}, profileBytes{}, reserved{};
    double widthPoints{}, heightPoints{};
    uint64_t colorBytes{}, alphaBytes{};
    std::array<uint8_t, 32> colorHash{}, alphaHash{}, profileHash{};
};
static_assert(sizeof(Page) == 160);

std::vector<uint8_t> Input() {
    const auto handle = GetStdHandle(STD_INPUT_HANDLE);
    LARGE_INTEGER size{};
    Require(GetFileSizeEx(handle, &size) && size.QuadPart > 0 && static_cast<uint64_t>(size.QuadPart) <= MaximumInput &&
        SetFilePointerEx(handle, {}, nullptr, FILE_BEGIN));
    std::vector<uint8_t> bytes(static_cast<size_t>(size.QuadPart));
    for (size_t offset = 0; offset < bytes.size();) {
        DWORD count{};
        Require(ReadFile(handle, bytes.data() + offset, static_cast<DWORD>(std::min(bytes.size() - offset, size_t{65536})), &count, nullptr) && count);
        offset += count;
    }
    Require(bytes.size() >= 8 && std::memcmp(bytes.data(), "%PDF-1.7", 8) == 0);
    return bytes;
}

void Output(const void* data, size_t length) {
    auto cursor = static_cast<const uint8_t*>(data);
    while (length) {
        DWORD count{};
        Require(WriteFile(GetStdHandle(STD_OUTPUT_HANDLE), cursor, static_cast<DWORD>(std::min(length, size_t{65536})), &count, nullptr) && count);
        cursor += count; length -= count;
    }
}

// Hash the independently decoded stream incrementally. Never allocate an
// inflated image or send its samples over the native reply pipe.
class QPDF_DLL_CLASS Digest final : public Pipeline {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    uint64_t limit;
public:
    uint64_t count = 0;
    std::array<uint8_t, 32> value{};
    std::string content;
    bool capture, finished = false;
    explicit Digest(uint64_t maximum, bool captureContent = false) : Pipeline("bounded image PDF digest", nullptr), limit(maximum), capture(captureContent) {
        Require(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0);
        if (BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) < 0) {
            BCryptCloseAlgorithmProvider(algorithm, 0); algorithm = nullptr; Require(false);
        }
    }
    ~Digest() override {
        if (hash) BCryptDestroyHash(hash);
        if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    }
    void write(const unsigned char* data, size_t length) override {
        Require(!finished && length <= limit - count);
        count += length;
        if (capture) content.append(reinterpret_cast<const char*>(data), length);
        while (length) {
            const auto part = static_cast<ULONG>(std::min(length, size_t{65536}));
            Require(BCryptHashData(hash, const_cast<PUCHAR>(data), part, 0) >= 0);
            data += part; length -= part;
        }
    }
    void finish() override {
        Require(!finished && BCryptFinishHash(hash, value.data(), static_cast<ULONG>(value.size()), 0) >= 0);
        finished = true;
    }
};

void Keys(QPDFObjectHandle object, std::initializer_list<const char*> names) {
    Require(object.isDictionary());
    std::set<std::string> expected;
    for (const auto name : names) expected.insert(name);
    Require(object.getKeys() == expected);
}

int64_t Integer(QPDFObjectHandle value, int64_t low, int64_t high, bool indirect = false) {
    Require(value.isIndirect() == indirect && value.isInteger());
    const auto result = value.getIntValue(); Require(result >= low && result <= high); return result;
}

double Number(QPDFObjectHandle value) {
    Require(!value.isIndirect() && value.isNumber());
    const auto number = value.getNumericValue(); Require(std::isfinite(number)); return number;
}

void Name(QPDFObjectHandle object, const char* name) {
    Require(!object.isIndirect() && object.isNameAndEquals(name));
}

void Array(QPDFObjectHandle object, int count) {
    Require(!object.isIndirect() && object.isArray() && object.getArrayNItems() == count);
}

void ReadStream(QPDFObjectHandle stream, Digest& digest) {
    bool filtered = false;
    Require(stream.pipeStreamData(&digest, &filtered, 0, qpdf_dl_generalized, true, false) && digest.finished);
}

void Content(QPDFObjectHandle stream, const Page& page) {
    Require(stream.isStream()); Keys(stream.getDict(), {"/Length"});
    const auto length = Integer(stream.getDict().getKey("/Length"), 1, 1024);
    Digest digest(static_cast<uint64_t>(length), true); ReadStream(stream, digest);
    Require(digest.count == static_cast<uint64_t>(length));
    std::istringstream input(digest.content); input.imbue(std::locale::classic());
    std::vector<std::string> tokens;
    std::string token;
    while (input >> token) { Require(tokens.size() < 11); tokens.push_back(token); }
    Require(tokens.size() == 11 && tokens[0] == "q" && tokens[7] == "cm" && tokens[8] == "/Im0" && tokens[9] == "Do" && tokens[10] == "Q");
    const std::array<double, 6> expected{page.widthPoints, 0, 0, page.heightPoints, 0, 0};
    for (size_t index = 0; index < expected.size(); ++index) {
        const auto& text = tokens[index + 1];
        Require(text.find_first_not_of("0123456789+.-") == std::string::npos);
        double value{};
        const auto [end, error] = std::from_chars(text.data(), text.data() + text.size(), value);
        Require(error == std::errc{} && end == text.data() + text.size() && value == expected[index]);
    }
}

std::vector<Page> Inspect(QPDF& document) {
    Require(document.getPDFVersion() == "1.7" && !document.isEncrypted());
    const auto trailer = document.getTrailer(); Keys(trailer, {"/Root", "/Size"});
    const auto size = Integer(trailer.getKey("/Size"), 8, 2 + 7 * MaximumPages + 1);
    const auto objects = document.getAllObjects(); Require(objects.size() == static_cast<size_t>(size - 1));
    std::set<QPDFObjGen> claimed;
    const auto Claim = [&](QPDFObjectHandle object) {
        Require(object.isIndirect() && object.getObjGen().getGen() == 0 && claimed.insert(object.getObjGen()).second);
    };
    auto catalog = trailer.getKey("/Root"); Claim(catalog); Keys(catalog, {"/Type", "/Pages"}); Name(catalog.getKey("/Type"), "/Catalog");
    auto tree = catalog.getKey("/Pages"); Claim(tree); Keys(tree, {"/Type", "/Count", "/Kids"}); Name(tree.getKey("/Type"), "/Pages");
    const auto count = static_cast<int>(Integer(tree.getKey("/Count"), 1, MaximumPages));
    const auto kids = tree.getKey("/Kids"); Array(kids, count);
    uint64_t totalPixels = 0;
    std::vector<Page> pages; pages.reserve(count);
    for (int index = 0; index < count; ++index) {
        auto object = kids.getArrayItem(index); Claim(object);
        Keys(object, {"/Type", "/Parent", "/MediaBox", "/Resources", "/Contents"}); Name(object.getKey("/Type"), "/Page");
        Require(object.getKey("/Parent").isIndirect() && object.getKey("/Parent").getObjGen() == tree.getObjGen());
        const auto box = object.getKey("/MediaBox"); Array(box, 4);
        Require(Number(box.getArrayItem(0)) == 0 && Number(box.getArrayItem(1)) == 0);
        Page page{}; page.index = static_cast<uint32_t>(index); page.widthPoints = Number(box.getArrayItem(2)); page.heightPoints = Number(box.getArrayItem(3));
        Require(page.widthPoints >= 0.01 && page.widthPoints <= 14400 && page.heightPoints >= 0.01 && page.heightPoints <= 14400);
        const auto resources = object.getKey("/Resources"); Require(!resources.isIndirect()); Keys(resources, {"/XObject"});
        const auto xobject = resources.getKey("/XObject"); Require(!xobject.isIndirect()); Keys(xobject, {"/Im0"});
        auto image = xobject.getKey("/Im0"); Claim(image); Require(image.isStream());
        const auto dictionary = image.getDict();
        page.alpha = dictionary.hasKey("/SMask") ? 1 : 0;
        if (page.alpha) Keys(dictionary, {"/Type", "/Subtype", "/Width", "/Height", "/BitsPerComponent", "/Interpolate", "/ColorSpace", "/SMask", "/Filter", "/Length"});
        else Keys(dictionary, {"/Type", "/Subtype", "/Width", "/Height", "/BitsPerComponent", "/Interpolate", "/ColorSpace", "/Filter", "/Length"});
        const auto Geometry = [&](QPDFObjectHandle dict) {
            Name(dict.getKey("/Type"), "/XObject"); Name(dict.getKey("/Subtype"), "/Image"); Name(dict.getKey("/Filter"), "/FlateDecode");
            Require(!dict.getKey("/Interpolate").isIndirect() && dict.getKey("/Interpolate").isBool() && !dict.getKey("/Interpolate").getBoolValue());
            Claim(dict.getKey("/Length")); Integer(dict.getKey("/Length"), 1, MaximumInput, true);
            return std::array<uint32_t, 3>{static_cast<uint32_t>(Integer(dict.getKey("/Width"), 1, 16384)),
                static_cast<uint32_t>(Integer(dict.getKey("/Height"), 1, 16384)), static_cast<uint32_t>(Integer(dict.getKey("/BitsPerComponent"), 8, 16))};
        };
        const auto dimensions = Geometry(dictionary); page.width = dimensions[0]; page.height = dimensions[1]; page.bits = dimensions[2];
        Require(page.bits == 8 || page.bits == 16);
        const auto pixels = static_cast<uint64_t>(page.width) * page.height; totalPixels += pixels;
        Require(pixels <= MaximumPixels && totalPixels <= MaximumTotalPixels);
        const auto colorSpace = dictionary.getKey("/ColorSpace"); Array(colorSpace, 2); Name(colorSpace.getArrayItem(0), "/ICCBased");
        auto profile = colorSpace.getArrayItem(1); Claim(profile); Require(profile.isStream());
        const auto profileDictionary = profile.getDict(); Keys(profileDictionary, {"/N", "/Alternate", "/Length"});
        page.components = static_cast<uint32_t>(Integer(profileDictionary.getKey("/N"), 1, 3)); Require(page.components == 1 || page.components == 3);
        Name(profileDictionary.getKey("/Alternate"), page.components == 1 ? "/DeviceGray" : "/DeviceRGB");
        page.profileBytes = static_cast<uint32_t>(Integer(profileDictionary.getKey("/Length"), 132, 4 * 1024 * 1024));
        Digest profileDigest(page.profileBytes); ReadStream(profile, profileDigest); Require(profileDigest.count == page.profileBytes); page.profileHash = profileDigest.value;
        page.colorBytes = pixels * page.components * page.bits / 8;
        Digest color(page.colorBytes); ReadStream(image, color); Require(color.count == page.colorBytes); page.colorHash = color.value;
        if (page.alpha) {
            auto mask = dictionary.getKey("/SMask"); Claim(mask); Require(mask.isStream());
            const auto maskDictionary = mask.getDict();
            Keys(maskDictionary, {"/Type", "/Subtype", "/Width", "/Height", "/BitsPerComponent", "/Interpolate", "/ColorSpace", "/Filter", "/Length"});
            Require(Geometry(maskDictionary) == dimensions); Name(maskDictionary.getKey("/ColorSpace"), "/DeviceGray");
            page.alphaBytes = pixels * page.bits / 8;
            Digest alpha(page.alphaBytes); ReadStream(mask, alpha); Require(alpha.count == page.alphaBytes); page.alphaHash = alpha.value;
        }
        auto content = object.getKey("/Contents"); Claim(content); Content(content, page);
        pages.push_back(page);
    }
    Require(claimed.size() == objects.size());
    for (const auto& object : objects) Require(claimed.contains(object.getObjGen()));
    Require(!document.anyWarnings());
    return pages;
}
}

int main(int argc, char** argv) {
    if (argc != 2 || std::string_view(argv[1]) != "--inspect") return 2;
    try {
        auto bytes = Input();
        QPDF document; document.setSuppressWarnings(true); document.setAttemptRecovery(false);
        document.processMemoryFile("image PDF candidate", reinterpret_cast<const char*>(bytes.data()), bytes.size());
        const auto pages = Inspect(document);
        const std::array<uint32_t, 4> header{Magic, 1, static_cast<uint32_t>(pages.size()), static_cast<uint32_t>(pages.size() * sizeof(Page))};
        Output(header.data(), sizeof(header)); Output(pages.data(), pages.size() * sizeof(Page));
        return 0;
    } catch (...) { return 3; } // No source paths, samples, profiles or diagnostics leave the host.
}
