#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>

#include <algorithm>
#include <charconv>
#include <filesystem>
#include <string>
#include <string_view>
#include <vector>

namespace
{
constexpr size_t MaximumRequestBytes = 4 * 1024 * 1024;
constexpr size_t MaximumSelectionCount = 4096;

struct Arguments
{
    std::filesystem::path activationFile;
    std::filesystem::path resultFile;
    bool validateOnly = false;
};

struct ActivationRequest
{
    std::wstring requestId;
    std::wstring operation;
    std::wstring action;
    std::vector<std::filesystem::path> paths;
};

std::wstring FromUtf8(std::string_view value)
{
    if (value.empty())
    {
        return {};
    }

    const int characterCount = MultiByteToWideChar(
        CP_UTF8,
        MB_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        nullptr,
        0);
    if (characterCount <= 0)
    {
        return {};
    }

    std::wstring result(static_cast<size_t>(characterCount), L'\0');
    if (MultiByteToWideChar(
            CP_UTF8,
            MB_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            characterCount) <= 0)
    {
        return {};
    }

    return result;
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

bool ParseArguments(Arguments& arguments, std::wstring& error)
{
    int count = 0;
    PWSTR* values = CommandLineToArgvW(GetCommandLineW(), &count);
    if (values == nullptr)
    {
        error = L"The command line could not be read.";
        return false;
    }

    for (int index = 1; index < count; ++index)
    {
        const std::wstring_view argument(values[index]);
        if (argument == L"--activation-file" && index + 1 < count)
        {
            arguments.activationFile = values[++index];
        }
        else if (argument == L"--result-file" && index + 1 < count)
        {
            arguments.resultFile = values[++index];
        }
        else if (argument == L"--validate-only")
        {
            arguments.validateOnly = true;
        }
        else
        {
            error = L"The activation arguments are not recognized.";
            LocalFree(values);
            return false;
        }
    }

    LocalFree(values);
    if (arguments.activationFile.empty())
    {
        error = L"No activation request was supplied.";
        return false;
    }
    if (!arguments.resultFile.empty() && !arguments.validateOnly)
    {
        error = L"A result file is available only in validation mode.";
        return false;
    }

    return true;
}

bool ReadRequestFile(const std::filesystem::path& path, std::string& content, std::wstring& error)
{
    HANDLE file = CreateFileW(
        path.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_DELETE,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        error = L"The activation request could not be opened.";
        return false;
    }

    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file, &size) || size.QuadPart <= 0 || size.QuadPart > MaximumRequestBytes)
    {
        CloseHandle(file);
        error = L"The activation request has an invalid size.";
        return false;
    }

    content.resize(static_cast<size_t>(size.QuadPart));
    size_t offset = 0;
    while (offset < content.size())
    {
        const DWORD requested = static_cast<DWORD>(content.size() - offset);
        DWORD bytesRead = 0;
        if (!ReadFile(file, content.data() + offset, requested, &bytesRead, nullptr) || bytesRead == 0)
        {
            CloseHandle(file);
            error = L"The activation request could not be read completely.";
            return false;
        }
        offset += bytesRead;
    }

    CloseHandle(file);
    return true;
}

std::vector<std::string_view> SplitLines(const std::string& content)
{
    std::vector<std::string_view> lines;
    size_t start = 0;
    while (start < content.size())
    {
        const size_t end = content.find('\n', start);
        const size_t length = end == std::string::npos ? content.size() - start : end - start;
        std::string_view line(content.data() + start, length);
        if (!line.empty() && line.back() == '\r')
        {
            line.remove_suffix(1);
        }
        lines.push_back(line);
        if (end == std::string::npos)
        {
            break;
        }
        start = end + 1;
    }
    return lines;
}

bool ReadField(std::string_view line, std::string_view key, std::wstring& value)
{
    if (!line.starts_with(key))
    {
        return false;
    }
    value = FromUtf8(line.substr(key.size()));
    return !value.empty();
}

