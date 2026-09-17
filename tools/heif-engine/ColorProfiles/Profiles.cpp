#include <lcms2.h>
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>

// Offline, authored profile generation. No additional native library ships.
int wmain(int argc, wchar_t** argv) {
    if (argc == 4 && std::wstring(argv[1]) == L"--compare") {
        auto source = cmsOpenProfileFromFile(std::filesystem::path(argv[2]).string().c_str(), "r");
        auto target = cmsOpenProfileFromFile(std::filesystem::path(argv[3]).string().c_str(), "r");
        if (!source || !target) return 2;
        for (cmsUInt32Number flags : {cmsUInt32Number(cmsFLAGS_HIGHRESPRECALC), cmsUInt32Number(cmsFLAGS_NOOPTIMIZE)}) {
            auto transform = cmsCreateTransform(source, TYPE_RGB_DBL, target, TYPE_RGB_DBL, INTENT_RELATIVE_COLORIMETRIC, flags);
            if (!transform) return 3;
            const double input[]{40.0 / 255, 95.0 / 255, 19.0 / 255};
            double output[3]{};
            cmsDoTransform(transform, input, output, 1);
            std::cout << flags << ": " << output[0] * 255 << ", " << output[1] * 255 << ", " << output[2] * 255 << "\n";
            cmsDeleteTransform(transform);
        }
        cmsCloseProfile(source); cmsCloseProfile(target);
        return 0;
    }
    if (argc != 2 || !std::filesystem::path(argv[1]).is_absolute() || std::filesystem::exists(argv[1])) return 1;
    std::filesystem::create_directory(argv[1]);
    const cmsCIExyY white{0.3127, 0.3290, 1.0};
    for (bool p3 : {false, true}) {
        const cmsCIExyYTRIPLE primaries = p3 ? cmsCIExyYTRIPLE{{0.68, 0.32, 1}, {0.265, 0.69, 1}, {0.15, 0.06, 1}} :
            cmsCIExyYTRIPLE{{0.64, 0.33, 1}, {0.30, 0.60, 1}, {0.15, 0.06, 1}};
        const double srgb[]{2.4, 1.0 / 1.055, 0.055 / 1.055, 1.0 / 12.92, 0.04045};
        const double bt709[]{1.0 / 0.45, 1.0 / 1.099, 0.099 / 1.099, 1.0 / 4.5, 0.081};
        auto curve = cmsBuildParametricToneCurve(nullptr, 4, p3 ? srgb : bt709);
        if (!curve) throw std::runtime_error("Cannot create profile transfer curve.");
        cmsToneCurve* curves[]{curve, curve, curve};
        auto profile = cmsCreateRGBProfile(&white, &primaries, curves);
        cmsFreeToneCurve(curve);
        if (!profile) throw std::runtime_error("Cannot create RGB profile.");
        auto description = cmsMLUalloc(nullptr, 1);
        if (!description || !cmsMLUsetASCII(description, "en", "US", p3 ? "Context Suite Display P3" : "Context Suite BT.709") ||
            !cmsWriteTag(profile, cmsSigProfileDescriptionTag, description)) throw std::runtime_error("Cannot describe profile.");
        cmsMLUfree(description);
        cmsUInt32Number size = 0;
        if (!cmsSaveProfileToMem(profile, nullptr, &size)) throw std::runtime_error("Cannot size profile.");
        std::vector<unsigned char> bytes(size);
        if (!cmsSaveProfileToMem(profile, bytes.data(), &size)) throw std::runtime_error("Cannot serialize profile.");
        cmsCloseProfile(profile);
        // Stable creation time and absent optional profile ID make repeat builds identical.
        const unsigned char date[]{0x07, 0xea, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0};
        std::copy(std::begin(date), std::end(date), bytes.begin() + 24);
        std::fill(bytes.begin() + 84, bytes.begin() + 100, 0);
        std::ofstream output(std::filesystem::path(argv[1]) / (p3 ? "DisplayP3.icc" : "Bt709.icc"), std::ios::binary);
        output.write(reinterpret_cast<const char*>(bytes.data()), bytes.size());
        output.close();
        if (!output) throw std::runtime_error("Cannot write profile.");
    }
}
