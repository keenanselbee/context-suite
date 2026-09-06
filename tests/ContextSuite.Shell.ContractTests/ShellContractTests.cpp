#include <windows.h>
#include <shlobj.h>
#include <shobjidl_core.h>

#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <string_view>
#include <vector>

namespace
{
constexpr CLSID AnalyzeCommandClsid =
    {0x7f71bfc7, 0x0125, 0x47b6, {0xa1, 0x63, 0x54, 0xea, 0xdd, 0xb5, 0x6e, 0x8b}};
constexpr CLSID ConvertCommandClsid =
    {0xc0a78640, 0x7313, 0x4ad7, {0x87, 0x0c, 0x2f, 0x0b, 0x83, 0x8a, 0x28, 0xc2}};
constexpr CLSID OptimizeCommandClsid =
    {0x8c4ca2a6, 0x99a2, 0x436a, {0x90, 0x21, 0x3d, 0x00, 0x46, 0x87, 0xfc, 0x4c}};

using DllGetClassObjectFunction = HRESULT(__stdcall*)(REFCLSID, REFIID, void**);

struct CommandExpectation
{
    CLSID classId;
    const wchar_t* title;
    const wchar_t* actionTitle;
    const char* operation;
    const char* action;
    bool hasSubcommands;
};

class ComScope
{
public:
    ComScope() : result_(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED)) {}
    ~ComScope()
    {
        if (SUCCEEDED(result_))
        {
            CoUninitialize();
        }
    }
    HRESULT Result() const { return result_; }

private:
    HRESULT result_;
};

bool CreateSelection(int pathCount, wchar_t** paths, IShellItemArray** selection)
{
    std::vector<PIDLIST_ABSOLUTE> itemIds;
    itemIds.reserve(static_cast<size_t>(pathCount));
    for (int index = 0; index < pathCount; ++index)
    {
        PIDLIST_ABSOLUTE itemId = nullptr;
        if (FAILED(SHParseDisplayName(paths[index], nullptr, &itemId, 0, nullptr)))
        {
            for (const auto value : itemIds)
            {
                CoTaskMemFree(value);
            }
            return false;
        }
        itemIds.push_back(itemId);
    }

    const HRESULT result = SHCreateShellItemArrayFromIDLists(
        static_cast<UINT>(itemIds.size()),
        const_cast<PCIDLIST_ABSOLUTE*>(itemIds.data()),
        selection);
    for (const auto value : itemIds)
    {
        CoTaskMemFree(value);
    }
    return SUCCEEDED(result);
}

