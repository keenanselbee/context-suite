#include <libheif/heif.h>
#include <libheif/heif_sequences.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

static void Check(heif_error error) {
    if (error.code != heif_error_Ok) throw std::runtime_error(error.message);
}
int wmain(int argc, wchar_t** argv) {
    try {
        if (argc != 3) throw std::runtime_error("One local research input and new output directory required.");
        const std::filesystem::path output(argv[2]);
        if (!output.is_absolute() || std::filesystem::exists(output)) throw std::runtime_error("Use a new absolute output directory.");
        Check(heif_init(nullptr));
        const bool hevc = heif_have_decoder_for_format(heif_compression_HEVC);
        const bool av1 = heif_have_decoder_for_format(heif_compression_AV1);
        if (!hevc || !av1 || heif_have_encoder_for_format(heif_compression_HEVC) || heif_have_encoder_for_format(heif_compression_AV1))
            throw std::runtime_error("Unexpected registered codec set.");
        const auto size = std::filesystem::file_size(argv[1]);
        if (size == 0 || size > 16 * 1024 * 1024) throw std::runtime_error("Research input limit.");
        std::vector<uint8_t> data(static_cast<size_t>(size));
        std::ifstream input(std::filesystem::path(argv[1]), std::ios::binary);
        if (!input.read(reinterpret_cast<char*>(data.data()), data.size())) throw std::runtime_error("Read failed.");
        std::unique_ptr<heif_context, decltype(&heif_context_free)> ctx(heif_context_alloc(), heif_context_free);
        if (!ctx) throw std::runtime_error("Context allocation failed.");
        auto limits = *heif_get_global_security_limits();
        limits.max_image_size_pixels = 40000000;
        limits.max_items = 1024;
        limits.max_number_of_tiles = 256;
        limits.max_memory_block_size = 256 * 1024 * 1024;
        limits.max_total_memory = 512 * 1024 * 1024;
        limits.max_sequence_frames = 16;
        Check(heif_context_set_security_limits(ctx.get(), &limits));
        heif_context_set_max_decoding_threads(ctx.get(), 1);
        Check(heif_context_read_from_memory_without_copy(ctx.get(), data.data(), data.size(), nullptr));
        heif_image_handle* raw = nullptr;
        Check(heif_context_get_primary_image_handle(ctx.get(), &raw));
        std::unique_ptr<heif_image_handle, decltype(&heif_image_handle_release)> handle(raw, heif_image_handle_release);
        heif_image* decoded = nullptr;
        std::unique_ptr<heif_decoding_options, decltype(&heif_decoding_options_free)> options(heif_decoding_options_alloc(), heif_decoding_options_free);
        if (!options) throw std::runtime_error("Options allocation failed.");
        options->strict_decoding = 1;
        options->convert_hdr_to_8bit = 0;
        // Keep source color values/profile; output color policy belongs to the caller.
        // A null output profile otherwise requests an implicit sRGB conversion.
        options->output_image_nclx_profile_passthrough = 1;
        const int sourceBits = heif_image_handle_get_luma_bits_per_pixel(handle.get());
        if (sourceBits != 8 && sourceBits != 10 && sourceBits != 12) throw std::runtime_error("Research precision limit.");
        const int bytesPerSample = sourceBits > 8 ? 2 : 1;
        Check(heif_decode_image(handle.get(), &decoded, heif_colorspace_RGB,
            sourceBits == 8 ? heif_chroma_interleaved_RGBA : heif_chroma_interleaved_RRGGBBAA_LE, options.get()));
        std::unique_ptr<heif_image, decltype(&heif_image_release)> pixels(decoded, heif_image_release);
        const int decodedBits = heif_image_get_bits_per_pixel_range(pixels.get(), heif_channel_interleaved);
        if (decodedBits != sourceBits) throw std::runtime_error("Decoder changed sample precision.");
        const int width = heif_image_get_primary_width(pixels.get());
        const int height = heif_image_get_primary_height(pixels.get());
        int stride = 0;
        const auto plane = heif_image_get_plane_readonly(pixels.get(), heif_channel_interleaved, &stride);
        if (!plane || width <= 0 || height <= 0 || uint64_t(width) * height > 40000000 || stride < width * 4 * bytesPerSample)
            throw std::runtime_error("Invalid decoded plane.");
        std::filesystem::create_directory(output);
        heif_color_profile_nclx* rawColor = nullptr;
        const auto colorError = heif_image_handle_get_nclx_color_profile(handle.get(), &rawColor);
        if (colorError.code != heif_error_Color_profile_does_not_exist) Check(colorError);
        std::unique_ptr<heif_color_profile_nclx, decltype(&heif_nclx_color_profile_free)>
            color(rawColor, heif_nclx_color_profile_free);
        heif_color_profile_nclx* rawDecodedColor = nullptr;
        const auto decodedColorError = heif_image_get_nclx_color_profile(pixels.get(), &rawDecodedColor);
        if (decodedColorError.code != heif_error_Color_profile_does_not_exist) Check(decodedColorError);
        std::unique_ptr<heif_color_profile_nclx, decltype(&heif_nclx_color_profile_free)>
            decodedColor(rawDecodedColor, heif_nclx_color_profile_free);
        const size_t iccSize = heif_image_handle_get_raw_color_profile_size(handle.get());
        if (iccSize > 4 * 1024 * 1024 || heif_image_get_raw_color_profile_size(pixels.get()) != iccSize)
            throw std::runtime_error("Research ICC limit or changed decoded profile size.");
        if (iccSize) {
            std::vector<uint8_t> icc(iccSize), decodedIcc(iccSize);
            Check(heif_image_handle_get_raw_color_profile(handle.get(), icc.data()));
            Check(heif_image_get_raw_color_profile(pixels.get(), decodedIcc.data()));
            if (icc != decodedIcc) throw std::runtime_error("Decoder changed ICC bytes.");
            std::ofstream profile(output / "profile.icc", std::ios::binary);
            profile.write(reinterpret_cast<const char*>(icc.data()), icc.size());
            profile.close();
            if (!profile) throw std::runtime_error("Cannot write research ICC profile.");
        }
        std::ofstream rgba(output / "pixels.rgba", std::ios::binary);
        for (int y = 0; y < height; ++y) rgba.write(reinterpret_cast<const char*>(plane + size_t(y) * stride), width * 4 * bytesPerSample);
        rgba.close();
        if (!rgba) throw std::runtime_error("Cannot write research pixels.");
        heif_item_id ids[32];
        const int metadataCount = heif_image_handle_get_number_of_metadata_blocks(handle.get(), nullptr);
        if (metadataCount < 0 || metadataCount > 32 || heif_image_handle_get_list_of_metadata_block_IDs(handle.get(), nullptr, ids, 32) != metadataCount)
            throw std::runtime_error("Research metadata limit.");
        for (int i = 0; i < metadataCount; ++i) {
            const size_t length = heif_image_handle_get_metadata_size(handle.get(), ids[i]);
            if (length > 1024 * 1024) throw std::runtime_error("Research metadata size limit.");
            std::vector<uint8_t> metadata(length);
            Check(heif_image_handle_get_metadata(handle.get(), ids[i], metadata.data()));
            std::ofstream block(output / ("metadata-" + std::to_string(i) + ".bin"), std::ios::binary);
            block.write(reinterpret_cast<const char*>(metadata.data()), metadata.size());
            block.close();
            if (!block) throw std::runtime_error("Cannot write research metadata.");
        }
        uint64_t digest = 14695981039346656037ULL;
        for (int y = 0; y < height; ++y) for (int x = 0; x < width * 4 * bytesPerSample; ++x) {
            digest ^= plane[size_t(y) * stride + x]; digest *= 1099511628211ULL;
        }
        std::cout << "{\"library\":\"" << heif_get_version() << "\",\"width\":" << width
            << ",\"height\":" << height << ",\"topLevelImages\":" << heif_context_get_number_of_top_level_images(ctx.get())
            << ",\"alpha\":" << heif_image_handle_has_alpha_channel(handle.get())
            << ",\"metadataCount\":" << metadataCount
            << ",\"sequenceTracks\":" << heif_context_number_of_sequence_tracks(ctx.get())
            << ",\"thumbnails\":" << heif_image_handle_get_number_of_thumbnails(handle.get())
            << ",\"otherAuxiliaryImages\":" << heif_image_handle_get_number_of_auxiliary_images(handle.get(), LIBHEIF_AUX_IMAGE_FILTER_OMIT_ALPHA)
            << ",\"depthImages\":" << heif_image_handle_get_number_of_depth_images(handle.get())
            << ",\"premultipliedAlpha\":" << heif_image_handle_is_premultiplied_alpha(handle.get())
            << ",\"contentLightLevel\":" << heif_image_handle_has_content_light_level(handle.get())
            << ",\"masteringDisplay\":" << heif_image_handle_has_mastering_display_colour_volume(handle.get())
            << ",\"iccBytes\":" << iccSize
            << ",\"sourceLumaBits\":" << sourceBits << ",\"decodedBits\":" << decodedBits
            << ",\"storageBits\":" << bytesPerSample * 8;
        for (const auto& entry : {std::pair{"sourceNclx", color.get()}, std::pair{"decodedNclx", decodedColor.get()}}) {
            std::cout << ",\"" << entry.first << "\":";
            if (!entry.second) std::cout << "null";
            else std::cout << "{\"primaries\":" << entry.second->color_primaries
                << ",\"transfer\":" << entry.second->transfer_characteristics
                << ",\"matrix\":" << entry.second->matrix_coefficients
                << ",\"fullRange\":" << int(entry.second->full_range_flag) << "}";
        }
        std::cout << ",\"rgbaFnv64\":\"" << std::hex << digest << "\"}\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << "\n"; return 1; }
}
