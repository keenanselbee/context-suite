#include <windows.h>
#include <shlobj.h>
#include <shobjidl_core.h>

#include <array>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <string_view>
#include <vector>
#include <wrl/client.h>

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

bool HasIcon(IExplorerCommand* command, const std::filesystem::path& expected)
{
    PWSTR icon = nullptr;
    const HRESULT result = command->GetIcon(nullptr, &icon);
    const bool valid = SUCCEEDED(result) && icon != nullptr &&
        std::filesystem::path(icon) == expected &&
        GetFileAttributesW(icon) != INVALID_FILE_ATTRIBUTES;
    if (!valid)
    {
        std::wcerr << L"Expected icon " << expected << L", got " <<
            (icon == nullptr ? L"(none)" : icon) << L".\n";
    }
    CoTaskMemFree(icon);
    return valid;
}

bool TestSettingsChildren(IEnumExplorerCommand* enumerator, IExplorerCommand* action,
    const CommandExpectation& expectation, HANDLE completionEvent, const std::filesystem::path& resultPath,
    const std::filesystem::path& iconDirectory, std::wstring_view iconSuffix,
    IShellItemArray* selection, size_t pathCount)
{
    constexpr std::array<const wchar_t*, 18> convertIcons{
        L"ConvertPng", L"ConvertJpeg", L"ConvertWebp", L"ConvertBmp", L"ConvertTga", L"ConvertDds",
        L"ConvertWav", L"ConvertFlac", L"ConvertMp3", L"ConvertM4a", L"ConvertOgg", L"ConvertOpus",
        L"ConvertPdf", L"ConvertDocx", L"ConvertWem", L"ConvertIco", L"ConvertAvif", L"ConvertPdf" };
    if (expectation.hasSubcommands)
    {
        const bool optimize = std::string_view(expectation.operation) == "optimize";
        const std::vector<const wchar_t*> titles = optimize ? std::vector<const wchar_t*>{ L"Lossless", L"Balanced", L"Smallest" } :
            std::vector<const wchar_t*>{ L"JPEG", L"WebP (lossless)", L"BMP", L"TGA", L"DDS...", L"WAV", L"FLAC", L"MP3", L"M4A (AAC)", L"Ogg Vorbis", L"Opus", L"PDF", L"Word (DOCX)", L"WEM...", L"ICO", L"AVIF", L"PDF (Dark mode)" };
        const std::vector<const char*> actions = optimize ? std::vector<const char*>{ "lossless", "balanced", "smallest" } :
            std::vector<const char*>{ "jpeg", "webp", "bmp", "tga", "dds", "wav", "flac", "mp3", "m4a", "vorbis", "opus", "pdf", "docx", "wem", "ico", "avif", "pdf-dark" };
        size_t index = 0;
        GUID previous{};
        action->GetCanonicalName(&previous);
        for (const auto titleExpected : titles)
        {
            IExplorerCommand* preset = nullptr;
            ULONG read = 0;
            if (enumerator->Next(1, &preset, &read) != S_OK || read != 1 || preset == nullptr) return false;
            PWSTR title = nullptr;
            GUID id{};
            bool valid = SUCCEEDED(preset->GetTitle(nullptr, &title)) && title != nullptr &&
                std::wstring_view(title) == titleExpected && SUCCEEDED(preset->GetCanonicalName(&id)) && !IsEqualGUID(previous, id);
            if (!optimize)
            {
                const std::filesystem::path expectedIcon = iconDirectory /
                    (std::wstring(convertIcons[index + 1]) + std::wstring(iconSuffix) + L".ico");
                valid = valid && HasIcon(preset, expectedIcon);
            }
            CoTaskMemFree(title);
            DeleteFileW(resultPath.c_str());
            SetEnvironmentVariableW(L"CONTEXT_SUITE_PROTOTYPE_TEST_RESULT", resultPath.c_str());
            ResetEvent(completionEvent);
            valid = valid && SUCCEEDED(preset->Invoke(selection, nullptr)) &&
                WaitForSingleObject(completionEvent, 10000) == WAIT_OBJECT_0 && ReadResult(resultPath, expectation.operation, actions[index++], pathCount);
            preset->Release();
            if (!valid) return false;
            previous = id;
        }
    }
    IExplorerCommand* children[2]{};
    ULONG fetched = 0;
    if (enumerator->Next(2, children, &fetched) != S_OK || fetched != 2)
    {
        for (auto* child : children) if (child != nullptr) child->Release();
        return false;
    }
    EXPCMDFLAGS flags{};
    GUID actionId{}, separatorId{}, settingsId{};
    PWSTR title = nullptr;
    EXPCMDSTATE state = ECS_HIDDEN;
    bool valid = SUCCEEDED(children[0]->GetFlags(&flags)) && flags == ECF_ISSEPARATOR &&
        children[0]->Invoke(nullptr, nullptr) == E_NOTIMPL &&
        SUCCEEDED(children[1]->GetFlags(&flags)) && flags == ECF_DEFAULT &&
        SUCCEEDED(children[1]->GetState(nullptr, FALSE, &state)) && state == ECS_ENABLED &&
        SUCCEEDED(children[1]->GetTitle(nullptr, &title)) && title != nullptr && std::wstring_view(title) == L"Settings..." &&
        SUCCEEDED(action->GetCanonicalName(&actionId)) && SUCCEEDED(children[0]->GetCanonicalName(&separatorId)) &&
        SUCCEEDED(children[1]->GetCanonicalName(&settingsId)) && !IsEqualGUID(actionId, settingsId) &&
        !IsEqualGUID(separatorId, settingsId) && !IsEqualGUID(actionId, separatorId);
    CoTaskMemFree(title);
    PWSTR separatorIcon = nullptr;
    valid = valid && children[0]->GetIcon(nullptr, &separatorIcon) == E_NOTIMPL && separatorIcon == nullptr &&
        HasIcon(children[1], iconDirectory / (L"Settings" + std::wstring(iconSuffix) + L".ico"));
    CoTaskMemFree(separatorIcon);
    IExplorerCommand* extra = nullptr;
    valid = valid && enumerator->Next(1, &extra, &fetched) == S_FALSE && fetched == 0 && extra == nullptr;
    if (extra != nullptr) extra->Release();

    IEnumExplorerCommand* clone = nullptr;
    valid = valid && enumerator->Reset() == S_OK && enumerator->Skip(std::string_view(expectation.operation) == "optimize" ? 4 : 18) == S_OK &&
        SUCCEEDED(enumerator->Clone(&clone)) && clone != nullptr;
    if (clone != nullptr)
    {
        valid = valid && clone->Skip(2) == S_OK && clone->Skip(1) == S_FALSE;
        clone->Release();
    }
    if (valid)
    {
        DeleteFileW(resultPath.c_str());
        SetEnvironmentVariableW(L"CONTEXT_SUITE_PROTOTYPE_TEST_RESULT", resultPath.c_str());
        ResetEvent(completionEvent);
        valid = SUCCEEDED(children[1]->Invoke(nullptr, nullptr)) &&
            WaitForSingleObject(completionEvent, 10000) == WAIT_OBJECT_0 &&
            ReadResult(resultPath, expectation.operation, "settings", 0);
    }
    for (auto* child : children) child->Release();
    return valid;
}

