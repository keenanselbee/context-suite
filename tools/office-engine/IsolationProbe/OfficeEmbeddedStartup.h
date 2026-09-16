#pragma once
#include <functional>
#include <thread>
#include <exception>
#include <shlobj.h>

// Independently declared stable ABI prefixes from the pinned public API.
struct OfficeKit;
struct OfficeKitDocument;
struct OfficeKitMethods {
    size_t bytes;
    void (*destroy)(OfficeKit*);
    OfficeKitDocument* (*load)(OfficeKit*, const char*);
    char* (*error)(OfficeKit*);
    OfficeKitDocument* (*loadWithOptions)(OfficeKit*, const char*, const char*);
    void (*freeError)(char*);
    void (*unusedStableMembers[7])();
    void (*runLoop)(OfficeKit*, int (*)(void*, int), void (*)(void*), void*);
};
struct OfficeKitDocumentMethods {
    size_t bytes;
    void (*destroy)(OfficeKitDocument*);
    int (*saveAs)(OfficeKitDocument*, const char*, const char*, const char*);
    int (*type)(OfficeKitDocument*);
    void (*unusedRenderingMembers[10])();
    void (*registerCallback)(OfficeKitDocument*, void (*)(int, const char*, void*), void*);
};
struct OfficeKitDocument { OfficeKitDocumentMethods* methods; };
struct OfficeKitFixture { const wchar_t* family; const wchar_t* extension; const wchar_t* profile; };
const OfficeKitFixture kitFixtures[] = {{L"Word", L"docx", L"w"}, {L"Excel", L"xlsx", L"x"}, {L"PowerPoint", L"pptx", L"p"}};
struct OfficeKit { OfficeKitMethods* methods; };

std::wstring KitCaseName(int index, bool isolated, bool fonts, bool missing) {
    return std::wstring(kitFixtures[index].family) + (fonts ? (missing ? L"-font-missing" : L"-font-control") : L"") +
        (isolated ? L"-isolated" : L"-control");
}

std::wstring KitFileStem(int index, bool fonts, bool missing) {
    return std::wstring(kitFixtures[index].family) + (fonts ? (missing ? L" font missing" : L" font control") : L" \u00fc");
}

struct FontCallbackObservation { unsigned count = 0; bool limited = false; };
void FontCallback(int type, const char* payload, void* context) noexcept {
    if (type != 57) return; // LOK_CALLBACK_FONTS_MISSING in the pinned 26.2.6.3 ABI.
    auto& observation = *static_cast<FontCallbackObservation*>(context);
    const auto length = payload ? strnlen_s(payload, 32769) : 0;
    if (++observation.count > 4 || length == 0 || length > 32768) { observation.limited = true; return; }
    // Authored evaluation fixtures only. Retain bounded raw callback data for
    // independent JSON inspection; it is not customer-facing text or a permit.
    std::cout << "{\"missingFontsCallback\":";
    std::cout.write(payload, static_cast<std::streamsize>(length));
    std::cout << "}\n" << std::flush;
}

std::string KitUtf8(const std::wstring& text) {
    const auto length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    Require(length > 0 && length < 32768, "Bound embedded engine path encoding");
    std::string output(static_cast<size_t>(length), '\0');
    Require(WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.c_str(), -1, output.data(), length, nullptr, nullptr) == length,
        "Encode embedded engine path");
    output.pop_back(); return output;
}

