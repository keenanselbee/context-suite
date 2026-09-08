PNG Palette Comparison: Dark Gradient Regression
================================================

Status: research only, 2026-09-08. Shipping presets are unchanged.

The user's dark space/monitor image exposes conspicuous contouring in the
production RGB6 result despite low numerical sample error. Earlier synthetic and
screenshot acceptance did not adequately cover this case. Reopen palette research
before treating the current lossy presets as visually robust.

Scope And Reproduction
----------------------

The user supplied an original 2560x1440 RGBA PNG and a reference optimizer output.
Only copies beneath `.codex-temp/png-quantization` are processed; neither original
nor reference executable is modified or executed. These images are not repository
fixtures and no redistribution permission is inferred.

The dedicated mode accepts copied `source.png` and `reference.png`. It uses the
existing pinned Magick.NET/curated native engine and oxipng, with no new dependency.
It admits bounded static 8-bit RGB/RGBA sources with a small understood metadata
set, not arbitrary screenshots or unknown `fdEC` chunks.

```powershell
./tools/Test-PngQuantization.ps1 -PaletteComparison -Corpus '.codex-temp/png-quantization/orbit-fae185e9cf9d44c39e0a86419b379658'
./tools/Test-PngQuantization.ps1 -PaletteComparison -Refine -Corpus '.codex-temp/png-quantization/orbit-fae185e9cf9d44c39e0a86419b379658'
```

The broad run tests RGB7/RGB6 and 128/192/256-colour quantization with no dither,
50% Floyd-Steinberg diffusion and Riemersma. The refinement tests 224/240/256 colours
with 10% and 25% Floyd-Steinberg diffusion. Every candidate starts from the original.
Source metadata is restored byte-for-byte after encoding; alpha and hidden RGB are
restored exactly before encoding. Indexed storage is used only when the resulting
RGBA tuples fit 256 entries; otherwise the candidate remains truecolour RGBA.
This avoids changing alpha simply to fit an indexed palette.

Evidence
--------

All 17 experimental outputs passed decoded fidelity to their quantized buffers,
exact alpha/hidden-RGB and retained-metadata checks. Copied sources remained exact.
This is preservation evidence, not a passed perceptual quality screen.

- Broad gallery and JSON: `.codex-temp/png-quantization/palette-comparison-31048abc84284f31af3bb7af6a1f1777/`.
- Refined gallery and JSON: `.codex-temp/png-quantization/palette-comparison-b6ccd45884c7440d9d2788f0a5c79a83/`.
- Original SHA256: `A49871865B7061DF4868DF3BEF2E81EA47E6F9C52EB7486EB55C9A0B78409395`.
- Reference SHA256: `3B9DA67DCC42AC659250513731819C5DD70A96030656AB469389E7713D6C8C79`.

Selected results, all at full original dimensions:

| Result | Bytes | Composite RMSE | Mean SSIM | Alpha maximum error |
| --- | ---: | ---: | ---: | ---: |
| User-provided reference | 1,131,347 | 2.105 | 0.99181 | 89 |
| RGB6 research baseline | 1,800,186 | 1.238 | 0.97682 | 0 |
| 256 colours, FS10 | 1,075,397 | 2.608 | 0.98303 | 0 |
| 256 colours, FS25 | 1,223,669 | 2.621 | 0.98226 | 0 |
| 256 colours, FS50 | 1,370,023 | 2.692 | 0.97681 | 0 |
| 224 colours, FS10 | 573,181 | 3.439 | 0.94133 | 0 |

The metadata-preserving RGB6 research encoding is 18 bytes larger than the supplied
production RGB6 file; do not confuse it with a byte-identical production rerun.
The reference changes alpha at some pixels and omits EXIF/text/resolution metadata;
its visual/size result is a target for comparison, not approval of those changes.
Reference composite metrics differ from raw RGB-only metrics because they include
black/white backgrounds and changed transparency.

Interpretation And Next Work
----------------------------

The 256-colour FS10 result is about 5% smaller than the reference; FS25 is about
8% larger. FS25 reduces some contours but still has visible texture/contouring;
size parity is not visual parity. Very small 128/192/224-colour results have
unacceptable visible degradation and must not be selected solely for size.
All experimental palette candidates exceed the existing Smallest worst-tile RMSE
ceiling of 12 on this image, as does the reference. No threshold was weakened and
no candidate has been promoted to production.

