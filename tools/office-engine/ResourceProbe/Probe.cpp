#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <psapi.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
#include <stdexcept>

constexpr SIZE_T MiB = 1024 * 1024;
void Require(bool result) { if (!result) throw std::runtime_error("Resource probe assertion failed"); }
std::wstring Environment(const wchar_t* name) {
    wchar_t value[32768]{};
    const auto size = GetEnvironmentVariableW(name, value, 32768);
    Require(size > 0 && size < 32768); return value;
}
struct Memory {
    void* value;
    DWORD error;
    explicit Memory(SIZE_T bytes) {
        MEMORYSTATUSEX available{}; available.dwLength = sizeof(available);
        Require(GlobalMemoryStatusEx(&available) && available.ullAvailPageFile >= 3ULL * 1024 * MiB);
        value = VirtualAlloc(nullptr, bytes, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        error = value ? ERROR_SUCCESS : GetLastError();
    }
    ~Memory() { if (value) VirtualFree(value, 0, MEM_RELEASE); }
};
SIZE_T PrivateCommit() {
    PROCESS_MEMORY_COUNTERS_EX usage{};
    Require(GetProcessMemoryInfo(GetCurrentProcess(), reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&usage), sizeof(usage)) != FALSE);
    return usage.PrivateUsage;
}
std::string Identity(HANDLE process, DWORD pid) {
    FILETIME created{}, exited{}, kernel{}, user{};
    Require(GetProcessTimes(process, &created, &exited, &kernel, &user) != FALSE);
    return "{\"Pid\":" + std::to_string(pid) + ",\"Created\":" +
        std::to_string((static_cast<ULONGLONG>(created.dwHighDateTime) << 32) | created.dwLowDateTime) + "}";
}
void Save(const std::filesystem::path& path, const std::string& value) {
    Require(!std::filesystem::exists(path));
    // Rename only a new test marker, so the parent never reads a partial report.
    const auto partial = path.native() + L".partial";
    { std::ofstream stream{std::filesystem::path(partial)}; stream << value; stream.flush(); Require(stream.good()); }
    std::filesystem::rename(partial, path);
}
int wmain(int argc, wchar_t** argv) {
    try {
        wchar_t module[32768]{};
        Require(GetModuleFileNameW(nullptr, module, 32768) != 0 &&
            std::wstring(module).find(L"\\.codex-temp\\office-isolation\\") != std::wstring::npos);
        HANDLE token = nullptr;
        Require(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token) != FALSE);
        DWORD isolated = 0, returned = 0;
        const auto checked = GetTokenInformation(token, TokenIsAppContainer, &isolated, sizeof(isolated), &returned);
        CloseHandle(token); Require(checked && isolated == 1);
        const auto marker = Environment(L"CS_OFFICE_OUTPUT");
        Require(marker.find(L"\\.codex-temp\\office-isolation\\") != std::wstring::npos);
        if (argc == 2 && std::wstring(argv[1]) == L"--hold") {
            Memory memory(600 * MiB); Require(memory.value != nullptr);
            Save(marker + L".child", "600-MiB-committed"); Sleep(30000); return 90;
        }
        Require(argc == 1);
        const auto mode = Environment(L"CS_OFFICE_CALCULATION");
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        Require(QueryInformationJobObject(nullptr, JobObjectExtendedLimitInformation, &limits, sizeof(limits), nullptr) != FALSE);
        Require(limits.BasicLimitInformation.ActiveProcessLimit == 8 && limits.ProcessMemoryLimit == 1024 * MiB &&
            limits.JobMemoryLimit == 1024 * MiB && limits.BasicLimitInformation.LimitFlags == (0x2000 | 8 | 0x100 | 0x200));
        std::vector<HANDLE> children;
        struct CloseChildren { std::vector<HANDLE>& items; ~CloseChildren() { for (const auto item : items) CloseHandle(item); } } close{children};
        std::string identities = "[";
        auto spawn = [&](bool hold) {
            STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION child{};
            auto command = L"\"" + std::wstring(module) + L"\"" + (hold ? L" --hold" : L"");
            if (!CreateProcessW(module, command.data(), nullptr, nullptr, FALSE, CREATE_SUSPENDED | CREATE_NO_WINDOW,
                nullptr, nullptr, &startup, &child)) return GetLastError();
            children.push_back(child.hProcess);
            if (children.size() != 1) identities += ',';
            identities += Identity(child.hProcess, child.dwProcessId);
            if (hold) Require(ResumeThread(child.hThread) != MAXDWORD);
            CloseHandle(child.hThread); return static_cast<DWORD>(0);
        };
        DWORD error = 0; SIZE_T requested = 0, before = 0, after = 0; bool allocated = false;
        if (mode == L"aggregate") {
            Require(spawn(true) == 0);
            const auto started = GetTickCount64();
            while (!std::filesystem::exists(marker + L".child")) {
                Require(GetTickCount64() - started < 5000 && WaitForSingleObject(children[0], 10) == WAIT_TIMEOUT);
            }
            std::ifstream ready{std::filesystem::path(marker + L".child")}; std::string value; std::getline(ready, value);
            Require(value == "600-MiB-committed");
        }
        if (mode == L"control" || mode == L"memory" || mode == L"aggregate") {
            requested = (mode == L"memory" ? 1040 : 600) * MiB; before = PrivateCommit();
            Memory memory(requested); allocated = memory.value != nullptr; error = memory.error; after = PrivateCommit();
            Require(allocated ? after >= before + requested : after < before + requested);
            Require(mode == L"control" ? allocated : !allocated);
            if (mode == L"aggregate") Require(WaitForSingleObject(children[0], 0) == WAIT_TIMEOUT);
        } else if (mode == L"process") {
            for (int attempt = 0; attempt < 8; ++attempt) { error = spawn(false); if (error != 0) break; }
            Require(children.size() == 7 && error == ERROR_NOT_ENOUGH_QUOTA);
            for (const auto child : children) Require(WaitForSingleObject(child, 0) == WAIT_TIMEOUT);
        } else Require(mode == L"stdout" || mode == L"stderr");
        identities += ']';
        const DWORD floodBytes = mode == L"stdout" ? 36 * 1024 + 1 : mode == L"stderr" ? 65537 : 0;
        const auto report = "{\"Root\":" + Identity(GetCurrentProcess(), GetCurrentProcessId()) + ",\"Children\":" + identities +
            ",\"Requested\":" + std::to_string(requested) + ",\"Allocated\":" + (allocated ? "true" : "false") +
            ",\"Error\":" + std::to_string(error) + ",\"PrivateBefore\":" + std::to_string(before) +
            ",\"PrivateAfter\":" + std::to_string(after) + ",\"FloodBytes\":" + std::to_string(floodBytes) +
            ",\"ProcessLimit\":8,\"MemoryLimit\":1073741824}";
        Save(marker, report);
        if (mode == L"stdout" || mode == L"stderr") {
            const std::string flood(floodBytes, 'x'); DWORD written = 0;
            Require(WriteFile(GetStdHandle(mode == L"stdout" ? STD_OUTPUT_HANDLE : STD_ERROR_HANDLE), flood.data(),
                static_cast<DWORD>(flood.size()), &written, nullptr) && written == flood.size());
            Sleep(30000); return 91;
        }
        if (mode != L"control") return 37;
        const std::string reply = "{\"Control\":true}"; DWORD written = 0;
        Require(WriteFile(GetStdHandle(STD_OUTPUT_HANDLE), reply.data(), static_cast<DWORD>(reply.size()), &written, nullptr) && written == reply.size());
        return 0;
    } catch (...) { return 99; }
}
