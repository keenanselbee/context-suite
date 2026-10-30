#pragma once
#include <cstdint>

// Internal ABI, not a file format. All buffers are owned by the caller except
// the opaque decoded result, which must be released with cs_heif_release.
struct HeifInfo {
    uint32_t abi, width, height, bits;
    uint32_t primaries, transfer, matrix, fullRange;
    uint32_t itemType, flags, iccBytes, exifBytes, xmpBytes;
    uint32_t codec;
    uint32_t effectivePrimaries, effectiveTransfer, effectiveMatrix, effectiveFullRange;
};
static_assert(sizeof(HeifInfo) == 72);
enum HeifFlags : uint32_t {
    HasAlpha = 1, Premultiplied = 2, HdrMetadata = 4, OtherMetadata = 8,
    HasNclx = 16, Transformed = 32, HasThumbnail = 64,
    MultipleImages = 128, HasSequence = 256, OtherAuxiliary = 512,
    DecodedColor = 1024, ColorDefaults = 2048
};
enum HeifStatus : int32_t { Success, BadArgument, InvalidInput, Unsupported, ResourceLimit, Cancelled, EngineFailure };
using HeifCancel = int (__cdecl*)(void*);

#define HEIF_BRIDGE extern "C" __declspec(dllexport)
HEIF_BRIDGE int32_t __cdecl cs_heif_decode(const uint8_t* bytes, uint64_t length,
    HeifCancel cancel, void* cancelContext, void** result) noexcept;
HEIF_BRIDGE int32_t __cdecl cs_heif_info(const void* result, HeifInfo* info, uint32_t size) noexcept;
// Block 0: little-endian normalized Q16 RGBA; 1: ICC; 2: raw TIFF EXIF; 3: XMP.
HEIF_BRIDGE int32_t __cdecl cs_heif_copy(const void* result, uint32_t block, uint8_t* destination, uint64_t length) noexcept;
HEIF_BRIDGE void __cdecl cs_heif_release(void* result) noexcept;