Next, improve palette allocation and gradient-sensitive candidate rejection, then
review native-scale crops and repeat on held-out dark gradients, colourful detail,
text and transparency fixtures. Do not claim a broadly better preset from this
single-image exploration or a numerical score alone. Keep lossless fallback and
the existing source/publication safeguards while the lossy strategy is reconsidered.

Palette And Gradient Follow-up
-----------------------------

Research command (copied corpus only):

```powershell
.\tools\Test-PngQuantization.ps1 -PaletteComparison -Gradients -Corpus <copied-corpus-directory>
```

This compares sRGB and Lab quantization at 256/512 colours with 25% Floyd-Steinberg
diffusion, plus bounded dark-gradient protection. Follow-up candidates compare
512 colours without dithering, 512 colours with 10% diffusion, and 1,024 colours
with 10% diffusion. More than 256 RGBA colours use ordinary truecolour PNG storage;
this does not invent a nonstandard indexed PNG or relax transparency preservation.

The protection experiment identifies opaque 5x5 neighbourhoods whose per-channel
range is at most six and maximum value at most 64, using original samples only.
Within that mask it limits each RGB channel's change to one byte value. Alpha and
unmasked pixels are untouched by protection. These are experimental parameters,
not approved preset thresholds; they do not cover bright or transparent gradients.

Reports now include a source-masked strong-edge diagnostic: the percentage of
horizontal/vertical adjacent pairs that acquire an RGB step of at least three
where the original step was at most one. This flags both false contours and dither
noise, not banding alone. Zero eligible pairs is no evidence of good gradients.
It is not used as a calibrated production acceptance score. Authored ramp checks
verify exact-match identity, detection of RGB6 steps, protection bounds, unchanged
unmasked samples and exclusion of transparency.

Lab quantization must explicitly transform back to sRGB before extracting samples.
Visual inspection caught that missing transform in the first development run;
`palette-comparison-afd5af3c0d9047378e11d70aabc2b7f7` is invalid colour-quality
evidence and has a scratch warning. New flat-colour roundtrip contracts cover both
working spaces. Corrected six-candidate run:
`palette-comparison-f034e499c5a94e2fb29b688a0b1721be`.

Initial corrected findings: sRGB 512/FS25 is 1,351,838 bytes, with worst-tile RMSE
10.751 and mean SSIM 0.98402. Unlike the previous 256-colour candidates it meets
the existing Smallest numerical screens on this image. Its source-masked new-edge
rate is nevertheless 22.200%, versus the reference's 9.465%; screen passage is not
visual equivalence. Protecting sRGB 256/FS25 reduces that rate from 22.336% to
10.602%, but increases size from 1,223,669 to 2,030,018 bytes and does not fix its
worst tile outside the mask. Lab is not a better default in this experiment.
All six corrected candidates passed exact decoded-sample, alpha, hidden-RGB and
retained-metadata checks. No production algorithm or quality threshold changed.

Completed nine-candidate run:
`palette-comparison-ded9dc0388c54317ac0f44e576475004` (HTML and JSON retained under
`.codex-temp/png-quantization`). All nine preservation checks passed. The six
repeated candidates were byte-identical to the corrected prior run. This is
repeatability on the same machine, not cross-version determinism or corpus coverage.

| Candidate | Bytes | Worst tile RMSE | Mean SSIM | New strong edges |
| --- | ---: | ---: | ---: | ---: |
| Reference | 1,131,347 | 15.662 | 0.99181 | 9.465% |
| sRGB 512, no dither | 1,617,113 | 13.256 | 0.99278 | 13.619% |
| sRGB 512, FS10 | 1,196,489 | 10.686 | 0.98457 | 12.077% |
| sRGB 512, FS25 | 1,351,838 | 10.751 | 0.98402 | 22.200% |
| sRGB 1,024, FS10 | 1,694,689 | 10.263 | 0.97811 | 13.780% |

Recommended next research candidate: **sRGB 512/FS10**, approximately 6% larger
than the reference and 34% smaller than the supplied RGB6 production output.
Its composite RMSE is 2.204, meeting the existing Smallest screens together with
the tile/SSIM values above. It improves the measured noise/step rate versus FS25,
but visible contouring remains; the diagnostic does not prove reference-quality
gradients. Keep FS25 available for side-by-side human review. The 1,024-colour
and broad protection approaches do not justify their size cost here.

