#include <fpdfview.h>
#include <fpdf_edit.h>
#include <fpdf_formfill.h>
#include <fpdf_save.h>
#include <fpdf_signature.h>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
template<auto Close, typename T>
auto Own(T value) {
    if (!value) throw std::runtime_error("PDFium returned no resource.");
    return std::unique_ptr<std::remove_pointer_t<T>, decltype(Close)>(value, Close);
}

std::vector<uint8_t> Read(const std::filesystem::path& path, size_t limit) {
    const auto size = std::filesystem::file_size(path);
    if (size == 0 || size > limit) throw std::runtime_error("Input limit exceeded.");
    std::vector<uint8_t> bytes(static_cast<size_t>(size));
    std::ifstream stream(path, std::ios::binary);
    if (!stream.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size())))
        throw std::runtime_error("Cannot read complete input.");
    return bytes;
}

struct Forms : FPDF_FORMFILLINFO {
    FPDF_PAGE current = nullptr;
    int index = -1;
    FPDF_FORMHANDLE handle = nullptr;
    explicit Forms(FPDF_DOCUMENT document) : FPDF_FORMFILLINFO{} {
        version = 1;
        FFI_GetPage = [](FPDF_FORMFILLINFO* self, FPDF_DOCUMENT, int pageIndex) -> FPDF_PAGE {
            auto* state = static_cast<Forms*>(self);
            return state->index == pageIndex ? state->current : nullptr;
        };
        FFI_GetCurrentPage = [](FPDF_FORMFILLINFO* self, FPDF_DOCUMENT) -> FPDF_PAGE {
            return static_cast<Forms*>(self)->current;
        };
        FFI_GetRotation = [](FPDF_FORMFILLINFO*, FPDF_PAGE page) -> int { return FPDFPage_GetRotation(page); };
        handle = FPDFDOC_InitFormFillEnvironment(document, this);
        if (!handle) throw std::runtime_error("Cannot initialize form rendering.");
        FPDF_RemoveFormFieldHighlight(handle);
        // No JavaScript platform, navigation, file, network or document-action
        // callbacks are supplied. The pinned build has neither V8 nor XFA.
    }
    ~Forms() { if (handle) FPDFDOC_ExitFormFillEnvironment(handle); }
};

void Render(FPDF_DOCUMENT document, const std::filesystem::path& prefix, int dpi, bool transparent, bool widgets) {
    const int pages = FPDF_GetPageCount(document);
    if (pages < 1 || pages > 16 || dpi < 1 || dpi > 300) throw std::runtime_error("Evaluation page or DPI limit exceeded.");
    Forms forms(document);
    std::cout << "{\"pages\":[";
    for (int index = 0; index < pages; ++index) {
        auto page = Own<FPDF_ClosePage>(FPDF_LoadPage(document, index));
        const double widthPoints = FPDF_GetPageWidthF(page.get());
        const double heightPoints = FPDF_GetPageHeightF(page.get());
        if (!std::isfinite(widthPoints) || !std::isfinite(heightPoints) || widthPoints <= 0 || heightPoints <= 0 ||
            widthPoints > 14400 || heightPoints > 14400) throw std::runtime_error("Invalid page geometry.");
        const auto width = static_cast<int>(std::ceil(widthPoints * dpi / 72));
        const auto height = static_cast<int>(std::ceil(heightPoints * dpi / 72));
        if (static_cast<int64_t>(width) * height > 16000000) throw std::runtime_error("Evaluation pixel limit exceeded.");
        auto bitmap = Own<FPDFBitmap_Destroy>(FPDFBitmap_Create(width, height, 1));
        if (!FPDFBitmap_FillRect(bitmap.get(), 0, 0, width, height, transparent ? 0 : 0xffffffff))
            throw std::runtime_error("Cannot initialize bitmap.");
        forms.current = page.get(); forms.index = index;
        FORM_OnAfterLoadPage(page.get(), forms.handle);
        FPDF_RenderPageBitmap(bitmap.get(), page.get(), 0, 0, width, height, 0, FPDF_ANNOT);
        if (widgets) FPDF_FFLDraw(forms.handle, bitmap.get(), page.get(), 0, 0, width, height, 0, FPDF_ANNOT);
        const auto stride = FPDFBitmap_GetStride(bitmap.get());
        if (stride < width * 4 || stride > width * 4 + 16) throw std::runtime_error("Unexpected bitmap stride.");
        const auto* buffer = static_cast<const char*>(FPDFBitmap_GetBuffer(bitmap.get()));
        std::ofstream output(prefix.wstring() + L"-" + std::to_wstring(index + 1) + L".bgra", std::ios::binary);
        output.write(buffer, static_cast<std::streamsize>(stride) * height);
        if (!output) throw std::runtime_error("Cannot write rendered pixels.");
        if (index != 0) std::cout << ',';
        std::cout << "{\"widthPoints\":" << widthPoints << ",\"heightPoints\":" << heightPoints
            << ",\"width\":" << width << ",\"height\":" << height << ",\"stride\":" << stride << '}';
        FORM_OnBeforeClosePage(page.get(), forms.handle);
        forms.current = nullptr; forms.index = -1;
    }
    std::cout << "]}" << std::endl;
}

