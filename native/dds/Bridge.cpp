#include <DirectXTex.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <new>

using namespace DirectX;

namespace
{
    constexpr size_t MaximumBytes = 128u * 1024u * 1024u;
    constexpr size_t MaximumPixels = 40000000;
    struct Result { uint8_t* bytes; size_t length; uint32_t width; uint32_t height; };

    bool Supported(DXGI_FORMAT format)
    {
        switch (format)
        {
        case DXGI_FORMAT_R8_UNORM: case DXGI_FORMAT_R8G8_UNORM:
        case DXGI_FORMAT_R8G8B8A8_UNORM: case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
        case DXGI_FORMAT_B8G8R8A8_UNORM: case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
        case DXGI_FORMAT_B8G8R8X8_UNORM: case DXGI_FORMAT_B8G8R8X8_UNORM_SRGB:
        case DXGI_FORMAT_BC1_UNORM: case DXGI_FORMAT_BC1_UNORM_SRGB:
        case DXGI_FORMAT_BC2_UNORM: case DXGI_FORMAT_BC2_UNORM_SRGB:
        case DXGI_FORMAT_BC3_UNORM: case DXGI_FORMAT_BC3_UNORM_SRGB:
        case DXGI_FORMAT_BC4_UNORM: case DXGI_FORMAT_BC4_SNORM:
        case DXGI_FORMAT_BC5_UNORM: case DXGI_FORMAT_BC5_SNORM:
        case DXGI_FORMAT_BC7_UNORM: case DXGI_FORMAT_BC7_UNORM_SRGB: return true;
        default: return false;
        }
    }

    HRESULT Load(const uint8_t* bytes, size_t length, ScratchImage& image)
    {
        if (!bytes || length < 128 || length > MaximumBytes) return E_INVALIDARG;
        TexMetadata metadata{};
        auto hr = GetMetadataFromDDSMemory(bytes, length, DDS_FLAGS_NONE, metadata);
        if (FAILED(hr)) return hr;
        if (!Supported(metadata.format) || metadata.dimension != TEX_DIMENSION_TEXTURE2D || metadata.IsCubemap() ||
            metadata.arraySize != 1 || metadata.depth != 1 || !metadata.width || !metadata.height ||
            metadata.width > 16384 || metadata.height > 16384 || !metadata.mipLevels || metadata.mipLevels > 15 ||
            metadata.GetAlphaMode() == TEX_ALPHA_MODE_PREMULTIPLIED)
            return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
        size_t count = 0;
        for (size_t level = 0; level < metadata.mipLevels; ++level)
        {
            count += std::max<size_t>(1, metadata.width >> level) * std::max<size_t>(1, metadata.height >> level);
            if (count > MaximumPixels) return E_OUTOFMEMORY;
        }
        return LoadFromDDSMemory(bytes, length, DDS_FLAGS_NONE, &metadata, image);
    }

    HRESULT FloatPixels(ScratchImage& source, ScratchImage& output)
    {
        // The caller chooses interpretation. Never infer a transfer from UNORM or
        // allow automatic sRGB conversion to apply it twice.
        source.OverrideFormat(MakeLinear(source.GetMetadata().format));
        if (IsCompressed(source.GetMetadata().format))
            return Decompress(source.GetImages(), source.GetImageCount(), source.GetMetadata(), DXGI_FORMAT_R32G32B32A32_FLOAT, output);
        return Convert(source.GetImages(), source.GetImageCount(), source.GetMetadata(), DXGI_FORMAT_R32G32B32A32_FLOAT,
            TEX_FILTER_DEFAULT, 0.5f, output);
    }

    HRESULT CopyResult(const void* data, size_t size, uint32_t width, uint32_t height, Result* result, size_t limit = MaximumBytes)
    {
        if (size > limit) return E_OUTOFMEMORY;
        auto bytes = std::make_unique<uint8_t[]>(size);
        std::memcpy(bytes.get(), data, size);
        *result = {bytes.release(), size, width, height};
        return S_OK;
    }
}

// Narrow in-memory ABI. No filenames, filesystem publication, codec commands or
// account state cross this boundary. The managed owner frees every successful result.
extern "C" __declspec(dllexport) uint32_t __cdecl CsDdsAbi() noexcept { return 1; }
extern "C" __declspec(dllexport) void __cdecl CsDdsFree(uint8_t* bytes) noexcept { delete[] bytes; }

extern "C" __declspec(dllexport) HRESULT __cdecl CsDdsReadMip(const uint8_t* bytes, size_t length, uint32_t mip, Result* result) noexcept
{
    if (!result) return E_POINTER;
    *result = {};
    try
    {
        ScratchImage source, pixels;
        auto hr = Load(bytes, length, source);
        if (FAILED(hr)) return hr;
        if (mip >= source.GetMetadata().mipLevels) return E_INVALIDARG;
        // Decode only the selected level; authored chains must not be expanded
        // repeatedly when probing or validating each mip.
        auto selected = *source.GetImage(mip, 0, 0);
        selected.format = MakeLinear(selected.format);
        hr = IsCompressed(selected.format) ? Decompress(selected, DXGI_FORMAT_R32G32B32A32_FLOAT, pixels) :
            Convert(selected, DXGI_FORMAT_R32G32B32A32_FLOAT, TEX_FILTER_DEFAULT, 0.5f, pixels);
        if (FAILED(hr)) return hr;
        const auto image = pixels.GetImage(0, 0, 0);
        if (!image || image->rowPitch != image->width * 16) return E_UNEXPECTED;
        return CopyResult(image->pixels, image->slicePitch, static_cast<uint32_t>(image->width), static_cast<uint32_t>(image->height), result, MaximumPixels * 16);
    }
    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    catch (...) { return E_FAIL; }
}

