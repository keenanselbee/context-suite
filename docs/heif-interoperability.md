HEIF Upstream Interoperability
=============================

Status: partial compatibility evidence, 2026-09-16. Six upstream still images
convert through the real worker; six color cases remain open. This is not full
HEIC/AVIF acceptance or production adoption.


Pinned local corpus
-------------------

The [input manifest](../tools/heif-engine/interop-inputs.json) fixes 16 upstream
HEIC/AVIF files, companion provenance/license documents and one source PNG.
libavif files come from revision `d98b4bcb67e9b2e5056d1f9ae18a088779d7945f`;
libheif files come from the already verified 1.23.4 source archive. They were
acquired independently of local reference material. Original files remain in
`.codex-temp/heif-interop-inputs/cc7e320077e946a7a8909c78b14cc631` and are not
committed, bundled or installed.

The pinned upstream [libavif fixture descriptions](https://github.com/AOMediaCodec/libavif/blob/d98b4bcb67e9b2e5056d1f9ae18a088779d7945f/tests/data/README.md)
identify file origins and licenses. The retained LICENSE/README accompany the
local inputs. libheif's tests supply independently stated crop dimensions;
its original COPYING remains alongside the corpus. These research files are
not approved customer-package assets.

The [interop runner](../tools/heif-engine/Test-HeifInterop.py) requires the
matching local corpus and [selected decoder](heif-distribution.md). It builds
the real worker and contract host sequentially, copies them into fresh scratch,
verifies all source/input/payload identities, and runs the ordinary named-pipe
worker and application output publisher. It does not use a simulated converter.

```powershell
python -B tools/heif-engine/Test-HeifInterop.py `
  --inputs '<repository scratch corpus directory>' `
  --bridge '<repository scratch candidate>/payload/ContextSuite.Heif.Native.dll'
```


Observed results
----------------

Final evidence: `.codex-temp/heif-interop/544dc5783347428c88366cd673d3a951`.
All **107 regression checks** pass, with **12 published JPEG/PNG copies** and
**six explicitly pending color cases**. Managed builds have zero warnings/errors.
The result deliberately records `CompatibilityComplete: false`.

| Files | Observed behavior |
| --- | --- |
| libavif `white_1x1.avif`, Kodak `kodim03_yuv420_8bpc.avif` | Both targets validate and publish; expected dimensions and opaque output retained |
| libavif `color_grid_alpha_nogrid.avif` | 80x80 tiled color with per-tile alpha converts; PNG retains transparency and JPEG requires explicit matte |
| libheif `clap_cropped.heic`, `clap_cropped.avif`, `clap_cropped_irot_imir.avif` | All publish at the independently stated 64x64 transformed dimensions |
| libavif animated, gain-map and shared-grid-tile files | Typed unsupported-feature refusal; no output |
| libheif two-image `example.heic` | Typed collection refusal; no output |

Every refusal is followed by a successful probe in the same worker. Inputs keep
their original bytes and write times; no output reservation remains abandoned.
Published files reopen with the expected format, dimensions, precision, ICC and
alpha policy. This checks interoperability and publication, not an independent
pixel-fidelity oracle for photographs, crop content or unusual alpha layouts.
The authored [sample/appearance tests](heif-input-evaluation.md) remain separate.

The upstream default-quality near-white AVIF decodes to opaque RGB 253, not the
uncompressed ideal 255. Source/output previews agree at that sample for both
targets. The first run at `heif-interop/338383d9605c484399c8a1ee517361c1` failed
an incorrect exact-white test assumption; it is retained, not passing evidence.
The corrected test compares the actual pinned near-white source and output.
An earlier runner attempt stopped before building because it required an optional
`Directory.Build.targets`; absent optional build files are now handled explicitly.


Compatibility gaps to resolve
-----------------------------

These refusals pass the current-policy regression, but do not count as accepting
the required input expansion:

- `rainbow-451x461.heic` has an ICC profile and no container NCLX. The current
  managed adapter requires NCLX before considering ICC, so it refuses the file.
- The two rotated `abc` AVIF files declare sRGB primaries/transfer with matrix
  coefficient 2 (unspecified), which is outside the current matrix allowlist.
- libheif `example.avif` and `conformance_window_padding.heic` have unspecified
  primaries/transfer. Their native decodes succeed but managed admission refuses.
- `clap_irot_imir_non_essential.avif` has no container NCLX or ICC. Native decoding
  produces 10x8 pixels, but managed color admission refuses it.

The next color work must distinguish container declarations, codec bitstream
declarations and defaults. The present bridge reports container NCLX only, while
the pinned library can obtain additional color data from the decoded bitstream.
Simply removing the managed NCLX guard would not prove the resulting color or
HDR admission correct. Add independently checked ICC-only and missing/unspecified
color fixtures, preserve the original ICC, retain HDR refusal and test conflicting
declarations before widening admission. Alpha-transform and crop-content fidelity
also need independent comparisons, beyond the dimensions checked here.

Keep these as explicit completion work in the [active goal](broad-file-support-goal.md).
No engine pins, production capabilities, reserved package, installed app or live
service changed in this interoperability checkpoint.
