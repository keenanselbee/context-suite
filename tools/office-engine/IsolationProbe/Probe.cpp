#include <winsock2.h>
#include <windows.h>
#include <aclapi.h>
#include <userenv.h>
#include <psapi.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace fs = std::filesystem;

// Independently authored OS-boundary experiment; never accepts customer documents.
struct Handle {
    HANDLE value = nullptr;
    explicit Handle(HANDLE handle = nullptr) : value(handle) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
struct LocalMemory {
    void* value = nullptr;
    ~LocalMemory() { if (value) LocalFree(value); }
};
struct Sid {
    PSID value = nullptr;
    ~Sid() { if (value) FreeSid(value); }
};
struct Profile {
    std::wstring name;
    bool created = false;
    ~Profile() {
        if (created && FAILED(DeleteAppContainerProfile(name.c_str())))
            std::cerr << "Disposable AppContainer profile cleanup failed; retain the recorded name for recovery.\n";
    }
    void Remove() {
        if (!created) return;
        const auto result = DeleteAppContainerProfile(name.c_str());
        if (FAILED(result)) throw std::runtime_error("Delete disposable AppContainer profile failed: " + std::to_string(result));
        created = false;
    }
};
struct Socket {
    SOCKET value = INVALID_SOCKET;
    explicit Socket(SOCKET socketValue = INVALID_SOCKET) : value(socketValue) {}
    ~Socket() { if (value != INVALID_SOCKET) closesocket(value); }
};
void Require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(std::string(message) + " (Windows error " + std::to_string(GetLastError()) + ")");
}
std::wstring Quote(const std::wstring& value) {
    std::wstring result = L"\"";
    size_t slashes = 0;
    for (const auto character : value) {
        if (character == L'\\') { ++slashes; continue; }
        result.append(character == L'"' ? slashes * 2 + 1 : slashes, L'\\');
        result.push_back(character); slashes = 0;
    }
    result.append(slashes * 2, L'\\'); result.push_back(L'"'); return result;
}
void Grant(const fs::path& path, PSID sid, DWORD access) {
    // Caller supplies only newly owned scratch paths, never an ancestor or system path.
    LocalMemory descriptor, replacement;
    PACL existing = nullptr;
    Require(GetNamedSecurityInfoW(path.c_str(), SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
        nullptr, nullptr, &existing, nullptr, &descriptor.value) == ERROR_SUCCESS, "Read scratch ACL");
    EXPLICIT_ACCESSW entry{};
    entry.grfAccessPermissions = access;
    entry.grfAccessMode = GRANT_ACCESS;
    entry.grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
    entry.Trustee.TrusteeForm = TRUSTEE_IS_SID;
    entry.Trustee.TrusteeType = TRUSTEE_IS_UNKNOWN;
    entry.Trustee.ptstrName = static_cast<LPWSTR>(sid);
    PACL acl = nullptr;
    Require(SetEntriesInAclW(1, &entry, existing, &acl) == ERROR_SUCCESS, "Build scratch ACL");
    replacement.value = acl;
    Require(SetNamedSecurityInfoW(const_cast<LPWSTR>(path.c_str()), SE_FILE_OBJECT,
        DACL_SECURITY_INFORMATION, nullptr, nullptr, acl, nullptr) == ERROR_SUCCESS, "Grant scratch access");
}
DWORD OpenAttempt(const fs::path& path, bool write) {
    Handle file(CreateFileW(path.c_str(), write ? GENERIC_WRITE : GENERIC_READ,
        FILE_SHARE_READ, nullptr, write ? CREATE_ALWAYS : OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
    return file.value == INVALID_HANDLE_VALUE ? GetLastError() : ERROR_SUCCESS;
}
int ConnectAttempt(unsigned short port) {
    Socket connection(socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
    if (connection.value == INVALID_SOCKET) return WSAGetLastError();
    u_long nonblocking = 1;
    if (ioctlsocket(connection.value, FIONBIO, &nonblocking) != 0) return WSAGetLastError();
    sockaddr_in address{};
    address.sin_family = AF_INET; address.sin_addr.s_addr = htonl(INADDR_LOOPBACK); address.sin_port = htons(port);
    if (connect(connection.value, reinterpret_cast<sockaddr*>(&address), sizeof(address)) == 0) return 0;
    const auto error = WSAGetLastError();
    if (error != WSAEWOULDBLOCK) return error;
    fd_set writeSet, errorSet; FD_ZERO(&writeSet); FD_ZERO(&errorSet);
    FD_SET(connection.value, &writeSet); FD_SET(connection.value, &errorSet);
    timeval timeout{ 2, 0 };
    if (select(0, nullptr, &writeSet, &errorSet, &timeout) <= 0) return WSAETIMEDOUT;
    int result = 0, length = sizeof(result);
    if (getsockopt(connection.value, SOL_SOCKET, SO_ERROR, reinterpret_cast<char*>(&result), &length) != 0) return WSAGetLastError();
    return result;
}
int Child(const fs::path& root, unsigned short port, bool isolated) {
    HANDLE tokenValue = nullptr;
    Require(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &tokenValue) != FALSE, "Read child token");
    Handle token(tokenValue);
    DWORD appContainer = 0, returned = 0;
    Require(GetTokenInformation(token.value, TokenIsAppContainer, &appContainer, sizeof(appContainer), &returned) != FALSE, "Read AppContainer flag");
    const auto allowedRead = OpenAttempt(root / L"allowed" / L"input.txt", false);
    const auto deniedRead = OpenAttempt(root / L"denied" / L"input.txt", false);
    const auto allowedWrite = OpenAttempt(root / L"writable" / L"output.txt", true);
    const auto deniedWrite = OpenAttempt(root / L"denied" / L"output.txt", true);
    const auto readOnlyWrite = OpenAttempt(root / L"allowed" / L"input.txt", true);
    const auto network = ConnectAttempt(port);
    std::cout << "{\"appContainer\":" << appContainer << ",\"allowedRead\":" << allowedRead
        << ",\"deniedRead\":" << deniedRead << ",\"allowedWrite\":" << allowedWrite
        << ",\"deniedWrite\":" << deniedWrite << ",\"readOnlyWrite\":" << readOnlyWrite
        << ",\"loopbackConnect\":" << network << "}\n";
    if (isolated) return appContainer == 1 && allowedRead == 0 && allowedWrite == 0 &&
        deniedRead == ERROR_ACCESS_DENIED && deniedWrite == ERROR_ACCESS_DENIED &&
        readOnlyWrite == ERROR_ACCESS_DENIED && network == WSAEACCES ? 0 : 3;
    return appContainer == 0 && allowedRead == 0 && deniedRead == 0 && allowedWrite == 0 &&
        deniedWrite == 0 && readOnlyWrite == 0 && network == 0 ? 0 : 4;
}
constexpr SIZE_T MiB = 1024ULL * 1024;
struct JobBudget { DWORD processes = 8; SIZE_T processBytes = 512 * MiB; SIZE_T jobBytes = 1024 * MiB; };
struct ChildResult { DWORD exitCode; std::string output; bool timedOut; bool outputLimit; DWORD totalProcesses; DWORD activeBeforeStop;
    SIZE_T peakProcessBytes; SIZE_T peakJobBytes; };
ChildResult Run(const fs::path& executable, const std::vector<std::wstring>& arguments,
    const fs::path& directory, PSID sid, ULONGLONG timeoutMilliseconds = 30000, JobBudget budget = {}) {
    Handle job(CreateJobObjectW(nullptr, nullptr));
    Require(job.value != nullptr, "Create owned job");
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
        JOB_OBJECT_LIMIT_ACTIVE_PROCESS | JOB_OBJECT_LIMIT_JOB_MEMORY | JOB_OBJECT_LIMIT_PROCESS_MEMORY;
    limits.BasicLimitInformation.ActiveProcessLimit = budget.processes;
    limits.ProcessMemoryLimit = budget.processBytes;
    limits.JobMemoryLimit = budget.jobBytes;
    Require(SetInformationJobObject(job.value, JobObjectExtendedLimitInformation, &limits, sizeof(limits)) != FALSE, "Set job limits");
    SECURITY_ATTRIBUTES security{ sizeof(security), nullptr, TRUE };
    HANDLE readValue = nullptr, writeValue = nullptr;
    Require(CreatePipe(&readValue, &writeValue, &security, 0) != FALSE, "Create diagnostics pipe");
    Handle read(readValue), write(writeValue);
    Require(SetHandleInformation(read.value, HANDLE_FLAG_INHERIT, 0) != FALSE, "Prevent read-handle inheritance");
    Handle input(CreateFileW(L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, &security, OPEN_EXISTING, 0, nullptr));
    Require(input.value != INVALID_HANDLE_VALUE, "Open empty input");
    HANDLE inherited[] = { input.value, write.value };
    SIZE_T bytes = 0;
    InitializeProcThreadAttributeList(nullptr, sid ? 2 : 1, 0, &bytes);
    Require(bytes > 0 && bytes <= 65536, "Size process attributes");
    std::vector<BYTE> storage(bytes);
    auto attributes = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(storage.data());
    Require(InitializeProcThreadAttributeList(attributes, sid ? 2 : 1, 0, &bytes) != FALSE, "Initialize process attributes");
    struct AttributesGuard { LPPROC_THREAD_ATTRIBUTE_LIST value; ~AttributesGuard() { DeleteProcThreadAttributeList(value); } } guard{ attributes };
    Require(UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited, sizeof(inherited), nullptr, nullptr) != FALSE, "Set inherited handle allowlist");
    SECURITY_CAPABILITIES capabilities{};
    capabilities.AppContainerSid = sid; // No network, broker or filesystem capabilities.
    if (sid) Require(UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES,
        &capabilities, sizeof(capabilities), nullptr, nullptr) != FALSE, "Set AppContainer token");
    STARTUPINFOEXW startup{};
    startup.StartupInfo.cb = sizeof(startup);
    startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
    startup.StartupInfo.hStdInput = input.value;
    startup.StartupInfo.hStdOutput = write.value;
    startup.StartupInfo.hStdError = write.value;
    startup.lpAttributeList = attributes;
    auto command = Quote(executable.native());
    for (const auto& argument : arguments) command += L" " + Quote(argument);
    Require(command.size() < 32767, "Bound command line");
    PROCESS_INFORMATION info{};
    Require(CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, TRUE,
        CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT, nullptr,
        directory.c_str(), &startup.StartupInfo, &info) != FALSE, "Create suspended child");
    Handle process(info.hProcess), thread(info.hThread);
    struct ProcessGuard { HANDLE value; ~ProcessGuard() { TerminateProcess(value, 1); } } terminate{ process.value };
    Require(AssignProcessToJobObject(job.value, process.value) != FALSE, "Assign before execution");
    Require(ResumeThread(thread.value) != MAXDWORD, "Resume contained child");
    CloseHandle(write.value); write.value = nullptr;
    std::string output;
    bool timedOut = false, outputLimit = false;
    const auto start = GetTickCount64();
    for (;;) {
        if (GetTickCount64() - start > timeoutMilliseconds) { timedOut = true; break; }
        DWORD available = 0;
        if (PeekNamedPipe(read.value, nullptr, 0, nullptr, &available, nullptr) && available) {
            char buffer[4096]; DWORD count = 0;
            Require(ReadFile(read.value, buffer, std::min<DWORD>(available, sizeof(buffer)), &count, nullptr) != FALSE, "Read diagnostics");
            if (output.size() + count > 65536) { outputLimit = true; break; }
            output.append(buffer, count);
            continue;
        }
        const auto waited = WaitForSingleObject(process.value, 20);
        Require(waited == WAIT_OBJECT_0 || waited == WAIT_TIMEOUT, "Observe child");
        if (waited == WAIT_OBJECT_0) {
            // Exit may race the first pipe peek. Drain bytes already written before returning.
            if (PeekNamedPipe(read.value, nullptr, 0, nullptr, &available, nullptr) && available) continue;
            break;
        }
    }
    JOBOBJECT_BASIC_ACCOUNTING_INFORMATION accounting{};
    Require(QueryInformationJobObject(job.value, JobObjectBasicAccountingInformation, &accounting, sizeof(accounting), nullptr) != FALSE, "Inspect owned descendants");
    const auto active = accounting.ActiveProcesses;
    const auto total = accounting.TotalProcesses;
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION observed{};
    Require(QueryInformationJobObject(job.value, JobObjectExtendedLimitInformation, &observed, sizeof(observed), nullptr) != FALSE, "Read actual job resource limits");
    Require(observed.BasicLimitInformation.LimitFlags == limits.BasicLimitInformation.LimitFlags &&
        observed.BasicLimitInformation.ActiveProcessLimit == budget.processes && observed.ProcessMemoryLimit == budget.processBytes &&
        observed.JobMemoryLimit == budget.jobBytes, "Job limits must retain the requested values");
    Require(TerminateJobObject(job.value, 1) != FALSE, "Terminate remaining owned descendants");
    const auto stopTime = GetTickCount64();
    do {
        Require(QueryInformationJobObject(job.value, JobObjectBasicAccountingInformation, &accounting, sizeof(accounting), nullptr) != FALSE, "Observe owned process cleanup");
        if (accounting.ActiveProcesses == 0) break;
        Require(GetTickCount64() - stopTime < 5000, "Owned processes must stop within cleanup deadline");
        Sleep(10);
    } while (true);
    DWORD exit = 0;
    Require(WaitForSingleObject(process.value, 1000) == WAIT_OBJECT_0 && GetExitCodeProcess(process.value, &exit) != FALSE, "Read stopped child result");
    return { exit, output, timedOut, outputLimit, total, active, observed.PeakProcessMemoryUsed, observed.PeakJobMemoryUsed };
}

