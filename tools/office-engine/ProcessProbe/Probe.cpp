#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <string>

// Authored process-boundary fixture. It never loads Office or reads a document.
int wmain(int argc, wchar_t** argv) {
    wchar_t module[32768]{}, profile[32768]{}, format[32]{};
    if (!GetModuleFileNameW(nullptr, module, 32768) ||
        std::wstring(module).find(L"\\.codex-temp\\office-isolation\\") == std::wstring::npos) return 2;
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return 3;
    DWORD isolated = 0, bytes = 0;
    const auto checked = GetTokenInformation(token, TokenIsAppContainer, &isolated, sizeof(isolated), &bytes);
    CloseHandle(token);
    if (!checked || isolated != 1) return 4;
    if (argc == 2 && std::wstring(argv[1]) == L"--sleep") { Sleep(60000); return 5; }
    if (argc != 1 || !GetEnvironmentVariableW(L"CS_OFFICE_FORMAT", format, 32)) return 6;
    if (std::wstring(format) == L"docx" || std::wstring(format) == L"xlsx") {
        const bool output = std::wstring(format) == L"docx";
        const std::string flood(output ? 4097 : 65537, 'x');
        DWORD written = 0;
        if (!WriteFile(GetStdHandle(output ? STD_OUTPUT_HANDLE : STD_ERROR_HANDLE), flood.data(),
            static_cast<DWORD>(flood.size()), &written, nullptr) || written != flood.size()) return 7;
        Sleep(60000); return 8;
    }
    if (std::wstring(format) != L"pptx" || !GetEnvironmentVariableW(L"CS_OFFICE_PROFILE", profile, 32768) ||
        std::wstring(profile).find(L"\\.codex-temp\\office-isolation\\") == std::wstring::npos || std::filesystem::exists(profile)) return 9;
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION child{};
    auto command = L"\"" + std::wstring(module) + L"\" --sleep";
    if (!CreateProcessW(module, command.data(), nullptr, nullptr, FALSE, CREATE_SUSPENDED | CREATE_NO_WINDOW,
        nullptr, nullptr, &startup, &child)) return 10;
    FILETIME created{}, exited{}, kernel{}, user{};
    bool recorded = false;
    if (GetProcessTimes(child.hProcess, &created, &exited, &kernel, &user)) {
        std::ofstream identity{std::filesystem::path(profile)};
        identity << child.dwProcessId << ' ' << ((static_cast<ULONGLONG>(created.dwHighDateTime) << 32) | created.dwLowDateTime);
        identity.flush(); recorded = identity.good();
    }
    const bool resumed = recorded && ResumeThread(child.hThread) != MAXDWORD;
    if (!resumed) TerminateProcess(child.hProcess, 11);
    CloseHandle(child.hThread); CloseHandle(child.hProcess);
    return resumed ? 37 : 12; // Deliberately fail while the owned descendant sleeps.
}
