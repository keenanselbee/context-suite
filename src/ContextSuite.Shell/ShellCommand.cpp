#include <windows.h>
#include <shlobj.h>
#include <shobjidl_core.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <filesystem>
#include <new>
#include <string>
#include <string_view>
#include <vector>

#define RETURN_IF_FAILED(expression) \
    do \
    { \
        const HRESULT returnIfFailedResult = (expression); \
        if (FAILED(returnIfFailedResult)) \
        { \
            return returnIfFailedResult; \
        } \
    } while (false)

namespace
{
constexpr CLSID AnalyzeCommandClsid =
    {0x7f71bfc7, 0x0125, 0x47b6, {0xa1, 0x63, 0x54, 0xea, 0xdd, 0xb5, 0x6e, 0x8b}};
constexpr CLSID ConvertCommandClsid =
    {0xc0a78640, 0x7313, 0x4ad7, {0x87, 0x0c, 0x2f, 0x0b, 0x83, 0x8a, 0x28, 0xc2}};
constexpr CLSID OptimizeCommandClsid =
    {0x8c4ca2a6, 0x99a2, 0x436a, {0x90, 0x21, 0x3d, 0x00, 0x46, 0x87, 0xfc, 0x4c}};
constexpr GUID AnalyzeActionGuid =
    {0x16a6f59c, 0x5738, 0x495c, {0xa8, 0x00, 0x69, 0xa4, 0xe2, 0x39, 0x2d, 0x50}};
constexpr GUID ConvertActionGuid =
    {0x1ef0b7b8, 0xafde, 0x4d6d, {0x8d, 0x47, 0xf5, 0xeb, 0x11, 0xba, 0x7d, 0xc1}};
constexpr GUID OptimizeActionGuid =
    {0xed6271f2, 0xf9ac, 0x4dd6, {0x8b, 0x0f, 0xc5, 0x32, 0x69, 0x3b, 0x48, 0x62}};

constexpr DWORD MaximumSelectionCount = 4096;
constexpr size_t MaximumRequestBytes = 4 * 1024 * 1024;

HINSTANCE moduleInstance = nullptr;
std::atomic_ulong objectCount = 0;

enum class CommandKind
{
    Analyze,
    Convert,
    Optimize,
};

enum class CommandRole { Root, Action, Separator, Settings };

struct CommandDefinition
{
    CommandKind kind;
    const wchar_t* title;
    const wchar_t* tooltip;
    const wchar_t* operation;
    const wchar_t* actionTitle;
    const wchar_t* actionTooltip;
    const wchar_t* action;
    const wchar_t* iconFileName;
    bool hasSubcommands;
    CLSID canonicalName;
    GUID actionCanonicalName;
};

const CommandDefinition& GetDefinition(CommandKind kind)
{
    static const CommandDefinition analyze{
        CommandKind::Analyze,
        L"Analyze",
        L"Analyze the selected media files with Context Suite",
        L"analyze",
        L"Open details...",
        L"Open analysis details for the complete selection",
        L"open-details",
        L"Analyze.ico",
        false,
        AnalyzeCommandClsid,
        AnalyzeActionGuid};
    static const CommandDefinition convert{
        CommandKind::Convert,
        L"Convert",
        L"Convert the selected media files with Context Suite",
        L"convert",
        L"Choose format...",
        L"Choose one conversion target for the complete selection",
        L"choose-format",
        L"Convert.ico",
        true,
        ConvertCommandClsid,
        ConvertActionGuid};
    static const CommandDefinition optimize{
        CommandKind::Optimize,
        L"Optimize",
        L"Optimize the selected media files with Context Suite",
        L"optimize",
        L"Auto",
        L"Balance quality and size; output follows Settings and preserve metadata",
        L"auto",
        L"Optimize.ico",
        true,
        OptimizeCommandClsid,
        OptimizeActionGuid};

    switch (kind)
    {
    case CommandKind::Analyze:
        return analyze;
    case CommandKind::Convert:
        return convert;
    default:
        return optimize;
    }
}

HRESULT DuplicateString(std::wstring_view value, PWSTR* destination)
{
    if (destination == nullptr)
    {
        return E_POINTER;
    }

    *destination = nullptr;
    const auto bytes = (value.size() + 1) * sizeof(wchar_t);
    auto* copy = static_cast<PWSTR>(CoTaskMemAlloc(bytes));
    if (copy == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    memcpy(copy, value.data(), value.size() * sizeof(wchar_t));
    copy[value.size()] = L'\0';
    *destination = copy;
    return S_OK;
}

std::string ToUtf8(std::wstring_view value)
{
    if (value.empty())
    {
        return {};
    }

    const int byteCount = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        nullptr,
        0,
        nullptr,
        nullptr);
    if (byteCount <= 0)
    {
        return {};
    }

    std::string result(static_cast<size_t>(byteCount), '\0');
    if (WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            byteCount,
            nullptr,
            nullptr) <= 0)
    {
        return {};
    }

