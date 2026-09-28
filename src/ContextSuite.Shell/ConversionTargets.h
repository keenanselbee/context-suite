#pragma once
#include <windows.h>
#include <string_view>

namespace ContextSuite
{
// Filename hints only: the application still probes and validates every input.
// Keep Explorer enumeration free of media reads and engine/network requests.
inline bool CanConvertFilename(std::wstring_view name, unsigned target)
{
    const auto dot = name.find_last_of(L'.');
    if (dot == std::wstring_view::npos || name.size() - dot > 8) return false;
    const auto extension = name.substr(dot);
    const auto is = [extension](std::wstring_view expected)
    {
        return CompareStringOrdinal(extension.data(), static_cast<int>(extension.size()),
            expected.data(), static_cast<int>(expected.size()), TRUE) == CSTR_EQUAL;
    };
    const int raster = is(L".png") ? 0 : is(L".jpg") || is(L".jpeg") || is(L".jpe") ? 1 :
        is(L".webp") ? 2 : is(L".bmp") ? 3 : is(L".tga") ? 4 : -1;
    if (raster >= 0) return (target <= 5 && target != static_cast<unsigned>(raster)) || target == 12;
    if (is(L".dds")) return target == 0 || target == 5;
    if (is(L".heic") || is(L".heif") || is(L".avif")) return target <= 1;
    if (is(L".pdf")) return target == 0 || target == 13;
    if (is(L".docx") || is(L".xlsx") || is(L".pptx")) return target == 12;
    if (target < 6 || target > 11) return false;
    const int audio = is(L".wav") || is(L".wave") ? 6 : is(L".flac") ? 7 : is(L".mp3") ? 8 :
        is(L".m4a") ? 9 : is(L".ogg") ? 10 : is(L".opus") ? 11 : -1;
    // Ogg containers can carry either Vorbis or Opus; content probing stays outside Explorer.
    if (is(L".ogg") || is(L".oga")) return true;
    return audio >= 0 ? target != static_cast<unsigned>(audio) :
        is(L".aif") || is(L".aiff") || is(L".aifc") || is(L".wma");
}
}