struct CommittedMemory {
    void* value;
    explicit CommittedMemory(SIZE_T bytes) : value(VirtualAlloc(nullptr, bytes, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE)) {}
    ~CommittedMemory() { if (value) VirtualFree(value, 0, MEM_RELEASE); }
};
SIZE_T PrivateCommit() {
    PROCESS_MEMORY_COUNTERS_EX counters{};
    Require(GetProcessMemoryInfo(GetCurrentProcess(), reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters), sizeof(counters)) != FALSE,
        "Read actual process private commit");
    return counters.PrivateUsage;
}
int MemoryAttempt(SIZE_T bytes) {
    const auto before = PrivateCommit();
    CommittedMemory memory(bytes);
    const auto error = memory.value ? ERROR_SUCCESS : GetLastError();
    const auto after = PrivateCommit();
    Require(memory.value ? after >= before + bytes : after < before + bytes, "Private commit must agree with the allocation result");
    std::cout << "{\"requestedBytes\":" << bytes << ",\"allocated\":" << (memory.value ? "true" : "false") << ",\"error\":" << error
        << ",\"privateCommitBefore\":" << before << ",\"privateCommitAfter\":" << after << "}\n";
    return memory.value ? 0 : 20;
}
int SpawnPressure(const fs::path& executable, DWORD expectedLimit) {
    DWORD created = 0, error = ERROR_SUCCESS;
    std::vector<HANDLE> processes;
    struct ProcessHandles { std::vector<HANDLE>& values; ~ProcessHandles() { for (const auto value : values) CloseHandle(value); } } cleanup{ processes };
    for (; created < 12; ++created) {
        STARTUPINFOW startup{}; startup.cb = sizeof(startup);
        PROCESS_INFORMATION info{};
        auto command = Quote(executable.native()) + L" --sleep";
        if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
            nullptr, nullptr, &startup, &info)) { error = GetLastError(); break; }
        processes.push_back(info.hProcess); Handle thread(info.hThread);
    }
    DWORD live = 0;
    for (const auto process : processes) if (WaitForSingleObject(process, 0) == WAIT_TIMEOUT) ++live;
    Require(live == created, "Every created pressure helper must remain live");
    if (created != 12) Require(created > 0 && created + 1 == expectedLimit && error == ERROR_NOT_ENOUGH_QUOTA,
        "Quota refusal must occur at the expected application-process count");
    std::cout << "{\"requested\":12,\"created\":" << created << ",\"liveHelpers\":" << live << ",\"error\":" << error << "}\n";
    return created == 12 ? 0 : 21;
}
int AggregateMemory(const fs::path& executable, const fs::path& marker) {
    Require(!fs::exists(marker), "Aggregate marker must be new");
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION info{};
    auto command = Quote(executable.native()) + L" --hold-memory " + Quote(marker.native());
    Require(CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
        nullptr, nullptr, &startup, &info) != FALSE, "Start first aggregate allocation");
    Handle process(info.hProcess), thread(info.hThread);
    const auto started = GetTickCount64();
    while (!fs::exists(marker)) {
        Require(WaitForSingleObject(process.value, 10) == WAIT_TIMEOUT, "First aggregate allocation must stay live");
        Require(GetTickCount64() - started < 5000, "First allocation readiness deadline");
    }
    std::ifstream ready(marker); std::string value; std::getline(ready, value);
    Require(value == "committed-40-MiB", "Validate first allocation readiness");
    const auto result = MemoryAttempt(40 * MiB);
    Require(WaitForSingleObject(process.value, 0) == WAIT_TIMEOUT, "First aggregate allocation must remain live during the second attempt");
    return result;
}
void SaveResource(const fs::path& root, const char* name, const ChildResult& result, JobBudget budget) {
    std::ofstream(root / (std::string(name) + "-child.log")) << result.output;
    std::ofstream(root / (std::string(name) + ".json")) << "{\"exitCode\":" << result.exitCode
        << ",\"processLimit\":" << budget.processes << ",\"processMemoryLimit\":" << budget.processBytes
        << ",\"jobMemoryLimit\":" << budget.jobBytes << ",\"peakProcessBytes\":" << result.peakProcessBytes
        << ",\"peakJobBytes\":" << result.peakJobBytes << ",\"totalProcesses\":" << result.totalProcesses
        << ",\"activeBeforeStop\":" << result.activeBeforeStop << ",\"activeAfterStop\":0,\"timedOut\":"
        << (result.timedOut ? "true" : "false") << ",\"outputLimit\":" << (result.outputLimit ? "true" : "false") << "}\n";
}
void ResourceContracts(const fs::path& child, const fs::path& root) {
    const JobBudget memoryControl{ 8, 128 * MiB, 256 * MiB }, processMemoryLimit{ 8, 32 * MiB, 256 * MiB };
    const auto memory = Run(child, { L"--memory" }, root, nullptr, 30000, memoryControl);
    SaveResource(root, "memory-control", memory, memoryControl);
    Require(memory.exitCode == 0 && memory.peakProcessBytes >= 64 * MiB && !memory.timedOut && !memory.outputLimit,
        "Control must commit the requested memory");
    const auto limited = Run(child, { L"--memory" }, root, nullptr, 30000, processMemoryLimit);
    SaveResource(root, "memory-limited", limited, processMemoryLimit);
    Require(limited.exitCode == 20 && limited.peakProcessBytes <= processMemoryLimit.processBytes && !limited.timedOut && !limited.outputLimit,
        "Per-process memory limit must refuse the same allocation");
    const JobBudget aggregateLimit{ 8, 128 * MiB, 64 * MiB };
    const auto aggregate = Run(child, { L"--aggregate", child.native(), (root / L"writable" / L"aggregate-control.txt").native() }, root, nullptr, 30000, memoryControl);
    SaveResource(root, "aggregate-control", aggregate, memoryControl);
    Require(aggregate.exitCode == 0 && aggregate.peakJobBytes >= 80 * MiB && aggregate.activeBeforeStop >= 1 && !aggregate.timedOut && !aggregate.outputLimit,
        "Control must retain the first allocation while committing the second");
    const auto aggregateLimited = Run(child, { L"--aggregate", child.native(), (root / L"writable" / L"aggregate-limited.txt").native() }, root, nullptr, 30000, aggregateLimit);
    SaveResource(root, "aggregate-limited", aggregateLimited, aggregateLimit);
    // Windows can report a job peak above the limit after a refused commit.
    // The allocation result and actual private-commit delta are authoritative.
    Require(aggregateLimited.exitCode == 20 &&
        aggregateLimited.activeBeforeStop >= 1 && !aggregateLimited.timedOut && !aggregateLimited.outputLimit,
        "Aggregate limit must refuse the second allocation and clean the retained descendant");
    const JobBudget spawnControl{ 32, 128 * MiB, 256 * MiB }, spawnLimit{ 8, 128 * MiB, 256 * MiB };
    const auto spawned = Run(child, { L"--spawn-pressure", child.native(), L"32" }, root, nullptr, 30000, spawnControl);
    SaveResource(root, "process-control", spawned, spawnControl);
    Require(spawned.exitCode == 0 && spawned.totalProcesses >= 13 && spawned.activeBeforeStop >= 12 && !spawned.timedOut && !spawned.outputLimit,
        "Control must create all twelve descendants");
    const auto spawnLimited = Run(child, { L"--spawn-pressure", child.native(), L"8" }, root, nullptr, 30000, spawnLimit);
    SaveResource(root, "process-limited", spawnLimited, spawnLimit);
    // Job accounting can additionally include Windows console-host processes.
    // The child verifies live helper handles and ERROR_NOT_ENOUGH_QUOTA at 8.
    Require(spawnLimited.exitCode == 21 && spawnLimited.activeBeforeStop >= 7 &&
        !spawnLimited.timedOut && !spawnLimited.outputLimit, "Active process limit must refuse excess descendants");
    std::cout << "PASS: process memory, aggregate memory and active process limits; positive controls and zero-process cleanup passed.\n";
}
ULONGLONG CreationTime(HANDLE process) {
    FILETIME created{}, exited{}, kernel{}, user{};
    Require(GetProcessTimes(process, &created, &exited, &kernel, &user) != FALSE, "Read owned process creation time");
    return (static_cast<ULONGLONG>(created.dwHighDateTime) << 32) | created.dwLowDateTime;
}
HANDLE OpenVerifiedProcess(DWORD id, ULONGLONG created, const fs::path& executable) {
    Handle process(OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE, FALSE, id));
    Require(process.value != nullptr && CreationTime(process.value) == created, "Verify owned process identity before observation");
    wchar_t image[32768]{}; DWORD size = static_cast<DWORD>(std::size(image));
    Require(QueryFullProcessImageNameW(process.value, 0, image, &size) != FALSE && fs::equivalent(fs::path(image), executable),
        "Observed process must run this case's private probe copy");
    const auto result = process.value; process.value = nullptr; return result;
}
void HoldTree(const fs::path& executable, const fs::path& marker) {
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION info{};
    auto command = Quote(executable.native()) + L" --sleep";
    Require(CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
        nullptr, nullptr, &startup, &info) != FALSE, "Start owned grandchild before crash");
    Handle process(info.hProcess), thread(info.hThread);
    const auto pending = fs::path(marker.native() + L".pending");
    Require(!fs::exists(marker) && !fs::exists(pending), "Crash readiness must be new");
    {
        std::ofstream ready(pending);
        ready << GetCurrentProcessId() << ' ' << CreationTime(GetCurrentProcess()) << ' '
            << info.dwProcessId << ' ' << CreationTime(process.value) << '\n';
        ready.flush(); Require(ready.good(), "Write crash readiness identities");
    }
    fs::rename(pending, marker);
    Sleep(60000);
}
void OwnerCrashContract(const fs::path& executable, const fs::path& root) {
    const auto marker = root / L"writable" / L"owner-crash-ready.txt";
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION info{};
    auto command = Quote(executable.native()) + L" --crash-owner " + Quote(executable.native()) + L" " + Quote(marker.native());
    Require(CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
        nullptr, root.c_str(), &startup, &info) != FALSE, "Start disposable job owner");
    Handle owner(info.hProcess), thread(info.hThread);
    struct StopOwned { HANDLE value; ~StopOwned() { if (value) TerminateProcess(value, 72); } } stopOwner{ owner.value };
    const auto started = GetTickCount64();
    while (!fs::exists(marker)) {
        Require(WaitForSingleObject(owner.value, 10) == WAIT_TIMEOUT, "Job owner must remain live before crash");
        Require(GetTickCount64() - started < 10000, "Owned tree readiness deadline");
    }
    Require(fs::file_size(marker) < 256, "Bound crash readiness record");
    DWORD childId = 0, grandchildId = 0; ULONGLONG childCreated = 0, grandchildCreated = 0;
    std::ifstream ready(marker); ready >> childId >> childCreated >> grandchildId >> grandchildCreated;
    Require(!ready.fail() && childId != grandchildId && childId != info.dwProcessId && grandchildId != info.dwProcessId,
        "Distinct owned tree identities");
    Handle child(OpenVerifiedProcess(childId, childCreated, executable)); StopOwned stopChild{ child.value };
    Handle grandchild(OpenVerifiedProcess(grandchildId, grandchildCreated, executable)); StopOwned stopGrandchild{ grandchild.value };
    // Holding process handles does not keep the owner's job handle alive.
    Sleep(300);
    Require(WaitForSingleObject(owner.value, 0) == WAIT_TIMEOUT && WaitForSingleObject(child.value, 0) == WAIT_TIMEOUT &&
        WaitForSingleObject(grandchild.value, 0) == WAIT_TIMEOUT, "Owner, child and grandchild must all be live before forced termination");
    const auto killed = GetTickCount64();
    Require(TerminateProcess(owner.value, 71) != FALSE && WaitForSingleObject(owner.value, 5000) == WAIT_OBJECT_0,
        "Forcibly terminate only the owned launcher");
    HANDLE descendants[] = { child.value, grandchild.value };
    Require(WaitForMultipleObjects(2, descendants, TRUE, 5000) == WAIT_OBJECT_0,
        "Closing the crashed owner's job must terminate child and grandchild");
    DWORD ownerExit = 0, childExit = 0, grandchildExit = 0;
    Require(GetExitCodeProcess(owner.value, &ownerExit) && GetExitCodeProcess(child.value, &childExit) &&
        GetExitCodeProcess(grandchild.value, &grandchildExit) && ownerExit == 71, "Read forced-owner and descendant results");
    std::ofstream(root / L"owner-crash.json") << "{\"ownerPid\":" << info.dwProcessId << ",\"ownerExit\":" << ownerExit
        << ",\"childPid\":" << childId << ",\"childCreationTime\":" << childCreated << ",\"childExit\":" << childExit
        << ",\"grandchildPid\":" << grandchildId << ",\"grandchildCreationTime\":" << grandchildCreated << ",\"grandchildExit\":" << grandchildExit
        << ",\"allLiveBeforeCrash\":true,\"bothDescendantsStopped\":true,\"cleanupMilliseconds\":" << GetTickCount64() - killed << "}\n";
    std::cout << "PASS: forcibly terminated job owner; verified child and grandchild exit without graceful launcher cleanup.\n";
}
int wmain(int argc, wchar_t** argv) {
    try {
        WSADATA winsock{};
        Require(WSAStartup(MAKEWORD(2, 2), &winsock) == 0, "Initialize local network fixture");
        struct WinsockGuard { ~WinsockGuard() { WSACleanup(); } } winsockGuard;
        if (argc == 4 && std::wstring(argv[1]) == L"--hold-tree") { HoldTree(argv[2], argv[3]); return 0; }
        if (argc == 4 && std::wstring(argv[1]) == L"--crash-owner") {
            const auto result = Run(argv[2], { L"--hold-tree", argv[2], argv[3] }, fs::path(argv[3]).parent_path(), nullptr);
            return static_cast<int>(result.exitCode);
        }
        if (argc == 2 && std::wstring(argv[1]) == L"--sleep") { Sleep(60000); return 0; }
        if (argc == 2 && std::wstring(argv[1]) == L"--flood") { std::cout << std::string(100000, 'x') << std::flush; return 0; }
        if (argc == 2 && std::wstring(argv[1]) == L"--memory") return MemoryAttempt(64 * MiB);
        if (argc == 4 && std::wstring(argv[1]) == L"--spawn-pressure") return SpawnPressure(argv[2], std::stoul(argv[3]));
        if (argc == 4 && std::wstring(argv[1]) == L"--aggregate") return AggregateMemory(argv[2], argv[3]);
        if (argc == 3 && std::wstring(argv[1]) == L"--hold-memory") {
            CommittedMemory memory(40 * MiB);
            Require(memory.value != nullptr, "Hold first aggregate allocation");
            const fs::path markerPath(argv[2]);
            const auto pending = fs::path(markerPath.native() + L".pending");
            Require(!fs::exists(markerPath) && !fs::exists(pending), "Readiness paths must be new");
            { std::ofstream marker(pending); marker << "committed-40-MiB\n"; marker.flush(); Require(marker.good(), "Write allocation readiness"); }
            fs::rename(pending, markerPath); // Publish only the complete readiness record.
            Sleep(60000); return 0;
        }
        if (argc == 3 && std::wstring(argv[1]) == L"--orphan") {
            STARTUPINFOW startup{}; startup.cb = sizeof(startup);
            PROCESS_INFORMATION info{};
            auto command = Quote(argv[2]) + L" --sleep";
            Require(CreateProcessW(argv[2], command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
                nullptr, nullptr, &startup, &info) != FALSE, "Create owned sleeping descendant");
            Handle process(info.hProcess), thread(info.hThread);
            std::cout << "{\"descendant\":" << info.dwProcessId << "}\n";
            return 0;
        }
        if (argc == 5 && std::wstring(argv[1]) == L"--child")
            return Child(argv[2], static_cast<unsigned short>(std::stoul(argv[3])), std::wstring(argv[4]) == L"isolated");
        const bool createProfile = argc == 3 && std::wstring(argv[2]) == L"--create-disposable-profile";
        if (argc != 2 && !createProfile) return 2;
        const auto root = fs::absolute(argv[1]).lexically_normal();
        Require(root.native().find(L"\\.codex-temp\\office-isolation\\") != std::wstring::npos && !fs::exists(root), "Use fresh owned office-isolation scratch");
        fs::create_directories(root / L"allowed"); fs::create_directories(root / L"denied"); fs::create_directories(root / L"writable");
        std::ofstream(root / L"allowed" / L"input.txt") << "generated readable fixture";
        std::ofstream(root / L"denied" / L"input.txt") << "generated withheld fixture";
        wchar_t module[32768]{};
        const auto moduleSize = GetModuleFileNameW(nullptr, module, static_cast<DWORD>(std::size(module)));
        Require(moduleSize > 0 && moduleSize < std::size(module), "Locate independently built probe");
        const auto child = root / L"allowed" / L"probe.exe";
        fs::copy_file(module, child);
        Sid sid;
        Profile profile{ L"ContextSuite.Office.Evaluation." + root.parent_path().filename().native() };
        std::ofstream(root / L"profile-name.txt") << fs::path(profile.name).string() << '\n';
        if (createProfile) {
            // Explicit test opt-in only: creates per-user profile data outside repository scratch.
            const auto result = CreateAppContainerProfile(profile.name.c_str(), profile.name.c_str(),
                L"Disposable Context Suite native isolation test", nullptr, 0, &sid.value);
            if (FAILED(result)) throw std::runtime_error("Create disposable AppContainer profile failed: " + std::to_string(result));
            profile.created = true; // Never reuse or delete an existing profile on a collision.
        } else Require(SUCCEEDED(DeriveAppContainerSidFromAppContainerName(profile.name.c_str(), &sid.value)), "Derive disposable SID");
        Grant(root / L"allowed", sid.value, FILE_GENERIC_READ | FILE_GENERIC_EXECUTE);
        Grant(root / L"writable", sid.value, FILE_GENERIC_READ | FILE_GENERIC_WRITE | FILE_GENERIC_EXECUTE | DELETE | FILE_DELETE_CHILD);
        Socket server(socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
        Require(server.value != INVALID_SOCKET, "Create local listener");
        sockaddr_in address{};
        address.sin_family = AF_INET; address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        Require(bind(server.value, reinterpret_cast<sockaddr*>(&address), sizeof(address)) == 0 && listen(server.value, 8) == 0, "Start local listener");
        int addressSize = sizeof(address);
        Require(getsockname(server.value, reinterpret_cast<sockaddr*>(&address), &addressSize) == 0, "Read local listener port");
        const auto port = std::to_wstring(ntohs(address.sin_port));
        const auto control = Run(child, { L"--child", root.native(), port, L"control" }, root, nullptr);
        std::ofstream(root / L"control.json") << control.output;
        std::cout << "Control: " << control.output;
        Require(control.exitCode == 0 && !control.timedOut && !control.outputLimit && !control.output.empty(), "Unrestricted control must demonstrate accessible fixtures/listener");
        const auto orphan = Run(child, { L"--orphan", child.native() }, root, nullptr);
        std::ofstream(root / L"orphan.json") << "{\"exitCode\":" << orphan.exitCode << ",\"totalProcesses\":" << orphan.totalProcesses
            << ",\"activeBeforeStop\":" << orphan.activeBeforeStop << ",\"timedOut\":" << orphan.timedOut << ",\"outputLimit\":" << orphan.outputLimit << "}\n";
        // Windows may add a console host to the job; require the launcher and its explicit
        // sleeping descendant without assuming an exact platform helper count.
        Require(orphan.exitCode == 0 && orphan.totalProcesses >= 2 && orphan.activeBeforeStop >= 1 &&
            !orphan.timedOut && !orphan.outputLimit, "Job must terminate a descendant after its launcher exits");
        const auto timeout = Run(child, { L"--sleep" }, root, nullptr, 250);
        Require(timeout.timedOut && timeout.activeBeforeStop >= 1, "Job must enforce timeout and clean up");
        const auto flood = Run(child, { L"--flood" }, root, nullptr);
        Require(flood.outputLimit && !flood.timedOut && flood.output.size() <= 65536, "Job must bound diagnostics and clean up");
        std::ofstream(root / L"lifetime.json") << "{\"launcherExitCleanup\":true,\"timeoutCleanup\":true,\"diagnosticLimitCleanup\":true,\"activeProcessesAfterEach\":0}\n";
        std::cout << "PASS: launcher-exit descendant cleanup, timeout and diagnostic budget; every owned job reports zero remaining processes.\n";
        ResourceContracts(child, root);
        OwnerCrashContract(child, root);
        if (!createProfile) {
            std::cout << "NOT RUN: AppContainer access matrix requires explicit disposable-profile opt-in. Unregistered launch previously failed with Windows error 2.\n";
            return 0;
        }
        const auto isolated = Run(child, { L"--child", root.native(), port, L"isolated" }, root, sid.value);
        std::ofstream(root / L"isolated.json") << isolated.output;
        std::cout << "Isolated: " << isolated.output;
        Require(isolated.exitCode == 0 && !isolated.timedOut && !isolated.outputLimit && !isolated.output.empty(), "AppContainer access matrix");
        profile.Remove();
        std::cout << "PASS: explicit scratch read/write, withheld file and write denial, read-only source denial, local network denial, actual AppContainer token.\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
