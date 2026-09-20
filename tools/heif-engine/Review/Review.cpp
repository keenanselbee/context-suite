#include <libheif/heif.h>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

static void Check(heif_error error) {
    if (error.code != heif_error_Ok) throw std::runtime_error(error.message);
}

struct Scene { const char* name; int width, height, bits, orientation; bool alpha; };

int wmain(int argc, wchar_t** argv) {
    try {
        if (argc != 2 || !std::filesystem::path(argv[1]).is_absolute())
            throw std::runtime_error("Use an absolute authored review source directory.");
        const std::filesystem::path directory(argv[1]);
        Check(heif_init(nullptr));
        for (const auto& scene : {Scene{"photograph", 768, 512, 8, 1, false}, {"orientation", 640, 400, 8, 6, false},
                                 {"transparency", 640, 400, 8, 1, true}, {"gradient", 2048, 256, 12, 1, false}}) {
            const auto raw = directory / (std::string(scene.name) + ".rgba16");
            const size_t count = size_t(scene.width) * scene.height * 4;
            if (std::filesystem::file_size(raw) != count * 2) throw std::runtime_error("Unexpected review raster size.");
            std::vector<uint16_t> samples(count);
            std::ifstream input(raw, std::ios::binary);
            if (!input.read(reinterpret_cast<char*>(samples.data()), static_cast<std::streamsize>(count * 2)))
                throw std::runtime_error("Cannot read review samples.");
            for (auto value : samples) if (value >= (1 << scene.bits)) throw std::runtime_error("Review sample exceeds its precision.");
            for (auto codec : {heif_compression_HEVC, heif_compression_AV1}) {
                const auto output = directory / (std::string(scene.name) + (codec == heif_compression_HEVC ? ".heic" : ".avif"));
                if (std::filesystem::exists(output)) throw std::runtime_error("Review input already exists.");
                std::unique_ptr<heif_context, decltype(&heif_context_free)> context(heif_context_alloc(), heif_context_free);
                if (!context) throw std::runtime_error("Cannot allocate review context.");
                heif_encoder* rawEncoder = nullptr;
                Check(heif_context_get_encoder_for_format(context.get(), codec, &rawEncoder));
                std::unique_ptr<heif_encoder, decltype(&heif_encoder_release)> encoder(rawEncoder, heif_encoder_release);
                Check(heif_encoder_set_lossless(encoder.get(), 1));
                Check(heif_encoder_set_parameter_string(encoder.get(), "chroma", "444"));
                if (codec == heif_compression_AV1) {
                    Check(heif_encoder_set_parameter_integer(encoder.get(), "speed", 8));
                    Check(heif_encoder_set_parameter_integer(encoder.get(), "threads", 1));
                    Check(heif_encoder_set_parameter_boolean(encoder.get(), "lossless-alpha", 1));
                } else {
                    Check(heif_encoder_set_parameter_string(encoder.get(), "preset", "ultrafast"));
                    Check(heif_encoder_set_parameter_string(encoder.get(), "x265:frame-threads", "1"));
                    Check(heif_encoder_set_parameter_string(encoder.get(), "x265:pools", "none"));
                }
                const auto chroma = scene.bits > 8 ? (scene.alpha ? heif_chroma_interleaved_RRGGBBAA_LE : heif_chroma_interleaved_RRGGBB_LE) :
                    (scene.alpha ? heif_chroma_interleaved_RGBA : heif_chroma_interleaved_RGB);
                heif_image* rawImage = nullptr;
                Check(heif_image_create(scene.width, scene.height, heif_colorspace_RGB, chroma, &rawImage));
                std::unique_ptr<heif_image, decltype(&heif_image_release)> image(rawImage, heif_image_release);
                Check(heif_image_add_plane(image.get(), heif_channel_interleaved, scene.width, scene.height, scene.bits));
                int stride = 0; auto plane = heif_image_get_plane(image.get(), heif_channel_interleaved, &stride);
                const int channels = scene.alpha ? 4 : 3; const int unit = scene.bits > 8 ? 2 : 1;
                if (!plane || stride < scene.width * channels * unit) throw std::runtime_error("Invalid review plane.");
                for (int y = 0; y < scene.height; y++) for (int x = 0; x < scene.width; x++) for (int c = 0; c < channels; c++) {
                    auto target = plane + size_t(y) * stride + (size_t(x) * channels + c) * unit;
                    auto value = samples[(size_t(y) * scene.width + x) * 4 + c];
                    target[0] = static_cast<uint8_t>(value);
                    if (unit == 2) target[1] = static_cast<uint8_t>(value >> 8);
                }
                std::unique_ptr<heif_color_profile_nclx, decltype(&heif_nclx_color_profile_free)> color(heif_nclx_color_profile_alloc(), heif_nclx_color_profile_free);
                if (!color) throw std::runtime_error("Cannot allocate review color profile.");
                Check(heif_nclx_color_profile_set_color_primaries(color.get(), 1));
                Check(heif_nclx_color_profile_set_transfer_characteristics(color.get(), 13));
                Check(heif_nclx_color_profile_set_matrix_coefficients(color.get(), 1));
                color->full_range_flag = 1;
                Check(heif_image_set_nclx_color_profile(image.get(), color.get()));
                std::unique_ptr<heif_encoding_options, decltype(&heif_encoding_options_free)> options(heif_encoding_options_alloc(), heif_encoding_options_free);
                if (!options) throw std::runtime_error("Cannot allocate review encoding options.");
                options->output_nclx_profile = color.get();
                options->image_orientation = static_cast<heif_orientation>(scene.orientation);
                Check(heif_context_encode_image(context.get(), image.get(), encoder.get(), options.get(), nullptr));
                Check(heif_context_write_to_file(context.get(), output.string().c_str()));
                std::cout << output.filename().string() << "\n";
            }
        }
        heif_deinit(); return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << "\n"; return 1;
    }
}