struct Writer : FPDF_FILEWRITE {
    std::ofstream output;
    size_t written = 0;
    explicit Writer(const std::filesystem::path& path) : FPDF_FILEWRITE{}, output(path, std::ios::binary) {
        version = 1;
        WriteBlock = [](FPDF_FILEWRITE* self, const void* data, unsigned long size) -> int {
            auto* writer = static_cast<Writer*>(self);
            if (writer->written + size > 16 * 1024 * 1024) return 0;
            writer->written += size;
            writer->output.write(static_cast<const char*>(data), size);
            return writer->output ? 1 : 0;
        };
    }
};

void ImagePdf(const std::filesystem::path& input, const std::filesystem::path& output) {
    // Fixed independently authored 64x48 BGRA fixture, not a customer converter.
    auto bytes = Read(input, 64 * 48 * 4);
    if (bytes.size() != 64 * 48 * 4) throw std::runtime_error("Unexpected image fixture size.");
    auto document = Own<FPDF_CloseDocument>(FPDF_CreateNewDocument());
    auto page = Own<FPDF_ClosePage>(FPDFPage_New(document.get(), 0, 64, 48));
    auto bitmap = Own<FPDFBitmap_Destroy>(FPDFBitmap_CreateEx(64, 48, FPDFBitmap_BGRA, bytes.data(), 64 * 4));
    auto image = Own<FPDFPageObj_Destroy>(FPDFPageObj_NewImageObj(document.get()));
    FPDF_PAGE pages[] = {page.get()};
    if (!FPDFImageObj_SetBitmap(pages, 1, image.get(), bitmap.get())) throw std::runtime_error("Cannot set PDF image.");
    const FS_MATRIX matrix{64, 0, 0, 48, 0, 0};
    if (!FPDFPageObj_SetMatrix(image.get(), &matrix)) throw std::runtime_error("Cannot place PDF image.");
    FPDFPage_InsertObject(page.get(), image.release());
    if (!FPDFPage_GenerateContent(page.get())) throw std::runtime_error("Cannot generate image page.");
    Writer writer(output);
    if (!FPDF_SaveWithVersion(document.get(), &writer, FPDF_NO_INCREMENTAL, 17)) throw std::runtime_error("Cannot save image PDF.");
    std::cout << "{\"pages\":1,\"widthPoints\":64,\"heightPoints\":48,\"bytes\":" << writer.written << '}';
}
}

int wmain(int argc, wchar_t** argv) {
    FPDF_InitLibrary();
    FPDF_SetSandBoxPolicy(FPDF_POLICY_MACHINETIME_ACCESS, 0);
    int result = 0;
    try {
        if (argc == 4 && std::wstring(argv[1]) == L"image-pdf") ImagePdf(argv[2], argv[3]);
        else {
            if (argc < 3) throw std::runtime_error("Expected bounded probe mode and generated PDF input.");
            const auto bytes = Read(argv[2], 16 * 1024 * 1024);
            const auto raw = FPDF_LoadMemDocument64(bytes.data(), bytes.size(), nullptr);
            if (!raw) {
                std::cout << "{\"loaded\":false,\"error\":" << FPDF_GetLastError() << '}';
                result = 2;
            } else {
                auto document = Own<FPDF_CloseDocument>(raw);
                if (std::wstring(argv[1]) == L"inspect")
                    std::cout << "{\"loaded\":true,\"pages\":" << FPDF_GetPageCount(document.get())
                        << ",\"signatures\":" << FPDF_GetSignatureCount(document.get()) << '}';
                else if (argc == 7 && std::wstring(argv[1]) == L"render")
                    Render(document.get(), argv[3], std::stoi(argv[4]), std::wstring(argv[5]) == L"transparent", std::wstring(argv[6]) == L"widgets");
                else throw std::runtime_error("Invalid evaluation mode.");
            }
        }
    } catch (const std::exception& error) { std::cerr << error.what() << std::endl; result = 3; }
    FPDF_DestroyLibrary();
    return result;
}
