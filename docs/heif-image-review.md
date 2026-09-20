HEIC And AVIF Image Review
=========================

Status: all sixteen comparisons are prepared; human visual acceptance is open.
This implements the sixteen-output H02 review in the
[local acceptance contract](local-candidate-acceptance.md). It does not substitute
for the authored/upstream decoder corpus, hostile-input and recovery checks, or
final combined-candidate replay.


Fixed scenes and references
--------------------------

Four scenes each supply one HEIC and one AVIF input, converted to PNG and JPEG:
eight original inputs and sixteen named output copies. The fixtures use explicit
sRGB primaries/transfer, full-range BT.709 matrix and 4:4:4 encoding. Test-only
encoders remain separate from the decoder-only production engine.

| Scene | Stored source | Expected appearance |
| --- | --- | --- |
| Photograph | 768 x 512, 8-bit RGB | Kodak hats photograph: saturated fabrics, fine texture and edges |
| Orientation | 640 x 400, 8-bit RGB, orientation 6 | 400 x 640; upward arrow rotates clockwise to point right, with asymmetric corner colors and marker |
| Transparency | 640 x 400, 8-bit RGBA | Colored geometry and horizontal alpha ramp; transparent PNG, explicitly selected white matte for JPEG |
| Gradient | 2048 x 256, 12-bit RGB | Neutral ramp plus dark and bright bands; 16-bit PNG must retain more than 1,024 distinct red levels |

The [Kodak Image 3 source page](https://r0k.us/graphics/kodak/kodim03.html)
credits Don Cochran, Bahamas. The
[collection page](https://r0k.us/graphics/kodak/) reports the maintainer's
understanding that Kodak released the images for unrestricted usage. Retain both
pages, the original PNG and hashes locally; this is a research/review asset,
not approval to include a third-party photograph in the customer package.
The original 502,888-byte PNG has SHA-256
`E25CA1FF2F0C0CB5FDFD5F9B0A0BB21AC4C3DE3C84A67F35B09A85D3306249DB`.

The other sources use explicit integer pixel formulas. Reference PNGs are written
before HEIF encoding, including a separately rotated expected image and the
white composite. Outputs never become their own reference. Windows bitmap
decoding compares all output pixels with those retained references. In 8-bit
equivalent units, predeclared RGB RMSE limits are 3 for ordinary PNG, 0.1 for the
16-bit gradient PNG, and 8 for JPEG. Alpha must agree within one 8-bit unit;
fully transparent hidden RGB does not affect the appearance comparison. Keep
these bounds when a run fails; investigate the source, fixture or product.


Prepare and inspect
-------------------

```powershell
python -B tools/heif-engine/Prepare-HeifReview.py `
  --payload '<inventoried isolated combined HEIF candidate>' `
  --encoder-sdk '<retained fixture-only encoder SDK>' `
  --decoder-sdk '<retained decoder SDK>' `
  --cmake '<existing CMake executable>' `
  --photograph '<pinned local Kodak PNG>'
```

The runner accepts repository-owned isolated inputs, verifies the complete
payload/inventory and records source/SDK/executable identities. It builds only
the test host and fixture encoder, then uses the unchanged candidate worker.
Every source and reference must retain its bytes/timestamp; publication records
and worker scratch must be removed. Routine conversions require no planner;
transparent JPEG requires its explicit white-background choice and preview.
The harness confirms that choice; it is not human acceptance of the prompt UI.

Open the printed `pack/review.html`. Compare reference/output at full size in an
intended image viewer. Check photo color/detail, arrow/corner direction, alpha
over the checkerboard, the white JPEG background and dark/bright gradient banding.
Record viewer version, monitor, scaling and color/HDR settings. A browser or
display may quantize a 16-bit preview; record that limitation rather than call
the underlying file 8-bit. Download the review notes before closing the page.
Exported notes identify the exact input, reference, output and result-report
hashes. Nothing is uploaded or automatically saved.

Actual human appearance review, worksheet interaction, accessibility, alternate
themes, scaling, monitors and final-candidate acceptance remain unverified.


Recorded preparation (2026-09-19)
---------------------------------

Pack `.codex-temp/heif-review/c09cd34f683b48f894134a35f5f8afae` passes all sixteen
direct commands against the unchanged combined HEIF scratch worker in
`heif-payload-tests/a35b0137522a40ce8e4da4da7a763e29/combined`. Both transparent
JPEG commands require the explicit matte choice; the other fourteen need no
planner. Originals, references, source/SDK/binary identities and complete payload
inventories remain unchanged, with verified worker/publication cleanup. This
scratch payload is not the final expanded versioned candidate.

| Comparison | Largest measured RGB RMSE, 8-bit units | Fixed limit |
| --- | --- | --- |
| Ordinary PNG references | 0.575 | 3 |
| High-precision gradient PNG | 0, exact reference samples | 0.1 |
| JPEG references | 2.293 | 8 |

Both gradient PNGs retain 16-bit output and 2,560 distinct red sample values.
Output dimensions, upright orientation, ICC presence and alpha/matte policy
pass throughout. An assistant spot-check of the orientation reference/output
and one white-matte JPEG agrees with their declared geometry; this is not owner
acceptance or verification on the intended display/viewer.

Native and managed Release builds have zero warnings/errors. All 4,384 foundation
contracts pass at `.codex-temp/heif-review-foundation-ea49a72935744eb7b4d66a156ae59055`.
Eight worksheet/native fixture guards pass at
`.codex-temp/heif-review-guards-cb3d3c50407e4718a559e3f5fbe7f18d`: a complete
sixteen-case page has 32 images and parseable hash-bound export code; missing or
duplicate cases, changed output hashes, outside-pack references and failed
preparation are refused. Existing encoded inputs and truncated raw rasters return
their specific native errors without modifying cloned inputs. Retained evidence
is not rewritten. Repository checks pass the public-source boundary, system-theme
policy and all 198 documentation files.
