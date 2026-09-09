Fixed Optimization Recipes
==========================

Status: implemented for PNG; bounded acceptance recorded in [integration evidence](../fixed-preset-integration.md)
Date: 2026-09-08


Context
-------

Users should choose an understandable preset and continue working. Trying several
encoders, palettes and compression settings on each invocation conflicts with that
promise. PNG research identified useful methods, but the application must not run
the research tournament for every file. Avoiding conspicuous banding, distracting
grain and damaged detail takes priority over matching a reference file size.


Decision
--------

Retain Auto, Lossless, Balanced and Smallest in that order. Each named preset
selects one versioned recipe with fixed settings, a quality contract and bounded
effort. Auto alone chooses a recipe using bounded preflight analysis; it does not
encode alternatives to discover which recipe to use. This is the suite-wide
optimization direction; the first concrete recipes apply to PNG only.

| Preset | Default PNG direction | Safety fallback |
| --- | --- | --- |
| Auto | Select conservative Balanced or Lossless from supported input properties; choose Lossless when classification is uncertain. Never select Smallest implicitly. | At most the selected recipe's one permitted fallback. |
| Lossless | One pinned, bounded lossless recompression recipe; exact decoded content. | Retain the original. |
| Balanced | One conservative RGB7 precision recipe with gradient/detail validation; do not run RGB6 or palette alternatives. | One lossless attempt if supported and time remains; otherwise retain the original. |
| Smallest | One corrected, deterministic ExoQuant recipe: dark-protected palette weighting, eight detail anchors, adaptive diffusion strength 0.5 and explicit alpha-aware allocation. | One Balanced RGB7 attempt if supported and time remains; otherwise retain the original. No further lossless attempt. |

The initial Smallest allocation targets at most 256 exact RGBA entries, preserving
hidden RGB and reserving 16 entries for partial-alpha groups, raised to the number
of groups when required. The remaining budget serves opaque colours. Preflight
must reject cases without useful opaque allocation or with too many mandatory
alpha/hidden-colour entries. This selects the tested dark-protected recipe, not
the smaller residual-only alternative or the 32-entry allocation sweep. Do not
change alpha to fit a palette. These are private implementation defaults, not
additional Explorer controls or authorization to package scratch binaries.


Execution Budget
----------------

- One primary recipe, at most one predetermined fallback, then stop. A recipe
  includes its fixed encode/cleanup stages; this is not a limit of one process
  launch. Count all stages against one end-to-end deadline.
- Do not recurse through fallback policies. Inapplicable Smallest goes directly
  to its Balanced fallback, not a subsequent chain through Lossless.
- No palette-size, dither-strength, colour-model or encoder-effort sweeps.
  Do not run a second successful recipe merely to seek additional savings.
- Quality rejection, inapplicable representation or no useful size reduction
  may use the designated fallback. Every attempt starts from the original,
  never from an already lossy candidate.
- Cancellation, malformed input, preservation/validation corruption and engine
  faults remain distinct outcomes; do not conceal them as ordinary quality
  fallback. A controlled candidate time-budget exhaustion may fall back only
  within the remaining overall budget, never after cancellation.
- Share decoded data and screen raw candidate quality before expensive encoding
  where possible. Still reopen and independently validate every published output.
- Publish only a smaller, validated result; otherwise report No smaller result.
  Do not encode a lossless baseline unconditionally just to compare its size.
- Preserve one sequential worker, copy-only quick actions and quiet completion.
  Identical results between presets are acceptable. Names express priorities,
  not guaranteed size ordering or a claim of global optimality.


Quality Contract
----------------

Smallest permits modest detail/texture loss, but its goal is to avoid major new
banding, distracting grain, colour shifts and damaged text/edges. Accept a larger
file rather than pursue reference size at the expense of those properties.
Selective dithering is not permission to hide contouring beneath excessive noise.

A separately versioned perceptual policy for Smallest is implemented
instead of applying the RGB6 maximum two-level sample-error rule to a palette.
This does not approve arbitrary pixel error or treat aggregate SSIM as sufficient.
The integration evidence freezes local-error and gradient safeguards and records
the bounded dark-texture visual approval. These checks are not universal artifact
detection or commercial release acceptance. Balanced retains its conservative limits; do not weaken it to rescue
a rejected image. Transparency, hidden RGB, dimensions, colour interpretation and
admitted metadata stay protected independently of the preset.


Implementation Boundary And Next Checks
---------------------------------------

The implementation now replaces the runtime precision-candidate tournament.
[Integration evidence](../fixed-preset-integration.md) freezes concrete policy
IDs, Auto routing, numeric safeguards, native packaging and measured timings.
Decision 0016 supersedes multi-candidate selection in decision 0015 and the
palette-search direction, not historical implementation evidence.

Implementation/release checks (bounded completion is tracked in the evidence):

1. Benchmark the single dark-protected recipe and its validation separately,
   including total latency, peak memory and cancellation. Research runs comparing
   several methods are not responsiveness targets.
2. Freeze Auto's inexpensive routing and benefit threshold without a mandatory
   second baseline encode. The existing runtime's 5% comparison against a
   lossless baseline remains historical behaviour until replaced and tested.
3. Freeze and visually accept the new Smallest artifact safeguards; test unseen
   gradients, text, photographs, alpha boundaries and noisy images. Numeric
   acceptance cannot promise an absence of visible artifacts on every image.
4. Add contracts for recipe identity, attempt counts, no recursive fallback,
   shared deadlines, all fallback/failure outcomes and smaller-only publication.
5. Independently package reviewed dependencies and notices. Do not depend on
   ignored research files or distribute user screenshots as fixtures.

No single-recipe end-to-end speed or final preset file-size guarantee is approved
by this decision. Keep these distinctions visible in status and benchmark reports.


Measured Size Illustration
--------------------------

An unseen 3440x1440 Hollow Knight screenshot provides a same-input illustration.
Values are decimal MB, not MiB. These are research encodings, not measurements of
the new integrated preset path or calibrated visual acceptance.

| Method | Bytes | MB | Reduction from prepared source |
| --- | ---: | ---: | ---: |
| Prepared source | 5,644,152 | 5.64 | -- |
| Lossless recipe | 4,458,402 | 4.46 | 21.0% |
| Balanced RGB7 recipe | 3,637,998 | 3.64 | 35.5% |
| Proposed Smallest dark-protected recipe | 2,028,943 | 2.03 | 64.1% |

This table remains the historical research measurement. Current Auto and preset
timings/sizes are in the integration evidence; Auto uses its selected recipe,
not a distinct compression algorithm. Smallest now has separate versioned
perceptual limits rather than the old RGB6 sample-error bound. The original screenshot was
untouched; one 17-byte fdEC chunk was removed from the research copy with decoded
RGBA identity checked. This is not an implicit metadata-removal policy.

Local evidence: `.codex-temp/png-quantization/exoquant-evaluation/`, specifically
`results/holdout-real-04/results.json`, `holdout-inputs/manifest.json` and
`final-evidence.json`. Original/prepared SHA256 values and output hashes are
recorded there. The independent holdout included five real screenshots and six
stress fixtures; only two real cases passed the palette research gate, reinforcing
the need for preflight and fallback rather than universal palette conversion.
