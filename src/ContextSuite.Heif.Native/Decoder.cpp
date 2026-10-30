#include "Decoder.h"
#include <libheif/heif.h>
#include <libheif/heif_items.h>
#include <libheif/heif_properties.h>
#include <libheif/heif_sequences.h>
#include <array>
#include <algorithm>
#include <cstring>
#include <memory>
#include <mutex>
#include <new>
#include <vector>

namespace {
struct Failure { HeifStatus status; };
struct Result {
    HeifInfo info{3};
    std::array<std::vector<uint8_t>, 4> blocks;
};
void Check(heif_error error) {
    if (error.code == heif_error_Ok) return;
    if (error.code == heif_error_Memory_allocation_error) throw Failure{ResourceLimit};
    if (error.code == heif_error_Unsupported_feature) throw Failure{Unsupported};
    throw Failure{InvalidInput};
}
void CheckCancel(HeifCancel callback, void* context) {
    if (callback && callback(context)) throw Failure{Cancelled};
}
void CheckSize(int width, int height) {
    if (width <= 0 || height <= 0) throw Failure{InvalidInput};
    if (width > 16384 || height > 16384 || uint64_t(width) * height > 40000000) throw Failure{ResourceLimit};
}
std::vector<int> TransformSignature(heif_context* context, heif_image_handle* handle) {
    const auto item = heif_image_handle_get_item_id(handle);
    std::array<heif_property_id, 32> properties{};
    const int count = heif_item_get_transformation_properties(context, item, nullptr, 0);
    if (count < 0 || count > static_cast<int>(properties.size())) throw Failure{ResourceLimit};
    if (heif_item_get_transformation_properties(context, item, properties.data(), count) != count) throw Failure{InvalidInput};
    int width = heif_image_handle_get_ispe_width(handle), height = heif_image_handle_get_ispe_height(handle);
    std::vector<int> signature;
    for (int index = 0; index < count; ++index) {
        const auto property = properties[static_cast<size_t>(index)];
        const auto type = heif_item_get_property_type(context, item, property);
        if (type == 0x69726f74) { // irot
            const int angle = heif_item_get_property_transform_rotation_ccw(context, item, property);
            if (angle < 0 || angle % 90 != 0) throw Failure{InvalidInput};
            if (angle) { signature.push_back(type); signature.push_back(angle); }
            if (angle % 180) std::swap(width, height);
        } else if (type == 0x696d6972) { // imir
            const auto direction = heif_item_get_property_transform_mirror(context, item, property);
            if (direction < 0) throw Failure{InvalidInput};
            signature.push_back(type); signature.push_back(direction);
        } else if (type == 0x636c6170) { // clap
            CheckSize(width, height);
            int left = 0, top = 0, right = 0, bottom = 0;
            heif_item_get_property_transform_crop_borders(context, item, property, width, height, &left, &top, &right, &bottom);
            if (left || top || right || bottom) signature.insert(signature.end(), {type, left, top, right, bottom});
            const int64_t croppedWidth = int64_t(width) - left - right, croppedHeight = int64_t(height) - top - bottom;
            if (croppedWidth <= 0 || croppedHeight <= 0) throw Failure{InvalidInput};
            if (croppedWidth > 16384 || croppedHeight > 16384) throw Failure{ResourceLimit};
            width = static_cast<int>(croppedWidth); height = static_cast<int>(croppedHeight);
            CheckSize(width, height);
        } else throw Failure{Unsupported};
    }
    return signature;
}
void CheckAlphaTransforms(heif_context* context, heif_image_handle* handle) {
    std::array<heif_item_id, 32> all{}, other{};
    const int count = heif_image_handle_get_number_of_auxiliary_images(handle, 0);
    if (count < 0 || count > static_cast<int>(all.size())) throw Failure{ResourceLimit};
    if (heif_image_handle_get_list_of_auxiliary_image_IDs(handle, 0, all.data(), count) != count) throw Failure{InvalidInput};
    const int otherCount = heif_image_handle_get_list_of_auxiliary_image_IDs(handle, LIBHEIF_AUX_IMAGE_FILTER_OMIT_ALPHA,
        other.data(), static_cast<int>(other.size()));
    if (otherCount < 0 || otherCount > count) throw Failure{InvalidInput};
    for (int index = 0; index < count; ++index) {
        const auto id = all[static_cast<size_t>(index)];
        if (std::find(other.begin(), other.begin() + otherCount, id) != other.begin() + otherCount) continue;
        heif_image_handle* rawAlpha = nullptr;
        Check(heif_image_handle_get_auxiliary_image_handle(handle, id, &rawAlpha));
        std::unique_ptr<heif_image_handle, decltype(&heif_image_handle_release)> alpha(rawAlpha, heif_image_handle_release);
        // Do not let implicit alpha resizing hide a missing rotation/mirror/crop.
        if (TransformSignature(context, handle) != TransformSignature(context, alpha.get())) throw Failure{Unsupported};
    }
}
void Initialize() {
    static std::once_flag initialized;
    std::call_once(initialized, [] {
        Check(heif_init(nullptr));
        if (std::strcmp(heif_get_version(), "1.23.4") != 0 ||
            !heif_have_decoder_for_format(heif_compression_HEVC) || !heif_have_decoder_for_format(heif_compression_AV1) ||
            heif_have_encoder_for_format(heif_compression_HEVC) || heif_have_encoder_for_format(heif_compression_AV1))
            throw Failure{EngineFailure};
    });
}
}

