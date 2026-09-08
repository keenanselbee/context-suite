PNG Quantization Evaluation
===========================

Status: initial bounded synthetic evaluation completed locally on September 8, 2026;
production lossy presets remain unimplemented. This evaluates the existing curated ImageMagick engine without
adding pngquant, libimagequant, paid dependencies or new product capabilities.
The expanded precision/alpha/dithering experiment is documented separately in
[phase two](png-quantization-improvements.md); the results below retain the
initial nine-candidate baseline rather than claiming the expanded matrix is identical.


Purpose And Boundaries
----------------------

Determine whether ImageMagick palette reduction plus the existing pinned oxipng
cleanup can support useful Balanced and Smallest PNG policies. Engine-specific
pngquant quality ranges are not transferable to ImageMagick. Fixed palette sizes
alone do not establish visual quality.

This is a private test-harness mode, not a second application edition or an
activation bypass. No application, trial/settings, Explorer registration,
installer or publication code changes. The runner accepts no customer input
paths and generates all media under a unique repository-local scratch directory.
Unacceptable outputs are retained only as clearly labelled research comparisons.

Use the existing curated native ImageMagick build; do not activate external
delegates or introduce a stock native fallback. Commercial redistribution still
requires the existing exact-payload review and notices. No new fee-bearing engine
is selected by this evaluation.


Reproduction
------------

From the public repository root, with the compatible private checkout and
already-staged curated, DDS and PNG engines:

```powershell
./tools/Test-PngQuantization.ps1 -Configuration Release
```

The script builds the existing real-image contract project, composes a unique
root-file-only test host (excluding NuGet runtime subfolders, as production does),
checks the curated engine and PNG engine identities/notices, and runs the bounded
evaluation with a five-minute process deadline. Native resource policy and the
existing owned oxipng subprocess deadline/job remain active. It does not download
media, install packages or automate the desktop. Isolated hosts and reports are
retained under `.codex-temp/png-quantization/` for reproduction and inspection.

Each report directory contains:

- `index.html`: offline original/candidate comparisons over black and white,
  with enlarged inspection and screening results; no remote resources or scripts.
- `results.json`: source/output hashes, engine/harness identities, exact metrics,
  candidate timings, palette counts, and whole-process peak working set.
- Original, lossless, quantized and opaque-background comparison PNG files.

Timing includes native quantization, encoding, supported metadata restoration
and oxipng; it excludes metrics, gallery rendering and final decode validation.
Peak working set is for the entire evaluation process, not each candidate, and
does not include the oxipng child's memory. These measurements are diagnostic,
not production latency or memory guarantees.


Corpus And Candidate Matrix
---------------------------

Twelve deterministic authored fixtures cover flat icons, independently encoded
indexed icons, small synthetic text, gray/color gradients, glow transparency,
soft transparent edges, hidden RGB, a textured terrain/sky proxy, seeded noise,
a tiny image and an already losslessly optimized indexed image. No system fonts,
third-party assets or reference binaries are used. The terrain/sky image is
synthetic, not photographic validation. Input PNGs are compressed, not deliberately
stored uncompressed; every candidate is also compared against an oxipng baseline.

The 108-case matrix crosses 256/128/64 requested palette entries with no dithering,
Floyd-Steinberg and Riemersma. Quantization uses sRGB and automatic tree depth;
an entirely opaque alpha channel is disabled before quantization without changing
decoded RGBA values. Actual transparency is never disabled. The PNG writer selects
the encoded color type; zero recorded palette entries means non-indexed output.
Production remains Q16 internally; 8-bit PNG
rounding may differ by at most one code value from exported quantizer samples.
Larger differences reject a candidate and remain explicit diagnostic results,
not a successful encoding. Cleanup must preserve encoded samples exactly.

Fixture metadata retention covers only known invariant sRGB, gAMA, pHYs and tEXt
chunks. The evaluation explicitly restores and verifies these bytes, never old
PLTE/tRNS or arbitrary chunks. This is not a general metadata-preservation service.
ICC/EXIF/XMP, other color spaces, 16-bit sources, animation, HDR and arbitrary
inputs remain outside this harness's admission scope.


