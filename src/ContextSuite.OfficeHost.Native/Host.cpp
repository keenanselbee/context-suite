#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <userenv.h>
#include <bcrypt.h>
#include <array>
#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <io.h>
#include <stdexcept>
#include <string>
#include <vector>
#include <functional>
#include <thread>
#include <exception>

namespace fs = std::filesystem;
namespace {
constexpr ULONGLONG MaximumSource = 64ULL * 1024 * 1024;
constexpr ULONGLONG MaximumOutput = 128ULL * 1024 * 1024;
constexpr ULONGLONG MaximumWorkbook = 64ULL * 1024 * 1024;
const char* phase = "configuration";
struct Handle {
    HANDLE value = nullptr;
    explicit Handle(HANDLE handle = nullptr) : value(handle) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
struct HostError : std::runtime_error {
    DWORD windowsError;
    explicit HostError(const char* message) : std::runtime_error(message), windowsError(GetLastError()) {}
};
void Require(bool condition, const char* message) { if (!condition) throw HostError(message); }

std::wstring Environment(const wchar_t* name) {
    wchar_t value[32768]{};
    const auto count = GetEnvironmentVariableW(name, value, static_cast<DWORD>(std::size(value)));
    Require(count > 0 && count < std::size(value), "Missing or oversized host configuration");
    return std::wstring(value, count);
}

void RequireIsolation() {
    Handle token;
    Require(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token.value) != FALSE, "Process token");
    DWORD isolated = 0, returned = 0;
    Require(GetTokenInformation(token.value, TokenIsAppContainer, &isolated, sizeof(isolated), &returned) != FALSE && isolated == 1,
        "Office requires AppContainer isolation");
    DWORD bytes = 0;
    GetTokenInformation(token.value, TokenCapabilities, nullptr, 0, &bytes);
    Require(bytes >= sizeof(DWORD) && bytes <= 65536, "Capability token size");
    std::vector<BYTE> capabilities(bytes);
    Require(GetTokenInformation(token.value, TokenCapabilities, capabilities.data(), bytes, &returned) != FALSE &&
        reinterpret_cast<TOKEN_GROUPS*>(capabilities.data())->GroupCount == 0, "Office requires zero capabilities");
    const auto name = Environment(L"CS_OFFICE_PROFILE_NAME");
    Require(name.starts_with(L"ContextSuite.Office.") && name.size() <= 128, "Owned profile name");
    PSID expected = nullptr;
    Require(SUCCEEDED(DeriveAppContainerSidFromAppContainerName(name.c_str(), &expected)), "Expected profile SID");
    struct SidGuard { PSID value; ~SidGuard() { FreeSid(value); } } guard{expected};
    bytes = 0;
    GetTokenInformation(token.value, TokenAppContainerSid, nullptr, 0, &bytes);
    Require(bytes >= sizeof(TOKEN_APPCONTAINER_INFORMATION) && bytes <= 65536, "AppContainer SID size");
    std::vector<BYTE> actual(bytes);
    Require(GetTokenInformation(token.value, TokenAppContainerSid, actual.data(), bytes, &returned) != FALSE &&
        EqualSid(expected, reinterpret_cast<TOKEN_APPCONTAINER_INFORMATION*>(actual.data())->TokenAppContainer), "AppContainer identity mismatch");
}

struct PathLeases {
    std::vector<HANDLE> values;
    ~PathLeases() { for (const auto value : values) CloseHandle(value); }
};

std::wstring FileIdentity(HANDLE handle) {
    FILE_ID_INFO identity{};
    Require(GetFileInformationByHandleEx(handle, FileIdInfo, &identity, sizeof(identity)) != FALSE, "Read staging file identity");
    const wchar_t* digits = L"0123456789ABCDEF";
    std::wstring text;
    for (int shift = 60; shift >= 0; shift -= 4) text.push_back(digits[(identity.VolumeSerialNumber >> shift) & 15]);
    for (const auto byte : identity.FileId.Identifier) { text.push_back(digits[byte >> 4]); text.push_back(digits[byte & 15]); }
    return text;
}

void CheckIdentity(HANDLE handle, const wchar_t* configuration) {
    const auto expected = Environment((std::wstring(configuration) + L"_IDENTITY").c_str());
    Require(expected.size() == 48 && expected.find_first_not_of(L"0123456789ABCDEF") == std::wstring::npos &&
        FileIdentity(handle) == expected, "Staging file identity mismatch");
    FILE_ATTRIBUTE_TAG_INFO attributes{};
    Require(GetFileInformationByHandleEx(handle, FileAttributeTagInfo, &attributes, sizeof(attributes)) != FALSE &&
        (attributes.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0, "Linked staging handle refused");
}

fs::path ConfiguredPath(const wchar_t* name, bool existing, PathLeases& leases) {
    const fs::path path(Environment(name));
    const auto text = path.native();
    Require(path.is_absolute() && text.size() > 3 && text[1] == L':' && text[2] == L'\\' &&
        text.find_first_of(L"\r\n") == std::wstring::npos && path == path.lexically_normal(), "Absolute local staging path required");
    // Ancestors outside the grant are deliberately hidden. The launcher checks
    // that complete chain; the isolated host retains and verifies the granted
    // target (or immediate parent of a new target) by opened handle.
    const auto target = existing ? path : path.parent_path();
    Handle location(CreateFileW(target.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    Require(location.value != INVALID_HANDLE_VALUE, "Open granted staging location");
    CheckIdentity(location.value, name);
    leases.values.push_back(location.value); location.value = nullptr;
    return path;
}

ULONGLONG CheckFile(HANDLE file, ULONGLONG maximum, bool readOnly) {
    FILE_STANDARD_INFO standard{}; FILE_BASIC_INFO basic{};
    Require(GetFileType(file) == FILE_TYPE_DISK && GetFileInformationByHandleEx(file, FileStandardInfo, &standard, sizeof(standard)) &&
        GetFileInformationByHandleEx(file, FileBasicInfo, &basic, sizeof(basic)), "Regular file handle");
    Require(!standard.Directory && !standard.DeletePending && standard.NumberOfLinks == 1 && standard.EndOfFile.QuadPart > 0 &&
        static_cast<ULONGLONG>(standard.EndOfFile.QuadPart) <= maximum && (basic.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0 &&
        (!readOnly || (basic.FileAttributes & FILE_ATTRIBUTE_READONLY) != 0), "Bounded single-link file required");
    return static_cast<ULONGLONG>(standard.EndOfFile.QuadPart);
}

std::string Hash(HANDLE input) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    Require(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0, "SHA-256 provider");
    struct AlgorithmGuard { BCRYPT_ALG_HANDLE value; ~AlgorithmGuard() { BCryptCloseAlgorithmProvider(value, 0); } } algorithmGuard{algorithm};
    BCRYPT_HASH_HANDLE hash = nullptr;
    Require(BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) == 0, "SHA-256 state");
    struct HashGuard { BCRYPT_HASH_HANDLE value; ~HashGuard() { BCryptDestroyHash(value); } } hashGuard{hash};
    Require(SetFilePointerEx(input, {}, nullptr, FILE_BEGIN) != FALSE, "Source hash position");
    std::array<BYTE, 65536> buffer{};
    for (;;) {
        DWORD count = 0;
        Require(ReadFile(input, buffer.data(), static_cast<DWORD>(buffer.size()), &count, nullptr) != FALSE, "Source hash read");
        if (!count) break;
        Require(BCryptHashData(hash, buffer.data(), count, 0) == 0, "Source hash update");
    }
    std::array<BYTE, 32> digest{};
    Require(BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) == 0, "Source hash finish");
    const char* digits = "0123456789ABCDEF";
    std::string result;
    for (const auto byte : digest) { result.push_back(digits[byte >> 4]); result.push_back(digits[byte & 15]); }
    return result;
}

void CreateProfile(const fs::path& path, const std::wstring& calculation) {
    Require(fs::symlink_status(path).type() == fs::file_type::not_found && fs::create_directory(path), "Fresh engine profile required");
    Require(fs::create_directory(path / L"user"), "Fresh profile settings directory");
    std::ofstream settings(path / L"user" / L"registrymodifications.xcu", std::ios::binary);
    settings << R"(<?xml version="1.0" encoding="UTF-8"?><oor:items xmlns:oor="http://openoffice.org/2001/registry" xmlns:xs="http://www.w3.org/2001/XMLSchema">)";
    const auto property = [&](const char* group, const char* name, const char* type, const char* value) {
        settings << "<item oor:path=\"" << group << "\"><prop oor:name=\"" << name << "\" oor:op=\"fuse\" oor:type=\"xs:"
            << type << "\"><value>" << value << "</value></prop></item>";
    };
    const char* scripting = "/org.openoffice.Office.Common/Security/Scripting";
    for (const char* name : {"DisableMacrosExecution", "DisableActiveContent", "DisablePythonRuntime"}) property(scripting, name, "boolean", "true");
    property(scripting, "MacroSecurityLevel", "int", "3");
    const char* updates = "/org.openoffice.Office.Jobs/Jobs/org.openoffice.Office.Jobs:Job['UpdateCheck']/Arguments";
    property(updates, "AutoCheckEnabled", "boolean", "false"); property(updates, "AutoDownloadEnabled", "boolean", "false");
    property("/org.openoffice.Office.Common/Misc", "UseOpenCL", "boolean", "false");
    if (calculation != L"none") property("/org.openoffice.Office.Calc/Formula/Load", "OOXMLRecalcMode", "int", calculation == L"recalculate" ? "0" : "1");
    settings << "</oor:items>"; settings.flush(); Require(settings.good(), "Write fixed profile settings");
}

#include "EmbeddedOffice.h"

void Reply(HANDLE output, const std::string& reply) {
    DWORD written = 0;
    Require(reply.size() <= 36 * 1024 && WriteFile(output, reply.data(), static_cast<DWORD>(reply.size()), &written, nullptr) != FALSE &&
        written == reply.size(), "Complete host reply");
}
}

int wmain(int argc, wchar_t**) {
    try {
        Require(argc == 1, "Office host accepts no command arguments");
        RequireIsolation(); // Before reading media/configured paths or loading any engine DLL.
        const auto request = Environment(L"CS_OFFICE_REQUEST_ID");
        Require(request.size() == 32 && request.find_first_not_of(L"0123456789abcdef") == std::wstring::npos &&
            request != std::wstring(32, L'0'), "Request identity");
        const auto format = Environment(L"CS_OFFICE_FORMAT");
        const int family = format == L"docx" ? 0 : format == L"xlsx" ? 1 : format == L"pptx" ? 2 : -1;
        Require(family >= 0, "Explicit Office source variant");
        const auto calculation = Environment(L"CS_OFFICE_CALCULATION");
        Require(family == 1 ? calculation == L"cached" || calculation == L"recalculate" : calculation == L"none", "Explicit calculation policy");
        PathLeases paths;
        const auto source = ConfiguredPath(L"CS_OFFICE_SOURCE", true, paths);
        const auto output = ConfiguredPath(L"CS_OFFICE_OUTPUT", false, paths);
        const auto profile = ConfiguredPath(L"CS_OFFICE_PROFILE", false, paths);
        const auto program = ConfiguredPath(L"CS_OFFICE_PROGRAM", true, paths);
        const auto workbook = output.parent_path() / L"calculated.xlsx";
        Require(family != 1 || fs::symlink_status(workbook).type() == fs::file_type::not_found, "Fresh calculated workbook required");
        Require(source.extension() == L"." + format && output.extension() == L".pdf" &&
            fs::symlink_status(output).type() == fs::file_type::not_found && fs::symlink_status(profile).type() == fs::file_type::not_found &&
            source != output && output != profile, "Fresh output/profile and matching source extension");
        Handle input(CreateFileW(source.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
        Require(input.value != INVALID_HANDLE_VALUE, "Open read-only source lease");
        CheckIdentity(input.value, L"CS_OFFICE_SOURCE");
        const auto sourceBytes = CheckFile(input.value, MaximumSource, true);
        char prefix[4]{}; DWORD read = 0;
        Require(ReadFile(input.value, prefix, sizeof(prefix), &read, nullptr) && read == sizeof(prefix) && std::memcmp(prefix, "PK\003\004", 4) == 0, "Office package prefix");
        const auto sourceHash = Hash(input.value);
        Handle terminal;
        Require(DuplicateHandle(GetCurrentProcess(), GetStdHandle(STD_OUTPUT_HANDLE), GetCurrentProcess(), &terminal.value, 0, FALSE, DUPLICATE_SAME_ACCESS) != FALSE,
            "Retain terminal reply channel");
        Require(_dup2(_fileno(stderr), _fileno(stdout)) == 0 && SetStdHandle(STD_OUTPUT_HANDLE, GetStdHandle(STD_ERROR_HANDLE)) != FALSE,
            "Separate engine diagnostics from terminal reply");
        SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
        // The engine also reads the CRT environment, whose startup snapshot is
        // not updated by SetEnvironmentVariable alone. Set both before DLL load.
        Require(_wputenv_s(L"SAL_LOK_OPTIONS", L"unipoll") == 0 && _wputenv_s(L"SAL_DISABLE_OPENCL", L"1") == 0 &&
            Environment(L"SAL_LOK_OPTIONS") == L"unipoll" && Environment(L"SAL_DISABLE_OPENCL") == L"1", "Fixed engine environment");
        CreateProfile(profile, calculation);
        const auto fontReports = Export(program, profile, source, output, family);
        Require(CheckFile(input.value, MaximumSource, true) == sourceBytes && Hash(input.value) == sourceHash, "Source preserved");
        CheckIdentity(input.value, L"CS_OFFICE_SOURCE");
        Handle pdf(CreateFileW(output.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
        Require(pdf.value != INVALID_HANDLE_VALUE, "Open completed candidate");
        const auto outputBytes = CheckFile(pdf.value, MaximumOutput, false);
        char pdfPrefix[5]{};
        Require(ReadFile(pdf.value, pdfPrefix, sizeof(pdfPrefix), &read, nullptr) && read == sizeof(pdfPrefix) && std::memcmp(pdfPrefix, "%PDF-", 5) == 0, "PDF candidate prefix");
        const auto outputHash = Hash(pdf.value);
        ULONGLONG workbookBytes = 0;
        std::string workbookHash = "null";
        Handle calculated;
        if (family == 1) {
            calculated.value = CreateFileW(workbook.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
            Require(calculated.value != INVALID_HANDLE_VALUE, "Open calculated workbook");
            workbookBytes = CheckFile(calculated.value, MaximumWorkbook, false);
            char workbookPrefix[4]{};
            Require(ReadFile(calculated.value, workbookPrefix, sizeof(workbookPrefix), &read, nullptr) && read == sizeof(workbookPrefix) &&
                std::memcmp(workbookPrefix, "PK\003\004", 4) == 0, "Calculated workbook package prefix");
            workbookHash = "\"" + Hash(calculated.value) + "\"";
        }
        Reply(terminal.value, "{\"version\":3,\"requestId\":\"" + Utf8(request) + "\",\"completed\":true,\"format\":\"" + Utf8(format) +
            "\",\"policy\":\"office-pdf-final-1\",\"calculation\":\"" + Utf8(calculation) + "\",\"sourceBytes\":" + std::to_string(sourceBytes) +
            ",\"outputBytes\":" + std::to_string(outputBytes) + ",\"sourceSha256\":\"" + sourceHash + "\",\"outputSha256\":\"" + outputHash + "\",\"fontReports\":" + fontReports +
            ",\"workbookBytes\":" + std::to_string(workbookBytes) + ",\"workbookSha256\":" + workbookHash + "}\n");
        return 0;
    } catch (const HostError& error) {
        // Require messages are fixed labels, never source paths or engine text.
        std::fprintf(stderr, "Office host did not complete: %s (Windows error %lu).\n", error.what(), error.windowsError);
        return 2;
    } catch (const std::bad_alloc&) {
        std::fprintf(stderr, "Office host exhausted memory during %s.\n", phase);
        return 2;
    } catch (const fs::filesystem_error& error) {
        std::fprintf(stderr, "Office host filesystem failure during %s (code %d).\n", phase, error.code().value());
        return 2;
    } catch (...) {
        // Fixed phase labels only: engine exceptions may contain document data.
        std::fprintf(stderr, "Office host did not complete during %s.\n", phase);
        return 2; // No terminal success reply is emitted on failure.
    }
}
