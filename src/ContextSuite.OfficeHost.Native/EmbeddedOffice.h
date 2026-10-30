#pragma once

// Independently declared prefixes of the pinned LibreOfficeKit C ABI.
struct OfficeKit;
struct OfficeDocument;
struct OfficeMethods {
    size_t bytes;
    void (*destroy)(OfficeKit*);
    OfficeDocument* (*load)(OfficeKit*, const char*);
    char* (*error)(OfficeKit*);
    OfficeDocument* (*loadWithOptions)(OfficeKit*, const char*, const char*);
    void (*freeError)(char*);
    void (*unusedStableMembers[7])();
    void (*runLoop)(OfficeKit*, int (*)(void*, int), void (*)(void*), void*);
};
struct DocumentMethods {
    size_t bytes;
    void (*destroy)(OfficeDocument*);
    int (*saveAs)(OfficeDocument*, const char*, const char*, const char*);
    int (*type)(OfficeDocument*);
    void (*unusedRenderingMembers[10])();
    void (*registerCallback)(OfficeDocument*, void (*)(int, const char*, void*), void*);
};
struct OfficeKit { OfficeMethods* methods; };
struct OfficeDocument { DocumentMethods* methods; };

struct FontReports {
    std::vector<std::string> payloads;
    size_t bytes = 0;
    bool invalid = false;
};

void ReportFonts(int type, const char* payload, void* context) noexcept {
    if (type != 57) return; // Pinned LOK_CALLBACK_FONTS_MISSING.
    auto& reports = *static_cast<FontReports*>(context);
    const auto length = payload ? strnlen_s(payload, 16385) : 0;
    if (reports.invalid || reports.payloads.size() >= 4 || length == 0 || length > 16384 - reports.bytes) {
        reports.invalid = true; return;
    }
    try { reports.payloads.emplace_back(payload, length); reports.bytes += length; }
    catch (...) { reports.invalid = true; }
}

std::string EncodeFontReports(const FontReports& reports) {
    Require(!reports.invalid, "Bounded renderer font reports");
    // Hex keeps untrusted engine JSON out of the terminal JSON syntax. The
    // managed reader validates decoded UTF-8, fields and family names before use.
    const char* digits = "0123456789ABCDEF";
    std::string result = "[";
    for (const auto& report : reports.payloads) {
        if (result.size() != 1) result += ',';
        result += '"';
        for (const unsigned char byte : report) { result += digits[byte >> 4]; result += digits[byte & 15]; }
        result += '"';
    }
    result += ']'; return result;
}

std::string Utf8(const std::wstring& text) {
    const auto count = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    Require(count > 0 && count < 32768, "Path encoding limit");
    std::string result(static_cast<size_t>(count), '\0');
    Require(WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.c_str(), -1, result.data(), count, nullptr, nullptr) == count, "Path encoding");
    result.pop_back(); return result;
}

