// Authored passive workbook evaluation only, launched in the managed harness's
// bounded job. This ordinary process does not provide filesystem/network isolation.
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iostream>
#include <stdexcept>
#include <string>
#include <thread>

namespace fs = std::filesystem;
void Require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

// Independently declared prefixes of the pinned LibreOfficeKit C ABI, matching
// the existing embedded evaluation. Only loading, copy-save and teardown are used.
struct OfficeKit;
struct Document;
struct OfficeMethods {
    size_t bytes;
    void (*destroy)(OfficeKit*);
    Document* (*load)(OfficeKit*, const char*);
    char* (*error)(OfficeKit*);
    Document* (*loadWithOptions)(OfficeKit*, const char*, const char*);
    void (*freeError)(char*);
    void (*unusedStableMembers[7])();
    void (*runLoop)(OfficeKit*, int (*)(void*, int), void (*)(void*), void*);
};
struct DocumentMethods {
    size_t bytes;
    void (*destroy)(Document*);
    int (*saveAs)(Document*, const char*, const char*, const char*);
    int (*type)(Document*);
};
struct OfficeKit { OfficeMethods* methods; };
struct Document { DocumentMethods* methods; };

std::string Utf8(const std::wstring& text) {
    const auto length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    Require(length > 0 && length < 32768, "Bound path encoding");
    std::string result(static_cast<size_t>(length), '\0');
    Require(WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.c_str(), -1, result.data(), length, nullptr, nullptr) == length, "Encode path");
    result.pop_back(); return result;
}

