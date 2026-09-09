Fixed PNG Preset Integration
============================

Date: 2026-09-08
Scope: local development integration, not commercial release clearance.

[Decision 0016](decisions/0016-fixed-optimization-recipes.md) replaces the runtime
candidate tournament. No reference binaries or research-folder dependencies are
loaded by production. The user approved the dark-protected Orbit image at native
scale for Smallest, including its remaining fine dark-panel texture.

Recipes
-------

| Preset | Policy | Primary and permitted fallback |
| --- | --- | --- |
| Auto | `png-auto-fixed-v2` | RGB7 for lossy-admitted inputs of at least 4096 pixels; otherwise Lossless. One Lossless fallback after RGB7 rejection. |
| Lossless | `png-lossless-preserve-v1` | One fixed oxipng recompression; otherwise keep original. |
| Balanced | `png-balanced-fixed-v3` | RGB7; at most one Lossless fallback. |
| Smallest | `png-smallest-palette-v3` | Fixed dark-protected palette; at most one RGB7 fallback. No recursive Lossless retry. |

Palette-ineligible inputs go directly to RGB7, without a further retry.
Representations outside all lossy admission use Lossless directly. Palette
admission requires 8-bit RGB/RGBA, at most 9 million pixels, an opaque group and
fewer than 128 mandatory hidden-RGB/partial-alpha entries. RGB7 retains its
16-million-pixel bound. Existing ICC, interlace and metadata admission remains.

Smallest uses corrected ExoQuant color-space inversion, canonically ordered
histogram traversal, dark weighting 4, eight fixed detail anchors, adaptive
serpentine diffusion strength 0.5, and 16 partial-alpha entries (raised to the
number of alpha groups). Diffusion never crosses alpha groups. Output has at
most 256 exact RGBA entries. Fully hidden RGB and every alpha value stay exact.

RGB7 retains maximum one-level channel error, composite RMSE <=2, worst 8x8 tile
RMSE <=6 and mean SSIM >=0.98. Palette uses a maximum 48-level channel outlier
ceiling, composite RMSE <=4, worst tile RMSE <=12 and mean SSIM >=0.95. Metrics
are checked over black and white composites at native resolution. Hidden RGB
and alpha have zero tolerance independently of those perceptual limits.

Both lossy paths additionally reject >=5% flattened eligible gradient runs when
at least 32 runs qualify: 32-pixel horizontal/vertical runs on an 8-pixel grid,
monotonic source deltas <=1.01, source progression 2--24, candidate progression
<=25% of source. This catches plateaus, not every possible artifact. There is no
claim of a universal grain detector or visual equivalence on unseen images.

Each recipe has fixed encode/cleanup stages. Candidates are screened before
oxipng; successful primary output is not compared with another encoding. Auto
requires >=5% savings against source before pixel changes; other presets require
strictly smaller output. Native reopen validates exact intended candidate pixels
and retained metadata. No smaller acceptable output means no publication.

The application retains its 120-second whole-operation deadline. The adapter
adds a shared 110-second recipe budget and each child is bounded to 90 seconds
and a 1 GiB Windows job. Cancellation, child/worker failure and validation
corruption are not quality fallback. One sequential worker, copy-only quick
actions, collision-safe publication and quiet success are unchanged.

Measured Production Path
------------------------

Sequential Debug adapter run on this development machine, not a hardware SLA or
Explorer end-to-end benchmark. Times include encoding and validation, but not
the initial probe, shell startup or publication. Sizes are decimal MB.

| Input | Auto/Balanced | Lossless | Smallest |
| --- | --- | --- | --- |
| Orbit | 2.560 MB / 8.3 s | 3.600 MB / 6.7 s | 1.050 MB / 9.3 s, palette, one attempt |
| Hollow Knight | 3.297 MB / 7.7 s | 4.458 MB / 5.5 s | 2.029 MB / 8.2 s, palette, one attempt |
| BG3 | 8.364 MB / 6.0 s | 9.701 MB / 3.6 s | 8.364 MB / 18.7 s, RGB7 fallback, two attempts |