bool TestConversionAvailability(DllGetClassObjectFunction getClassObject, const std::filesystem::path& root)
{
    using Microsoft::WRL::ComPtr;
    ComPtr<IClassFactory> factory;
    ComPtr<IExplorerCommand> command;
    if (FAILED(getClassObject(ConvertCommandClsid, IID_PPV_ARGS(&factory))) ||
        FAILED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&command)))) return false;
    struct Case { std::vector<std::wstring> names; unsigned mask; bool directory = false; };
    const Case cases[] = {
        {{L"page.PDF"}, 3u | (1u << 13) | (1u << 17)}, {{L"document.docx"}, 1u << 12},
        {{L"image.png"}, (1u << 16) | (1u << 15) | (1u << 12) | 62u},
        {{L"image.JPEG"}, (1u << 16) | (1u << 15) | (1u << 12) | (63u & ~(1u << 1))},
        {{L"image.jfif"}, (1u << 16) | (1u << 15) | (1u << 12) | (63u & ~(1u << 1))},
        {{L"image.JfIf", L"unknown.xyz"}, (1u << 16) | (1u << 15) | (1u << 12) | (63u & ~(1u << 1))},
        {{L"image.webp"}, (1u << 16) | (1u << 15) | (1u << 12) | (63u & ~(1u << 2))},
        {{L"image.bmp"}, (1u << 16) | (1u << 15) | (1u << 12) | (63u & ~(1u << 3))},
        {{L"image.tga"}, (1u << 16) | (1u << 15) | (1u << 12) | (63u & ~(1u << 4))},
        {{L"page.PDF", L"image.png"}, (1u << 17) | (1u << 16) | (1u << 15) | (1u << 13) | (1u << 12) | 63u},
        {{L"unknown.xyz"}, 0u}, {{L"index.html"}, 0u}, {{L"page.HTM", L"unknown.xyz"}, 0u},
        {{L"no-extension"}, 0u}, {{L"folder.png"}, 0u, true},
        {{L"index.html", L"image.png"}, (1u << 16) | (1u << 15) | (1u << 12) | 62u},
        {{L"song.MP3"}, ((63u << 6) & ~(1u << 8)) | (1u << 14)},
        {{L"song.Mp3", L"unknown.xyz"}, ((63u << 6) & ~(1u << 8)) | (1u << 14)},
        {{L"image.avif"}, 3u}, {{L"image.heic"}, 3u}, {{L"texture.dds"}, 33u},
        {{L"image.tiff"}, 3u | (1u << 12)}, {{L"image.TIF"}, 3u | (1u << 12)}, {{L"image.svg"}, 3u},
        {{L"image.SVG"}, 3u}, {{L"image.svg", L"unknown.xyz"}, 3u},
        {{L"image.svg", L"image.JPEG"}, (1u << 16) | (1u << 15) | (1u << 12) | 63u},
        {{L"image.ico"}, 1u}, {{L"image.ICO", L"unknown.xyz"}, 1u},
        {{L"image.gif"}, 1u}, {{L"image.GIF", L"unknown.xyz"}, 1u},
        {{L"clip.mp4"}, 7u << 6}, {{L"clip.MOV"}, 7u << 6}, {{L"clip.WebM", L"unknown.xyz"}, 7u << 6},
        {{L"clip.MKV", L"unknown.xyz"}, 7u << 6}, {{L"clip.m4v"}, 0u},
        {{L"image.tiff", L"image.png"}, (1u << 16) | (1u << 15) | (1u << 12) | 63u},
        {{L"voice.WeM"}, 1u << 6}, {{L"voice.WeM", L"unknown.xyz"}, 1u << 6},
        {{L"voice.WeM", L"image.png"}, (1u << 16) | (1u << 15) | (1u << 6) | (1u << 12) | 62u},
        {{L"voice.WaV"}, ((63u << 6) & ~(1u << 6)) | (1u << 14)},
        {{L"voice.wave", L"unknown.xyz"}, ((63u << 6) & ~(1u << 6)) | (1u << 14)},
        {{L"voice.WaV", L"song.Mp3"}, (63u << 6) | (1u << 14)},
        {{L"voice.wav", L"voice.wem"}, (63u << 6) | (1u << 14)}
    };
    for (const auto& test : cases)
    {
        std::vector<std::wstring> paths;
        for (const auto& name : test.names)
        {
            paths.push_back((root / name).wstring());
            if (test.directory) std::filesystem::create_directory(root / name);
            else { std::ofstream file(root / name); file << "Not media: menu checks must not parse it."; }
        }
        std::vector<wchar_t*> pointers;
        for (auto& path : paths) pointers.push_back(path.data());
        ComPtr<IShellItemArray> selection;
        if (!CreateSelection(static_cast<int>(pointers.size()), pointers.data(), &selection)) return false;
        for (BOOL slow : {FALSE, TRUE})
        {
            EXPCMDSTATE rootState = ECS_ENABLED;
            if (FAILED(command->GetState(selection.Get(), slow, &rootState)) ||
                rootState != (test.mask ? ECS_ENABLED : ECS_HIDDEN))
            {
                std::wcerr << L"Conversion root availability failed for " << test.names[0] << L".\n";
                return false;
            }
        }
        ComPtr<IEnumExplorerCommand> enumerator;
        if (FAILED(command->EnumSubCommands(&enumerator))) return false;
        for (unsigned target = 0; target < 18; ++target)
        {
            ComPtr<IExplorerCommand> child;
            ULONG fetched = 0;
            EXPCMDSTATE state = ECS_ENABLED;
            if (enumerator->Next(1, &child, &fetched) != S_OK || fetched != 1 ||
                FAILED(child->GetState(selection.Get(), FALSE, &state)) ||
                state != static_cast<EXPCMDSTATE>((test.mask & (1u << target)) ? ECS_ENABLED : ECS_HIDDEN))
            {
                std::wcerr << L"Conversion availability failed for " << test.names[0] << L" target " << target << L".\n";
                return false;
            }
        }
    }
    std::wcout << std::size(cases) * 20 << L" conversion root/child availability checks passed without reading media.\n";
    return true;
}

