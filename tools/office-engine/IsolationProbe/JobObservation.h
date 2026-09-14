#pragma once
#include <iomanip>
#include <set>

// Read-only process observation for the owned diagnostic job. Holding process
// handles preserves actual child exit codes after their PIDs leave the job list.
class JobObservation {
    struct Child { HANDLE process; std::wstring name; FILETIME created; bool exitedBeforeCleanup = false; };
    std::map<DWORD, Child> children;
    struct Window { DWORD pid; std::wstring kind; std::wstring text; bool visible; };
    std::vector<Window> windows;
    std::set<std::wstring> seenWindows;
    ULONGLONG nextWindowPoll = 0;
    unsigned textRequests = 0;
    static BOOL CALLBACK VisitWindow(HWND window, LPARAM context) {
        auto& observation = *reinterpret_cast<JobObservation*>(context);
        DWORD pid = 0; GetWindowThreadProcessId(window, &pid);
        if (!observation.children.contains(pid)) return TRUE;
        observation.ReadWindow(window, pid);
        EnumChildWindows(window, VisitChild, context);
        return TRUE;
    }
    static BOOL CALLBACK VisitChild(HWND window, LPARAM context) {
        auto& observation = *reinterpret_cast<JobObservation*>(context);
        DWORD pid = 0; GetWindowThreadProcessId(window, &pid);
        if (observation.children.contains(pid)) observation.ReadWindow(window, pid);
        return TRUE;
    }
    void ReadWindow(HWND window, DWORD pid) {
        if (windows.size() >= 24 || textRequests >= 32) return;
        wchar_t kind[128]{}, text[256]{};
        if (!GetClassNameW(window, kind, static_cast<int>(std::size(kind)))) return;
        const auto identity = std::to_wstring(reinterpret_cast<ULONG_PTR>(window)) + L":" + kind;
        if (seenWindows.contains(identity)) return;
        seenWindows.insert(identity); ++textRequests;
        DWORD_PTR copied = 0;
        SendMessageTimeoutW(window, WM_GETTEXT, std::size(text), reinterpret_cast<LPARAM>(text),
            SMTO_ABORTIFHUNG | SMTO_BLOCK, 50, &copied);
        text[std::size(text) - 1] = L'\0';
        windows.push_back({ pid, kind, text, IsWindowVisible(window) != FALSE });
    }
    static void Text(std::ostream& output, const std::wstring& value) {
        const auto flags = output.flags(); const auto fill = output.fill();
        output << '"';
        for (const auto character : value) output << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<unsigned>(character);
        output << '"'; output.flags(flags); output.fill(fill);
    }
public:
    ~JobObservation() { for (const auto& [pid, child] : children) CloseHandle(child.process); }
    void Poll(HANDLE job) {
        alignas(JOBOBJECT_BASIC_PROCESS_ID_LIST) BYTE storage[sizeof(JOBOBJECT_BASIC_PROCESS_ID_LIST) + 8 * sizeof(ULONG_PTR)]{};
        auto list = reinterpret_cast<JOBOBJECT_BASIC_PROCESS_ID_LIST*>(storage);
        Require(QueryInformationJobObject(job, JobObjectBasicProcessIdList, storage, sizeof(storage), nullptr) != FALSE,
            "Read owned diagnostic job membership");
        Require(list->NumberOfProcessIdsInList <= 8, "Bound observed job processes");
        for (DWORD index = 0; index < list->NumberOfProcessIdsInList; ++index) {
            const auto pid = static_cast<DWORD>(list->ProcessIdList[index]);
            if (children.contains(pid)) continue;
            Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, pid));
            if (!process.value) continue; // A child can finish before observation opens it.
            BOOL member = FALSE;
            Require(IsProcessInJob(process.value, job, &member) != FALSE, "Verify observed process ownership");
            if (!member) continue;
            wchar_t image[32768]{}; DWORD length = static_cast<DWORD>(std::size(image));
            FILETIME created{}, exit{}, kernel{}, user{};
            if (!QueryFullProcessImageNameW(process.value, 0, image, &length) ||
                !GetProcessTimes(process.value, &created, &exit, &kernel, &user)) continue;
            children.emplace(pid, Child{ process.value, fs::path(image).filename().native(), created });
            process.value = nullptr;
        }
        if (GetTickCount64() >= nextWindowPoll && windows.size() < 24 && textRequests < 32) {
            nextWindowPoll = GetTickCount64() + 500;
            EnumWindows(VisitWindow, reinterpret_cast<LPARAM>(this));
        }
    }
    void BeforeCleanup() {
        for (auto& [pid, child] : children) child.exitedBeforeCleanup = WaitForSingleObject(child.process, 0) == WAIT_OBJECT_0;
    }
    void Save(const fs::path& path) const {
        std::ofstream output(path); output << '['; bool first = true;
        for (const auto& [pid, child] : children) {
            DWORD exit = 0;
            Require(GetExitCodeProcess(child.process, &exit) != FALSE, "Read held child exit code");
            // Engine/runtime image basenames are ASCII. Refuse unexpected text
            // instead of writing unescaped process data into the JSON record.
            Require(child.name.find_first_not_of(L"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-") == std::wstring::npos,
                "Unexpected diagnostic image basename");
            std::string name;
            for (const auto character : child.name) name.push_back(static_cast<char>(character));
            if (!first) output << ','; first = false;
            output << "{\"pid\":" << pid << ",\"image\":\"" << name
                << "\",\"creationTime\":" << ((static_cast<ULONGLONG>(child.created.dwHighDateTime) << 32) | child.created.dwLowDateTime)
                << ",\"exitedBeforeCleanup\":" << (child.exitedBeforeCleanup ? "true" : "false") << ",\"exitCode\":" << exit << '}';
        }
        output << "]\n"; Require(output.good(), "Retain owned child observation");
        std::ofstream windowOutput(path.native() + L".windows.json"); windowOutput << '['; first = true;
        for (const auto& window : windows) {
            if (!first) windowOutput << ','; first = false;
            windowOutput << "{\"pid\":" << window.pid << ",\"class\":"; Text(windowOutput, window.kind);
            windowOutput << ",\"text\":"; Text(windowOutput, window.text);
            windowOutput << ",\"visible\":" << (window.visible ? "true" : "false") << '}';
        }
        windowOutput << "]\n"; Require(windowOutput.good(), "Retain owned window observation");
    }
};