Measurements And Provisional Screens
------------------------------------

Measure decoded RGBA at full resolution. Report:

- Worst black/white-composited RGB RMSE in display-referred sRGB code values
  (0 to 255), plus maximum non-overlapping 8x8 tile RMSE.
- Lower black/white mean tile luminance SSIM, using 8x8 tiles, population moments,
  luminance weights 0.2126/0.7152/0.0722 and constants 6.5025/58.5225.
  This precisely defined screening metric is not interchangeable with every
  library's SSIM implementation.
- Mean/maximum alpha error, changed RGBA pixels and hidden-RGB changes separately.
- Output size against both original and lossless baseline, elapsed time and
  whether required fixture metadata survived.

Initial experimental screens (not approved shipping thresholds):

| Screen | Composite RMSE maximum | Worst tile maximum | Mean SSIM minimum |
| --- | --- | --- | --- |
| Balanced | 2 | 6 | 0.98 |
| Smallest | 4 | 12 | 0.95 |

Both require encoding fidelity within one 8-bit rounding unit, exact alpha,
retained fixture metadata and an output strictly
smaller than both the original and lossless baseline. Failed screens remain
visible in the gallery, never become production output, and do not trigger a
weaker retry. Black/white composites do not fully evaluate colored backgrounds,
linear-light compositing or all color-managed viewers.

Self-tests cover identical/extreme/hidden-RGB/alpha metric cases, removed/restored
metadata and malformed PNG signature/length/CRC. Each candidate asserts decoded
structure, palette bounds, supported metadata and no additional sample loss
through oxipng; encoding-fidelity failures are recorded and excluded from both
screens. Original hashes must remain unchanged. A successful harness exit does
not mean all candidate encodings or quality screens passed.


Findings And Recommendation
---------------------------

The completed automatic-color-type/opaque-alpha-normalization run evaluated all
108 candidates. No final candidate exceeded the one-code-value encoding-fidelity
limit. Quantization itself still introduced visible error or alpha changes in
many cases; successful execution is not successful quality acceptance.

| Candidate | Balanced screen | Smallest screen |
| --- | --- | --- |
| 256 / None | 5 of 12 | 6 of 12 |
| 256 / Floyd-Steinberg | 3 of 12 | 3 of 12 |
| 256 / Riemersma | 3 of 12 | 4 of 12 |
| 128 / None | 3 of 12 | 6 of 12 |
| 128 / Floyd-Steinberg | 3 of 12 | 3 of 12 |
| 128 / Riemersma | 3 of 12 | 5 of 12 |
| 64 / None | 3 of 12 | 5 of 12 |
| 64 / Floyd-Steinberg | 3 of 12 | 3 of 12 |
| 64 / Riemersma | 3 of 12 | 3 of 12 |

Three fixtures are related icon variants, so these counts are not a representative
market-wide success rate. Four of the five 256/None Balanced matches have exact
decoded pixels; the small-text fixture is the only lossy Balanced match. A later
representation-reducing lossless policy could capture those exact-pixel savings,
but changing today's strict representation-preserving lossless policy is outside
this evaluation.

Examples for 256/None (bytes, not estimates):

| Fixture | Original | Lossless baseline | Candidate | Outcome |
| --- | --- | --- | --- | --- |
| Flat icons | 3,934 | 1,989 | 698 | Exact pixels; both screens pass |
| Small synthetic text | 9,210 | 8,693 | 2,797 | Balanced screen passes; RMSE 1.11 |
| Gray gradient | 5,127 | 1,531 | 1,015 | Exact pixels; both screens pass |
| Color gradient | 182,970 | 6,956 | 6,840 | Reject: banding; RMSE 4.93, worst tile 12.72 |
| Soft alpha edge | 18,277 | 6,236 | 4,639 | Reject: alpha error up to 19/255 |
| Synthetic textured scene | 216,474 | 143,565 | 48,399 | Smallest screen only; RMSE 2.90 |
| Tiny image | 210 | 209 | 261 | Reject: larger output despite exact pixels |

