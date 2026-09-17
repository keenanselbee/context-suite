#include <libheif/heif.h>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
constexpr int Width = 64;
constexpr int Height = 48;

void Check(heif_error error) {
    if (error.code != heif_error_Ok) throw std::runtime_error(error.message);
}

uint16_t Gray(int x, int y, int bits) {
    const int value = (x * 7 + y * 11 + ((x / 8) ^ (y / 8)) * 19) & 255;
    const int maximum = (1 << bits) - 1;
    const int detail = (x + 3 * y) & ((1 << (bits - 8)) - 1);
    return static_cast<uint16_t>(((value * maximum + 127) / 255 + detail) & maximum);
}

uint16_t Alpha(int x, bool enabled, int bits) {
    const int maximum = (1 << bits) - 1;
    return static_cast<uint16_t>(enabled ? ((x / 8) % 4) * (maximum / 3) : maximum);
}

void Generate(const std::filesystem::path& directory, heif_compression_format codec,
              const std::string& extension, int bits, int orientation, bool alpha, bool metadata, bool colored = false,
              const std::string& variant = "", const std::filesystem::path& profiles = {}) {
    std::unique_ptr<heif_context, decltype(&heif_context_free)> context(heif_context_alloc(), heif_context_free);
    if (!context) throw std::runtime_error("Cannot allocate fixture context.");
    heif_encoder* rawEncoder = nullptr;
    Check(heif_context_get_encoder_for_format(context.get(), codec, &rawEncoder));
    std::unique_ptr<heif_encoder, decltype(&heif_encoder_release)> encoder(rawEncoder, heif_encoder_release);
    Check(heif_encoder_set_parameter_string(encoder.get(), "tune", "psnr"));
    Check(heif_encoder_set_lossless(encoder.get(), 1));
    Check(heif_encoder_set_parameter_string(encoder.get(), "chroma", "444"));
    if (codec == heif_compression_AV1) {
        Check(heif_encoder_set_parameter_integer(encoder.get(), "threads", 1));
        Check(heif_encoder_set_parameter_integer(encoder.get(), "speed", 8));
        Check(heif_encoder_set_parameter_boolean(encoder.get(), "lossless-alpha", 1));
    } else {
        Check(heif_encoder_set_parameter_string(encoder.get(), "preset", "ultrafast"));
        Check(heif_encoder_set_parameter_string(encoder.get(), "x265:frame-threads", "1"));
        Check(heif_encoder_set_parameter_string(encoder.get(), "x265:pools", "none"));
    }
    heif_image* rawImage = nullptr;
    Check(heif_image_create(Width, Height, heif_colorspace_YCbCr, heif_chroma_444, &rawImage));
    std::unique_ptr<heif_image, decltype(&heif_image_release)> image(rawImage, heif_image_release);
    for (auto channel : {heif_channel_Y, heif_channel_Cb, heif_channel_Cr, heif_channel_Alpha}) {
        if (channel == heif_channel_Alpha && !alpha) continue;
        Check(heif_image_add_plane(image.get(), channel, Width, Height, bits));
        int stride = 0;
        auto plane = heif_image_get_plane(image.get(), channel, &stride);
        if (!plane || stride < Width * (bits > 8 ? 2 : 1)) throw std::runtime_error("Invalid fixture plane.");
        for (int y = 0; y < Height; ++y) for (int x = 0; x < Width; ++x) {
            const int chroma = !colored ? 128 : channel == heif_channel_Cb ?
                96 + ((x / 8) % 4) * 16 : 104 + ((y / 8) % 4) * 16;
            const uint16_t value = channel == heif_channel_Y ? Gray(x, y, bits) :
                channel == heif_channel_Alpha ? Alpha(x, alpha, bits) : static_cast<uint16_t>(chroma << (bits - 8));
            if (bits == 8) plane[size_t(y) * stride + x] = static_cast<uint8_t>(value);
            else reinterpret_cast<uint16_t*>(plane + size_t(y) * stride)[x] = value;
        }
    }
    std::unique_ptr<heif_color_profile_nclx, decltype(&heif_nclx_color_profile_free)>
        color(heif_nclx_color_profile_alloc(), heif_nclx_color_profile_free);
    if (!color) throw std::runtime_error("Cannot allocate fixture color profile.");
    Check(heif_nclx_color_profile_set_color_primaries(color.get(), variant == "p3" ? 12 : 1));
    Check(heif_nclx_color_profile_set_transfer_characteristics(color.get(),
        variant == "bt709" || variant == "icc-adobe-bt709" ? 1 : variant == "pq" ? 16 : variant == "hlg" ? 18 : 13));
    Check(heif_nclx_color_profile_set_matrix_coefficients(color.get(), 1));
    color->full_range_flag = 1;
    Check(heif_image_set_nclx_color_profile(image.get(), color.get()));
    if (variant.rfind("icc-", 0) == 0) {
        const auto path = profiles / (variant == "icc-srgb" ? "srgb.icc" : "adobe.icc");
        const auto size = std::filesystem::file_size(path);
        if (size < 132 || size > 4 * 1024 * 1024) throw std::runtime_error("Fixture ICC size limit.");
        std::vector<uint8_t> icc(static_cast<size_t>(size));
        std::ifstream source(path, std::ios::binary);
        if (!source.read(reinterpret_cast<char*>(icc.data()), icc.size())) throw std::runtime_error("Cannot read fixture ICC.");
        Check(heif_image_set_raw_color_profile(image.get(), "prof", icc.data(), icc.size()));
    }
    std::unique_ptr<heif_encoding_options, decltype(&heif_encoding_options_free)>
        options(heif_encoding_options_alloc(), heif_encoding_options_free);
    if (!options) throw std::runtime_error("Cannot allocate fixture encoding options.");
    options->image_orientation = static_cast<heif_orientation>(orientation);
    options->output_nclx_profile = color.get();
    options->save_two_colr_boxes_when_ICC_and_nclx_available = 1;
    heif_image_handle* rawHandle = nullptr;
    Check(heif_context_encode_image(context.get(), image.get(), encoder.get(), options.get(), &rawHandle));
    std::unique_ptr<heif_image_handle, decltype(&heif_image_handle_release)> handle(rawHandle, heif_image_handle_release);
    if (variant == "collection" || variant == "thumbnail") {
        heif_image_handle* rawSecond = nullptr;
        if (variant == "thumbnail")
            Check(heif_context_encode_thumbnail(context.get(), image.get(), handle.get(), encoder.get(), options.get(), 32, &rawSecond));
        else Check(heif_context_encode_image(context.get(), image.get(), encoder.get(), options.get(), &rawSecond));
        std::unique_ptr<heif_image_handle, decltype(&heif_image_handle_release)> second(rawSecond, heif_image_handle_release);
    }
    if (metadata) {
        // Authored little-endian TIFF with one Orientation=1 entry.
        const uint8_t exif[] = {'I', 'I', 42, 0, 8, 0, 0, 0, 1, 0,
            0x12, 1, 3, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0};
        const std::string xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
            "<rdf:Description xmlns:dc=\"http://purl.org/dc/elements/1.1/\" dc:description=\"Context Suite generated fixture\"/></rdf:RDF></x:xmpmeta>";
        Check(heif_context_add_exif_metadata(context.get(), handle.get(), exif, sizeof(exif)));
        Check(heif_context_add_XMP_metadata(context.get(), handle.get(), xmp.data(), static_cast<int>(xmp.size())));
    }
    const std::string name = "orientation-" + std::to_string(orientation) + (alpha ? "-alpha" : "") +
        (metadata ? "-metadata" : "") + (colored ? "-color" : "") + (variant.empty() ? "" : "-" + variant);
    Check(heif_context_write_to_file(context.get(), (directory / (name + "." + extension)).string().c_str()));
    std::cout << extension << " " << name << "\n";
}
}