Next validation must use held-out images and native-scale visual review, including
bright/dark ramps, text, colourful detail, translucent gradients and metadata.
Investigate image-adaptive palette allocation and more selective protection if
those comparisons still show contours. Do not add this single-image result as a
shipping preset or weaken production quality limits without that evidence.

Preset Candidate Validation
---------------------------

The user approved keeping Auto/Lossless/Balanced/Smallest as quality policies,
with bounded image-specific candidates and lossless fallback, not user-facing
colour-count/dither controls. The production algorithms remain unchanged.

```powershell
.\tools\Test-PngQuantization.ps1 -PresetResearch
.\tools\Test-PngQuantization.ps1 -PresetResearch -Corpus <copied-screenshot-directory>
```

This focused matrix compares RGB7 as the existing baseline against sRGB 512/FS10,
512/FS25 and 1,024/FS10. It requires exact encoding, alpha and hidden RGB, fixture
metadata preservation and a result smaller than both source and lossless baseline.
Auto uses the existing Balanced screen plus at least 5% additional savings.
The report includes Auto/Balanced/Smallest numerical selections and identifies
when no smaller lossy candidate is acceptable. That means lossless fallback if
smaller, otherwise No smaller result, not a reason to weaken quality limits.

Synthetic run `evaluation-c2706d1ed26a4c5887e7e7ad57a642db` completed **60 experiments**
across 15 fixtures: icons, indexed/already-optimized icons, text, bright and dark
gray/colour gradients, glow/soft-edge/hidden/translucent alpha, texture, noise and
tiny images. Preservation, metric self-tests, independent fixture decode and
cleanup invariants passed, with zero encoding-fidelity rejections.

| Candidate | Balanced screen | Smallest screen |
| --- | ---: | ---: |
| RGB7 baseline | 9/15 | 9/15 |
| sRGB 512/FS10 | 3/15 | 6/15 |
| sRGB 512/FS25 | 2/15 | 3/15 |
| sRGB 1,024/FS10 | 4/15 | 6/15 |

These counts combine quality and size: failure to beat an already-small lossless
baseline is not visual damage. Conversely, a numerical pass does not establish
freedom from contours. Synthetic scenes are not photographic evidence. The new
512/FS10 method must not replace every preset: it sometimes loses to lossless or
the existing gentler candidate, and does not meet every fixture's quality screen.
Research output and isolated hosts are retained under `.codex-temp/png-quantization`.

**Visual rejection:** native-size inspection of `dark-color-gradient` against
`dark-color-gradient-sRGB-512-fs10` in that synthetic report shows obvious blocky
colour plateaus, even though this candidate passes the numerical Balanced/Auto
screens. Treat this pair as a required rejection example when developing the
gradient gate. Existing RMSE/SSIM screens alone are insufficient to approve
automatic palette selection. A low strong-edge diagnostic is also not proof of
smooth shading, since coherent small steps can form visible broad contours.
Do not promote 512/FS10 into Auto or Balanced based on numerical pass counts;
Smallest also requires visual acceptance rather than assuming its floor is enough.

Additional full-resolution screenshot run
`evaluation-a441b478b24040d18ee32886e7df87c6` exceeded the runner's five-minute
deadline and was stopped. Its partial files are diagnostic, **not a completed
three-image acceptance report**. The run compares four candidates per screenshot
and writes full-size previews, so this timeout is not a measured single-click
production latency. Future corpus work should be sharded or make preview generation
optional without weakening execution limits. A viewed first-screenshot FS10 crop
also shows blockiness in smooth blue shadows; this reinforces the gradient concern.

Inputs were additional copies of existing user-authorized scratch screenshots
(Fall of Avalon, Skyrim and Elden Ring). The existing research loader removes
unknown fdEC from in-memory research inputs only; copied files remain untouched.
This run does not establish production support for fdEC or permission to strip
customer metadata. Do not infer completed checks or preset pass counts from its
partial outputs. The completed synthetic matrix above remains the verification
evidence for this change; no production or installed-app changes were made.

Gradient Veto And Bounded Runner
-------------------------------

