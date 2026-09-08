PNG Quantization Improvements: Research Phase Two
=================================================

Status: completed local synthetic and screenshot experiments on September 8, 2026;
research-only extension of the [initial evaluation](png-quantization-evaluation.md).
No shipping preset, worker protocol, settings, trial, installer or media capability
has changed. No new third-party engine, package or license fee is introduced.

Follow-up: the user approved these visual results on September 8, 2026.
[Production integration](png-lossy-presets.md) now implements the precision
policies under a stricter metadata/representation boundary. Statements below
describe the completed research phase, not current production status.

Later user feedback found visible dark-gradient banding in production RGB6.
The [dark-gradient palette comparison](png-palette-comparison.md) reopens the
lossy strategy with metadata/alpha-preserving experiments; the earlier visual
approval is not evidence that RGB precision works well on this image class.


Experiments
-----------

Keep the original nine palette candidates and add nine alternatives, for 216
experiments on the same twelve synthetic fixtures. Keep both numerical quality
screens, exact-alpha requirements and the smaller-than-lossless requirement
unchanged. Every candidate starts from original pixels.

- RGB precision: independently round each visible RGB sample onto 128 or 64
  evenly spaced levels including 0 and 255, while storing ordinary 8-bit PNG.
  The integer transform has maximum sample error 1 or 2 respectively; alpha and
  RGB beneath fully transparent pixels remain exact. This is not a 128/64-color
  combined palette and does not reduce the PNG's declared sample depth.
- Partial Floyd-Steinberg: 256 colors with 25%, 50% and 75% diffusion, using
  ImageMagick's `dither:diffusion-amount` artifact.
- Exact-alpha palette variants: quantize RGB to 256/128 colors without alpha,
  then restore original alpha and fully transparent RGB. Also test 256 colors
  with 50% diffusion. Hidden RGB participates in palette training in this simple
  experiment; hiding it from training is not implemented or claimed.
- K-means: one deliberately bounded comparison using 128 fixed sRGB cube seeds
  (8x4x4), eight iterations maximum, tolerance 0.01, full-resolution training and
  exact-alpha restoration. Results do not evaluate all initialization strategies
  or the maximum quality achievable with k-means.

Precision and alpha-restoration variants encode explicitly as truecolor RGB or
RGBA, not indexed PNG. An early expanded run let ImageMagick automatically
choose the PNG representation; four noisy-image reconstruction outputs changed
samples by more than the rounding allowance. Those artifacts were rejected.
Explicit RGB/RGBA avoids another palette encoding step after reconstruction;
the final decoded fidelity check remains required.

The harness self-tests all 256 sample values for precision endpoints, distinct
level counts, maximum error, idempotence, exact alpha and hidden RGB. Every
exact-alpha candidate also verifies decoded alpha and hidden RGB after cleanup.
No thresholds were weakened to make additional candidates pass.


Reproduction And Screenshot Scope
----------------------------------

```powershell
# Full synthetic matrix, private checkout and staged engines required.
./tools/Test-PngQuantization.ps1

# Optional real-image research, ONLY an already-copied folder inside this path.
./tools/Test-PngQuantization.ps1 -Corpus '.codex-temp/png-quantization/screenshots-<id>'
```

The optional corpus mode accepts one to three ordinary PNG copies, up to 16 MiB
each and 4096x2160, static, non-interlaced, untagged 8-bit RGB. It runs only the
256-color/no-dither baseline and the two precision variants at full resolution.
The public runner does not accept an original media folder as its corpus. Both
modes retain unique test hosts and offline reports locally and use the same
five-minute outer deadline. Each report includes its actual candidate list,
provenance and file hashes; user media is never added to public fixtures.

The user authorized read/copy access to the Afterburner screenshot directory,
explicitly prohibiting edits there. Three PNGs were selected deterministically
from the sorted filename list (first, middle, last), spanning different games.
All work uses copies in `.codex-temp`; no file in the original directory is written,
moved, renamed or deleted. Selection is a small exploratory sample, not a
representative or held-out acceptance corpus.

These copies contain an undocumented unsafe-to-copy `fdEC` chunk. Research
inputs remove that chunk only in memory; untouched original copies are retained,
and original/research pixel equality is checked. This explicitly excludes general
metadata preservation and production admission from the result. Do not silently
strip that chunk in the application based on this experiment. Other metadata and
unsupported variants are rejected by this bounded corpus loader. Untagged RGB
is interpreted as sRGB for measurement without changing source color samples.

The gallery opens with a per-fixture shortlist showing the smallest candidate
passing each unchanged numerical screen and savings against the lossless baseline.
These are research selections, not a shipping search/fallback implementation or
automatic visual approval. Click through to compare original/candidate images.


Results And Recommendation
--------------------------

The final 216-case synthetic matrix passed structural, fixture metadata, sample
fidelity, source-integrity and precision/alpha assertions, with zero rejected
encoding-fidelity candidates. A second run reproduced all 216 output hashes and
metrics, including the fixed-seed k-means cases. Quality-screen failures remain
failures; a successful harness exit is not approval of every image.