std::string FileUri(const fs::path& path) {
    const auto text = path.generic_wstring();
    Require(text.size() > 3 && text[1] == L':' && text[2] == L'/', "Local staging path required");
    const auto bytes = Utf8(text);
    const char* digits = "0123456789ABCDEF";
    std::string result = "file:///";
    for (const unsigned char value : bytes) {
        if ((value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') || (value >= '0' && value <= '9') ||
            value == '-' || value == '.' || value == '_' || value == '~' || value == '/' || value == ':') result.push_back(static_cast<char>(value));
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

std::string Export(const fs::path& program, const fs::path& profile, const fs::path& source, const fs::path& output, int family) {
    Require(SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_USER_DIRS) != FALSE, "DLL search policy");
    Require(AddDllDirectory(program.c_str()) != nullptr, "Verified engine directory");
    Require(LoadLibraryExW((program / L"sal3.dll").c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS) != nullptr, "Engine runtime load");
    const auto module = LoadLibraryExW((program / L"sofficeapp.dll").c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    Require(module != nullptr, "Engine load");
    const auto initialize = reinterpret_cast<OfficeKit* (*)(const char*, const char*)>(GetProcAddress(module, "libreofficekit_hook_2"));
    Require(initialize != nullptr, "Pinned engine hook");
    phase = "engine initialization";
    const auto kit = initialize(Utf8(program.native()).c_str(), FileUri(profile).c_str());
    Require(kit && kit->methods && kit->methods->bytes >= sizeof(OfficeMethods) && kit->methods->destroy &&
        kit->methods->loadWithOptions && kit->methods->runLoop, "Engine ABI");
    // The pinned source-built renderer keeps its Windows loop in a separate library.
    const auto loopModule = LoadLibraryExW((program / L"vcllo.dll").c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    Require(loopModule != nullptr, "Pinned Windows loop library");
    inExecute = reinterpret_cast<bool (*)()>(GetProcAddress(loopModule, "?IsInExecute@Application@@SA_NXZ"));
    onMainThread = reinterpret_cast<bool (*)()>(GetProcAddress(loopModule, "?IsMainThread@Application@@SA_NXZ"));
    Require(inExecute && onMainThread, "Pinned Windows loop exports");
    std::exception_ptr failure;
    bool destroyed = false, exported = false;
    FontReports fontReports;
    std::thread destroyer;
    work = [&] {
        try {
            Require(onMainThread(), "Engine main thread");
            phase = "document loading";
            const auto document = kit->methods->loadWithOptions(kit, FileUri(source).c_str(),
                "Batch=true,EnableMacrosExecution=false,MacroSecurityLevel=3");
            Require(document && document->methods && document->methods->bytes >= sizeof(DocumentMethods) &&
                document->methods->destroy && document->methods->saveAs && document->methods->type && document->methods->registerCallback, "Document load");
            struct DocumentGuard { OfficeDocument* value; ~DocumentGuard() { value->methods->destroy(value); } } documentGuard{document};
            Require(document->methods->type(document) == family, "Document family");
            document->methods->registerCallback(document, ReportFonts, &fontReports);
            Require(!fontReports.invalid, "Bounded renderer font reports");
            const char* options = R"({"UseLosslessCompression":{"type":"boolean","value":"true"},"ReduceImageResolution":{"type":"boolean","value":"false"},"UseTaggedPDF":{"type":"boolean","value":"true"},"ExportBookmarks":{"type":"boolean","value":"true"},"ExportNotes":{"type":"boolean","value":"false"},"ExportNotesPages":{"type":"boolean","value":"false"},"ExportOnlyNotesPages":{"type":"boolean","value":"false"},"ExportHiddenSlides":{"type":"boolean","value":"false"},"SinglePageSheets":{"type":"boolean","value":"false"},"ExportFormFields":{"type":"boolean","value":"false"},"IsAddStream":{"type":"boolean","value":"false"},"EncryptFile":{"type":"boolean","value":"false"},"ExportTrackedChanges":{"type":"boolean","value":"false"},"SelectPdfVersion":{"type":"long","value":"17"}})";
            phase = "PDF export";
            Require(document->methods->saveAs(document, FileUri(output).c_str(), "pdf", options) != 0, "PDF export");
            if (family == 1) {
                // Same loaded document; no TakeOwnership or source rewrite.
                // The owner independently inspects these calculated values.
                phase = "calculated workbook export";
                const auto workbook = output.parent_path() / L"calculated.xlsx";
                Require(document->methods->saveAs(document, FileUri(workbook).c_str(), "xlsx", nullptr) != 0, "Calculated workbook export");
            }
            document->methods->registerCallback(document, nullptr, nullptr);
            Require(!fontReports.invalid, "Bounded renderer font reports");
            exported = true;
        } catch (...) { failure = std::current_exception(); }
        destroyer = std::thread([&] { kit->methods->destroy(kit); destroyed = true; });
    };
    const auto timer = SetTimer(nullptr, 0, 100, WorkTimer);
    Require(timer != 0, "Engine readiness timer");
    struct TimerGuard { UINT_PTR value; ~TimerGuard() { KillTimer(nullptr, value); } } timerGuard{timer};
    kit->methods->runLoop(kit, [](void*, int) { return 0; }, [](void*) {}, nullptr);
    if (destroyer.joinable()) destroyer.join();
    work = {};
    if (failure) std::rethrow_exception(failure);
    Require(exported && destroyed, "Complete export and engine shutdown");
    // Loaded runtime modules remain mapped until this single-operation process exits.
    return EncodeFontReports(fontReports);
}