std::string FileUri(const fs::path& path) {
    const auto text = path.generic_wstring();
    Require(text.size() > 3 && text[1] == L':' && text[2] == L'/', "Local absolute fixture path");
    const char* digits = "0123456789ABCDEF";
    std::string result = "file:///";
    for (const unsigned char value : Utf8(text)) {
        if ((value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') ||
            (value >= '0' && value <= '9') || value == '-' || value == '.' || value == '_' || value == '~' || value == '/' || value == ':')
            result.push_back(value);
        else { result.push_back('%'); result.push_back(digits[value >> 4]); result.push_back(digits[value & 15]); }
    }
    return result;
}

std::function<void()> work;
bool (*inExecute)() = nullptr;
bool (*onMainThread)() = nullptr;
void CALLBACK WorkTimer(HWND, UINT, UINT_PTR timer, DWORD) {
    if (!inExecute()) return;
    KillTimer(nullptr, timer);
    work();
}

int wmain(int argc, wchar_t** argv) {
    try {
        Require(argc == 5, "Expected runtime program, source, owned profile and output directory");
        const fs::path program(argv[1]), source(argv[2]), profile(argv[3]), output(argv[4]);
        for (const auto& path : { program, source, profile, output }) {
            Require(path.is_absolute() && path.native().find(L"\\.codex-temp\\") != std::wstring::npos, "Repository scratch paths only");
            for (auto current = path; !current.empty() && current != current.parent_path(); current = current.parent_path()) {
                const auto attributes = GetFileAttributesW(current.c_str());
                Require(attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0, "Ordinary existing fixture paths");
            }
        }
        Require(source.extension() == L".xlsx" && fs::file_size(source) > 0 && fs::file_size(source) <= 1024 * 1024, "Bound authored source");
        const auto before = output / L"before.pdf", after = output / L"after.pdf", snapshot = output / source.filename();
        Require(!fs::exists(before) && !fs::exists(after) && !fs::exists(snapshot), "Fresh output files");
        Require(!fs::exists(output / L"progress.txt"), "Fresh progress evidence");
        std::ofstream progress(output / L"progress.txt", std::ios::binary);
        auto mark = [&](const char* value) { progress << value << '\n' << std::flush; Require(progress.good(), "Write progress evidence"); };
        mark("starting");
        Require(_wputenv_s(L"SAL_LOK_OPTIONS", L"unipoll") == 0 && _wputenv_s(L"SAL_DISABLE_OPENCL", L"1") == 0, "Fixed engine environment");
        SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
        Require(SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_USER_DIRS) != FALSE, "Restrict DLL search");
        Require(AddDllDirectory(program.c_str()) != nullptr, "Verified runtime DLL directory");
        Require(LoadLibraryExW((program / L"sal3.dll").c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS) != nullptr, "Load runtime");
        const auto module = LoadLibraryExW((program / L"mergedlo.dll").c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        Require(module != nullptr, "Load engine");
        mark("modules-loaded");
        const auto initialize = reinterpret_cast<OfficeKit* (*)(const char*, const char*)>(GetProcAddress(module, "libreofficekit_hook_2"));
        Require(initialize != nullptr, "Pinned embedded hook");
        const auto kit = initialize(Utf8(program.native()).c_str(), FileUri(profile).c_str());
        Require(kit && kit->methods && kit->methods->bytes >= sizeof(OfficeMethods) && kit->methods->destroy && kit->methods->loadWithOptions && kit->methods->runLoop, "Engine ABI");
        mark("engine-initialized");
        inExecute = reinterpret_cast<bool (*)()>(GetProcAddress(module, "?IsInExecute@Application@@SA_NXZ"));
        onMainThread = reinterpret_cast<bool (*)()>(GetProcAddress(module, "?IsMainThread@Application@@SA_NXZ"));
        Require(inExecute && onMainThread, "Pinned Windows loop exports");
        std::exception_ptr failure;
        bool destroyed = false, completed = false;
        std::thread destroyer;
        work = [&] {
            try {
                Require(onMainThread(), "Document work on main thread");
                mark("loop-ready");
                const auto document = kit->methods->loadWithOptions(kit, FileUri(source).c_str(), "Batch=true,EnableMacrosExecution=false,MacroSecurityLevel=3");
                Require(document && document->methods && document->methods->bytes >= sizeof(DocumentMethods) && document->methods->destroy && document->methods->saveAs && document->methods->type, "Document ABI");
                struct Guard { Document* value; ~Guard() { value->methods->destroy(value); } } guard{document};
                Require(document->methods->type(document) == 1, "Spreadsheet family");
                mark("document-loaded");
                const char* options = R"({"UseLosslessCompression":{"type":"boolean","value":"true"},"ReduceImageResolution":{"type":"boolean","value":"false"},"UseTaggedPDF":{"type":"boolean","value":"true"},"ExportBookmarks":{"type":"boolean","value":"true"},"ExportNotes":{"type":"boolean","value":"false"},"SinglePageSheets":{"type":"boolean","value":"false"},"ExportFormFields":{"type":"boolean","value":"false"},"IsAddStream":{"type":"boolean","value":"false"},"EncryptFile":{"type":"boolean","value":"false"},"SelectPdfVersion":{"type":"long","value":"17"}})";
                Require(document->methods->saveAs(document, FileUri(before).c_str(), "pdf", options) != 0, "PDF before copy-save");
                mark("before-exported");
                // No TakeOwnership: keep the same loaded document and original URL.
                Require(document->methods->saveAs(document, FileUri(snapshot).c_str(), "xlsx", nullptr) != 0, "Calculated workbook copy");
                mark("snapshot-exported");
                Require(document->methods->saveAs(document, FileUri(after).c_str(), "pdf", options) != 0, "PDF after copy-save");
                mark("after-exported");
                for (const auto& path : { before, snapshot, after })
                    Require(fs::file_size(path) > 0 && fs::file_size(path) <= 16 * 1024 * 1024, "Bound completed output");
                completed = true;
            } catch (...) { failure = std::current_exception(); }
            destroyer = std::thread([&] { kit->methods->destroy(kit); destroyed = true; });
        };
        const auto timer = SetTimer(nullptr, 0, 100, WorkTimer);
        Require(timer != 0, "Readiness timer");
        kit->methods->runLoop(kit, [](void*, int) { return 0; }, [](void*) {}, nullptr);
        KillTimer(nullptr, timer);
        if (destroyer.joinable()) destroyer.join();
        work = {};
        if (failure) std::rethrow_exception(failure);
        Require(completed && destroyed, "Complete three exports and engine shutdown");
        mark("complete");
        std::cout << "{\"sameDocumentExportsComplete\":true}\n" << std::flush;
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "Authored date experiment failed: " << error.what() << '\n';
        return 2;
    }
}
