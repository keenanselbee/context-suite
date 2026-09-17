#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#include <libheif/heif.h>
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <iostream>
#include <memory>
#include <stdexcept>

// Match the x64 test-only thread-context sampler to the installed Windows SDK.
static_assert(sizeof(CONTEXT) == 1232 && offsetof(CONTEXT, ContextFlags) == 48 && offsetof(CONTEXT, Rip) == 248);

static void Check(heif_error error) {
    if (error.code != heif_error_Ok) throw std::runtime_error(error.message);
}

int wmain(int argc, wchar_t** argv) {
    try {
        if (argc != 2 || !std::filesystem::path(argv[1]).is_absolute() || std::filesystem::exists(argv[1]))
            throw std::runtime_error("Use a new absolute stress fixture directory.");
        Check(heif_init(nullptr));
        std::filesystem::create_directory(argv[1]);
        for (auto codec : {heif_compression_HEVC, heif_compression_AV1}) {
            std::unique_ptr<heif_context, decltype(&heif_context_free)> context(heif_context_alloc(), heif_context_free);
            if (!context) throw std::runtime_error("Cannot allocate stress context.");
            heif_encoder* rawEncoder = nullptr;
            Check(heif_context_get_encoder_for_format(context.get(), codec, &rawEncoder));
            std::unique_ptr<heif_encoder, decltype(&heif_encoder_release)> encoder(rawEncoder, heif_encoder_release);
            Check(heif_encoder_set_lossless(encoder.get(), 1));
            Check(heif_encoder_set_parameter_string(encoder.get(), "chroma", "420"));
            if (codec == heif_compression_AV1) {
                Check(heif_encoder_set_parameter_integer(encoder.get(), "speed", 8));
                Check(heif_encoder_set_parameter_integer(encoder.get(), "threads", 1));
            } else {
                Check(heif_encoder_set_parameter_string(encoder.get(), "preset", "ultrafast"));
                Check(heif_encoder_set_parameter_string(encoder.get(), "x265:frame-threads", "1"));
                Check(heif_encoder_set_parameter_string(encoder.get(), "x265:pools", "none"));
            }
            // Keep this single HEVC slice below libde265's 16 MiB NAL limit.
            constexpr int dimension = 3072;
            heif_image* rawImage = nullptr;
            Check(heif_image_create(dimension, dimension, heif_colorspace_YCbCr, heif_chroma_420, &rawImage));
            std::unique_ptr<heif_image, decltype(&heif_image_release)> image(rawImage, heif_image_release);
            uint32_t state = 0x1937429;
            for (auto channel : {heif_channel_Y, heif_channel_Cb, heif_channel_Cr}) {
                const int size = channel == heif_channel_Y ? dimension : dimension / 2;
                Check(heif_image_add_plane(image.get(), channel, size, size, 8));
                int stride = 0;
                auto plane = heif_image_get_plane(image.get(), channel, &stride);
                if (!plane || stride < size) throw std::runtime_error("Invalid stress plane.");
                for (int y = 0; y < size; ++y) for (int x = 0; x < size; ++x) {
                    state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                    plane[size_t(y) * stride + x] = channel == heif_channel_Y ? static_cast<uint8_t>(state) : 128;
                }
            }
            std::unique_ptr<heif_color_profile_nclx, decltype(&heif_nclx_color_profile_free)> color(heif_nclx_color_profile_alloc(), heif_nclx_color_profile_free);
            if (!color) throw std::runtime_error("Cannot allocate stress color profile.");
            Check(heif_nclx_color_profile_set_color_primaries(color.get(), 1));
            Check(heif_nclx_color_profile_set_transfer_characteristics(color.get(), 13));
            Check(heif_nclx_color_profile_set_matrix_coefficients(color.get(), 1));
            color->full_range_flag = 1;
            Check(heif_image_set_nclx_color_profile(image.get(), color.get()));
            std::unique_ptr<heif_encoding_options, decltype(&heif_encoding_options_free)> options(heif_encoding_options_alloc(), heif_encoding_options_free);
            if (!options) throw std::runtime_error("Cannot allocate stress options.");
            options->output_nclx_profile = color.get();
            Check(heif_context_encode_image(context.get(), image.get(), encoder.get(), options.get(), nullptr));
            const auto name = codec == heif_compression_HEVC ? "stress.heic" : "stress.avif";
            Check(heif_context_write_to_file(context.get(), (std::filesystem::path(argv[1]) / name).string().c_str()));
            std::cout << name << "\n";
        }
        heif_deinit();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << "\n";
        return 1;
    }
}
