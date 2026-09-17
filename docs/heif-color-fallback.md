HEIF Color Fallback And Alpha Alignment
======================================

Status: isolated implementation and focused verification passed, 2026-09-16.
Production adoption, packaging, budgets and visible acceptance remain separate.


Color interpretation
--------------------

The earlier [upstream corpus](heif-interoperability.md) found that container-only
NCLX admission refused ordinary ICC-only and incompletely tagged images. The
private bridge now exposes both the original container declaration and the
effective decoded declaration in ABI 3 (72 bytes). For a missing profile or
unspecified color code, it first requests native-color pixels, reads their
consolidated color declaration and releases that image before requesting RGB.
Fully specified container color retains the existing single-decode path.

This follows the selected libheif implementation's container precedence and
bitstream fallback. Its RGB conversion fills unspecified code 2 with BT.709/sRGB
primaries, sRGB transfer and BT.601 matrix coefficients; the bridge reports those
same choices instead of inventing a different profile for the resulting pixels.
Range remains the decoder's declared range. Reserved/unrecognized codes are not
changed to defaults by the bridge. The managed adapter retains its supported
matrix/profile checks and refuses PQ/HLG from either declaration.

Original RGB ICC bytes stay attached to their corresponding decoded samples.
Without ICC, supported explicit or defaulted color uses the existing sRGB,
BT.709 or Display P3 profiles. Any use of defaults marks information that cannot
round-trip exactly; Automatic therefore forces copy output even with a standing
overwrite preference. It adds no routine prompt. These are interpretation rules,
not a claim that a missing tag proves the original author's intended appearance.

The native-color preflight can add decoding work for incompletely tagged files.
Existing per-decode limits and cancellation remain; release timing and process
memory budgets still need measurement. Releasing a local image handle is not a
claim that every library cache is freed or that the worker has a RAM job cap.


Dependency corrections and transparency
--------------------------------------

The authored missing-profile tests exposed libde265 replacing HLG transfer code
18 and Display P3 primary code 12 with unspecified. The retained
[color-signals patch](../tools/heif-engine/libde265-color-signals.patch) preserves
all signalled values for caller validation, including reserved values rather
than silently converting them to defaults. It changes neither compressed sample
decoding nor resource limits.

Container NCLX normally overrides bitstream color. A narrow
[libheif patch](../tools/heif-engine/libheif-hidden-hdr.patch) makes strict decoding
refuse a container override that would hide bitstream PQ/HLG. It does not alter
ordinary container precedence or tone-map HDR. Both patches accompany the source
and runtime review bundles, alongside the earlier NAL error-propagation patch.

The independent source-PNG comparison also exposed misaligned alpha in upstream
`abc_color_irot_alpha_NOirot.avif`: its color rotates, but its alpha is missing the
corresponding association. The library resizes the mismatched plane and returns
plausible dimensions. The initial output had alpha RMSE about 40 on an eight-bit
scale, despite exact RGB. That output is failed evidence, not acceptance.

The bridge now compares ordered rotation, mirror and crop operations for color
and alpha auxiliary items, including tile attachments, before decoding. Missing
or inconsistent transforms receive an unsupported-feature refusal instead of
publishing corrupted transparency. Counts and crop arithmetic are bounded.
The correctly associated upstream image matches its source PNG's rotated alpha
and RGB exactly. This check does not infer or repair missing authored intent.

The same guard refuses `clap_irot_imir_non_essential.avif`: its 12x34 color item
has crop borders 4/6/0/18, 90-degree rotation and horizontal mirror, while its
12x34 alpha item has no transform associations. This is an explicit unsupported
alignment case, not a claim that every nonessential transform is invalid.


Verification approach
---------------------

[Test-HeifColor.py](../tools/heif-engine/Test-HeifColor.py) makes bounded variants
of the independently authored fixtures without changing compressed media bytes
or item extents. It removes a container NCLX property by making it nonessential
padding, or replaces selected declarations with unspecified codes. Cases cover
ICC-only sRGB/Adobe RGB, bitstream-only sRGB/BT.709/Display P3, unspecified SDR,
PQ/HLG without container tags and container declarations hiding HDR, at 8/10/12
bits for HEIC and AVIF. Independent sample evidence and exact ICC identities
remain bound to each generated file.

The private `--heif-color` contract mode verifies JPEG/PNG output, exact PNG
samples, JPEG against encoding of those samples, preserved ICC, copy policy and
HDR refusal. The public interoperability runner accepts `--color-evidence` to
exercise those files alongside upstream inputs through the real worker and output
publisher. The rotated upstream PNG comparison uses fixed alpha RMSE <= 3, visible
RGB RMSE <= 5 and at most 1% alpha-threshold-mask disagreement. These thresholds
are unchanged after the failed alpha result; the inconsistent file is refused.

The comparison verifies sample placement/transport within its declared bounds;
it is not human visual acceptance or a general photograph fidelity oracle.


Verified candidate and evidence
-------------------------------

