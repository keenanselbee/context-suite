#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <fpdfview.h>
#include <fpdf_edit.h>
#include <fpdf_formfill.h>
#include <qpdf/QPDF.hh>
#include <qpdf/QPDFPageDocumentHelper.hh>
#include <algorithm>
#include <array>
#include <bit>
#include <charconv>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <string_view>
#include <vector>

namespace {
constexpr uint32_t Magic = 0x31525043; // CPR1, followed by protocol version and reply kind.
constexpr int64_t MaximumSource = 128 * 1024 * 1024;
constexpr int MaximumPages = 4096;
constexpr int Dpi = 150;
constexpr int64_t MaximumPixels = 16000000;
static_assert(std::endian::native == std::endian::little);
struct ProcessingLimit {};

template<auto Close, typename T>
auto Own(T value) {
    if (!value) throw std::runtime_error("No PDF resource.");
    return std::unique_ptr<std::remove_pointer_t<T>, decltype(Close)>(value, Close);
}

std::vector<uint8_t> Input() {
    const auto handle = GetStdHandle(STD_INPUT_HANDLE);
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(handle, &size) || size.QuadPart <= 0 ||
        !SetFilePointerEx(handle, {}, nullptr, FILE_BEGIN)) throw std::runtime_error("Invalid bounded input.");
    if (size.QuadPart > MaximumSource) throw ProcessingLimit{};
    std::vector<uint8_t> bytes(static_cast<size_t>(size.QuadPart));
    size_t offset = 0;
    while (offset < bytes.size()) {
        DWORD count = 0;
        const auto requested = static_cast<DWORD>((std::min)(bytes.size() - offset, size_t{65536}));
        if (!ReadFile(handle, bytes.data() + offset, requested, &count, nullptr) || count == 0)
            throw std::runtime_error("Incomplete input.");
        offset += count;
    }
    if (bytes.size() < 5 || std::memcmp(bytes.data(), "%PDF-", 5) != 0) throw std::runtime_error("Not PDF.");
    return bytes;
}

void Output(const void* data, size_t bytes) {
    const auto handle = GetStdHandle(STD_OUTPUT_HANDLE);
    const auto* cursor = static_cast<const uint8_t*>(data);
    while (bytes != 0) {
        DWORD written = 0;
        const auto count = static_cast<DWORD>((std::min)(bytes, size_t{65536}));
        if (!WriteFile(handle, cursor, count, &written, nullptr) || written == 0) throw std::runtime_error("Output closed.");
        cursor += written; bytes -= written;
    }
}

struct Geometry {
    uint32_t index, rotation, width, height;
    double widthPoints, heightPoints;
};
static_assert(sizeof(Geometry) == 32);

Geometry Measure(FPDF_PAGE page, int index, double userUnit) {
    const double w = FPDF_GetPageWidthF(page) * userUnit, h = FPDF_GetPageHeightF(page) * userUnit;
    const int rotation = FPDFPage_GetRotation(page);
    if (!std::isfinite(w) || !std::isfinite(h) || w <= 0 || h <= 0 || rotation < 0 || rotation > 3)
        throw std::runtime_error("Unsupported page geometry.");
    if (w > 14400 || h > 14400) throw ProcessingLimit{};
    const auto width = static_cast<uint32_t>(std::ceil(w * Dpi / 72));
    const auto height = static_cast<uint32_t>(std::ceil(h * Dpi / 72));
    if (width > 16384 || height > 16384 || static_cast<int64_t>(width) * height > MaximumPixels) throw ProcessingLimit{};
    return {static_cast<uint32_t>(index), static_cast<uint32_t>(rotation), width, height, w, h};
}

struct Forms : FPDF_FORMFILLINFO {
    FPDF_PAGE page = nullptr;
    int index = -1;
    FPDF_FORMHANDLE handle = nullptr;
    explicit Forms(FPDF_DOCUMENT document) : FPDF_FORMFILLINFO{} {
        version = 1;
        FFI_GetPage = [](FPDF_FORMFILLINFO* self, FPDF_DOCUMENT, int index) -> FPDF_PAGE {
            auto* state = static_cast<Forms*>(self);
            return state->index == index ? state->page : nullptr;
        };
        FFI_GetCurrentPage = [](FPDF_FORMFILLINFO* self, FPDF_DOCUMENT) -> FPDF_PAGE { return static_cast<Forms*>(self)->page; };
        FFI_GetRotation = [](FPDF_FORMFILLINFO*, FPDF_PAGE page) -> int { return FPDFPage_GetRotation(page); };
        handle = FPDFDOC_InitFormFillEnvironment(document, this);
        if (!handle) throw std::runtime_error("No form environment.");
        FPDF_RemoveFormFieldHighlight(handle);
        // No actions, JavaScript platform, navigation, files or network callbacks.
    }
    ~Forms() { if (handle) FPDFDOC_ExitFormFillEnvironment(handle); }
};

