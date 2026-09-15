#pragma once

// Independently declared stable ABI prefix; no document methods are invoked.
struct OfficeKit;
struct OfficeKitMethods { size_t bytes; void (*destroy)(OfficeKit*); };
struct OfficeKit { OfficeKitMethods* methods; };

std::string KitUtf8(const std::wstring& text) {
    const auto length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    Require(length > 0 && length < 32768, "Bound embedded engine path encoding");
    std::string output(static_cast<size_t>(length), '\0');
    Require(WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.c_str(), -1, output.data(), length, nullptr, nullptr) == length,
        "Encode embedded engine path");
    output.pop_back(); return output;
}

int OfficeKitChild(const fs::path& root, bool isolated) {
    const auto program = root.parent_path() / L"runtime" / L"office" / L"program";
    const auto profile = root / L"writable" / (isolated ? L"ki" : L"kc");
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
    wchar_t uri[32768]{}; DWORD length = static_cast<DWORD>(std::size(uri));
    Require(SUCCEEDED(UrlCreateFromPathW(profile.c_str(), uri, &length, 0)), "Create embedded profile URI");
    const auto programUtf8 = KitUtf8(program.native()); const auto profileUtf8 = KitUtf8(uri);
    std::cout << "kit hook calling\n" << std::flush;
    const auto kit = initialize(programUtf8.c_str(), profileUtf8.c_str());
    Require(kit && kit->methods && kit->methods->bytes >= sizeof(OfficeKitMethods) && kit->methods->destroy,
        "Embedded engine initialization and stable ABI prefix");
    std::cout << "{\"kitInitialized\":true}\n" << std::flush;
    kit->methods->destroy(kit);
    std::cout << "{\"kitDestroyed\":true}\n" << std::flush;
    return 0;
}

bool OfficeEmbeddedStartup(const fs::path& root, PSID sid) {
    (void)OfficeRuntime(root, sid);
    auto values = AccessEnvironmentValues(root);
    values[L"SAL_DISABLE_OPENCL"] = L"1";
    values[L"SAL_LOG"] = L"+INFO+WARN+TIMESTAMP";
    const auto environment = EnvironmentBlock(values);
    const auto previous = SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    struct ErrorModeGuard { UINT value; ~ErrorModeGuard() { SetErrorMode(value); } } guard{previous};
    bool passed = true;
    for (const bool isolated : {false, true}) {
        const auto profile = root / L"writable" / (isolated ? L"ki" : L"kc");
        fs::create_directories(profile / L"user");
        fs::copy_file(root.parent_path() / L"office-fixtures" / L"settings.xcu", profile / L"user" / L"registrymodifications.xcu");
        const auto name = isolated ? L"kit-isolated" : L"kit-control";
        const auto child = root / L"allowed" / (std::wstring(name) + L".exe");
        fs::copy_file(root / L"allowed" / L"probe.exe", child);
        const auto evidence = root / (std::wstring(name) + L"-children.json");
        const auto started = GetTickCount64();
        const auto result = Run(child, {},
            root, isolated ? sid : nullptr, 60000, {}, &environment, &evidence);
        std::ofstream(root / (std::wstring(name) + L".log")) << result.output;
        const bool initialized = result.output.find("{\"kitInitialized\":true}") != std::string::npos;
        const bool destroyed = result.output.find("{\"kitDestroyed\":true}") != std::string::npos;
        const bool completed = result.exitCode == 0 && !result.timedOut && !result.outputLimit && initialized && destroyed;
        std::ofstream(root / (std::wstring(name) + L".json")) << "{\"completed\":" << (completed ? "true" : "false")
            << ",\"initialized\":" << (initialized ? "true" : "false") << ",\"destroyed\":" << (destroyed ? "true" : "false")
            << ",\"exitCode\":" << result.exitCode << ",\"timedOut\":" << (result.timedOut ? "true" : "false")
            << ",\"outputLimit\":" << (result.outputLimit ? "true" : "false") << ",\"milliseconds\":" << GetTickCount64() - started
            << ",\"totalProcesses\":" << result.totalProcesses << ",\"activeAfterCleanup\":0,\"rootAppContainerTokenVerified\":" << (isolated ? "true" : "false") << "}\n";
        passed &= completed;
    }
    return passed;
}