Orbit Smallest is exactly 1,049,996 bytes and byte-for-byte identical to the
approved research PNG: SHA256
`5BBA20FB02265ECD79C2C10F1C3A50C7D657130EB59A26C68BEF3B273794FFD6`.
The other screenshots are held-out examples, not separately user-approved
visuals. Identical results between presets are intentional, and named presets
do not guarantee monotonically smaller files on every input.

Evidence: `.codex-temp/fixed-preset-acceptance/production-9469cdfc5fd844c7af1c5f702d2c7f7d/results.json`.
All 12 outcomes retained exact alpha, hidden RGB, admitted metadata and source
hashes. Inputs were already-prepared research copies; no original media was
edited and this does not authorize automatic metadata stripping.

Build And Verification
----------------------

The private `proprietary/tools/png-palette/Build-PaletteCandidate.ps1 -Rustc <path>`
builds the helper from independently downloaded, archive-hash-verified ExoQuant
revision `6ef61711b07e58c107decbd4721d9d50e646dac8` using Rust 1.90.0 and the
installed MSVC linker. Authored adapters live in the private repository; upstream
files remain unmodified. The two deterministic histogram contracts run at build.

`tools/palette-engine/production.json` selects the exact reviewed native binary
and notices. `Stage-PaletteEngine.ps1 -CandidateDirectory <path>` verifies all
three file hashes before staging. Rebuilding is not currently byte-reproducible
across linker runs; a different hash is deliberately rejected until reviewed
and selected. Deterministic image results are tested separately from binary
reproducibility. Preserve the selected candidate rather than blindly re-pinning
whatever a build produces. Fresh-source verification imports this pinned native
candidate and records that fact; it does not claim to rebuild the helper.

```powershell
./tools/palette-engine/Stage-PaletteEngine.ps1
./tools/Test-ImageConversion.ps1 -Configuration Debug
./tools/Test-Foundation.ps1 -Configuration Debug -Integration
./tools/curated-engine/Test-ProductionPackaging.ps1 -Configuration Debug
./tools/Test-Repository.ps1
```

Local verification: 883 Release engine/adapter contracts passed, including fresh-process
determinism, native protocol rejection, alpha allocation, exact indexed PNG
round-trip, gradient oracle, exact attempt counts and existing conversion regressions.
All 581 Debug foundation/integration checks passed, including real native child
and worker crashes, timeout, cancellation in the primary and RGB7 fallback,
no orphan/no publication, and subsequent worker reuse. The fallback interruption
fixture uses 1024-square noise so the second stage starts within its 25-second
observation budget; primary interruption retains 2048-square coverage.
Sixteen Release packaging contracts passed, including changed
binary/identity/license/Rust-notice rejection. Documentation checks passed for
49 files, with public/private whitespace checks passing.

Release image evidence: `.codex-temp/image-tests/engine-b1df3e5771454b998c6b4f99fa85a481/`.
Integration log: `.codex-temp/fixed-preset-acceptance/foundation-debug.log`.
Release packaging: `.codex-temp/production-packaging-c20fc60c89044637aec763561ec6fe1a/`.

The existing registered `artifacts/production/Release` payload was refreshed after
those checks. Application, worker, core and private assembly hashes match their
current Release build outputs; palette binary/notice pins also pass. All three
installed sparse packages report healthy and their COM classes activate. No
package re-registration, Explorer restart, trial reset or original-file edits
were performed. The previous payload is retained at
`.codex-temp/fixed-preset-acceptance/previous-release-641a035b37c94b4dbce23c6f98ee9ac3/`.
This is a local development payload update, not a signed installer release.
Interactive Explorer execution/visual UI acceptance was not repeated this turn.
The fresh-source and installer-candidate workflows were updated for the new
dependency, but their full snapshot/installer runs were not repeated.

The payload carries the ExoQuant MIT notice, Rust library third-party notices and
an identity manifest. These are engineering packaging checks, not clearance of
all static components, toolchain eligibility or release terms. See
[redistribution gates](release-redistribution.md). No paid quantizer, custom
account backend, new settings UI or reference-code redistribution was added.