bool ParseRequest(const std::string& content, ActivationRequest& request, std::wstring& error)
{
    const auto lines = SplitLines(content);
    if (lines.size() < 5 || lines[0] != "ContextSuiteActivation/1")
    {
        error = L"The activation request schema is unknown.";
        return false;
    }

    if (!ReadField(lines[1], "requestId=", request.requestId) ||
        !ReadField(lines[2], "operation=", request.operation) ||
        !ReadField(lines[3], "action=", request.action))
    {
        error = L"The activation request is missing a required field.";
        return false;
    }

    GUID parsedRequestId{};
    const std::wstring bracedRequestId = L"{" + request.requestId + L"}";
    if (FAILED(CLSIDFromString(bracedRequestId.c_str(), &parsedRequestId)))
    {
        error = L"The activation request identifier is invalid.";
        return false;
    }

    if (request.operation != L"analyze" && request.operation != L"convert" &&
        request.operation != L"optimize")
    {
        error = L"The requested operation is not supported.";
        return false;
    }
    const bool isSettings = request.action == L"settings" && request.operation != L"analyze";
    const bool actionMatchesOperation = isSettings ||
        (request.operation == L"analyze" && request.action == L"open-details") ||
        (request.operation == L"convert" && (request.action == L"choose-format" || request.action == L"png" ||
            request.action == L"jpeg" || request.action == L"webp" || request.action == L"bmp" || request.action == L"tga")) ||
        (request.operation == L"optimize" && (request.action == L"choose-preset" || request.action == L"auto" ||
            request.action == L"lossless" || request.action == L"balanced" || request.action == L"smallest"));
    if (!actionMatchesOperation)
    {
        error = L"The requested action is not supported.";
        return false;
    }

    constexpr std::string_view countPrefix = "pathCount=";
    if (!lines[4].starts_with(countPrefix))
    {
        error = L"The activation request has no selection count.";
        return false;
    }

    size_t pathCount = 0;
    const std::string_view countText = lines[4].substr(countPrefix.size());
    const auto countResult = std::from_chars(countText.data(), countText.data() + countText.size(), pathCount);
    if (countResult.ec != std::errc{} || countResult.ptr != countText.data() + countText.size() ||
        (isSettings ? pathCount != 0 : pathCount == 0) || pathCount > MaximumSelectionCount || lines.size() != 5 + pathCount)
    {
        error = L"The activation selection count is invalid.";
        return false;
    }

    request.paths.reserve(pathCount);
    for (size_t index = 0; index < pathCount; ++index)
    {
        std::wstring pathText;
        if (!ReadField(lines[5 + index], "path=", pathText) || pathText.size() > 32767)
        {
            error = L"The activation request contains an invalid path.";
            return false;
        }

        std::filesystem::path path(pathText);
        if (!path.is_absolute())
        {
            error = L"The activation request contains a relative path.";
            return false;
        }

        const DWORD attributes = GetFileAttributesW(path.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
        {
            error = L"A selected file is missing or is not a regular file.";
            return false;
        }
        request.paths.push_back(std::move(path));
    }

    return true;
}

bool GetExpectedActivationDirectory(std::filesystem::path& directory)
{
    PWSTR localAppData = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &localAppData)))
    {
        return false;
    }
    directory = std::filesystem::path(localAppData) / L"ContextSuite" / L"Prototype" / L"Activations";
    CoTaskMemFree(localAppData);
    return true;
}

bool IsTrustedActivationPath(const std::filesystem::path& path)
{
    std::filesystem::path expectedDirectory;
    if (!GetExpectedActivationDirectory(expectedDirectory) || path.extension() != L".request")
    {
        return false;
    }

    std::error_code error;
    const auto actualParent = std::filesystem::weakly_canonical(path.parent_path(), error);
    if (error)
    {
        return false;
    }
    const auto expectedParent = std::filesystem::weakly_canonical(expectedDirectory, error);
    return !error && _wcsicmp(actualParent.c_str(), expectedParent.c_str()) == 0;
}

std::wstring DisplayOperation(std::wstring_view operation)
{
    if (operation == L"analyze")
    {
        return L"Analyze";
    }
    if (operation == L"convert")
    {
        return L"Convert";
    }
    return L"Optimize";
}