int32_t __cdecl cs_heif_decode(const uint8_t* bytes, uint64_t length,
    HeifCancel cancel, void* cancelContext, void** result) noexcept {
    if (!result) return BadArgument;
    *result = nullptr;
    if (!bytes || length < 12) return BadArgument;
    if (length > 128 * 1024 * 1024) return ResourceLimit;
    try {
        CheckCancel(cancel, cancelContext);
        Initialize();
        std::unique_ptr<heif_context, decltype(&heif_context_free)> context(heif_context_alloc(), heif_context_free);
        if (!context) throw std::bad_alloc();
        auto limits = *heif_get_global_security_limits();
        limits.max_image_size_pixels = 40000000;
        limits.max_items = 1024;
        limits.max_number_of_tiles = 256;
        limits.max_memory_block_size = 256 * 1024 * 1024;
        limits.max_total_memory = 512 * 1024 * 1024;
        limits.max_color_profile_size = 4 * 1024 * 1024;
        limits.max_sequence_frames = 16;
        Check(heif_context_set_security_limits(context.get(), &limits));
        heif_context_set_max_decoding_threads(context.get(), 1);
        Check(heif_context_read_from_memory_without_copy(context.get(), bytes, static_cast<size_t>(length), nullptr));
        CheckCancel(cancel, cancelContext);
        heif_image_handle* rawHandle = nullptr;
        Check(heif_context_get_primary_image_handle(context.get(), &rawHandle));
        std::unique_ptr<heif_image_handle, decltype(&heif_image_handle_release)> handle(rawHandle, heif_image_handle_release);
        CheckSize(heif_image_handle_get_width(handle.get()), heif_image_handle_get_height(handle.get()));
        auto decoded = std::make_unique<Result>();
        auto& info = decoded->info;
        const auto item = heif_image_handle_get_item_id(handle.get());
        CheckAlphaTransforms(context.get(), handle.get());
        info.itemType = heif_item_get_item_type(context.get(), item);
        if (heif_image_handle_has_alpha_channel(handle.get())) info.flags |= HasAlpha;
        if (heif_image_handle_is_premultiplied_alpha(handle.get())) info.flags |= Premultiplied;
        if (heif_image_handle_has_content_light_level(handle.get()) || heif_image_handle_has_mastering_display_colour_volume(handle.get())) info.flags |= HdrMetadata;
        if (heif_context_get_number_of_top_level_images(context.get()) != 1) info.flags |= MultipleImages;
        if (heif_context_number_of_sequence_tracks(context.get())) info.flags |= HasSequence;
        if (heif_image_handle_get_number_of_auxiliary_images(handle.get(), LIBHEIF_AUX_IMAGE_FILTER_OMIT_ALPHA) ||
            heif_image_handle_get_number_of_depth_images(handle.get())) info.flags |= OtherAuxiliary;
        if (heif_image_handle_get_number_of_thumbnails(handle.get())) info.flags |= HasThumbnail;
        if (heif_item_get_transformation_properties(context.get(), item, nullptr, 0)) info.flags |= Transformed;
        heif_color_profile_nclx* rawColor = nullptr;
        const auto colorError = heif_image_handle_get_nclx_color_profile(handle.get(), &rawColor);
        if (colorError.code != heif_error_Color_profile_does_not_exist) Check(colorError);
        std::unique_ptr<heif_color_profile_nclx, decltype(&heif_nclx_color_profile_free)> color(rawColor, heif_nclx_color_profile_free);
        if (color) {
            info.flags |= HasNclx;
            info.primaries = color->color_primaries;
            info.transfer = color->transfer_characteristics;
            info.matrix = color->matrix_coefficients;
            info.fullRange = color->full_range_flag;
        }
        const auto iccBytes = heif_image_handle_get_raw_color_profile_size(handle.get());
        if (iccBytes > 4 * 1024 * 1024) throw Failure{ResourceLimit};
        if (iccBytes) {
            decoded->blocks[1].resize(iccBytes);
            Check(heif_image_handle_get_raw_color_profile(handle.get(), decoded->blocks[1].data()));
            info.iccBytes = static_cast<uint32_t>(iccBytes);
        }
        info.codec = info.itemType;
        if (info.itemType == 0x67726964) { // grid
            heif_image_tiling tiling{};
            Check(heif_image_handle_get_image_tiling(handle.get(), 0, &tiling));
            if (!tiling.num_columns || !tiling.num_rows ||
                uint64_t(tiling.num_columns) * tiling.num_rows > 256 || tiling.number_of_extra_dimensions)
                throw Failure{ResourceLimit};
            info.codec = 0;
            for (uint32_t y = 0; y < tiling.num_rows; ++y) for (uint32_t x = 0; x < tiling.num_columns; ++x) {
                CheckCancel(cancel, cancelContext);
                heif_item_id tileId = 0;
                Check(heif_image_handle_get_grid_image_tile_id(handle.get(), 0, x, y, &tileId));
                const auto codec = heif_item_get_item_type(context.get(), tileId);
                if ((codec != 0x68766331 && codec != 0x61763031) || (info.codec && info.codec != codec))
                    throw Failure{Unsupported};
                info.codec = codec;
                heif_image_handle* rawTile = nullptr;
                Check(heif_context_get_image_handle(context.get(), tileId, &rawTile));
                std::unique_ptr<heif_image_handle, decltype(&heif_image_handle_release)> tile(rawTile, heif_image_handle_release);
                CheckAlphaTransforms(context.get(), tile.get());
                CheckSize(heif_image_handle_get_width(tile.get()), heif_image_handle_get_height(tile.get()));
                if (heif_image_handle_get_luma_bits_per_pixel(tile.get()) != heif_image_handle_get_luma_bits_per_pixel(handle.get()) ||
                    heif_image_handle_has_alpha_channel(tile.get()) != heif_image_handle_has_alpha_channel(handle.get()) ||
                    heif_image_handle_is_premultiplied_alpha(tile.get()) ||
                    heif_image_handle_has_content_light_level(tile.get()) || heif_image_handle_has_mastering_display_colour_volume(tile.get()) ||
                    heif_image_handle_get_number_of_auxiliary_images(tile.get(), LIBHEIF_AUX_IMAGE_FILTER_OMIT_ALPHA) ||
                    heif_image_handle_get_number_of_depth_images(tile.get()) ||
                    heif_image_handle_get_number_of_metadata_blocks(tile.get(), nullptr))
                    throw Failure{Unsupported};
                if (heif_image_handle_get_number_of_thumbnails(tile.get())) info.flags |= HasThumbnail;
                heif_color_profile_nclx* rawTileColor = nullptr;
                const auto tileColorError = heif_image_handle_get_nclx_color_profile(tile.get(), &rawTileColor);
                if (tileColorError.code != heif_error_Color_profile_does_not_exist) Check(tileColorError);
                std::unique_ptr<heif_color_profile_nclx, decltype(&heif_nclx_color_profile_free)> tileColor(rawTileColor, heif_nclx_color_profile_free);
                if (tileColor && (!color || tileColor->color_primaries != color->color_primaries ||
                    tileColor->transfer_characteristics != color->transfer_characteristics ||
                    tileColor->matrix_coefficients != color->matrix_coefficients || tileColor->full_range_flag != color->full_range_flag))
                    throw Failure{Unsupported};
                const auto tileIccSize = heif_image_handle_get_raw_color_profile_size(tile.get());
                if (tileIccSize) {
                    if (tileIccSize != iccBytes) throw Failure{Unsupported};
                    std::vector<uint8_t> tileIcc(tileIccSize);
                    Check(heif_image_handle_get_raw_color_profile(tile.get(), tileIcc.data()));
                    if (tileIcc != decoded->blocks[1]) throw Failure{Unsupported};
                }
            }
        }
        std::array<heif_item_id, 32> metadata{};
        const int count = heif_image_handle_get_number_of_metadata_blocks(handle.get(), nullptr);
        if (count < 0 || count > static_cast<int>(metadata.size())) throw Failure{ResourceLimit};
        if (heif_image_handle_get_list_of_metadata_block_IDs(handle.get(), nullptr, metadata.data(), static_cast<int>(metadata.size())) != count)
            throw Failure{InvalidInput};
        for (int i = 0; i < count; ++i) {
            const auto id = metadata[static_cast<size_t>(i)];
            const auto type = heif_image_handle_get_metadata_type(handle.get(), id);
            const auto contentType = heif_image_handle_get_metadata_content_type(handle.get(), id);
            const int block = type && std::strcmp(type, "Exif") == 0 ? 2 :
                type && contentType && std::strcmp(type, "mime") == 0 && std::strcmp(contentType, "application/rdf+xml") == 0 ? 3 : 0;
            if (!block) { info.flags |= OtherMetadata; continue; }
            const auto size = heif_image_handle_get_metadata_size(handle.get(), id);
            if (!size || size > 4 * 1024 * 1024 || !decoded->blocks[block].empty()) throw Failure{InvalidInput};
            auto& data = decoded->blocks[block];
            data.resize(size);
            Check(heif_image_handle_get_metadata(handle.get(), id, data.data()));
            if (block == 2) {
                if (size < 4) throw Failure{InvalidInput};
                const uint32_t offset = (uint32_t(data[0]) << 24) | (uint32_t(data[1]) << 16) | (uint32_t(data[2]) << 8) | data[3];
                if (offset > size - 4 || size - 4 - offset < 8) throw Failure{InvalidInput};
                data.erase(data.begin(), data.begin() + 4 + offset);
            }
        }
        info.exifBytes = static_cast<uint32_t>(decoded->blocks[2].size());
        info.xmpBytes = static_cast<uint32_t>(decoded->blocks[3].size());
        std::unique_ptr<heif_decoding_options, decltype(&heif_decoding_options_free)> options(heif_decoding_options_alloc(), heif_decoding_options_free);
        if (!options) throw std::bad_alloc();
        options->strict_decoding = 1;
        options->convert_hdr_to_8bit = 0;
        options->output_image_nclx_profile_passthrough = 1;
        options->cancel_decoding = cancel;
        options->progress_user_data = cancelContext;
        options->num_codec_threads = 1;
        const int bits = heif_image_handle_get_luma_bits_per_pixel(handle.get());
        if (bits != 8 && bits != 10 && bits != 12) throw Failure{Unsupported};
        info.effectivePrimaries = info.primaries;
        info.effectiveTransfer = info.transfer;
        info.effectiveMatrix = info.matrix;
        info.effectiveFullRange = info.fullRange;
        if (!color || info.primaries == 2 || info.transfer == 2 || info.matrix == 2) {
            // Decode in its native colorspace before RGB conversion can discard
            // the consolidated container/bitstream color declaration. Release
            // these planes before requesting RGB to bound simultaneous storage.
            heif_image* rawSource = nullptr;
            const auto sourceError = heif_decode_image(handle.get(), &rawSource,
                heif_colorspace_undefined, heif_chroma_undefined, options.get());
            std::unique_ptr<heif_image, decltype(&heif_image_release)> source(rawSource, heif_image_release);
            CheckCancel(cancel, cancelContext);
            Check(sourceError);
            if (heif_image_get_decoding_warnings(source.get(), 0, nullptr, 0) != 0) throw Failure{InvalidInput};
            heif_color_profile_nclx* rawEffective = nullptr;
            const auto effectiveError = heif_image_get_nclx_color_profile(source.get(), &rawEffective);
            if (effectiveError.code != heif_error_Color_profile_does_not_exist) Check(effectiveError);
            std::unique_ptr<heif_color_profile_nclx, decltype(&heif_nclx_color_profile_free)> effective(rawEffective, heif_nclx_color_profile_free);
            info.flags |= DecodedColor;
            info.effectivePrimaries = effective ? effective->color_primaries : 2;
            info.effectiveTransfer = effective ? effective->transfer_characteristics : 2;
            info.effectiveMatrix = effective ? effective->matrix_coefficients : 2;
            info.effectiveFullRange = effective ? effective->full_range_flag : 1;
            // Match the pinned library's RGB conversion defaults only for
            // unspecified (2), never reserved or unrecognized declarations.
            if (info.effectivePrimaries == 2) { info.effectivePrimaries = 1; info.flags |= ColorDefaults; }
            if (info.effectiveTransfer == 2) { info.effectiveTransfer = 13; info.flags |= ColorDefaults; }
            if (info.effectiveMatrix == 2) { info.effectiveMatrix = 6; info.flags |= ColorDefaults; }
        }
        heif_image* rawImage = nullptr;
        const auto decodeError = heif_decode_image(handle.get(), &rawImage, heif_colorspace_RGB,
            bits == 8 ? heif_chroma_interleaved_RGBA : heif_chroma_interleaved_RRGGBBAA_LE, options.get());
        std::unique_ptr<heif_image, decltype(&heif_image_release)> image(rawImage, heif_image_release);
        CheckCancel(cancel, cancelContext);
        Check(decodeError);
        if (heif_image_get_decoding_warnings(image.get(), 0, nullptr, 0) != 0) throw Failure{InvalidInput};
        if (heif_image_get_bits_per_pixel_range(image.get(), heif_channel_interleaved) != bits) throw Failure{Unsupported};
        const int width = heif_image_get_primary_width(image.get()), height = heif_image_get_primary_height(image.get());
        CheckSize(width, height);
        int stride = 0;
        const auto plane = heif_image_get_plane_readonly(image.get(), heif_channel_interleaved, &stride);
        if (!plane || stride < width * 4 * (bits == 8 ? 1 : 2)) throw Failure{InvalidInput};
        info.width = static_cast<uint32_t>(width);
        info.height = static_cast<uint32_t>(height);
        info.bits = static_cast<uint32_t>(bits);
        auto& pixels = decoded->blocks[0];
        pixels.resize(size_t(width) * height * 8);
        const uint32_t maximum = (1u << bits) - 1;
        for (int y = 0; y < height; ++y) {
            CheckCancel(cancel, cancelContext);
            const auto row = plane + size_t(y) * stride;
            for (int x = 0; x < width * 4; ++x) {
                const uint32_t sample = bits == 8 ? row[x] : uint32_t(row[x * 2]) | (uint32_t(row[x * 2 + 1]) << 8);
                if (sample > maximum) throw Failure{InvalidInput};
                const uint32_t normalized = (sample * 65535u + maximum / 2) / maximum;
                const auto offset = (size_t(y) * width * 4 + x) * 2;
                pixels[offset] = static_cast<uint8_t>(normalized);
                pixels[offset + 1] = static_cast<uint8_t>(normalized >> 8);
            }
        }
        *result = decoded.release();
        return Success;
    } catch (const Failure& failure) { return failure.status; }
    catch (const std::bad_alloc&) { return ResourceLimit; }
    catch (...) { return EngineFailure; }
}

int32_t __cdecl cs_heif_info(const void* result, HeifInfo* info, uint32_t size) noexcept {
    if (!result || !info || size != sizeof(HeifInfo)) return BadArgument;
    *info = static_cast<const Result*>(result)->info;
    return Success;
}
int32_t __cdecl cs_heif_copy(const void* result, uint32_t block, uint8_t* destination, uint64_t length) noexcept {
    if (!result || block > 3) return BadArgument;
    const auto& data = static_cast<const Result*>(result)->blocks[block];
    if (length != data.size() || (length && !destination)) return BadArgument;
    if (length) std::memcpy(destination, data.data(), data.size());
    return Success;
}
void __cdecl cs_heif_release(void* result) noexcept { delete static_cast<Result*>(result); }