// These fixtures use absolute local drive paths. Escape UTF-8 bytes, including
// percent and fragment/query characters; the Windows shell URI helper uses
// legacy non-ASCII escapes that the embedded loader rejects.
std::string KitFileUri(const fs::path& path) {
    const auto text = path.generic_wstring();
    Require(text.size() > 3 && text[1] == L':' && text[2] == L'/', "Use an absolute local fixture path");
    const auto utf8 = KitUtf8(text);
    std::string uri = "file:///";
    const char* digits = "0123456789ABCDEF";
    for (const unsigned char value : utf8) {
        if ((value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') ||
            (value >= '0' && value <= '9') || value == '-' || value == '.' || value == '_' || value == '~' || value == '/' || value == ':')
            uri.push_back(static_cast<char>(value));
        else { uri.push_back('%'); uri.push_back(digits[value >> 4]); uri.push_back(digits[value & 15]); }
    }
    return uri;
}


std::function<void()> kitWork;
// These checked VCL exports belong to the pinned Windows runtime, outside
// the stable LibreOfficeKit C ABI.
bool (*kitInExecute)() = nullptr;
bool (*kitMainThread)() = nullptr;
void CALLBACK KitTimer(HWND, UINT, UINT_PTR timer, DWORD) {
    if (!kitInExecute()) return;
    KillTimer(nullptr, timer);
    kitWork();
}
int OfficeKitChild(const fs::path& root, bool isolated, int fixtureIndex = -1, bool fonts = false, bool missing = false) {
    Require(fixtureIndex >= -1 && fixtureIndex < 3, "Use an authored embedded fixture");
    const auto program = root.parent_path() / L"runtime" / L"office" / L"program";
    const auto profileName = fixtureIndex < 0 ? std::wstring(isolated ? L"ki" : L"kc") :
        std::wstring(kitFixtures[fixtureIndex].profile) + (fonts ? (missing ? L"m" : L"a") : L"") + (isolated ? L"i" : L"c");
    const auto profile = root / L"writable" / profileName;
    Require(SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_USER_DIRS) != FALSE,
        "Restrict owned embedded DLL search");
    Require(AddDllDirectory(program.c_str()) != nullptr, "Add verified engine DLL directory");
    const auto sal = LoadLibraryExW((program / L"sal3.dll").c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    Require(sal != nullptr, "Load embedded engine runtime");
    std::cout << "kit runtime loaded\n" << std::flush;
    const auto module = LoadLibraryExW((program / L"mergedlo.dll").c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    Require(module != nullptr, "Load verified embedded engine");
    std::cout << "kit module loaded\n" << std::flush;
    const auto initialize = reinterpret_cast<OfficeKit* (*)(const char*, const char*)>(GetProcAddress(module, "libreofficekit_hook_2"));
    Require(initialize != nullptr, "Locate supported embedded hook");
    const auto programUtf8 = KitUtf8(program.native()); const auto profileUtf8 = KitFileUri(profile);
    std::cout << "kit hook calling\n" << std::flush;
    const auto kit = initialize(programUtf8.c_str(), profileUtf8.c_str());
    Require(kit && kit->methods && kit->methods->bytes >= sizeof(OfficeKitMethods) && kit->methods->destroy,
        "Embedded engine initialization and stable ABI prefix");
    std::cout << "{\"kitInitialized\":true}\n" << std::flush;

    if (fixtureIndex < 0) {
        kit->methods->destroy(kit);
        std::cout << "{\"kitDestroyed\":true}\n" << std::flush;
        return 0;
    }
    Require(kit->methods->runLoop != nullptr, "Embedded main loop callback");
    kitInExecute = reinterpret_cast<bool (*)()>(GetProcAddress(module, "?IsInExecute@Application@@SA_NXZ"));
    kitMainThread = reinterpret_cast<bool (*)()>(GetProcAddress(module, "?IsMainThread@Application@@SA_NXZ"));
    Require(kitInExecute && kitMainThread, "Pinned Windows engine loop-state exports");
    std::exception_ptr failure;
    bool destroyed = false;
    std::thread destroyer;
    kitWork = [&] {
        try {
            Require(kitMainThread(), "Run document work on the engine main thread");
            std::cout << "{\"loopReady\":true}\n" << std::flush;
            Require(kit->methods->loadWithOptions && kit->methods->error && kit->methods->freeError,
                "Embedded document API callbacks");
            const auto& fixture = kitFixtures[fixtureIndex];
            const auto name = KitCaseName(fixtureIndex, isolated, fonts, missing);
            const auto stem = KitFileStem(fixtureIndex, fonts, missing);
            const auto source = root / L"allowed" / name / (stem + L"." + fixture.extension);
            const auto pdf = root / L"writable" / name / (stem + L".pdf");
            Require(!fs::exists(pdf), "Keep embedded output fresh");
            const auto sourceUri = KitFileUri(source);
            // The pinned loader otherwise resets MacroSecurityLevel to 1.
            const auto document = kit->methods->loadWithOptions(kit, sourceUri.c_str(),
                "Batch=true,EnableMacrosExecution=false,MacroSecurityLevel=3");
            if (!document) {
                const auto error = kit->methods->error(kit);
                if (error) { std::cout << std::string(error, strnlen_s(error, 4096)) << std::endl; kit->methods->freeError(error); }
            }
            Require(document && document->methods && document->methods->bytes >= sizeof(OfficeKitDocumentMethods) &&
                document->methods->destroy && document->methods->saveAs && document->methods->type, "Load authored document and verify ABI");
            FontCallbackObservation fontObservation;
            struct DocumentGuard { OfficeKitDocument* document; ~DocumentGuard() { document->methods->destroy(document); } } documentGuard{document};
            Require(document->methods->type(document) == fixtureIndex, "Match authored document family");
            std::cout << "{\"documentLoaded\":true}\n" << std::flush;
            if (fonts) {
                Require(document->methods->registerCallback != nullptr, "Pinned document font callback API");
                document->methods->registerCallback(document, FontCallback, &fontObservation);
                Require(!fontObservation.limited, "Bound font callback evidence");
                std::cout << "{\"fontCallbackRegistered\":true}\n" << std::flush;
            }
            const auto pdfUri = KitFileUri(pdf);
            const char* options = R"({"UseLosslessCompression":{"type":"boolean","value":"true"},"ReduceImageResolution":{"type":"boolean","value":"false"},"UseTaggedPDF":{"type":"boolean","value":"true"},"ExportBookmarks":{"type":"boolean","value":"true"},"ExportNotes":{"type":"boolean","value":"false"},"ExportNotesPages":{"type":"boolean","value":"false"},"ExportOnlyNotesPages":{"type":"boolean","value":"false"},"ExportHiddenSlides":{"type":"boolean","value":"false"},"SinglePageSheets":{"type":"boolean","value":"false"},"ExportFormFields":{"type":"boolean","value":"false"},"IsAddStream":{"type":"boolean","value":"false"},"EncryptFile":{"type":"boolean","value":"false"},"ExportTrackedChanges":{"type":"boolean","value":"false"},"SelectPdfVersion":{"type":"long","value":"17"}})";
            const auto saved = document->methods->saveAs(document, pdfUri.c_str(), "pdf", options);
            if (!saved) {
                const auto error = kit->methods->error(kit);
                if (error) { std::cout << std::string(error, strnlen_s(error, 4096)) << std::endl; kit->methods->freeError(error); }
            }
            Require(saved != 0, "Export authored PDF");
            if (fonts) {
                document->methods->registerCallback(document, nullptr, nullptr);
                Require(!fontObservation.limited, "Bound font callback evidence through PDF export");
                std::cout << "{\"fontCallbackCount\":" << fontObservation.count << "}\n" << std::flush;
            }
            std::cout << "{\"documentExported\":true}\n" << std::flush;
        } catch (...) { failure = std::current_exception(); }
        destroyer = std::thread([&] {
            kit->methods->destroy(kit);
            destroyed = true;
            std::cout << "{\"kitDestroyed\":true}\n" << std::flush;
        });
    };
    const auto timer = SetTimer(nullptr, 0, 100, KitTimer);
    Require(timer != 0, "Check engine loop readiness on the initializing thread");
    struct TimerGuard { UINT_PTR timer; ~TimerGuard() { KillTimer(nullptr, timer); } } timerGuard{timer};
    kit->methods->runLoop(kit, [](void*, int) { return 0; }, [](void*) {}, nullptr);
    if (destroyer.joinable()) destroyer.join();
    kitWork = {};
    Require(destroyed, "Embedded loop and destruction both complete");
    if (failure) std::rethrow_exception(failure);
    return 0;
}

bool OfficeEmbeddedStartup(const fs::path& root, PSID sid, bool exports = false, bool fonts = false) {
    (void)OfficeRuntime(root, sid);
    if (exports) Grant(root.parent_path() / L"office-fixtures", sid, FILE_GENERIC_READ | FILE_GENERIC_EXECUTE);
    auto values = AccessEnvironmentValues(root);
    if (fonts) {
        // Match the production native-profile base, not the older access probe's
        // redirected LOCALAPPDATA experiment. No caller search paths are inherited.
        PWSTR local = nullptr;
        Require(SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local)) && local,
            "Locate real native-profile base for font comparison");
        values[L"LOCALAPPDATA"] = local;
        CoTaskMemFree(local);
    }
    values[L"SAL_DISABLE_OPENCL"] = L"1";
    if (exports) values[L"SAL_LOK_OPTIONS"] = L"unipoll";
    values[L"SAL_LOG"] = L"+INFO+WARN+TIMESTAMP";
    const auto environment = EnvironmentBlock(values);
    const auto previous = SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    struct ErrorModeGuard { UINT value; ~ErrorModeGuard() { SetErrorMode(value); } } guard{previous};
    bool passed = true;
    for (int variant = 0; variant < (fonts ? 2 : 1); ++variant)
    for (int index = exports ? 0 : -1; index < (exports ? 3 : 0); ++index) for (const bool isolated : {false, true}) {
        const bool missing = variant == 1;
        const auto profileName = exports ? std::wstring(kitFixtures[index].profile) + (fonts ? (missing ? L"m" : L"a") : L"") + (isolated ? L"i" : L"c") :
            std::wstring(isolated ? L"ki" : L"kc");
        const auto profile = root / L"writable" / profileName;
        fs::create_directories(profile / L"user");
        fs::copy_file(root.parent_path() / L"office-fixtures" / L"settings.xcu", profile / L"user" / L"registrymodifications.xcu");
        const auto name = exports ? KitCaseName(index, isolated, fonts, missing) :
            std::wstring(isolated ? L"kit-isolated" : L"kit-control");
        const auto alias = exports ? L"kit-" + name : name;
        if (exports) {
            fs::create_directory(root / L"writable" / name);
            const auto input = root / L"allowed" / name;
            fs::create_directory(input);
            const auto filename = KitFileStem(index, fonts, missing) + L"." + kitFixtures[index].extension;
            fs::copy_file(root.parent_path() / L"office-fixtures" / filename, input / filename);
            Require(SetFileAttributesW((input / filename).c_str(), FILE_ATTRIBUTE_READONLY) != FALSE,
                "Make the owned input copy read-only");
            WIN32_FILE_ATTRIBUTE_DATA information{};
            Require(GetFileAttributesExW((input / filename).c_str(), GetFileExInfoStandard, &information) != FALSE,
                "Record owned input attributes");
            const auto written = (static_cast<ULONGLONG>(information.ftLastWriteTime.dwHighDateTime) << 32) | information.ftLastWriteTime.dwLowDateTime;
            std::ofstream(root / (name + L"-input.json")) << "{\"lastWriteTime\":" << written << "}\n";
        }
        const auto child = root / L"allowed" / (alias + L".exe");
        fs::copy_file(root / L"allowed" / L"probe.exe", child);
        const auto evidence = root / (std::wstring(name) + L"-children.json");
        const auto started = GetTickCount64();
        const auto result = Run(child, {},
            root, isolated ? sid : nullptr, 60000, {}, &environment, &evidence);
        std::ofstream(root / (std::wstring(name) + L".log")) << result.output;
        const bool initialized = result.output.find("{\"kitInitialized\":true}") != std::string::npos;
        const bool destroyed = result.output.find("{\"kitDestroyed\":true}") != std::string::npos;
        const auto pdf = exports ? root / L"writable" / name / (KitFileStem(index, fonts, missing) + L".pdf") : fs::path{};
        const auto bytes = exports && fs::is_regular_file(pdf) ? fs::file_size(pdf) : 0;
        const bool exported = exports && result.output.find("{\"documentExported\":true}") != std::string::npos;
        const bool loopReady = result.output.find("{\"loopReady\":true}") != std::string::npos;
        const bool completed = result.exitCode == 0 && !result.timedOut && !result.outputLimit && initialized && destroyed &&
            (!exports || (loopReady && exported && bytes > 0 && bytes <= 16 * MiB)) &&
            (!fonts || result.output.find("{\"fontCallbackRegistered\":true}") != std::string::npos);
        std::ofstream(root / (name + (exports ? L"-export.json" : L".json"))) << "{\"completed\":" << (completed ? "true" : "false")
            << ",\"inputCopy\":" << (exports ? "true" : "false") << ",\"loopReady\":" << (loopReady ? "true" : "false")
            << ",\"initialized\":" << (initialized ? "true" : "false") << ",\"destroyed\":" << (destroyed ? "true" : "false")
            << ",\"exported\":" << (exported ? "true" : "false") << ",\"pdfBytes\":" << bytes
            << ",\"exitCode\":" << result.exitCode << ",\"timedOut\":" << (result.timedOut ? "true" : "false")
            << ",\"outputLimit\":" << (result.outputLimit ? "true" : "false") << ",\"milliseconds\":" << GetTickCount64() - started
            << ",\"totalProcesses\":" << result.totalProcesses << ",\"activeAfterCleanup\":0,\"rootAppContainerTokenVerified\":" << (isolated ? "true" : "false") << "}\n";
        passed &= completed;
        if (fonts) std::cout << "Font callback case " << KitUtf8(name) << ": " << (completed ? "completed" : "failed") << '\n' << std::flush;
    }
    return passed;
}