| Candidate | Balanced screen | Smallest screen |
| --- | --- | --- |
| Original 256 / None | 5/12 | 6/12 |
| RGB 7-bit / exact alpha | 7/12 | 7/12 |
| RGB 6-bit / exact alpha | 6/12 | 7/12 |
| 256 / FS 25%, 50%, or 75% (each) | 3/12 | 6/12 |
| RGB 256 / exact alpha | 3/12 | 4/12 |
| RGB 128 / exact alpha | 1/12 | 4/12 |
| RGB 256 / FS 50% / exact alpha | 1/12 | 3/12 |
| K-means 128 / 8 iterations / exact alpha | 1/12 | 1/12 |

These counts include three related icon fixtures and are not market-wide success
rates. Some precision outputs fail only because lossless is already smaller;
that is the correct no-change outcome. Preserving alpha separately works but
does not guarantee palette-based processing will produce useful savings.
This bounded k-means trial did not justify its added complexity; it does not
establish that every k-means initialization or configuration is inferior.

The three screenshot copies were evaluated at their full original dimensions:
2560x1440 for Fall of Avalon and Skyrim, and 3440x1440 for Baldur's Gate 3.
Both precision candidates passed the unchanged Balanced and Smallest numerical
screens on all three. The 256-color baseline passed Balanced on none and Smallest
on one. Figures below are additional savings against the lossless baseline,
not the larger original file. fdEC removal is excluded from the size comparison
by using the same normalized research input for every method.

| Screenshot | Lossless bytes | RGB 7-bit bytes | Extra saving | RGB 6-bit bytes | Extra saving |
| --- | --- | --- | --- | --- | --- |
| Fall of Avalon | 1,760,554 | 1,178,062 | 33.1% | 763,768 | 56.6% |
| Skyrim | 4,894,016 | 3,898,699 | 20.3% | 2,765,290 | 43.5% |
| Baldur's Gate 3 | 9,125,179 | 7,947,861 | 12.9% | 6,684,046 | 26.8% |

RGB 7-bit screenshot RMSE was 0.698-0.711 and mean tile SSIM 0.9985-0.9993;
RGB 6-bit was 1.247-1.256 and 0.9946-0.9969. Alpha was exact. These global/local
measurements do not prove that subtle banding is invisible on every display.
Full-image agent inspection found the precision outputs substantially more
promising than the original synthetic palette-gradient results; native-scale
center crops and true 2x gallery inspection are provided for user review.
Native-scale Fall of Avalon center-crop inspection shows some added contouring
in the dark sky, especially at 6-bit precision. Treat Smallest as an explicitly
lossy choice, not an invisible-change promise.

Recommend **RGB 7-bit precision for Balanced** and **RGB 6-bit precision for
Smallest**, subject to visual acceptance and production safety/metadata integration.
Do not ship an adaptive palette/k-means search or additional dither controls yet.
These simpler policies have explicit per-channel error bounds, exact alpha and
hidden RGB, no resolution change, and standard 8-bit RGB/RGBA PNG output.
Keep Lossless as default and retain Unchanged when a valid smaller result is not
available. No new runtime quantizer dependency is needed.

Original Afterburner files were compared by SHA-256 with their retained copies
after the runs; all three matched. The existing 457 image/adapter regression
contracts also passed. No user screenshots or reports are tracked, committed,
published or added to distributable test fixtures.

Synthetic evidence:
`.codex-temp/png-quantization/evaluation-f2da3d6a5c0348c589fd45d90b4493c7/`
and repeat `.codex-temp/png-quantization/evaluation-2e3a8891dbc64a8a95a83b6db10440be/`.
Screenshot evidence:
`.codex-temp/png-quantization/evaluation-1b82585c9e714531857a30e6016cdcdd/`.
The repeat gallery with native-scale crops is
`.codex-temp/png-quantization/evaluation-f6a5455d2a664ccaa914e0398a7c8a86/`.
All nine screenshot output hashes and metric sets matched between runs.
These reports retain an overly broad `Policy.PngColorType: automatic` label;
precision/reconstruction outputs actually used explicit RGB/RGBA as described
above. The harness label has been corrected for future reports.
Earlier reports with automatic RGB-reconstruction palette encoding are diagnostic
only and are superseded by the explicit truecolor results above. Engine identities,
precise candidate settings, timings and provenance live in each `results.json`.
Synthetic and screenshot runs partly overlapped; timings are diagnostic, not
isolated performance benchmarks.


Remaining Gates
---------------

- Inspect real image results at native scale and enlarged, including dark smooth
  areas, UI text and edges; obtain user visual acceptance.
- Retain a separate hold-out corpus before calibrating production thresholds.
- Decide the explicit metadata policy and supported-profile/alpha boundaries.
- Test representative large-batch memory, cancellation and failure behavior in
  the real worker; the research process does not validate that integration.
- Add immutable production policies, planner controls, smaller-only publication
  and per-file reporting only after the candidate behavior is approved.

The existing lossless workflow stays available and unchanged throughout.
