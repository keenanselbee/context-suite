#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>

int wmain(int argc, wchar_t** argv) {
    try {
        if (argc != 2) throw std::runtime_error("Use a fresh owned probe directory.");
        const std::filesystem::path root(argv[1]);
        if (!root.is_absolute() || root.native().find(L"\\.codex-temp\\office-path\\") == std::wstring::npos ||
            root.native().size() > 200 || std::filesystem::exists(root)) throw std::runtime_error("Invalid probe directory.");
        std::filesystem::create_directories(root);
        std::cout << '[';
        bool first = true;
        for (const size_t length : {247, 248, 249, 260, 261, 320}) {
            const auto path = root.native() + L"\\" + std::wstring(length - root.native().size() - 1, L'x');
            const auto extended = L"\\\\?\\" + path;
            const bool ordinary = CreateDirectoryW(path.c_str(), nullptr) != FALSE;
            const auto error = ordinary ? ERROR_SUCCESS : GetLastError();
            if (ordinary && !RemoveDirectoryW(extended.c_str())) throw std::runtime_error("Plain probe cleanup failed.");
            const bool prefixed = CreateDirectoryW(extended.c_str(), nullptr) != FALSE;
            const auto prefixedError = prefixed ? ERROR_SUCCESS : GetLastError();
            if (prefixed && !RemoveDirectoryW(extended.c_str())) throw std::runtime_error("Prefixed probe cleanup failed.");
            if (!first) std::cout << ',';
            first = false;
            std::cout << "{\"length\":" << length << ",\"plain\":" << (ordinary ? "true" : "false") <<
                ",\"plainError\":" << error << ",\"prefixed\":" << (prefixed ? "true" : "false") <<
                ",\"prefixedError\":" << prefixedError << '}';
        }
        std::cout << ']' << std::endl;
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << std::endl; return 1; }
}