std::vector<double> PageUnits(const std::vector<uint8_t>& bytes, int count) {
    // PDFium 8044 measures boxes in default user-space coordinates without
    // applying UserUnit. Read actual leaf dictionaries with the reviewed qpdf
    // parser; never scan arbitrary PDF bytes or inherit this page-only key.
    QPDF dictionary;
    dictionary.setSuppressWarnings(true);
    dictionary.setAttemptRecovery(false);
    dictionary.processMemoryFile("PDF page geometry", reinterpret_cast<const char*>(bytes.data()), bytes.size());
    if (dictionary.isEncrypted()) throw std::runtime_error("Protected PDF geometry.");
    const auto pages = QPDFPageDocumentHelper(dictionary).getAllPages();
    if (pages.size() != static_cast<size_t>(count)) throw std::runtime_error("Inconsistent PDF page tree.");
    std::vector<double> units;
    units.reserve(pages.size());
    for (auto page : pages) {
        const auto value = page.getObjectHandle().getKey("/UserUnit");
        double unit = 1;
        if (!value.isNull()) {
            if (!value.isNumber()) throw std::runtime_error("Invalid PDF user unit.");
            unit = value.getNumericValue();
            if (!std::isfinite(unit) || unit <= 0) throw std::runtime_error("Invalid PDF user unit.");
            if (unit > 75000) throw ProcessingLimit{};
        }
        units.push_back(unit);
    }
    if (dictionary.anyWarnings()) throw std::runtime_error("Uncertain PDF geometry.");
    return units;
}

void Inspect(FPDF_DOCUMENT document, int pages, const std::vector<double>& units) {
    std::vector<Geometry> geometry;
    geometry.reserve(pages);
    for (int index = 0; index < pages; ++index) {
        auto page = Own<FPDF_ClosePage>(FPDF_LoadPage(document, index));
        geometry.push_back(Measure(page.get(), index, units[index]));
    }
    const std::array<uint32_t, 8> header{Magic, 1, 1, static_cast<uint32_t>(pages), 0, 0, 0, static_cast<uint32_t>(geometry.size() * sizeof(Geometry))};
    Output(header.data(), sizeof(header));
    Output(geometry.data(), geometry.size() * sizeof(Geometry));
}

void Render(FPDF_DOCUMENT document, int index, double userUnit) {
    auto page = Own<FPDF_ClosePage>(FPDF_LoadPage(document, index));
    const auto geometry = Measure(page.get(), index, userUnit);
    const auto stride = geometry.width * 4;
    std::vector<uint8_t> pixels(static_cast<size_t>(stride) * geometry.height, 0);
    auto bitmap = Own<FPDFBitmap_Destroy>(FPDFBitmap_CreateEx(static_cast<int>(geometry.width), static_cast<int>(geometry.height),
        FPDFBitmap_BGRA, pixels.data(), static_cast<int>(stride)));
    Forms forms(document);
    forms.page = page.get(); forms.index = index;
    FORM_OnAfterLoadPage(page.get(), forms.handle);
    FPDF_RenderPageBitmap(bitmap.get(), page.get(), 0, 0, static_cast<int>(geometry.width), static_cast<int>(geometry.height), 0, FPDF_ANNOT);
    FPDF_FFLDraw(forms.handle, bitmap.get(), page.get(), 0, 0, static_cast<int>(geometry.width), static_cast<int>(geometry.height), 0, FPDF_ANNOT);
    FORM_OnBeforeClosePage(page.get(), forms.handle);
    forms.page = nullptr; forms.index = -1;
    const std::array<uint32_t, 8> header{Magic, 1, 2, static_cast<uint32_t>(index), geometry.width, geometry.height, stride, static_cast<uint32_t>(pixels.size())};
    Output(header.data(), sizeof(header));
    Output(pixels.data(), pixels.size());
}
}

int main(int argc, char** argv) {
    int index = -1;
    const bool inspect = argc == 2 && std::string_view(argv[1]) == "--inspect";
    if (!inspect) {
        if (argc != 3 || std::string_view(argv[1]) != "--render-page") return 2;
        const auto argument = std::string_view(argv[2]);
        const auto [end, error] = std::from_chars(argument.data(), argument.data() + argument.size(), index);
        if (error != std::errc{} || end != argument.data() + argument.size() || index < 0) return 2;
    }
    FPDF_InitLibrary();
    FPDF_SetSandBoxPolicy(FPDF_POLICY_MACHINETIME_ACCESS, 0);
    int result = 0;
    try {
        auto bytes = Input();
        auto document = Own<FPDF_CloseDocument>(FPDF_LoadMemDocument64(bytes.data(), bytes.size(), nullptr));
        const auto formType = FPDF_GetFormType(document.get());
        if (FPDF_GetSecurityHandlerRevision(document.get()) != -1 || formType == FORMTYPE_XFA_FULL || formType == FORMTYPE_XFA_FOREGROUND)
            throw std::runtime_error("Protected or unsupported forms.");
        const int pages = FPDF_GetPageCount(document.get());
        if (pages < 1 || index >= pages) throw std::runtime_error("Unsupported page count or index.");
        if (pages > MaximumPages) throw ProcessingLimit{};
        const auto units = PageUnits(bytes, pages);
        if (inspect) Inspect(document.get(), pages, units); else Render(document.get(), index, units[index]);
    } catch (const ProcessingLimit&) {
        // A recognized refusal is an explicit complete protocol reply. These
        // guards run before either operation writes any inventory or pixels.
        const std::array<uint32_t, 8> failure{Magic, 1, 3, 1, 0, 0, 0, 0};
        try { Output(failure.data(), sizeof(failure)); }
        catch (...) { result = 3; }
    } catch (...) {
        result = 3; // No source contents, filenames or engine diagnostics leave this host.
    }
    FPDF_DestroyLibrary();
    return result;
}