    return result;
}

HRESULT GetSelectionPaths(IShellItemArray* items, std::vector<std::wstring>& paths)
{
    if (items == nullptr)
    {
        return E_INVALIDARG;
    }

    DWORD count = 0;
    RETURN_IF_FAILED(items->GetCount(&count));
    if (count == 0 || count > MaximumSelectionCount)
    {
        return HRESULT_FROM_WIN32(ERROR_INVALID_PARAMETER);
    }

    paths.reserve(count);
    for (DWORD index = 0; index < count; ++index)
    {
        IShellItem* item = nullptr;
        HRESULT result = items->GetItemAt(index, &item);
        if (FAILED(result))
        {
            return result;
        }

        PWSTR rawPath = nullptr;
        result = item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
        item->Release();
        if (FAILED(result))
        {
            return result;
        }

        std::wstring path(rawPath);
        CoTaskMemFree(rawPath);
        if (path.empty() || path.find_first_of(L"\r\n") != std::wstring::npos)
        {
            return HRESULT_FROM_WIN32(ERROR_INVALID_NAME);
        }

        paths.push_back(std::move(path));
    }

    return S_OK;
}

HRESULT EnsureDirectory(const std::filesystem::path& path)
{
    std::error_code error;
    std::filesystem::create_directories(path, error);
    if (error)
    {
        return HRESULT_FROM_WIN32(error.value());
    }

    return S_OK;
}

HRESULT GetActivationDirectory(std::filesystem::path& directory)
{
    PWSTR localAppData = nullptr;
    const HRESULT result = SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_CREATE, nullptr, &localAppData);
    if (FAILED(result))
    {
        return result;
    }

    directory = std::filesystem::path(localAppData) / L"ContextSuite" / L"Prototype" / L"Activations";
    CoTaskMemFree(localAppData);
    return EnsureDirectory(directory);
}

std::wstring CreateRequestName()
{
    GUID requestId{};
    if (FAILED(CoCreateGuid(&requestId)))
    {
        return {};
    }

    wchar_t buffer[40]{};
    if (StringFromGUID2(requestId, buffer, ARRAYSIZE(buffer)) == 0)
    {
        return {};
    }

    std::wstring value(buffer);
    if (value.size() >= 2 && value.front() == L'{' && value.back() == L'}')
    {
        value = value.substr(1, value.size() - 2);
    }

    return value;
}

HRESULT WriteAll(HANDLE file, const std::string& content)
{
    size_t offset = 0;
    while (offset < content.size())
    {
        const auto remaining = content.size() - offset;
        const DWORD requested = static_cast<DWORD>(std::min<size_t>(remaining, MAXDWORD));
        DWORD written = 0;
        if (!WriteFile(file, content.data() + offset, requested, &written, nullptr))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        if (written == 0)
        {
            return HRESULT_FROM_WIN32(ERROR_WRITE_FAULT);
        }
        offset += written;
    }

    return S_OK;
}