Research now has an **additional plateau veto**, separate from the earlier
single-pixel strong-edge diagnostic. It samples native-resolution 32-pixel
horizontal and vertical runs on an 8-pixel grid, independently for RGB channels
over black and white composites. The source must be monotonic, with adjacent
steps no larger than 1.01 and a difference of 2–24 between its first/last four
sample means. A run is flattened when the candidate retains at most 25% of that
signed progression (including reversal). At least 32 eligible channel/background
runs and 5% flattened runs trigger rejection for every research preset.

These are provisional engineering thresholds, not a perceptual guarantee. They
detect broad small-step plateaus that the earlier edge diagnostic missed, without
hardcoding the user's image coordinates. They do not cover every scale, direction,
textured gradient or spatially localized artifact. Zero eligible runs is **no
evidence**, not a quality pass. False-positive calibration and broader human review
remain necessary before production integration. Existing RMSE/SSIM/alpha/metadata
limits were not weakened; a rejected candidate cannot appear in the research
Auto/Balanced/Smallest shortlist.

Authored controls cover unchanged gradients, horizontal/vertical and descending
plateaus, translucency, mild zero-mean dithering, opaque flat areas, hidden RGB and
tiny images. The known `dark-color-gradient` / `sRGB-512-fs10` visual failure is a
mandatory research regression: it must now be rejected.

The runner supports exact fixture/candidate filters, optional preview generation,
live line-by-line progress, and atomic per-candidate `progress.json` checkpoints.
Checkpoints stay `Complete=false` until the selected run finishes. Partial counts
are never full-corpus acceptance. Final reports record filters, preview mode,
elapsed time, candidate/source identities and the new gate measurements. The
five-minute limit remains; errors clean up only the owned evaluation process.
No-preview reports retain full input/output PNG links without broken preview links.

```powershell
# Fast complete synthetic matrix; no redundant composite/crop previews.
.\tools\Test-PngQuantization.ps1 -PresetResearch -NoPreviews
# Reproduce the known rejection, with visual previews.
.\tools\Test-PngQuantization.ps1 -PresetResearch -Fixture dark-color-gradient -Candidate sRGB-512-fs10
# One full-resolution screenshot/candidate per bounded invocation.
.\tools\Test-PngQuantization.ps1 -PresetResearch -NoPreviews -Corpus <copied-corpus> -Fixture screenshot-1 -Candidate sRGB-512-fs10
```

Screenshot numbers are stable positions in the ordinally sorted copied corpus;
filtering skips other files before decode. Repeat with `screenshot-2` and
`screenshot-3`. Omit `-Candidate` to compare the four research candidates on one
fixture; omit `-NoPreviews` when visual inspection needs composite/crop images.
Preview/filter switches are rejected outside `-PresetResearch`, and unmatched
filters fail instead of reporting an empty successful run.

Verification evidence (under `.codex-temp/png-quantization`):

- `evaluation-6b442ad93bb34383ac7a965021fc2ff2`: all **60** synthetic experiments
  completed in approximately **19.5 seconds** without previews. All preservation
  and encoding assertions passed. Six dark-gradient palette cases triggered the
  additional veto. All 60 output hashes match the preceding pre-veto experiment:
  this change improves rejection, not the underlying encoding.
- Known blocky dark-colour FS10 output: **37.69%** flattened eligible runs, now
  rejected from every research preset. The old numerical screen admitted it.
- `evaluation-5933eda42afb4119b99a708b06d98b34` and
  `evaluation-5f40db2c33a64144aef13c76581f2c90`: focused no-preview/preview runs;
  identical candidate bytes, complete checkpoint counts and valid local HTML links.
- `evaluation-27c9b4eb8ed3452c9d262e0b7924cac0`,
  `evaluation-6da7d6f3c06848ba8ee5d41c78aeffa4` and
  `evaluation-f38628c98a0a4ffaa38797c1e960556c`: screenshot 1/2/3 with 512/FS10
  completed in **16.5 / 15.1 / 16.5 seconds** respectively. All preservation checks
  passed; the plateau veto rejected all three candidates. These three completed
  cases do not retroactively complete the prior timed-out twelve-case matrix.

Timings exclude build/staging and are not production click-to-result benchmarks.
The screenshot research loader's fdEC normalization caveat above still applies.
No production candidate integration, quality-policy version change, metadata
permission change or installed-app update was made. Next work is visual
calibration of this veto and better palette allocation, not admitting blocked
candidates by lowering its threshold merely to recover savings.