std::wstring BuildSummary(const ActivationRequest& request)
{
    std::wstring summary = L"Shell activation received.\n\nOperation: ";
    summary += DisplayOperation(request.operation);
    summary += L"\nSelected files: " + std::to_wstring(request.paths.size()) + L"\n\n";

    const size_t displayedCount = std::min<size_t>(request.paths.size(), 12);
    for (size_t index = 0; index < displayedCount; ++index)
    {
        summary += std::to_wstring(index + 1) + L". " + request.paths[index].filename().wstring() + L"\n";
    }
    if (request.paths.size() > displayedCount)
    {
        summary += L"...and " + std::to_wstring(request.paths.size() - displayedCount) + L" more";
    }
    return summary;
}

bool WriteValidationResult(
    const std::filesystem::path& path,
    const ActivationRequest& request,
    std::wstring& error)
{
    std::wstring result = L"VALID\noperation=" + request.operation +
        L"\naction=" + request.action +
        L"\npathCount=" + std::to_wstring(request.paths.size()) + L"\n";
    for (const auto& selectedPath : request.paths)
    {
        result += L"file=" + selectedPath.filename().wstring() + L"\n";
    }
    const std::string encoded = ToUtf8(result);

    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        error = L"The validation result could not be created.";
        return false;
    }
    DWORD written = 0;
    const bool succeeded = WriteFile(file, encoded.data(), static_cast<DWORD>(encoded.size()), &written, nullptr) &&
        written == encoded.size();
    CloseHandle(file);
    if (!succeeded)
    {
        error = L"The validation result could not be written.";
    }
    return succeeded;
}

bool CompleteShellContractTest(const ActivationRequest& request, std::wstring& error)
{
    wchar_t resultPath[32768]{};
    wchar_t eventName[256]{};
    const DWORD resultLength = GetEnvironmentVariableW(
        L"CONTEXT_SUITE_PROTOTYPE_TEST_RESULT",
        resultPath,
        ARRAYSIZE(resultPath));
    const DWORD eventLength = GetEnvironmentVariableW(
        L"CONTEXT_SUITE_PROTOTYPE_TEST_EVENT",
        eventName,
        ARRAYSIZE(eventName));
    if (resultLength == 0 || resultLength >= ARRAYSIZE(resultPath) ||
        eventLength == 0 || eventLength >= ARRAYSIZE(eventName))
    {
        return false;
    }

    const bool wroteResult = WriteValidationResult(resultPath, request, error);
    HANDLE event = OpenEventW(EVENT_MODIFY_STATE, FALSE, eventName);
    if (event != nullptr)
    {
        SetEvent(event);
        CloseHandle(event);
    }
    return wroteResult;
}

int Fail(const Arguments& arguments, std::wstring_view message)
{
    if (!arguments.validateOnly)
    {
        MessageBoxW(nullptr, std::wstring(message).c_str(), L"Context Suite activation error", MB_OK | MB_ICONERROR);
    }
    return 1;
}
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    Arguments arguments;
    std::wstring error;
    if (!ParseArguments(arguments, error))
    {
        return Fail(arguments, error);
    }

    if (!arguments.validateOnly && !IsTrustedActivationPath(arguments.activationFile))
    {
        return Fail(arguments, L"The activation request came from an untrusted location.");
    }

    std::string content;
    if (!ReadRequestFile(arguments.activationFile, content, error))
    {
        return Fail(arguments, error);
    }

    if (!arguments.validateOnly)
    {
        DeleteFileW(arguments.activationFile.c_str());
    }

    ActivationRequest request;
    if (!ParseRequest(content, request, error))
    {
        return Fail(arguments, error);
    }

    if (arguments.validateOnly)
    {
        if (!arguments.resultFile.empty() && !WriteValidationResult(arguments.resultFile, request, error))
        {
            return Fail(arguments, error);
        }
        return 0;
    }

    if (CompleteShellContractTest(request, error))
    {
        return 0;
    }

    const std::wstring title = L"Context Suite \u2014 " + DisplayOperation(request.operation);
    const std::wstring summary = BuildSummary(request);
    MessageBoxW(nullptr, summary.c_str(), title.c_str(), MB_OK | MB_ICONINFORMATION);
    return 0;
}