HRESULT CreateActivationRequest(
    const CommandDefinition& definition,
    std::wstring_view action,
    const std::vector<std::wstring>& paths,
    std::filesystem::path& requestPath)
{
    std::filesystem::path directory;
    RETURN_IF_FAILED(GetActivationDirectory(directory));

    const std::wstring requestId = CreateRequestName();
    if (requestId.empty())
    {
        return E_FAIL;
    }

    std::string content = "ContextSuiteActivation/1\n";
    content += "requestId=" + ToUtf8(requestId) + "\n";
    content += "operation=" + ToUtf8(definition.operation) + "\n";
    content += "action=" + ToUtf8(action) + "\n";
    content += "pathCount=" + std::to_string(paths.size()) + "\n";
    for (const auto& path : paths)
    {
        const std::string encodedPath = ToUtf8(path);
        if (encodedPath.empty())
        {
            return HRESULT_FROM_WIN32(ERROR_NO_UNICODE_TRANSLATION);
        }
        content += "path=" + encodedPath + "\n";
        if (content.size() > MaximumRequestBytes)
        {
            return HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE);
        }
    }

    const std::filesystem::path temporaryPath = directory / (requestId + L".tmp");
    requestPath = directory / (requestId + L".request");
    HANDLE file = CreateFileW(
        temporaryPath.c_str(),
        GENERIC_WRITE,
        0,
        nullptr,
        CREATE_NEW,
        FILE_ATTRIBUTE_TEMPORARY,
        nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    HRESULT result = WriteAll(file, content);
    if (SUCCEEDED(result) && !FlushFileBuffers(file))
    {
        result = HRESULT_FROM_WIN32(GetLastError());
    }
    CloseHandle(file);

    if (FAILED(result))
    {
        DeleteFileW(temporaryPath.c_str());
        return result;
    }

    if (!MoveFileExW(temporaryPath.c_str(), requestPath.c_str(), MOVEFILE_WRITE_THROUGH))
    {
        result = HRESULT_FROM_WIN32(GetLastError());
        DeleteFileW(temporaryPath.c_str());
        return result;
    }

    return S_OK;
}

std::filesystem::path GetHostPath()
{
    std::vector<wchar_t> buffer(512);
    while (buffer.size() <= 32768)
    {
        const DWORD length = GetModuleFileNameW(moduleInstance, buffer.data(), static_cast<DWORD>(buffer.size()));
        if (length == 0)
        {
            return {};
        }
        if (length < buffer.size() - 1)
        {
            const auto directory = std::filesystem::path(std::wstring(buffer.data(), length)).parent_path();
            const auto application = directory / L"ContextSuite.Application.exe";
            if (GetFileAttributesW(application.c_str()) != INVALID_FILE_ATTRIBUTES)
            {
                return application;
            }
            return directory / L"ContextSuite.Host.exe";
        }
        buffer.resize(buffer.size() * 2);
    }

    return {};
}

std::wstring QuoteArgument(const std::wstring& value)
{
    std::wstring result = L"\"";
    size_t backslashCount = 0;
    for (const wchar_t character : value)
    {
        if (character == L'\\')
        {
            ++backslashCount;
            continue;
        }
        if (character == L'\"')
        {
            result.append(backslashCount * 2 + 1, L'\\');
            result.push_back(character);
            backslashCount = 0;
            continue;
        }
        result.append(backslashCount, L'\\');
        backslashCount = 0;
        result.push_back(character);
    }
    result.append(backslashCount * 2, L'\\');
    result.push_back(L'\"');
    return result;
}

