#pragma once
#include <cstdint>

// Internal ABI. The caller owns inputs; release a successful result exactly once.
enum AvifStatus : int32_t { Success, BadArgument, InvalidInput, Unsupported, ResourceLimit, Cancelled, EngineFailure };
using AvifCancel = int(__cdecl*)(void*);

#define AVIF_BRIDGE extern "C" __declspec(dllexport)
AVIF_BRIDGE uint32_t __cdecl cs_avif_abi() noexcept;
// rgba contains straight (unassociated) sRGB RGBA8, tightly packed, with normalized orientation.
AVIF_BRIDGE int32_t __cdecl cs_avif_encode(const uint8_t* rgba, uint64_t rgbaLength,
    int32_t width, int32_t height, int32_t stride,
    const uint8_t* icc, uint64_t iccLength,
    const uint8_t* exif, uint64_t exifLength,
    const uint8_t* xmp, uint64_t xmpLength,
    AvifCancel cancel, void* cancelContext, void** result) noexcept;
AVIF_BRIDGE int32_t __cdecl cs_avif_length(const void* result, uint64_t* length) noexcept;
AVIF_BRIDGE int32_t __cdecl cs_avif_copy(const void* result, uint8_t* destination, uint64_t length) noexcept;
AVIF_BRIDGE void __cdecl cs_avif_release(void* result) noexcept;