int wmain(int argc, wchar_t** argv) {
    try {
        if (argc < 2 || argc > 4) throw std::runtime_error("Specify a new fixture directory, optional bit depth and profile directory.");
        const int bits = argc >= 3 ? std::stoi(std::wstring(argv[2])) : 8;
        if (bits != 8 && bits != 10 && bits != 12) throw std::runtime_error("Use 8, 10 or 12 bits.");
        const std::filesystem::path directory(argv[1]);
        if (!directory.is_absolute() || std::filesystem::exists(directory))
            throw std::runtime_error("Fixture directory must be new and absolute.");
        Check(heif_init(nullptr));
        if (!heif_have_encoder_for_format(heif_compression_HEVC) || !heif_have_encoder_for_format(heif_compression_AV1))
            throw std::runtime_error("The fixture-only engine must provide both encoders.");
        std::filesystem::create_directory(directory);
        for (auto codec : {heif_compression_HEVC, heif_compression_AV1}) {
            const std::string extension = codec == heif_compression_HEVC ? "heic" : "avif";
            for (int orientation = 1; orientation <= 8; ++orientation)
                Generate(directory, codec, extension, bits, orientation, false, false);
            Generate(directory, codec, extension, bits, 1, true, false);
            Generate(directory, codec, extension, bits, 6, true, true);
            Generate(directory, codec, extension, bits, 1, false, false, true);
            Generate(directory, codec, extension, bits, 6, true, true, true);
            if (argc == 4) {
                const std::filesystem::path profiles(argv[3]);
                if (!profiles.is_absolute()) throw std::runtime_error("Use an absolute profile directory.");
                for (const auto& variant : {"bt709", "p3", "pq", "hlg", "icc-srgb", "icc-adobe", "icc-adobe-bt709", "collection", "thumbnail"})
                    Generate(directory, codec, extension, bits, 1, false, false, true, variant, profiles);
            }
        }
        heif_deinit();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << "\n";
        return 1;
    }
}
