#pragma once

// Fixed empty-profile initialization, without document loading or wider access.
bool OfficeStartupDiagnostics(const fs::path& root, PSID sid) {
    const auto runtime = root.parent_path() / L"office";
    const auto fixtures = root.parent_path() / L"office-fixtures";
    Grant(runtime, sid, FILE_GENERIC_READ | FILE_GENERIC_EXECUTE);
    Grant(fixtures, sid, FILE_GENERIC_READ | FILE_GENERIC_EXECUTE);
    auto values = AccessEnvironmentValues(root);
    values[L"SAL_LOG"] = L"+INFO+WARN+TIMESTAMP";
    values[L"SAL_DISABLE_OPENCL"] = L"1";
    const auto environment = EnvironmentBlock(values);
    const auto previousMode = SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    struct ErrorModeGuard { UINT value; ~ErrorModeGuard() { SetErrorMode(value); } } errorMode{ previousMode };
    bool passed = true;
    for (const bool isolated : { false, true }) {
        const auto name = isolated ? L"startup-isolated" : L"startup-control";
        try {
            const auto profile = root / L"writable" / (isolated ? L"si" : L"sc");
            const auto settings = profile / L"user" / L"registrymodifications.xcu";
            fs::create_directories(settings.parent_path());
            fs::copy_file(fixtures / L"settings.xcu", settings);
            wchar_t uri[32768]{}; DWORD length = static_cast<DWORD>(std::size(uri));
            Require(SUCCEEDED(UrlCreateFromPathW(profile.c_str(), uri, &length, 0)), "Create diagnostic profile URI");
            const auto started = GetTickCount64();
            const auto processEvidence = root / (std::wstring(name) + L"-children.json");
            const auto result = Run(runtime / L"program" / L"soffice.com",
                { std::wstring(L"-env:UserInstallation=") + uri, L"--headless", L"--nologo", L"--nodefault",
                  L"--norestore", L"--unaccept=all", L"--terminate_after_init" },
                root, isolated ? sid : nullptr, 60000, {}, &environment, &processEvidence);
            std::ofstream(root / (std::wstring(name) + L".log")) << result.output;
            const bool completed = result.exitCode == 0 && !result.timedOut && !result.outputLimit;
            std::ofstream(root / (std::wstring(name) + L".json")) << "{\"completed\":" << (completed ? "true" : "false")
                << ",\"exitCode\":" << result.exitCode << ",\"timedOut\":" << (result.timedOut ? "true" : "false")
                << ",\"outputLimit\":" << (result.outputLimit ? "true" : "false") << ",\"milliseconds\":" << GetTickCount64() - started
                << ",\"diagnosticBytes\":" << result.output.size() << ",\"totalProcesses\":" << result.totalProcesses
                << ",\"activeAfterCleanup\":0,\"rootAppContainerTokenVerified\":" << (isolated ? "true" : "false") << "}\n";
            passed &= completed;
            std::wcout << name << L" completed=" << completed << std::endl;
        } catch (const std::exception& error) {
            std::ofstream(root / (std::wstring(name) + L".err")) << error.what() << '\n';
            std::wcout << name << L" failed; retained diagnostic." << std::endl;
            passed = false;
        }
    }
    return passed;
}
