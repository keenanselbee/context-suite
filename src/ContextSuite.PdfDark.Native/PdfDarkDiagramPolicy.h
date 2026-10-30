#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace pdf_dark_diagram {

// This is an appearance classifier for light, rasterized diagrams. It does not
// establish that a placement is safe over earlier PDF paint. The caller must
// separately apply the existing transparent-panel placement policy.
inline bool TryRecolor(std::string const& pixels, int channels, int width, int height,
    std::string const& mask, std::string& rgb)
{
    rgb.clear();
    if (width <= 0 || height <= 0 || (channels != 1 && channels != 3)) return false;
    size_t count = static_cast<size_t>(width) * static_cast<size_t>(height);
    if (count > 16ull * 1024 * 1024 || pixels.size() != count * channels ||
        (!mask.empty() && mask.size() != count)) return false;

    auto byte = [](char value) { return static_cast<unsigned char>(value); };
    auto color = [&](size_t pixel, int channel) {
        return byte(pixels[pixel * channels + (channels == 1 ? 0 : channel)]);
    };
    size_t transparent = 0;
    for (size_t pixel = 0; pixel < mask.size(); ++pixel) {
        unsigned alpha = byte(mask[pixel]);
        if (alpha == 255) continue;
        // Only an exact binary edge cutout is eligible. Interior holes and
        // partial alpha require more placement information than this policy has.
        if (alpha != 0 || (pixel / width != 0 && pixel / width + 1 != static_cast<size_t>(height) &&
            pixel % width != 0 && pixel % width + 1 != static_cast<size_t>(width))) return false;
        ++transparent;
    }
    if (transparent * 100 > count * 2) return false;

    std::array<size_t, 4096> bins{};
    std::vector<std::array<uint32_t, 4096>> tileBins(64);
    std::array<size_t, 16> marks{};
    std::array<size_t, 64> tilePixels{};
    std::array<size_t, 64> tileWhite{};
    std::array<size_t, 64> tileDark{};
    size_t white = 0, dark = 0, nonwhite = 0, strongEdges = 0;
    for (size_t row = 0; row < static_cast<size_t>(height); ++row) {
        for (size_t column = 0; column < static_cast<size_t>(width); ++column) {
            size_t pixel = row * width + column;
            if (!mask.empty() && byte(mask[pixel]) == 0) continue;
            unsigned r = color(pixel, 0), g = color(pixel, 1), b = color(pixel, 2);
            unsigned low = std::min({r, g, b}), high = std::max({r, g, b});
            unsigned bin = ((r / 16) << 8) | ((g / 16) << 4) | (b / 16);
            size_t tileY = row * 8 / height, tileX = column * 8 / width;
            size_t tile = tileY * 8 + tileX;
            ++bins[bin];
            ++tileBins[tile][bin];
            ++tilePixels[tile];
            bool isWhite = low >= 240;
            white += isWhite;
            tileWhite[tile] += isWhite;
            nonwhite += !isWhite;
            bool isDark = high <= 135;
            dark += isDark;
            tileDark[tile] += isDark;
            if (isDark || (high - low >= 32 && low < 230))
                ++marks[(row * 4 / height) * 4 + column * 4 / width];
            if (column + 1 < static_cast<size_t>(width) &&
                (mask.empty() || byte(mask[pixel + 1]) == 255)) {
                unsigned rr = color(pixel + 1, 0), gg = color(pixel + 1, 1), bb = color(pixel + 1, 2);
                if (std::min({rr, gg, bb}) < 240 &&
                    std::max({static_cast<unsigned>(std::abs(static_cast<int>(r) - static_cast<int>(rr))),
                        static_cast<unsigned>(std::abs(static_cast<int>(g) - static_cast<int>(gg))),
                        static_cast<unsigned>(std::abs(static_cast<int>(b) - static_cast<int>(bb)))}) >= 40)
                    ++strongEdges;
            }
        }
    }
    size_t visible = count - transparent;
    if (white * 100 < visible * 55 || dark * 1000 < visible * 5 ||
        strongEdges * 100 < nonwhite * 5) return false;
    size_t occupied = 0;
    for (size_t mark : marks) occupied += mark * 500 >= visible;
    if (occupied < 3) return false;

    auto topEight = [](std::array<size_t, 4096> const& values) {
        std::array<size_t, 8> top{};
        for (size_t value : values) {
            if (value <= top[0]) continue;
            top[0] = value;
            for (size_t i = 0; i + 1 < top.size() && top[i] > top[i + 1]; ++i)
                std::swap(top[i], top[i + 1]);
        }
        size_t sum = 0;
        for (size_t value : top) sum += value;
        return sum;
    };
    if (topEight(bins) * 100 < visible * 86) return false;
    // A photograph embedded in paper tends to be locally varied even when
    // the paper makes the global histogram look sparse.
    for (size_t tile = 0; tile < tileBins.size(); ++tile) {
        if (!tilePixels[tile] || tileWhite[tile] * 5 >= tilePixels[tile] * 3) continue;
        std::array<size_t, 4096> local{};
        for (size_t bin = 0; bin < local.size(); ++bin) local[bin] = tileBins[tile][bin];
        if (topEight(local) * 100 < tilePixels[tile] * 65) return false;
    }

    // Broad chromatic modes are panel fills; sparse colored marks are ink.
    // RGB bins are deliberately coarse enough to tolerate JPEG ringing.
    std::array<std::array<unsigned, 3>, 8> fills{};
    std::array<unsigned, 8> fillBins{};
    size_t fillCount = 0;
    for (size_t bin = 0; bin < bins.size() && fillCount < fills.size(); ++bin) {
        if (bins[bin] * 100 < visible) continue;
        unsigned r = ((bin >> 8) & 15) * 16 + 8;
        unsigned g = ((bin >> 4) & 15) * 16 + 8;
        unsigned b = (bin & 15) * 16 + 8;
        if (std::max({r, g, b}) - std::min({r, g, b}) < 30) continue;
        fillBins[fillCount] = static_cast<unsigned>(bin);
        fills[fillCount++] = {r, g, b};
    }

    // Estimate the actual panel color in each spatial tile instead of treating
    // the center of a JPEG palette bin as the background of every box. Fall
    // back to the global mode when the local sample is too small.
    using FillSums = std::array<std::array<uint64_t, 3>, 8>;
    std::array<FillSums, 64> localSums{};
    std::array<std::array<size_t, 8>, 64> localCounts{};
    FillSums globalSums{};
    std::array<size_t, 8> globalCounts{};
    for (size_t pixel = 0; pixel < count && fillCount; ++pixel) {
        if (!mask.empty() && byte(mask[pixel]) != 255) continue;
        unsigned r = color(pixel, 0), g = color(pixel, 1), b = color(pixel, 2);
        unsigned bin = ((r / 16) << 8) | ((g / 16) << 4) | (b / 16);
        size_t tile = ((pixel / width) * 8 / height) * 8 + (pixel % width) * 8 / width;
        for (size_t fill = 0; fill < fillCount; ++fill) {
            if (fillBins[fill] != bin) continue;
            ++localCounts[tile][fill]; ++globalCounts[fill];
            for (size_t c = 0; c < 3; ++c) {
                localSums[tile][fill][c] += color(pixel, static_cast<int>(c));
                globalSums[fill][c] += color(pixel, static_cast<int>(c));
            }
        }
    }
    std::array<std::array<std::array<double, 3>, 8>, 64> localFills{};
    for (size_t tile = 0; tile < 64; ++tile) for (size_t fill = 0; fill < fillCount; ++fill)
        for (size_t c = 0; c < 3; ++c) {
            localFills[tile][fill][c] = localCounts[tile][fill] >= 8 ?
                static_cast<double>(localSums[tile][fill][c]) / localCounts[tile][fill] :
                (globalCounts[fill] ? static_cast<double>(globalSums[fill][c]) / globalCounts[fill] : fills[fill][c]);
        }
    auto strengthenInk = [](double coverage) {
        // Keep paper noise quiet and preserve intermediate antialiasing samples.
        // Boost existing ink coverage without expanding or inventing strokes.
        double blend = std::clamp((coverage - .08) / .22, 0.0, 1.0);
        blend = blend * blend * (3 - 2 * blend);
        return coverage + blend * coverage * (1 - coverage);
    };

    std::string output;
    output.resize(count * 3);
    std::vector<bool> paperSamples(count);
    constexpr std::array<double, 3> paper{18, 19, 20};
    constexpr std::array<double, 3> ink{244, 235, 216};
    for (size_t pixel = 0; pixel < count; ++pixel) {
        size_t tile = ((pixel / width) * 8 / height) * 8 + (pixel % width) * 8 / width;
        unsigned source[3]{color(pixel, 0), color(pixel, 1), color(pixel, 2)};
        unsigned low = std::min({source[0], source[1], source[2]});
        unsigned high = std::max({source[0], source[1], source[2]});
        double target[3]{};
        size_t nearest = fillCount;
        double coverage = 0;
        bool darkInk = false;
        double residual = 1000;
        for (size_t fill = 0; fill < fillCount; ++fill) {
            auto const& base = localFills[tile][fill];
            double dot = 0, length = 0, paperDot = 0, paperLength = 0;
            for (size_t channel = 0; channel < 3; ++channel) {
                dot += source[channel] * base[channel];
                length += base[channel] * base[channel];
                double deviation = 255.0 - base[channel];
                paperDot += (255.0 - source[channel]) * deviation;
                paperLength += deviation * deviation;
            }
            double inkCoverage = std::clamp(dot / length, 0.0, 1.0);
            double fillCoverage = std::clamp(paperDot / paperLength, 0.0, 1.0);
            double inkError = 0, fillError = 0;
            for (size_t channel = 0; channel < 3; ++channel) {
                inkError = std::max(inkError, std::abs(source[channel] - inkCoverage * base[channel]));
                fillError = std::max(fillError, std::abs(source[channel] -
                    (255.0 * (1 - fillCoverage) + base[channel] * fillCoverage)));
            }
            if (inkError < residual) {
                residual = inkError; nearest = fill; coverage = inkCoverage; darkInk = true;
            }
            if (fillError < residual) {
                residual = fillError; nearest = fill; coverage = fillCoverage; darkInk = false;
            }
        }
        if (nearest < fillCount && residual <= 28 &&
            (high - low >= 12 || high <= 150 || !darkInk)) {
            auto const& fill = localFills[tile][nearest];
            double fillMax = static_cast<double>(std::max({fill[0], fill[1], fill[2]}));
            double fillMin = static_cast<double>(std::min({fill[0], fill[1], fill[2]}));
            for (size_t channel = 0; channel < 3; ++channel)
            {
                double darkFill = 15 + fill[channel] * 15.0 / fillMax +
                    (fill[channel] - fillMin) * 30.0 / std::max(1.0, fillMax - fillMin);
                double inkAmount = strengthenInk(1 - coverage);
                target[channel] = darkInk ? ink[channel] * inkAmount + darkFill * (1 - inkAmount) :
                    paper[channel] * (1 - coverage) + darkFill * coverage;
            }
        } else if (high - low < 25) {
            paperSamples[pixel] = true;
            // Neutral raster marks can sit beneath separate PDF labels. Keep
            // their established luminance mapping so the label's local paper
            // contrast is preserved; strengthen ink within colored panels only.
            double darkness = 1.0 - (source[0] + source[1] + source[2]) / (3.0 * 255);
            for (size_t channel = 0; channel < 3; ++channel)
                target[channel] = paper[channel] * (1 - darkness) + ink[channel] * darkness;
        } else {
            paperSamples[pixel] = true;
            double scale = 220.0 / std::max(1u, high);
            for (size_t channel = 0; channel < 3; ++channel)
                target[channel] = std::clamp(source[channel] * scale + 25.0, 0.0, 255.0);
        }
        for (size_t channel = 0; channel < 3; ++channel)
            output[pixel * 3 + channel] = static_cast<char>(std::lround(target[channel]));
    }

    // Sparse colored lettering includes mixtures of its ink and white paper.
    // Scaling every mixture to full brightness produces a pale outline. Find
    // nearby, stronger ink of the same hue and retain the measured coverage.
    // A bounded neighborhood avoids treating a distant panel as the ink source.
    for (size_t row = 0; row < static_cast<size_t>(height); ++row) {
        for (size_t column = 0; column < static_cast<size_t>(width); ++column) {
            size_t pixel = row * width + column;
            if (!mask.empty() && byte(mask[pixel]) != 255) continue;
            std::array<double, 3> source{static_cast<double>(color(pixel, 0)),
                static_cast<double>(color(pixel, 1)), static_cast<double>(color(pixel, 2))};
            double low = std::min({source[0], source[1], source[2]});
            double high = std::max({source[0], source[1], source[2]});
            if (high - low < 8 || high < 150) continue;
            // Only repair the bright sparse-ink branch, never dark panel fills
            // or labels already reconstructed against a colored background.
            if (std::max({byte(output[pixel * 3]), byte(output[pixel * 3 + 1]),
                byte(output[pixel * 3 + 2])}) < 230) continue;
            std::array<double, 3> foreground{};
            std::array<double, 3> background = paper;
            double bestChroma = 0, bestCoverage = 0;
            for (size_t y = row > 3 ? row - 3 : 0;
                y <= std::min(row + 3, static_cast<size_t>(height - 1)); ++y) {
                for (size_t x = column > 3 ? column - 3 : 0;
                    x <= std::min(column + 3, static_cast<size_t>(width - 1)); ++x) {
                    size_t neighbor = y * width + x;
                    if (!mask.empty() && byte(mask[neighbor]) != 255) continue;
                    std::array<double, 3> candidate{static_cast<double>(color(neighbor, 0)),
                        static_cast<double>(color(neighbor, 1)), static_cast<double>(color(neighbor, 2))};
                    double chroma = std::max({candidate[0], candidate[1], candidate[2]}) -
                        std::min({candidate[0], candidate[1], candidate[2]});
                    if (chroma < 50 || chroma <= bestChroma) continue;
                    size_t tile = (row * 8 / height) * 8 + column * 8 / width;
                    double bestError = 13;
                    for (size_t base = 0; base <= fillCount; ++base) {
                        if (base && localCounts[tile][base - 1] < 8) continue;
                        std::array<double, 3> sourceBackground = base ? localFills[tile][base - 1] :
                            std::array<double, 3>{255, 255, 255};
                        double dot = 0, length = 0;
                        for (size_t c = 0; c < 3; ++c) {
                            dot += (sourceBackground[c] - source[c]) * (sourceBackground[c] - candidate[c]);
                            length += (sourceBackground[c] - candidate[c]) * (sourceBackground[c] - candidate[c]);
                        }
                        if (length < 2500) continue;
                        double amount = std::clamp(dot / length, 0.0, 1.0);
                        double error = 0;
                        for (size_t c = 0; c < 3; ++c)
                            error = std::max(error, std::abs(source[c] -
                                (sourceBackground[c] * (1 - amount) + candidate[c] * amount)));
                        if (error > 12 || error >= bestError) continue;
                        bestError = error;
                        foreground = candidate; bestChroma = chroma; bestCoverage = amount;
                        background = paper;
                        if (base) {
                            double maximum = std::max({sourceBackground[0], sourceBackground[1], sourceBackground[2]});
                            double minimum = std::min({sourceBackground[0], sourceBackground[1], sourceBackground[2]});
                            for (size_t c = 0; c < 3; ++c)
                                background[c] = 15 + sourceBackground[c] * 15 / maximum +
                                    (sourceBackground[c] - minimum) * 30 / std::max(1.0, maximum - minimum);
                        }
                    }
                }
            }
            if (bestChroma == 0) continue;
            double scale = 220.0 / std::max({foreground[0], foreground[1], foreground[2]});
            for (size_t c = 0; c < 3; ++c) {
                double target = std::clamp(foreground[c] * scale + 25, 0.0, 255.0);
                output[pixel * 3 + c] = static_cast<char>(std::lround(
                    background[c] * (1 - bestCoverage) + target * bestCoverage));
            }
        }
    }

    // Repair genuine colored antialiasing above before handling weak JPEG
    // chroma. Remaining full-brightness samples near neutral ink must follow
    // a continuous coverage ramp instead of jumping at the palette threshold.
    for (size_t pixel = 0; pixel < count; ++pixel) {
        unsigned source[3]{color(pixel, 0), color(pixel, 1), color(pixel, 2)};
        unsigned span = std::max({source[0], source[1], source[2]}) -
            std::min({source[0], source[1], source[2]});
        if (span < 25 || span >= 64 ||
            std::max({byte(output[pixel * 3]), byte(output[pixel * 3 + 1]),
                byte(output[pixel * 3 + 2])}) < 230) continue;
        double chroma = (span - 25.0) / 39.0;
        double coloredWeight = chroma * chroma * (3 - 2 * chroma);
        double darkness = 1.0 - (source[0] + source[1] + source[2]) / (3.0 * 255);
        for (size_t c = 0; c < 3; ++c) {
            double neutral = paper[c] * (1 - darkness) + ink[c] * darkness;
            output[pixel * 3 + c] = static_cast<char>(std::lround(
                neutral * (1 - coloredWeight) + byte(output[pixel * 3 + c]) * coloredWeight));
        }
    }

    // A chromatic shape with no label or other dark ink can become nearly
    // indistinguishable from dark paper when its pale source fill is remapped.
    // Require a substantial local area and real color edges before refusing;
    // isolated JPEG ringing and colored text do not establish a lost shape.
    std::array<double, 256> linear{};
    for (size_t value = 0; value < linear.size(); ++value) {
        double encoded = value / 255.0;
        linear[value] = encoded <= .04045 ? encoded / 12.92 :
            std::pow((encoded + .055) / 1.055, 2.4);
    }
    auto luminance = [&](size_t pixel) {
        return .2126 * linear[byte(output[pixel * 3])] +
            .7152 * linear[byte(output[pixel * 3 + 1])] +
            .0722 * linear[byte(output[pixel * 3 + 2])];
    };
    double paperLuminance = .2126 * linear[18] + .7152 * linear[19] + .0722 * linear[20];
    std::array<size_t, 64> weakColor{};
    std::array<size_t, 64> weakEdges{};
    // Repeated jumps in almost equal neutral samples indicate amplified JPEG
    // noise, including in exact-masked diagrams. Keep the original resource
    // rather than accepting new speckled edges. Check both pixel directions.
    size_t noiseEdges = 0;
    for (size_t row = 0; row < static_cast<size_t>(height); ++row) {
        for (size_t column = 0; column < static_cast<size_t>(width); ++column) {
            size_t pixel = row * width + column;
            if (!mask.empty() && byte(mask[pixel]) != 255) continue;
            size_t tile = (row * 8 / height) * 8 + column * 8 / width;
            unsigned r = color(pixel, 0), g = color(pixel, 1), b = color(pixel, 2);
            if (paperSamples[pixel] &&
                std::max({r, g, b}) - std::min({r, g, b}) <= 35 &&
                r + g + b >= 105 && r + g + b <= 690) {
                for (size_t next : {column + 1 < static_cast<size_t>(width) ? pixel + 1 : pixel,
                    row + 1 < static_cast<size_t>(height) ? pixel + width : pixel}) {
                    if (next == pixel || !paperSamples[next] ||
                        (!mask.empty() && byte(mask[next]) != 255)) continue;
                    unsigned low = 255, high = 0, sum = 0, sourceDelta = 0, outputDelta = 0;
                    for (int c = 0; c < 3; ++c) {
                        unsigned value = color(next, c);
                        sum += value;
                        low = std::min(low, value); high = std::max(high, value);
                        sourceDelta = std::max(sourceDelta, static_cast<unsigned>(std::abs(
                            static_cast<int>(color(pixel, c)) - static_cast<int>(value))));
                        outputDelta = std::max(outputDelta, static_cast<unsigned>(std::abs(
                            static_cast<int>(byte(output[pixel * 3 + c])) -
                            static_cast<int>(byte(output[next * 3 + c])))));
                    }
                    if (high - low <= 35 && sum >= 105 && sum <= 690 &&
                        sourceDelta <= 5 && outputDelta > 80 && ++noiseEdges >= 16) return false;
                }
            }
            bool colored = std::max({r, g, b}) - std::min({r, g, b}) >= 30;
            double first = luminance(pixel);
            if (colored && (std::max(first, paperLuminance) + .05) /
                (std::min(first, paperLuminance) + .05) < 1.6) ++weakColor[tile];
            if (column + 1 == static_cast<size_t>(width) ||
                (!mask.empty() && byte(mask[pixel + 1]) != 255)) continue;
            unsigned rr = color(pixel + 1, 0), gg = color(pixel + 1, 1), bb = color(pixel + 1, 2);
            bool otherColored = std::max({rr, gg, bb}) - std::min({rr, gg, bb}) >= 30;
            if (!colored && !otherColored) continue;
            if (std::max({static_cast<unsigned>(std::abs(static_cast<int>(r) - static_cast<int>(rr))),
                static_cast<unsigned>(std::abs(static_cast<int>(g) - static_cast<int>(gg))),
                static_cast<unsigned>(std::abs(static_cast<int>(b) - static_cast<int>(bb)))}) < 20) continue;
            double second = luminance(pixel + 1);
            if ((std::max(first, second) + .05) / (std::min(first, second) + .05) < 1.6)
                ++weakEdges[tile];
        }
    }
    for (size_t tile = 0; tile < tilePixels.size(); ++tile)
        if (weakColor[tile] * 10 >= tilePixels[tile] &&
            tileDark[tile] * 200 < tilePixels[tile] && weakEdges[tile] >= 24)
            return false;
    rgb = std::move(output);
    return true;
}

} // namespace pdf_dark_diagram
