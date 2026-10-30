#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <string>
#include <vector>

namespace pdf_dark_image {

enum class MaskPolicy { Unsupported, PreservePanel, RecolorInk };

// Bounded appearance signals, not semantic image recognition. The caller must
// still validate the occurrence's graphics state and its rendered appearance.
inline MaskPolicy Evaluate(std::string const& pixels, int channels, int width, int height,
    std::string const& mask)
{
    if (width <= 0 || height <= 0 || (channels != 1 && channels != 3)) return MaskPolicy::Unsupported;
    size_t count = static_cast<size_t>(width) * height;
    if (count > 16ull * 1024 * 1024 || mask.size() != count ||
        pixels.size() != count * static_cast<size_t>(channels)) return MaskPolicy::Unsupported;
    auto byte = [](char component) { return static_cast<unsigned char>(component); };
    auto alpha = [&](size_t pixel) { return byte(mask[pixel]); };
    auto color = [&](size_t pixel, size_t component) {
        return byte(pixels[pixel * channels + (channels == 1 ? 0 : component)]);
    };
    auto luminance = [&](size_t pixel) {
        return (.2126 * color(pixel, 0) + .7152 * color(pixel, 1) +
            .0722 * color(pixel, 2)) / 255.0;
    };
    auto atLeast = [](size_t part, size_t whole, size_t percent) {
        return whole && part * 100 >= whole * percent;
    };

    size_t high = 0, middle = 0, brightHigh = 0, darkHigh = 0, protectedDark = 0;
    size_t anyAlpha = 0, support16 = 0, support128 = 0;
    for (size_t pixel = 0; pixel < count; ++pixel) {
        unsigned a = alpha(pixel);
        anyAlpha += a != 0;
        support16 += a >= 16;
        support128 += a >= 128;
        middle += a > 0 && a < 250;
        if (a < 250) continue;
        ++high;
        double light = luminance(pixel);
        brightHigh += light >= .68;
        if (light >= .45) continue;
        ++darkHigh;
        size_t row = pixel / width, column = pixel % width;
        bool touchesContour = false;
        for (size_t y = row > 2 ? row - 2 : 0;
            y <= std::min(row + 2, static_cast<size_t>(height) - 1) && !touchesContour; ++y) {
            for (size_t x = column > 2 ? column - 2 : 0;
                x <= std::min(column + 2, static_cast<size_t>(width) - 1); ++x) {
                if (alpha(y * width + x) < 250) { touchesContour = true; break; }
            }
        }
        protectedDark += !touchesContour;
    }

    if (atLeast(high, count, 85) && middle * 20 <= count &&
        atLeast(brightHigh, high, 80) && atLeast(darkHigh, high, 1) &&
        atLeast(protectedDark, darkHigh, 90)) {
        // With alpha >=250, changing white paper to (18,19,20) changes an
        // interior pixel by at most 5/255 per channel. A substantial edge
        // cannot fade or emerge inside the panel; only its exterior-connected
        // low-alpha silhouette may change that much.
        std::vector<unsigned char> exterior(count);
        std::vector<uint32_t> pending;
        pending.reserve(count - high);
        auto seed = [&](size_t pixel) {
            if (alpha(pixel) < 250 && !exterior[pixel]) {
                exterior[pixel] = 1;
                pending.push_back(static_cast<uint32_t>(pixel));
            }
        };
        for (size_t x = 0; x < static_cast<size_t>(width); ++x) {
            seed(x);
            seed((static_cast<size_t>(height) - 1) * width + x);
        }
        for (size_t y = 0; y < static_cast<size_t>(height); ++y) {
            seed(y * width);
            seed(y * width + width - 1);
        }
        for (size_t index = 0; index < pending.size(); ++index) {
            size_t pixel = pending[index], row = pixel / width, column = pixel % width;
            if (row) seed(pixel - width);
            if (row + 1 < static_cast<size_t>(height)) seed(pixel + width);
            if (column) seed(pixel - 1);
            if (column + 1 < static_cast<size_t>(width)) seed(pixel + 1);
        }
        auto edgeRetained = [&](size_t first, size_t second) {
            constexpr std::array<int, 3> paper{18, 19, 20};
            int before = 0, after = 0;
            for (size_t channel = 0; channel < 3; ++channel) {
                int a = alpha(first), b = alpha(second);
                int p = color(first, channel), q = color(second, channel);
                before = std::max(before, std::abs(a * p + (255 - a) * 255 -
                    b * q - (255 - b) * 255));
                after = std::max(after, std::abs(a * p + (255 - a) * paper[channel] -
                    b * q - (255 - b) * paper[channel]));
            }
            bool faded = before >= 40 * 255 && after * 2 < before;
            bool emerged = after >= 40 * 255 && before * 2 < after;
            return !(faded || emerged) || exterior[first] || exterior[second];
        };
        bool retained = true;
        for (size_t row = 0; row < static_cast<size_t>(height) && retained; ++row) {
            for (size_t column = 0; column < static_cast<size_t>(width); ++column) {
                size_t pixel = row * width + column;
                if ((column + 1 < static_cast<size_t>(width) && !edgeRetained(pixel, pixel + 1)) ||
                    (row + 1 < static_cast<size_t>(height) && !edgeRetained(pixel, pixel + width))) {
                    retained = false;
                    break;
                }
            }
        }
        if (retained) return MaskPolicy::PreservePanel;
    }

    // Sparse strokes need foreground recoloring. A broad translucent gradient
    // or filled cutout cannot satisfy this support and three-step erosion test.
    if (!support16 || !support128 || !high || anyAlpha * 10 > count ||
        support16 * 10 > count || support128 * 20 > count) return MaskPolicy::Unsupported;
    std::vector<unsigned char> current(count), next(count);
    for (size_t pixel = 0; pixel < count; ++pixel) current[pixel] = alpha(pixel) >= 16;
    for (unsigned step = 0; step < 3; ++step) {
        std::fill(next.begin(), next.end(), static_cast<unsigned char>(0));
        for (size_t row = 1; row + 1 < static_cast<size_t>(height); ++row) {
            for (size_t column = 1; column + 1 < static_cast<size_t>(width); ++column) {
                size_t pixel = row * width + column;
                if (!current[pixel]) continue;
                next[pixel] = current[pixel - width - 1] && current[pixel - width] &&
                    current[pixel - width + 1] && current[pixel - 1] &&
                    current[pixel + 1] && current[pixel + width - 1] &&
                    current[pixel + width] && current[pixel + width + 1];
            }
        }
        current.swap(next);
    }
    if (std::any_of(current.begin(), current.end(), [](unsigned char pixel) { return pixel != 0; }))
        return MaskPolicy::Unsupported;

    std::array<size_t, 4096> colorBins{};
    std::array<size_t, 12> hueBins{};
    size_t saturated = 0;
    for (size_t pixel = 0; pixel < count; ++pixel) {
        if (alpha(pixel) < 250) continue;
        unsigned r = color(pixel, 0), g = color(pixel, 1), b = color(pixel, 2);
        ++colorBins[((r / 16) << 8) | ((g / 16) << 4) | (b / 16)];
        unsigned maximum = std::max({r, g, b}), minimum = std::min({r, g, b});
        if (!maximum || (maximum - minimum) * 100 < maximum * 15) continue;
        ++saturated;
        double delta = static_cast<double>(maximum - minimum);
        double sector = maximum == r ? (static_cast<double>(g) - b) / delta :
            maximum == g ? (static_cast<double>(b) - r) / delta + 2 :
            (static_cast<double>(r) - g) / delta + 4;
        if (sector < 0) sector += 6;
        ++hueBins[std::min<size_t>(11, static_cast<size_t>(sector * 2))];
    }
    auto top = [](auto bins, size_t number) {
        std::sort(bins.begin(), bins.end(), std::greater<>{});
        size_t sum = 0;
        for (size_t index = 0; index < number; ++index) sum += bins[index];
        return sum;
    };
    size_t topEight = top(colorBins, 8);
    if (!atLeast(topEight, high, 60) ||
        (saturated && !atLeast(top(hueBins, 3), saturated, 80)))
        return MaskPolicy::Unsupported;

    size_t adjacent = 0, strong = 0;
    for (size_t row = 0; row < static_cast<size_t>(height); ++row) {
        for (size_t column = 0; column < static_cast<size_t>(width); ++column) {
            size_t pixel = row * width + column;
            if (alpha(pixel) < 128) continue;
            auto check = [&](size_t other) {
                if (alpha(other) < 128) return;
                ++adjacent;
                unsigned difference = 0;
                for (size_t channel = 0; channel < 3; ++channel)
                    difference = std::max(difference, static_cast<unsigned>(std::abs(
                        static_cast<int>(color(pixel, channel)) - static_cast<int>(color(other, channel)))));
                strong += difference > 24;
            };
            if (column + 1 < static_cast<size_t>(width)) check(pixel + 1);
            if (row + 1 < static_cast<size_t>(height)) check(pixel + width);
        }
    }
    if (!atLeast(topEight, high, 95) && !atLeast(strong, adjacent, 20))
        return MaskPolicy::Unsupported;
    return MaskPolicy::RecolorInk;
}

} // namespace pdf_dark_image