The full build is `.codex-temp/heif-engine/2b504753422e4342bb39c4fd440e4e27`.
Its retained source review verifies 884 source files and exactly the three
reviewed patched files. The original inventory SHA-256 remains
`EA2637516F6C005416D36BAD45A4141E4ED0E07DA6549DFBD8318613EA05261F`;
the patched inventory is
`16C24F3EEDB58806DDDA56AB89297B381E6A3ACF6FE3461523B4E6BA0C9127D8`.
Build-input receipt SHA-256:
`0B1E319916506A8D4A90DDDF52E8BC6B8875C936C92F9CAE31DEBAEEFCB1FF30`.

The private loader and public [candidate manifest](../tools/heif-engine/distribution-candidate.json)
select the same three files, totaling 3,653,120 bytes:

| File | Bytes | SHA-256 |
| --- | --- | --- |
| `ContextSuite.Heif.Native.dll` | 146,944 | `798A9EC3D940E013B9C9B07A54A9B1F525513C35353AD8E9AB9BC2738C12E013` |
| `heif.dll` | 2,950,656 | `709AA42349807BDD790E36AC05C163C577C02260ADD1955AF5820DAD6EA0BE4A` |
| `libde265.dll` | 555,520 | `E95DFC0405C9DE6C22B730BDFE8A01B20AA1F2B8F44C99FB3D5B7A9398C8F78C` |

All paths below are beneath repository `.codex-temp`:

| Verification | Retained directory | Result |
| --- | --- | --- |
| Authored bridge and resource limits | `heif-bridge-tests/5fa91cf0f118490e9c71c0d1c29c90f5` | 3,820 checks, 186 original fixtures |
| Missing/unspecified color and hidden HDR | `heif-color/95627656d54b47e8a38af5801bac3d94` | 355 checks, 66 generated variants |
| Private color output fidelity | `heif-color-conversion/ee11558668e84abdadc8a77488183ec5/color` | 576 checks, 84 outputs |
| Existing adapter fidelity | `heif-color-conversion/ee11558668e84abdadc8a77488183ec5/ordinary` | 2,819 checks, 336 outputs |
| Actual worker/publication and native interruption | `heif-worker/b5a61b7711c045d28bf7e75c589b9120` | 1,181 ordinary checks, 336 outputs; 38 active-native interruption/recovery checks |
| Loader path and ownership | `heif-loader/9a8ed0cf8dc443ab82581fa9ffdbdf37` | 25 checks across six isolated scenarios |
| Upstream plus authored color publication | `heif-interop/725185139dfa43269c03bd24fa3c6f0d` | 659 checks, 104 outputs, no pending color-admission cases in this corpus |
| Review-bundle mutation checks | `heif-distribution-tests/2bf2fe4bbe484c4c89a940c367f5d786` | 16 checks |

The combined corpus contains 16 upstream inputs and 66 authored variants. Ten
upstream still images and 42 authored SDR variants produce both targets. Six
upstream feature/alignment cases and 24 authored HDR variants are refused with
same-worker recovery. The correctly tagged rotated upstream image has alpha
RMSE 0, visible RGB RMSE 0 and zero mask differences across 131,072 pixels.
This resolves the earlier color-admission cases without claiming support for
their two inconsistent-alpha representations.

Managed builds have zero warnings/errors. The native build retains the known
upstream C4805 bool/byte warning. General foundation/image and Office suites were
not rerun for this focused slice. No visible UI, screen reader, alternate theme,
DPI or customer photograph review was performed.


Source delivery and retained failures
------------------------------------

The revised review bundle is
`.codex-temp/heif-distribution/bd207dd987e948e1b080faf261b288f9`:

| Archive | Bytes | SHA-256 |
| --- | --- | --- |
| `heif-source-review.zip` | 21,667,656 | `B3A3035E1F0413D900BEBEB43E4330F4102B93CDD0E0AA684978421BB5FEBEB4` |
| `heif-runtime-review.zip` | 1,571,532 | `E39F98D00FA7C04EEB3CE1527C72BAA0C59BCEBD3933FDEE0B0574548F5096B6` |

Both additional color patches accompany the existing NAL patch and source
recipe. The source ZIP is byte-identical to the kit already rebuilt at
`.codex-temp/hr-a1d1f75355c4`, whose nested build is
`heif-engine/8176e54bf3ff478a909b8752ad2d2562`. That successful receipt confirms
unchanged kit inputs and absence of the private checkout; it is reused for the
identical source kit, not described as another rebuild. AOM's pinned static
artifact remains reused. The private bridge is only in the runtime bundle.

Failed evidence is retained. Initial missing-profile HLG testing at
`heif-color/49f0811dd5b34544bee8bc2d43f5c70b` exposed the libde265 normalization.
`heif-interop/ab9fef2fc1ae4f7bbb61239228d54501` exposed the rotated-alpha mismatch.
The later `heif-interop/5d206fe46d2f42318aaf38f1e5787e60` stopped on the second
inconsistent-alpha file before its explicit refusal policy was recorded. Earlier
test-harness failures included an ABI-2 assertion after the ABI-3 update and a
copy-policy assertion that accidentally requested strict Preserve instead of
Automatic. None of those failed attempts counts as passing evidence.

The [redistribution decisions](heif-distribution.md#remaining-release-decisions),
production adoption, performance/package budgets and human acceptance remain
open. No reserved stage, installed application, Explorer registration or live
service changed. The active release-candidate goal remains incomplete.