bool ReadResult(
    const std::filesystem::path& path,
    std::string_view operation,
    std::string_view action,
    size_t pathCount)
{
    std::ifstream input(path, std::ios::binary);
    if (!input)
    {
        return false;
    }
    const std::string content((std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
    return content.find("VALID\n") == 0 &&
        content.find("operation=" + std::string(operation) + "\n") != std::string::npos &&
        content.find("action=" + std::string(action) + "\n") != std::string::npos &&
        content.find("pathCount=" + std::to_string(pathCount) + "\n") != std::string::npos;
}

bool TestCommand(
    DllGetClassObjectFunction getClassObject,
    const CommandExpectation& expectation,
    IShellItemArray* selection,
    HANDLE completionEvent,
    const std::filesystem::path& resultPath,
    size_t pathCount)
{
    IClassFactory* factory = nullptr;
    HRESULT result = getClassObject(expectation.classId, IID_PPV_ARGS(&factory));
    if (FAILED(result))
    {
        std::wcerr << L"DllGetClassObject failed for " << expectation.title << L".\n";
        return false;
    }

    IExplorerCommand* command = nullptr;
    result = factory->CreateInstance(nullptr, IID_PPV_ARGS(&command));
    factory->Release();
    if (FAILED(result))
    {
        std::wcerr << L"CreateInstance failed for " << expectation.title << L".\n";
        return false;
    }

    PWSTR actualTitle = nullptr;
    result = command->GetTitle(selection, &actualTitle);
    const bool titleMatches = SUCCEEDED(result) && actualTitle != nullptr &&
        std::wstring_view(actualTitle) == expectation.title;
    CoTaskMemFree(actualTitle);
    if (!titleMatches)
    {
        std::wcerr << L"Unexpected title for " << expectation.title << L".\n";
        command->Release();
        return false;
    }

    PWSTR iconPath = nullptr;
    result = command->GetIcon(selection, &iconPath);
    const bool iconExists = SUCCEEDED(result) && iconPath != nullptr &&
        GetFileAttributesW(iconPath) != INVALID_FILE_ATTRIBUTES;
    CoTaskMemFree(iconPath);
    if (!iconExists)
    {
        std::wcerr << L"Command icon failed for " << expectation.title << L".\n";
        command->Release();
        return false;
    }

    GUID canonicalName{};
    EXPCMDSTATE state = ECS_HIDDEN;
    EXPCMDFLAGS flags = ECF_DEFAULT;
    const EXPCMDFLAGS expectedFlags = expectation.hasSubcommands ? ECF_HASSUBCOMMANDS : ECF_DEFAULT;
    if (FAILED(command->GetCanonicalName(&canonicalName)) ||
        !IsEqualGUID(canonicalName, expectation.classId) ||
        FAILED(command->GetState(selection, FALSE, &state)) || state != ECS_ENABLED ||
        FAILED(command->GetFlags(&flags)) || flags != expectedFlags)
    {
        std::wcerr << L"Command metadata failed for " << expectation.title << L".\n";
        command->Release();
        return false;
    }

    IEnumExplorerCommand* enumerator = nullptr;
    IExplorerCommand* actionCommand = nullptr;
    if (expectation.hasSubcommands)
    {
        ULONG fetched = 0;
        PWSTR actualActionTitle = nullptr;
        const bool subcommandIsValid = SUCCEEDED(command->EnumSubCommands(&enumerator)) &&
            enumerator != nullptr &&
            enumerator->Next(1, &actionCommand, &fetched) == S_OK && fetched == 1 && actionCommand != nullptr &&
            SUCCEEDED(actionCommand->GetTitle(selection, &actualActionTitle)) && actualActionTitle != nullptr &&
            std::wstring_view(actualActionTitle) == expectation.actionTitle &&
            SUCCEEDED(actionCommand->GetFlags(&flags)) && flags == ECF_DEFAULT;
        CoTaskMemFree(actualActionTitle);
        if (enumerator != nullptr)
        {
            enumerator->Release();
        }
        if (!subcommandIsValid)
        {
            if (actionCommand != nullptr)
            {
                actionCommand->Release();
            }
            std::wcerr << L"Subcommand metadata failed for " << expectation.title << L".\n";
            command->Release();
            return false;
        }
    }
    else
    {
        actionCommand = command;
        command = nullptr;
    }
    if (command != nullptr)
    {
        command->Release();
    }

    DeleteFileW(resultPath.c_str());
    SetEnvironmentVariableW(L"CONTEXT_SUITE_PROTOTYPE_TEST_RESULT", resultPath.c_str());
    ResetEvent(completionEvent);
    result = actionCommand->Invoke(selection, nullptr);
    actionCommand->Release();
    if (FAILED(result))
    {
        std::wcerr << L"Invoke failed for " << expectation.title << L".\n";
        return false;
    }

    if (WaitForSingleObject(completionEvent, 10000) != WAIT_OBJECT_0 ||
        !ReadResult(resultPath, expectation.operation, expectation.action, pathCount))
    {
        std::wcerr << L"The host did not acknowledge the complete " << expectation.title << L" batch.\n";
        return false;
    }

    return true;
}
}

int wmain(int argumentCount, wchar_t** arguments)
{
    if (argumentCount < 5)
    {
        std::wcerr << L"Usage: ContextSuite.Shell.ContractTests <shell-dll> <result-dir> <file> <file> [...]\n";
        return 2;
    }

    ComScope com;
    if (FAILED(com.Result()))
    {
        std::wcerr << L"COM initialization failed.\n";
        return 1;
    }

    HMODULE module = LoadLibraryW(arguments[1]);
    if (module == nullptr)
    {
        std::wcerr << L"The shell DLL could not be loaded.\n";
        return 1;
    }

    const auto getClassObject = reinterpret_cast<DllGetClassObjectFunction>(
        GetProcAddress(module, "DllGetClassObject"));
    if (getClassObject == nullptr)
    {
        std::wcerr << L"DllGetClassObject is not exported.\n";
        FreeLibrary(module);
        return 1;
    }

    IShellItemArray* selection = nullptr;
    if (!CreateSelection(argumentCount - 3, arguments + 3, &selection))
    {
        std::wcerr << L"The shell item selection could not be created.\n";
        FreeLibrary(module);
        return 1;
    }

    const std::wstring eventName = L"Local\\ContextSuiteShellPrototypeTest-" +
        std::to_wstring(GetCurrentProcessId());
    HANDLE completionEvent = CreateEventW(nullptr, TRUE, FALSE, eventName.c_str());
    if (completionEvent == nullptr)
    {
        std::wcerr << L"The host completion event could not be created.\n";
        selection->Release();
        FreeLibrary(module);
        return 1;
    }
    SetEnvironmentVariableW(L"CONTEXT_SUITE_PROTOTYPE_TEST_EVENT", eventName.c_str());

    const std::filesystem::path resultDirectory(arguments[2]);
    const CommandExpectation expectations[] = {
        {AnalyzeCommandClsid, L"Analyze", L"Open details...", "analyze", "open-details", false},
        {ConvertCommandClsid, L"Convert", L"Choose format...", "convert", "choose-format", true},
        {OptimizeCommandClsid, L"Optimize", L"Choose preset...", "optimize", "choose-preset", true},
    };

    bool succeeded = true;
    for (const auto& expectation : expectations)
    {
        const auto resultPath = resultDirectory / (std::string(expectation.operation) + ".shell-result");
        if (!TestCommand(
                getClassObject,
                expectation,
                selection,
                completionEvent,
                resultPath,
                static_cast<size_t>(argumentCount - 3)))
        {
            succeeded = false;
            break;
        }
    }

    SetEnvironmentVariableW(L"CONTEXT_SUITE_PROTOTYPE_TEST_RESULT", nullptr);
    SetEnvironmentVariableW(L"CONTEXT_SUITE_PROTOTYPE_TEST_EVENT", nullptr);
    CloseHandle(completionEvent);
    selection->Release();
    FreeLibrary(module);

    if (!succeeded)
    {
        return 1;
    }

    std::wcout << L"All shell COM commands activated one host batch with the complete selection.\n";
    return 0;
}
