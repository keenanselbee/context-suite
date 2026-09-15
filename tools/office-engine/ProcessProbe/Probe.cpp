#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <string>

DWORD Access(const wchar_t* path, DWORD access, DWORD creation, bool write) {
    const auto file = CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, creation, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return GetLastError();
    DWORD result = 0;
    if (write) {
        const char text[] = "owned output"; DWORD written = 0;
        if (!WriteFile(file, text, sizeof(text) - 1, &written, nullptr) || written != sizeof(text) - 1) result = 1;
    }
    CloseHandle(file); return result;
}

int CheckAccess(bool revoked) {
    wchar_t source[32768]{}, denied[32768]{}, output[32768]{};
    if (!GetEnvironmentVariableW(L"CS_OFFICE_SOURCE", source, 32768) ||
        !GetEnvironmentVariableW(L"CS_OFFICE_PROGRAM", denied, 32768) ||
        !GetEnvironmentVariableW(L"CS_OFFICE_OUTPUT", output, 32768)) return 20;
    for (const auto path : {source, denied, output})
        if (std::wstring(path).find(L"\\.codex-temp\\office-isolation\\") == std::wstring::npos) return 21;
    const auto read = Access(source, GENERIC_READ, OPEN_EXISTING, false);
    const auto sourceWrite = Access(source, GENERIC_WRITE, OPEN_EXISTING, false);
    const auto withheld = Access(denied, GENERIC_READ, OPEN_EXISTING, false);
    const auto created = Access(output, GENERIC_WRITE, CREATE_NEW, true);
    const auto reply = "{\"sourceRead\":" + std::to_string(read) + ",\"sourceWrite\":" + std::to_string(sourceWrite) +
        ",\"withheldRead\":" + std::to_string(withheld) + ",\"outputWrite\":" + std::to_string(created) + "}\n";
    DWORD written = 0;
    if (!WriteFile(GetStdHandle(STD_OUTPUT_HANDLE), reply.data(), static_cast<DWORD>(reply.size()), &written, nullptr) || written != reply.size()) return 22;
    const DWORD expected = revoked ? ERROR_ACCESS_DENIED : ERROR_SUCCESS;
    return read == expected && sourceWrite == ERROR_ACCESS_DENIED && withheld == ERROR_ACCESS_DENIED && created == expected ? 0 : 23;
}

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
    wchar_t policy[32]{};
    if (argc == 1 && GetEnvironmentVariableW(L"CS_OFFICE_CALCULATION", policy, 32)) {
        if (std::wstring(policy) == L"access-test") return CheckAccess(false);
        if (std::wstring(policy) == L"access-revoked") return CheckAccess(true);
        if (std::wstring(policy) == L"access-hold") {
            if (CheckAccess(false) != 0 || !GetEnvironmentVariableW(L"CS_OFFICE_PROFILE", profile, 32768) ||
                std::wstring(profile).find(L"\\.codex-temp\\office-isolation\\") == std::wstring::npos || std::filesystem::exists(profile)) return 24;
            FILETIME created{}, exited{}, kernel{}, user{};
            if (!GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user)) return 25;
            {
                std::ofstream identity{std::filesystem::path(profile)};
                identity << GetCurrentProcessId() << ' ' << ((static_cast<ULONGLONG>(created.dwHighDateTime) << 32) | created.dwLowDateTime);
                identity.flush(); if (!identity.good()) return 26;
            }
            Sleep(60000); return 27;
        }
    }
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
