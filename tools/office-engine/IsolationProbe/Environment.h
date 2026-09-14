#pragma once
#include <map>

// Explicit process inputs for the authored access probe. No caller values or
// user-specific runtime/search paths are copied into this environment.
std::map<std::wstring, std::wstring> AccessEnvironmentValues(const fs::path& root, bool isolated = false) {
    wchar_t windows[32768]{};
    const auto length = GetWindowsDirectoryW(windows, static_cast<UINT>(std::size(windows)));
    Require(length > 0 && length < std::size(windows), "Locate Windows directory");
    const auto profile = root / L"writable" / L"profile \u00fc";
    auto local = profile / L"AppData" / L"Local";
    auto temporary = root / L"writable" / L"temp \u00fc";
    if (isolated) {
        // Windows redirects these three values during AppContainer creation,
        // using the supplied LOCALAPPDATA base and normalized profile moniker.
        local /= L"Packages";
        local /= L"contextsuite.office.evaluation." + root.parent_path().filename().native();
        local /= L"AC";
        temporary = local / L"Temp";
    }
    return {
        { L"APPDATA", (profile / L"AppData" / L"Roaming").native() },
        { L"LOCALAPPDATA", local.native() },
        { L"PATH", (fs::path(windows) / L"System32").native() },
        { L"SYSTEMROOT", windows }, { L"TEMP", temporary.native() },
        { L"TMP", temporary.native() }, { L"USERPROFILE", profile.native() },
        { L"WINDIR", windows }
    };
}

std::vector<wchar_t> EnvironmentBlock(const std::map<std::wstring, std::wstring>& values) {
    std::vector<wchar_t> block;
    for (const auto& [name, value] : values) {
        const auto entry = name + L"=" + value;
        block.insert(block.end(), entry.begin(), entry.end());
        block.push_back(L'\0');
    }
    block.push_back(L'\0');
    Require(block.size() <= 32767, "Bound explicit environment");
    return block;
}

bool AccessEnvironmentMatches(const fs::path& root, bool isolated = false, bool retainKnownValues = false) {
    auto strings = GetEnvironmentStringsW();
    Require(strings != nullptr, "Read child environment");
    struct EnvironmentGuard { wchar_t* value; ~EnvironmentGuard() { FreeEnvironmentStringsW(value); } } guard{ strings };
    std::map<std::wstring, std::wstring> actual;
    size_t total = 0;
    for (const auto* next = strings; *next; ) {
        const std::wstring entry(next);
        total += entry.size() + 1;
        if (total > 32767) return false;
        next += entry.size() + 1;
        const auto equals = entry.find(L'=');
        if (equals == std::wstring::npos || equals == 0) return false;
        auto name = entry.substr(0, equals);
        for (auto& character : name) if (character >= L'a' && character <= L'z') character -= L'a' - L'A';
        if (!actual.emplace(name, entry.substr(equals + 1)).second) return false;
    }
    const auto expected = AccessEnvironmentValues(root, isolated);
    if (retainKnownValues) {
        // Retain only the eight supplied non-secret path variables. Unknown
        // variables are counted, never copied into diagnostic evidence.
        std::ofstream report(root / L"writable" / L"environment-known.txt");
        report << "actual-count=" << actual.size() << '\n';
        for (const auto& [name, value] : expected) {
            const auto found = actual.find(name);
            const auto line = name + L"=" + (found == actual.end() ? L"<absent>" : found->second) + L"\n";
            const auto bytes = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, line.data(), static_cast<int>(line.size()), nullptr, 0, nullptr, nullptr);
            Require(bytes > 0, "Size environment observation");
            std::string encoded(bytes, '\0');
            Require(WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, line.data(), static_cast<int>(line.size()), encoded.data(), bytes, nullptr, nullptr) == bytes,
                "Encode environment observation");
            report << encoded;
        }
        Require(report.good(), "Retain known environment observation");
    }
    return actual == expected;
}