bool TestOptimizationAvailability(DllGetClassObjectFunction getClassObject, const std::filesystem::path& root)
{
    using Microsoft::WRL::ComPtr;
    ComPtr<IClassFactory> factory;
    ComPtr<IExplorerCommand> command;
    if (FAILED(getClassObject(OptimizeCommandClsid, IID_PPV_ARGS(&factory))) ||
        FAILED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&command)))) return false;
    struct Case { std::vector<std::wstring> names; unsigned mask; bool directory = false; };
    const Case cases[] = {
        {{L"index.html"}, 0}, {{L"page.HTM", L"unknown.xyz"}, 0}, {{L"no-extension"}, 0},
        {{L"image.JPEG"}, 0}, {{L"song.mp3"}, 0}, {{L"image.webp"}, 0},
        {{L"image.PnG"}, 15}, {{L"song.FlAc"}, 3}, {{L"page.PDF"}, 3},
        {{L"index.html", L"image.PnG"}, 15}, {{L"index.html", L"song.FlAc"}, 3},
        {{L"page.PDF", L"song.FlAc"}, 3}, {{L"page.PDF", L"image.PnG"}, 15},
        {{L"optimize-folder.png"}, 0, true}
    };
    EXPCMDSTATE emptyState = ECS_ENABLED;
    if (FAILED(command->GetState(nullptr, FALSE, &emptyState)) || emptyState != ECS_HIDDEN) return false;
    for (const auto& test : cases)
    {
        std::vector<std::wstring> paths;
        for (const auto& name : test.names)
        {
            paths.push_back((root / name).wstring());
            if (test.directory) std::filesystem::create_directory(root / name);
            else { std::ofstream file(root / name); file << "Not media: filename hints only."; }
        }
        std::vector<wchar_t*> pointers;
        for (auto& path : paths) pointers.push_back(path.data());
        ComPtr<IShellItemArray> selection;
        if (!CreateSelection(static_cast<int>(pointers.size()), pointers.data(), &selection)) return false;
        for (BOOL slow : {FALSE, TRUE})
        {
            EXPCMDSTATE state = ECS_ENABLED;
            if (FAILED(command->GetState(selection.Get(), slow, &state)) ||
                state != (test.mask ? ECS_ENABLED : ECS_HIDDEN)) return false;
        }
        ComPtr<IEnumExplorerCommand> enumerator;
        if (FAILED(command->EnumSubCommands(&enumerator))) return false;
        for (unsigned preset = 0; preset < 4; ++preset)
        {
            ComPtr<IExplorerCommand> child;
            ULONG fetched = 0;
            EXPCMDSTATE state = ECS_ENABLED;
            if (enumerator->Next(1, &child, &fetched) != S_OK || fetched != 1 ||
                FAILED(child->GetState(selection.Get(), FALSE, &state)) ||
                state != ((test.mask & (1u << preset)) ? ECS_ENABLED : ECS_HIDDEN))
            {
                std::wcerr << L"Optimization availability failed for " << test.names[0] << L" preset " << preset << L".\n";
                return false;
            }
        }
    }
    // Analyze remains useful for unsupported conversion/optimization formats.
    ComPtr<IClassFactory> analyzeFactory;
    ComPtr<IExplorerCommand> analyze;
    std::wstring htmlPath = (root / L"index.html").wstring();
    wchar_t* htmlPointer = htmlPath.data();
    ComPtr<IShellItemArray> htmlSelection;
    EXPCMDSTATE analyzeState = ECS_HIDDEN;
    if (FAILED(getClassObject(AnalyzeCommandClsid, IID_PPV_ARGS(&analyzeFactory))) ||
        FAILED(analyzeFactory->CreateInstance(nullptr, IID_PPV_ARGS(&analyze))) ||
        !CreateSelection(1, &htmlPointer, &htmlSelection) ||
        FAILED(analyze->GetState(htmlSelection.Get(), FALSE, &analyzeState)) || analyzeState != ECS_ENABLED) return false;
    std::wcout << std::size(cases) * 6 + 2 << L" optimization root/child and Analyze availability checks passed.\n";
    return true;
}