The gradient illustrates why the lossless comparator matters: large apparent
savings against the original were almost entirely available without quantization.
The no-dither color-gradient output visibly bands in local image inspection.
Soft alpha edges show palette steps; opaque grayscale renders correctly in the
final run. This is targeted agent image inspection, not user acceptance of the
entire gallery. Dithering can reduce perceived banding while worsening these
error scores and increasing file size; do not conclude that no dithering is
universally visually superior from this matrix alone.

The 128/None textured-scene candidate passes the numerical Smallest screen but
still shows visible bands in its synthetic sky. This is direct evidence that
these initial thresholds are insufficient for general visual acceptance, not a
reason to label that result near-lossless.

Recommend 256/None as the first **Balanced research candidate**, and 128/None
as the first **Smallest research candidate**, keeping 256/Riemersma for visual
comparison. Do not promote 64 colors or automatic content detection yet. Keep
all shipping lossy presets disabled until representative real-image review and
production quality/admission checks exist. Initially limit that integration to
opaque, supported sRGB 8-bit static PNGs; general alpha support needs its own
policy and evidence. Preserve the existing lossless route for everything it
already supports.

Early exploratory runs exposed severe grayscale encoding errors with an opaque
alpha channel retained, and forcing indexed PNG/PaletteAlpha worsened transparent
cases in this local engine path. A separate Windows System.Drawing decode of an
early grayscale output confirmed damaged samples, not just a metric discrepancy.
The final harness disables only fully opaque alpha before quantization, permits
automatic PNG color type, and checks exported quantizer samples against decoded
output. Do not carry the rejected forced-type settings into production or infer
an upstream root cause without a separate minimal reproduction. Early reports
are retained as diagnostics, not current acceptance evidence.

Existing image/adapter regression tests were rerun: 457 passed. Repository link,
whitespace, source-boundary and theme checks passed. No production behavior was
changed and no installer, hosted CI, customer files or interactive desktop was
used. Exact final report locations and repeatability evidence are recorded below.

Final report: `.codex-temp/png-quantization/evaluation-748dabd64bdb48e38fcafecab7be1037/index.html`.
The prior equivalent pixel-policy run is
`.codex-temp/png-quantization/evaluation-9692bbdaa94f41f7b2d33a7d0c49a2da/index.html`.
All twelve source/lossless hashes and all 108 output hashes, metrics and screens
matched across these two runs; timings and the harness hash differ because the
final report adds explicit policy metadata and clearer screen wording. All 1,092
local gallery references resolved. PNGs were inspected directly; browser layout
and user visual acceptance remain unverified.

Final diagnostic timings ranged from 31 to 707 ms per candidate (mean 198 ms)
on this machine for this small synthetic corpus. Whole-process peak working set
was 684,998,656 bytes, including managed fixture/metric/report allocations but
excluding the oxipng child. Do not extrapolate this to large production batches;
memory and time acceptance still need representative-size tests.


Next Acceptance Gate
--------------------

Use results to recommend candidates, not to claim universal near-lossless quality.
Before shipping lossy policies:

1. Add explicitly licensed or user-owned real photographs, screenshots and art,
   including small high-contrast details and multiple image sizes.
2. Review the shortlist at native resolution and enlarged, especially gradients,
   text and transparent edges. Calibrate thresholds on one set and confirm them
   on held-out images; metrics alone cannot approve visual quality.
3. Set alpha and metadata admission rules; unsupported variants remain blocked,
   rather than silently flattened, normalized or stripped.
4. Add production planner/worker/publication integration and relevant failure,
   cancellation, crash, timeout, collision, no-change, trial and UI tests. Existing
   lossless tests provide reusable infrastructure, not lossy acceptance evidence.
5. Freeze a versioned policy only after these checks. Lossless remains the default;
   no approved lossy result means Unchanged, not permission to lower the floor.

See [optimizer design](optimizer-design.md) and the separate
[implemented lossless goal](png-optimization-goal.md).