extern "C" __declspec(dllexport) HRESULT __cdecl CsDdsEncode(const uint8_t* bytes, size_t length, uint32_t target,
    float threshold, uint32_t quality, Result* result) noexcept
{
    if (!result) return E_POINTER;
    *result = {};
    const auto format = static_cast<DXGI_FORMAT>(target);
    if (!Supported(format) || !std::isfinite(threshold) || threshold < 0 || threshold > 1 || quality > 2) return E_INVALIDARG;
    try
    {
        ScratchImage source, pixels, encoded;
        auto hr = Load(bytes, length, source);
        if (FAILED(hr)) return hr;
        const auto metadata = source.GetMetadata();
        hr = FloatPixels(source, pixels);
        if (FAILED(hr)) return hr;
        // Pixel values are already in the chosen output encoding. This primitive
        // changes representation only; color/mip policies are validated separately.
        const auto linearFormat = MakeLinear(format);
        if (IsCompressed(format))
        {
            auto flags = quality == 0 ? TEX_COMPRESS_BC7_QUICK : quality == 2 ? TEX_COMPRESS_BC7_USE_3SUBSETS : TEX_COMPRESS_DEFAULT;
            hr = Compress(pixels.GetImages(), pixels.GetImageCount(), pixels.GetMetadata(), linearFormat, flags, threshold, encoded);
        }
        else hr = Convert(pixels.GetImages(), pixels.GetImageCount(), pixels.GetMetadata(), linearFormat, TEX_FILTER_DEFAULT, threshold, encoded);
        if (FAILED(hr)) return hr;
        encoded.OverrideFormat(format);
        Blob output;
        auto outputMetadata = encoded.GetMetadata();
        outputMetadata.SetAlphaMode(metadata.GetAlphaMode());
        hr = SaveToDDSMemory(encoded.GetImages(), encoded.GetImageCount(), outputMetadata, DDS_FLAGS_FORCE_DX10_EXT_MISC2, output);
        if (FAILED(hr)) return hr;
        return CopyResult(output.GetBufferPointer(), output.GetBufferSize(), static_cast<uint32_t>(metadata.width), static_cast<uint32_t>(metadata.height), result);
    }
    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    catch (...) { return E_FAIL; }
}

extern "C" __declspec(dllexport) HRESULT __cdecl CsDdsEncodePixels(const float* rgba, size_t count, uint32_t width, uint32_t height,
    uint32_t mipLevels, uint32_t target, float threshold, uint32_t quality, uint32_t alphaMode, Result* result) noexcept
{
    if (!result) return E_POINTER;
    *result = {};
    const auto format = static_cast<DXGI_FORMAT>(target);
    if (!rgba || !width || !height || width > 16384 || height > 16384 || !mipLevels || mipLevels > 15 ||
        count > MaximumPixels * 4 || !Supported(format) || quality > 2 || !std::isfinite(threshold) || threshold < 0 || threshold > 1 ||
        alphaMode > 4 || alphaMode == 2) return E_INVALIDARG;
    size_t required = 0;
    for (uint32_t level = 0; level < mipLevels; ++level)
        required += std::max<size_t>(1, width >> level) * std::max<size_t>(1, height >> level) * 4;
    if (count != required) return E_INVALIDARG;
    for (size_t i = 0; i < count; ++i) if (!std::isfinite(rgba[i])) return E_INVALIDARG;
    try
    {
        ScratchImage pixels, encoded;
        auto hr = pixels.Initialize2D(DXGI_FORMAT_R32G32B32A32_FLOAT, width, height, 1, mipLevels);
        if (FAILED(hr)) return hr;
        if (pixels.GetPixelsSize() != count * sizeof(float)) return E_UNEXPECTED;
        std::memcpy(pixels.GetPixels(), rgba, count * sizeof(float));
        const auto linearFormat = MakeLinear(format);
        if (IsCompressed(format))
        {
            auto flags = quality == 0 ? TEX_COMPRESS_BC7_QUICK : quality == 2 ? TEX_COMPRESS_BC7_USE_3SUBSETS : TEX_COMPRESS_DEFAULT;
            hr = Compress(pixels.GetImages(), pixels.GetImageCount(), pixels.GetMetadata(), linearFormat, flags, threshold, encoded);
        }
        else hr = Convert(pixels.GetImages(), pixels.GetImageCount(), pixels.GetMetadata(), linearFormat, TEX_FILTER_DEFAULT, threshold, encoded);
        if (FAILED(hr)) return hr;
        encoded.OverrideFormat(format);
        auto metadata = encoded.GetMetadata();
        metadata.SetAlphaMode(static_cast<TEX_ALPHA_MODE>(alphaMode));
        Blob output;
        hr = SaveToDDSMemory(encoded.GetImages(), encoded.GetImageCount(), metadata, DDS_FLAGS_FORCE_DX10_EXT_MISC2, output);
        if (FAILED(hr)) return hr;
        return CopyResult(output.GetBufferPointer(), output.GetBufferSize(), width, height, result);
    }
    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    catch (...) { return E_FAIL; }
}