bool TestCommand(
    DllGetClassObjectFunction getClassObject,
    const CommandExpectation& expectation,
    IShellItemArray* selection,
    HANDLE completionEvent,
    const std::filesystem::path& resultPath,
    const std::filesystem::path& iconDirectory,
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
    const std::wstring_view rootIconName = std::string_view(expectation.operation) == "analyze" ? L"Analyze" :
        std::string_view(expectation.operation) == "convert" ? L"Convert" : L"Optimize";
    const std::filesystem::path lightIcon = iconDirectory / (std::wstring(rootIconName) + L".ico");
    const std::filesystem::path darkIcon = iconDirectory / (std::wstring(rootIconName) + L"Dark.ico");
    const bool iconExists = SUCCEEDED(result) && iconPath != nullptr &&
        (std::filesystem::path(iconPath) == lightIcon || std::filesystem::path(iconPath) == darkIcon) &&
        GetFileAttributesW(iconPath) != INVALID_FILE_ATTRIBUTES;
    const std::wstring iconSuffix = iconExists && std::filesystem::path(iconPath) == darkIcon ? L"Dark" : L"";
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
        bool subcommandIsValid = SUCCEEDED(command->EnumSubCommands(&enumerator)) &&
            enumerator != nullptr &&
            enumerator->Next(1, &actionCommand, &fetched) == S_OK && fetched == 1 && actionCommand != nullptr &&
            SUCCEEDED(actionCommand->GetTitle(selection, &actualActionTitle)) && actualActionTitle != nullptr &&
            std::wstring_view(actualActionTitle) == expectation.actionTitle &&
            SUCCEEDED(actionCommand->GetFlags(&flags)) && flags == ECF_DEFAULT;
        CoTaskMemFree(actualActionTitle);
        if (subcommandIsValid)
        {
            if (std::string_view(expectation.operation) == "convert")
                subcommandIsValid = HasIcon(actionCommand,
                    iconDirectory / (L"ConvertPng" + iconSuffix + L".ico"));
            if (subcommandIsValid)
                subcommandIsValid = TestSettingsChildren(enumerator, actionCommand, expectation,
                    completionEvent, resultPath, iconDirectory, iconSuffix, selection, pathCount);
        }
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

bool TestMissingTargetIconFallback(const std::filesystem::path& shellPath,
    const std::filesystem::path& resultDirectory)
{
    using Microsoft::WRL::ComPtr;
    const auto isolatedDirectory = resultDirectory / L"missing-target-icons";
    const auto isolatedAssets = isolatedDirectory / L"Assets";
    std::error_code error;
    std::filesystem::create_directories(isolatedAssets, error);
    if (error) return false;

    const auto sourceAssets = shellPath.parent_path() / L"Assets";
    std::filesystem::copy_file(shellPath, isolatedDirectory / shellPath.filename(),
        std::filesystem::copy_options::overwrite_existing, error);
    for (const wchar_t* name : { L"Convert.ico", L"ConvertDark.ico" })
    {
        if (error) break;
        std::filesystem::copy_file(sourceAssets / name, isolatedAssets / name,
            std::filesystem::copy_options::overwrite_existing, error);
    }

    bool valid = !error;
    HMODULE isolatedModule = valid ? LoadLibraryW((isolatedDirectory / shellPath.filename()).c_str()) : nullptr;
    valid = valid && isolatedModule != nullptr;
    if (valid)
    {
        const auto isolatedGetClassObject = reinterpret_cast<DllGetClassObjectFunction>(
            GetProcAddress(isolatedModule, "DllGetClassObject"));
        ComPtr<IClassFactory> factory;
        ComPtr<IExplorerCommand> command;
        ComPtr<IEnumExplorerCommand> enumerator;
        valid = isolatedGetClassObject != nullptr &&
            SUCCEEDED(isolatedGetClassObject(ConvertCommandClsid, IID_PPV_ARGS(&factory))) &&
            SUCCEEDED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&command))) &&
            SUCCEEDED(command->EnumSubCommands(&enumerator));
        if (valid)
        {
            PWSTR rootIcon = nullptr;
            const HRESULT rootResult = command->GetIcon(nullptr, &rootIcon);
            const auto expectedRoot = rootIcon == nullptr ? std::filesystem::path{} : std::filesystem::path(rootIcon);
            valid = SUCCEEDED(rootResult) && rootIcon != nullptr &&
                expectedRoot.parent_path() == isolatedAssets &&
                (expectedRoot.filename() == L"Convert.ico" || expectedRoot.filename() == L"ConvertDark.ico");
            CoTaskMemFree(rootIcon);
            ComPtr<IExplorerCommand> png;
            ComPtr<IExplorerCommand> ico;
            ComPtr<IExplorerCommand> avif;
            ComPtr<IExplorerCommand> pdfDark;
            ComPtr<IExplorerCommand> settings;
            ULONG fetched = 0;
            valid = valid && enumerator->Next(1, &png, &fetched) == S_OK && fetched == 1 &&
                enumerator->Skip(14) == S_OK &&
                enumerator->Next(1, &ico, &fetched) == S_OK && fetched == 1 &&
                enumerator->Next(1, &avif, &fetched) == S_OK && fetched == 1 &&
                enumerator->Next(1, &pdfDark, &fetched) == S_OK && fetched == 1 &&
                enumerator->Skip(1) == S_OK &&
                enumerator->Next(1, &settings, &fetched) == S_OK && fetched == 1 &&
                HasIcon(png.Get(), expectedRoot) && HasIcon(ico.Get(), expectedRoot) &&
                HasIcon(avif.Get(), expectedRoot) && HasIcon(pdfDark.Get(), expectedRoot) && HasIcon(settings.Get(), expectedRoot);
        }
    }
    if (isolatedModule != nullptr) FreeLibrary(isolatedModule);
    std::filesystem::remove_all(isolatedDirectory, error);
    if (!valid) std::wcerr << L"Missing target icon fallback failed.\n";
    return valid && !error;
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
        {ConvertCommandClsid, L"Convert", L"PNG", "convert", "png", true},
        {OptimizeCommandClsid, L"Optimize", L"Auto", "optimize", "auto", true},
    };

    const std::filesystem::path shellPath(arguments[1]);
    const auto iconDirectory = shellPath.parent_path() / L"Assets";
    bool succeeded = TestConversionAvailability(getClassObject, resultDirectory) &&
        TestOptimizationAvailability(getClassObject, resultDirectory);
    for (const auto& expectation : expectations)
    {
        const auto resultPath = resultDirectory / (std::string(expectation.operation) + ".shell-result");
        if (!TestCommand(
                getClassObject,
                expectation,
                selection,
                completionEvent,
                resultPath,
                iconDirectory,
                static_cast<size_t>(argumentCount - 3)))
        {
            succeeded = false;
            break;
        }
    }
    if (succeeded)
    {
        succeeded = TestMissingTargetIconFallback(shellPath, resultDirectory);
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