HRESULT LaunchHost(const std::filesystem::path& requestPath)
{
    const std::filesystem::path hostPath = GetHostPath();
    if (hostPath.empty() || GetFileAttributesW(hostPath.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    std::wstring commandLine = QuoteArgument(hostPath.wstring()) + L" --activation-file " +
        QuoteArgument(requestPath.wstring());
    STARTUPINFOW startupInfo{};
    startupInfo.cb = sizeof(startupInfo);
    PROCESS_INFORMATION processInfo{};
    if (!CreateProcessW(
            hostPath.c_str(),
            commandLine.data(),
            nullptr,
            nullptr,
            FALSE,
            CREATE_DEFAULT_ERROR_MODE,
            nullptr,
            hostPath.parent_path().c_str(),
            &startupInfo,
            &processInfo))
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    CloseHandle(processInfo.hThread);
    CloseHandle(processInfo.hProcess);
    return S_OK;
}

std::filesystem::path GetCommandIconPath(const CommandDefinition& definition)
{
    const std::filesystem::path hostPath = GetHostPath();
    if (hostPath.empty())
    {
        return {};
    }

    const std::filesystem::path iconPath = hostPath.parent_path() / L"Assets" / definition.iconFileName;
    return GetFileAttributesW(iconPath.c_str()) == INVALID_FILE_ATTRIBUTES ? std::filesystem::path{} : iconPath;
}

class ExplorerCommand final : public IExplorerCommand
{
public:
    explicit ExplorerCommand(CommandKind kind, CommandRole role = CommandRole::Root, unsigned preset = 0) :
        definition_(GetDefinition(kind)),
        role_(role), preset_(preset)
    {
        ++objectCount;
    }

    ~ExplorerCommand()
    {
        --objectCount;
    }

    IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** object) override
    {
        if (object == nullptr)
        {
            return E_POINTER;
        }

        *object = nullptr;
        if (IsEqualIID(interfaceId, IID_IUnknown) || IsEqualIID(interfaceId, IID_IExplorerCommand))
        {
            *object = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return ++referenceCount_;
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const ULONG remaining = --referenceCount_;
        if (remaining == 0)
        {
            delete this;
        }
        return remaining;
    }

    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) override
    {
        if (role_ == CommandRole::Separator) return DuplicateString(L"", title);
        if (role_ == CommandRole::Settings) return DuplicateString(L"Settings...", title);
        if (role_ == CommandRole::Action && definition_.kind == CommandKind::Optimize)
            return DuplicateString(PresetTitles[preset_], title);
        if (role_ == CommandRole::Action && definition_.kind == CommandKind::Convert)
            return DuplicateString(ConvertTitles[preset_], title);
        return DuplicateString(role_ == CommandRole::Root ? definition_.title : definition_.actionTitle, title);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override
    {
        if (icon == nullptr)
        {
            return E_POINTER;
        }
        *icon = nullptr;
        if (role_ == CommandRole::Separator) return E_NOTIMPL;
        const std::filesystem::path iconPath = GetCommandIconPath(definition_);
        return iconPath.empty() ? E_NOTIMPL : DuplicateString(iconPath.wstring(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tooltip) override
    {
        if (role_ == CommandRole::Settings) return DuplicateString(L"Open settings without processing selected files", tooltip);
        if (role_ == CommandRole::Separator) return DuplicateString(L"", tooltip);
        if (role_ == CommandRole::Action && definition_.kind == CommandKind::Optimize)
            return DuplicateString(PresetTooltips[preset_], tooltip);
        if (role_ == CommandRole::Action && definition_.kind == CommandKind::Convert)
            return DuplicateString(preset_ == 5 ? L"Choose format, quality and advanced settings" :
                L"Create converted copies; ask only when transparency, metadata or quality needs a decision", tooltip);
        return DuplicateString(role_ == CommandRole::Root ? definition_.tooltip : definition_.actionTooltip, tooltip);
    }

    IFACEMETHODIMP GetCanonicalName(GUID* canonicalName) override
    {
        if (canonicalName == nullptr)
        {
            return E_POINTER;
        }
        *canonicalName = role_ == CommandRole::Root ? definition_.canonicalName : definition_.actionCanonicalName;
        // Stable distinct IDs for auxiliary commands; existing root/action IDs do not change.
        if (role_ == CommandRole::Settings) canonicalName->Data1 ^= 0x40000000;
        if (role_ == CommandRole::Separator) canonicalName->Data1 ^= 0x80000000;
        if (role_ == CommandRole::Action && definition_.kind == CommandKind::Optimize) canonicalName->Data1 ^= preset_ + 1;
        if (role_ == CommandRole::Action && definition_.kind == CommandKind::Convert && preset_ < 5) canonicalName->Data1 ^= preset_ + 1;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        if (state == nullptr)
        {
            return E_POINTER;
        }

        if (role_ == CommandRole::Settings || role_ == CommandRole::Separator)
        {
            *state = ECS_ENABLED;
            return S_OK;
        }
        DWORD count = 0;
        if (items == nullptr || FAILED(items->GetCount(&count)) || count == 0 || count > MaximumSelectionCount)
        {
            *state = ECS_HIDDEN;
            return S_OK;
        }

        *state = ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        if (role_ == CommandRole::Separator || (role_ == CommandRole::Root && definition_.hasSubcommands))
        {
            return E_NOTIMPL;
        }

        std::vector<std::wstring> paths;
        if (role_ != CommandRole::Settings) RETURN_IF_FAILED(GetSelectionPaths(items, paths));

        std::filesystem::path requestPath;
        const auto action = role_ == CommandRole::Settings ? L"settings" :
            definition_.kind == CommandKind::Optimize ? PresetActions[preset_] :
            definition_.kind == CommandKind::Convert ? ConvertActions[preset_] : definition_.action;
        RETURN_IF_FAILED(CreateActivationRequest(definition_, action, paths, requestPath));

        const HRESULT result = LaunchHost(requestPath);
        if (FAILED(result))
        {
            DeleteFileW(requestPath.c_str());
        }
        return result;
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        if (flags == nullptr)
        {
            return E_POINTER;
        }
        *flags = role_ == CommandRole::Separator ? ECF_ISSEPARATOR :
            (role_ == CommandRole::Root && definition_.hasSubcommands ? ECF_HASSUBCOMMANDS : ECF_DEFAULT);
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override;

private:
    std::atomic_ulong referenceCount_{1};
    const CommandDefinition& definition_;
    CommandRole role_;
    unsigned preset_;
    static constexpr const wchar_t* ConvertTitles[] = { L"PNG", L"JPEG", L"WebP (lossless)", L"BMP", L"TGA", L"DDS..." };
    static constexpr const wchar_t* ConvertActions[] = { L"png", L"jpeg", L"webp", L"bmp", L"tga", L"dds" };
    static constexpr const wchar_t* PresetTitles[] = { L"Auto", L"Lossless", L"Balanced", L"Smallest" };
    static constexpr const wchar_t* PresetActions[] = { L"auto", L"lossless", L"balanced", L"smallest" };
    static constexpr const wchar_t* PresetTooltips[] = {
        L"Balance quality and size with gentle loss only when worthwhile; output follows Settings",
        L"Reduce file size without changing pixels; output follows Settings",
        L"Allow slight RGB precision loss for smaller files; output follows Settings",
        L"Allow stronger RGB precision loss; banding may be visible; output follows Settings" };
};

class CommandEnumerator final : public IEnumExplorerCommand
{
public:
    explicit CommandEnumerator(CommandKind kind) :
        kind_(kind), commandCount_(kind == CommandKind::Optimize ? 6 : 8)
    {
        ++objectCount;
        const unsigned count = commandCount_ - 2;
        for (unsigned preset = 0; preset < count; ++preset)
            commands_[preset] = new (std::nothrow) ExplorerCommand(kind, CommandRole::Action, preset);
        commands_[count] = new (std::nothrow) ExplorerCommand(kind, CommandRole::Separator);
        commands_[count + 1] = new (std::nothrow) ExplorerCommand(kind, CommandRole::Settings);
    }

    ~CommandEnumerator()
    {
        for (auto* command : commands_)
        {
            if (command != nullptr) command->Release();
        }
        --objectCount;
    }

    bool IsValid() const
    {
        return std::all_of(commands_.begin(), commands_.begin() + commandCount_, [](auto* command) { return command != nullptr; });
    }

    IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** object) override
    {
        if (object == nullptr)
        {
            return E_POINTER;
        }
        *object = nullptr;
        if (IsEqualIID(interfaceId, IID_IUnknown) || IsEqualIID(interfaceId, IID_IEnumExplorerCommand))
        {
            *object = static_cast<IEnumExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return ++referenceCount_;
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const ULONG remaining = --referenceCount_;
        if (remaining == 0)
        {
            delete this;
        }
        return remaining;
    }

    IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override
    {
        if (commands == nullptr || (count != 1 && fetched == nullptr))
        {
            return E_POINTER;
        }
        if (fetched != nullptr)
        {
            *fetched = 0;
        }
        for (ULONG index = 0; index < count; ++index)
        {
            commands[index] = nullptr;
        }
        ULONG actual = 0;
        while (actual < count && position_ < commandCount_)
        {
            commands_[position_]->AddRef();
            commands[actual++] = commands_[position_++];
        }
        if (fetched != nullptr) *fetched = actual;
        return actual == count ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Skip(ULONG count) override
    {
        const auto actual = std::min(count, commandCount_ - position_);
        position_ += actual;
        return actual == count ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Reset() override
    {
        position_ = 0;
        return S_OK;
    }

    IFACEMETHODIMP Clone(IEnumExplorerCommand** commands) override
    {
        if (commands == nullptr)
        {
            return E_POINTER;
        }
        *commands = nullptr;
        auto* clone = new (std::nothrow) CommandEnumerator(kind_);
        if (clone == nullptr || !clone->IsValid())
        {
            delete clone;
            return E_OUTOFMEMORY;
        }
        clone->position_ = position_;
        *commands = clone;
        return S_OK;
    }

private:
    std::atomic_ulong referenceCount_{1};
    std::array<ExplorerCommand*, 8> commands_{};
    CommandKind kind_;
    ULONG commandCount_;
    ULONG position_ = 0;
};

HRESULT ExplorerCommand::EnumSubCommands(IEnumExplorerCommand** commands)
{
    if (commands == nullptr)
    {
        return E_POINTER;
    }
    *commands = nullptr;
    if (role_ != CommandRole::Root || !definition_.hasSubcommands)
    {
        return E_NOTIMPL;
    }

    auto* enumerator = new (std::nothrow) CommandEnumerator(definition_.kind);
    if (enumerator == nullptr || !enumerator->IsValid())
    {
        delete enumerator;
        return E_OUTOFMEMORY;
    }
    *commands = enumerator;
    return S_OK;
}

class CommandFactory final : public IClassFactory
{
public:
    explicit CommandFactory(CommandKind kind) : kind_(kind)
    {
        ++objectCount;
    }

    ~CommandFactory()
    {
        --objectCount;
    }

    IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** object) override
    {
        if (object == nullptr)
        {
            return E_POINTER;
        }

        *object = nullptr;
        if (IsEqualIID(interfaceId, IID_IUnknown) || IsEqualIID(interfaceId, IID_IClassFactory))
        {
            *object = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return ++referenceCount_;
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const ULONG remaining = --referenceCount_;
        if (remaining == 0)
        {
            delete this;
        }
        return remaining;
    }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID interfaceId, void** object) override
    {
        if (outer != nullptr)
        {
            return CLASS_E_NOAGGREGATION;
        }

        auto* command = new (std::nothrow) ExplorerCommand(kind_);
        if (command == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        const HRESULT result = command->QueryInterface(interfaceId, object);
        command->Release();
        return result;
    }

    IFACEMETHODIMP LockServer(BOOL lock) override
    {
        if (lock)
        {
            ++objectCount;
        }
        else
        {
            --objectCount;
        }
        return S_OK;
    }

private:
    std::atomic_ulong referenceCount_{1};
    CommandKind kind_;
};
}

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        moduleInstance = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID classId, REFIID interfaceId, void** object)
{
    CommandKind kind{};
    if (IsEqualCLSID(classId, AnalyzeCommandClsid))
    {
        kind = CommandKind::Analyze;
    }
    else if (IsEqualCLSID(classId, ConvertCommandClsid))
    {
        kind = CommandKind::Convert;
    }
    else if (IsEqualCLSID(classId, OptimizeCommandClsid))
    {
        kind = CommandKind::Optimize;
    }
    else
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) CommandFactory(kind);
    if (factory == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    const HRESULT result = factory->QueryInterface(interfaceId, object);
    factory->Release();
    return result;
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return objectCount.load() == 0 ? S_OK : S_FALSE;
}
