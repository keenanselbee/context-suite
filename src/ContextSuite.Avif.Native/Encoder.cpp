#include "Encoder.h"
#include <avif/avif.h>
#include <cstring>
#include <memory>
#include <new>
#include <vector>

namespace {
constexpr uint64_t MaxMetadata = 4ull * 1024 * 1024;
constexpr uint64_t MaxEncoded = 128ull * 1024 * 1024;
struct Failure { AvifStatus status; };
struct Result { std::vector<uint8_t> bytes; };

void Check(avifResult result) {
    if (result == AVIF_RESULT_OK) return;
    if (result == AVIF_RESULT_OUT_OF_MEMORY) throw Failure{ResourceLimit};
    if (result == AVIF_RESULT_INVALID_ARGUMENT || result == AVIF_RESULT_INVALID_EXIF_PAYLOAD)
        throw Failure{InvalidInput};
    if (result == AVIF_RESULT_NO_CODEC_AVAILABLE || result == AVIF_RESULT_NOT_IMPLEMENTED)
        throw Failure{Unsupported};
    throw Failure{EngineFailure};
}
void CheckCancel(AvifCancel callback, void* context) {
    if (callback && callback(context)) throw Failure{Cancelled};
}
void CheckMetadata(const uint8_t* bytes, uint64_t length) {
    if (length > MaxMetadata) throw Failure{ResourceLimit};
    if ((bytes == nullptr) != (length == 0)) throw Failure{BadArgument};
}
}

uint32_t __cdecl cs_avif_abi() noexcept { return 1; }
int32_t __cdecl cs_avif_encode(const uint8_t* rgba, uint64_t rgbaLength,
    int32_t width, int32_t height, int32_t stride,
    const uint8_t* icc, uint64_t iccLength,
    const uint8_t* exif, uint64_t exifLength,
    const uint8_t* xmp, uint64_t xmpLength,
    AvifCancel cancel, void* cancelContext, void** result) noexcept {
    if (!result) return BadArgument;
    *result = nullptr;
    try {
        if (!rgba || width <= 0 || height <= 0 || stride <= 0) return BadArgument;
        if (width > 16384 || height > 16384 || uint64_t(width) * height > 40000000) return ResourceLimit;
        if (uint64_t(stride) != uint64_t(width) * 4 || rgbaLength != uint64_t(stride) * height)
            return BadArgument;
        CheckMetadata(icc, iccLength);
        CheckMetadata(exif, exifLength);
        CheckMetadata(xmp, xmpLength);
        CheckCancel(cancel, cancelContext);
        if (std::strcmp(avifVersion(), "1.4.2") != 0) return EngineFailure;
        std::unique_ptr<avifImage, decltype(&avifImageDestroy)> image(
            avifImageCreate(width, height, 8, AVIF_PIXEL_FORMAT_YUV444), avifImageDestroy);
        if (!image) return ResourceLimit;
        image->colorPrimaries = AVIF_COLOR_PRIMARIES_BT709;
        image->transferCharacteristics = AVIF_TRANSFER_CHARACTERISTICS_SRGB;
        image->matrixCoefficients = AVIF_MATRIX_COEFFICIENTS_BT709;
        image->yuvRange = AVIF_RANGE_FULL;
        image->alphaPremultiplied = AVIF_FALSE;
        if (iccLength) Check(avifImageSetProfileICC(image.get(), icc, static_cast<size_t>(iccLength)));
        if (exifLength) Check(avifImageSetMetadataExif(image.get(), exif, static_cast<size_t>(exifLength)));
        if (xmpLength) Check(avifImageSetMetadataXMP(image.get(), xmp, static_cast<size_t>(xmpLength)));
        // Pixels supplied by the caller are already oriented; never add an AVIF transform.
        image->transformFlags = AVIF_TRANSFORM_NONE;
        avifRGBImage rgb{};
        avifRGBImageSetDefaults(&rgb, image.get());
        rgb.format = AVIF_RGB_FORMAT_RGBA;
        rgb.depth = 8;
        rgb.pixels = const_cast<uint8_t*>(rgba);
        rgb.rowBytes = static_cast<uint32_t>(stride);
        Check(avifImageRGBToYUV(image.get(), &rgb));
        CheckCancel(cancel, cancelContext);
        std::unique_ptr<avifEncoder, decltype(&avifEncoderDestroy)> encoder(avifEncoderCreate(), avifEncoderDestroy);
        if (!encoder) return ResourceLimit;
        encoder->codecChoice = AVIF_CODEC_CHOICE_AOM;
        encoder->quality = 95;
        encoder->qualityAlpha = 100;
        encoder->speed = 6;
        encoder->maxThreads = 2;
        avifRWData output = AVIF_DATA_EMPTY;
        try {
            Check(avifEncoderWrite(encoder.get(), image.get(), &output));
            CheckCancel(cancel, cancelContext);
            if (output.size == 0 || output.size > MaxEncoded) throw Failure{ResourceLimit};
            auto owned = std::make_unique<Result>();
            owned->bytes.assign(output.data, output.data + output.size);
            avifRWDataFree(&output);
            *result = owned.release();
            return Success;
        } catch (...) {
            avifRWDataFree(&output);
            throw;
        }
    } catch (const Failure& failure) { return failure.status; }
      catch (const std::bad_alloc&) { return ResourceLimit; }
      catch (...) { return EngineFailure; }
}

int32_t __cdecl cs_avif_length(const void* result, uint64_t* length) noexcept {
    if (!result || !length) return BadArgument;
    *length = static_cast<const Result*>(result)->bytes.size();
    return Success;
}
int32_t __cdecl cs_avif_copy(const void* result, uint8_t* destination, uint64_t length) noexcept {
    if (!result || !destination) return BadArgument;
    const auto& bytes = static_cast<const Result*>(result)->bytes;
    if (length != bytes.size()) return BadArgument;
    std::memcpy(destination, bytes.data(), bytes.size());
    return Success;
}
void __cdecl cs_avif_release(void* result) noexcept { delete static_cast<Result*>(result); }
