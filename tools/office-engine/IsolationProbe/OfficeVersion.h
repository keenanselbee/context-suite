#pragma once

fs::path OfficeRuntime(const fs::path& root, PSID sid) {
    const auto boundary = root.parent_path() / L"runtime";
    const auto runtime = boundary / L"office";
    Require(fs::is_regular_file(runtime / L"program" / L"soffice.com"), "Prepared Office runtime copy required");
    size_t entries = 0;
    for (const auto& entry : fs::directory_iterator(boundary)) {
        Require(entry.path() == runtime, "Runtime boundary must contain only the copied engine");
        ++entries;
    }
    Require(entries == 1, "Use the dedicated runtime boundary");
    // FindFirstFile(runtime) enumerates its parent. Permit that lookup only in
    // the dedicated engine boundary, never in the surrounding staging directory.
    Grant(boundary, sid, FILE_GENERIC_READ | FILE_GENERIC_EXECUTE);
    return runtime;
}

// Fixed version-only viability check. No document path or arbitrary engine
// argument is accepted, and no source/runtime ACL outside this copy is changed.
bool OfficeVersion(const fs::path& root, PSID sid, const std::vector<wchar_t>& environment) {
    const auto runtime = OfficeRuntime(root, sid);
    const auto executable = runtime / L"program" / L"soffice.com";
    const auto previousMode = SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    struct ErrorModeGuard { UINT value; ~ErrorModeGuard() { SetErrorMode(value); } } errorMode{ previousMode };
    bool passed = true;
    for (const bool isolated : { false, true }) {
        const auto name = isolated ? L"office-isolated" : L"office-control";
        try {
            const auto profile = root / L"writable" / name;
            fs::create_directories(profile);
            wchar_t uri[32768]{};
            DWORD length = static_cast<DWORD>(std::size(uri));
            Require(SUCCEEDED(UrlCreateFromPathW(profile.c_str(), uri, &length, 0)), "Create owned Office profile URI");
            const auto result = Run(executable, { std::wstring(L"-env:UserInstallation=") + uri,
                L"--headless", L"--nologo", L"--nodefault", L"--norestore", L"--unaccept=all", L"--version" },
                root, isolated ? sid : nullptr, 30000, {}, &environment);
            std::ofstream(root / (std::wstring(name) + L".log")) << result.output;
            const bool matched = result.exitCode == 0 && !result.timedOut && !result.outputLimit &&
                result.output.find("LibreOffice 26.2.6.3 8221e31b3ac356a1623c672912a3d2b492f7e3d1") != std::string::npos;
            std::ofstream(root / (std::wstring(name) + L".json")) << "{\"matched\":" << (matched ? "true" : "false")
                << ",\"exitCode\":" << result.exitCode << ",\"timedOut\":" << (result.timedOut ? "true" : "false")
                << ",\"outputLimit\":" << (result.outputLimit ? "true" : "false")
                << ",\"totalProcesses\":" << result.totalProcesses << ",\"activeAfterCleanup\":0"
                << ",\"rootAppContainerTokenVerified\":" << (isolated ? "true" : "false") << "}\n";
            passed &= matched;
            std::cout << (isolated ? "Office isolated" : "Office control") << " version matched=" << matched << '\n';
        } catch (const std::exception& error) {
            std::ofstream(root / (std::wstring(name) + L".err")) << error.what() << '\n';
            std::cout << (isolated ? "Office isolated" : "Office control") << " version failed; retained diagnostic.\n";
            passed = false;
        }
    }
    return passed;
}
