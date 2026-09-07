BMP/TGA And Curated Engine Integration
=====================================

Date: 2026-09-07
Status: production development integration implemented; commercial release not cleared

The WPF planner, worker and private catalog now support twenty cross-format
pairs among PNG, JPEG, WebP, BMP and TGA, subject to content admission and an
explicit valid plan. Same-format re-encoding remains outside Convert. DDS is
not enabled or selected. Existing copy/replacement, trial and queue rules apply.

Accepted bitmap variants
------------------------

| Format | Input | Output |
| --- | --- | --- |
| BMP | Windows 40-byte INFOHEADER, 24-bit BI_RGB, ordinary pixel offset, padded rows, positive/bottom-up or negative/top-down height | Uncompressed 24-bit RGB, 40-byte INFOHEADER |
| TGA | True-color type 2 or RLE type 10, 24-bit RGB or 32-bit straight RGBA with eight declared alpha bits; all four raster origins | Uncompressed true-color, 24-bit RGB or 32-bit RGBA when transparency is present |

BMP palette/RLE/bitfield/32-bit alpha, OS/2 and V4/V5 variants are not yet
supported. TGA palettes, grayscale/16-bit pixels, interleave, nonzero page
origins, extension/developer areas and ambiguous alpha are rejected. A TGA v2
footer with zero extension/developer offsets is accepted. Image IDs require
explicit descriptive-metadata removal. Unknown trailing bytes are not ignored.
The decoder is forced from admitted content, never selected by filename alone.

Admission validates headers, dimensions, lengths, BMP stride and TGA packet
bounds before native decoding. Existing file-size, pixel, native-resource,
deadline and cancellation limits remain active. Header checks do not replace
semantic decoding or constitute a security sandbox.

BMP cannot retain alpha in this slice: selecting it for transparent input
requires an explicit matte, a fresh encoded preview and acknowledgment. TGA
preserves straight alpha. Outputs use eight bits/channel; higher-precision
sources carry a reduction warning. No resize occurs unless requested.

BMP/TGA outputs use untagged sRGB. Embedded ICC profiles are applied to pixels
before removal, with a visible color-conversion warning; this is not tag-only
reinterpretation. Both targets disclose the untagged-sRGB interpretation.
Their selected output variants do not preserve descriptive profiles or
resolution/aspect fields. Such sources block in Preserve mode until the user
explicitly chooses metadata removal. BMP input pixels/metre can be retained
exactly through the existing PNG/JPEG/WebP resolution normalization path.

The worker forces true-color output even for grayscale or few-color inputs;
otherwise ImageMagick can choose an unadvertised grayscale TGA variant.

Production engine and packaging
-------------------------------

The selected DLL remains the evaluated 5,959,168-byte native candidate:
`2B74DB7BA2F1B25BFFFDE9F89E09F9D3BE53C82FFBFF1A2F386CB8CB9DAE7637`.
The exact native and notice identities live in
[production.json](../tools/curated-engine/production.json). The prototype's build
and link evidence remains in [its historical report](curated-engine-prototype.md).

```powershell
./tools/curated-engine/Stage-ProductionEngine.ps1 -RunName prototype-2
./tools/Build-Production.ps1 -Configuration Release -SkipShell
./tools/curated-engine/Test-ProductionPackaging.ps1
```

Staging copies the selected candidate into ignored `artifacts/engines/curated-win-x64`.
A fresh upstream rebuild is not automatically trusted: its hash and notice need
review before changing the selection. The private project excludes NuGet native
assets and explicitly propagates the curated native/notice/identity files.
Missing curated inputs fail clearly. The full production build verifies those
identities before and after composition. It does not install Explorer packages.

Production now has an explicit file/package allowlist and `payload-inventory.json`
with relative paths, sizes and SHA256 hashes. Extra runtime engines, unexpected
files/packages, missing notices and stale engine selections fail checks. The
application still has no private codec reference; only the worker owns it.
The exact engine notice, explicit IJG acknowledgment and local SDK's .NET
license/third-party notices accompany the output. Developer PDBs remain in this
development layout; this directory is not an approved customer installer.

Verification
------------

- 339 engine/adapter contracts passed in
  `.codex-temp/image-tests/engine-09b4726b0ed8476b965522f9598d1a35`.
  This includes all twenty pairs, actual loaded native SHA verification,
  independently authored BMP/TGA pixels, four TGA origins, RLE/raw packets,
  alpha/matte/preview behavior, exact lossless pixels, resolution, ICC pixel
  transformation, resize, gray/bilevel output types and malformed variants.
- 297 public/foundation/integration contracts passed, including BMP/TGA worker
  publication and re-probing, the BMP matte-preview gate and exact catalog set.
- Fifteen curated/native production guard contracts passed. The original nine
  checks now also exercise the production identity gate on six cases. Stock
  negative fixtures come from the pinned NuGet cache, not the now-curated
  production output.
- Six full packaging contracts passed in
  `.codex-temp/production-packaging-8860185d979044f39637a7ea0963bc5c`.
- All 29 desktop checks passed in
  `.codex-temp/desktop-smoke/images-91be852085144888b01edf9660f8952d`,
  including fresh BMP matte/preview consent and TGA preview without starting trial.
  Native file selection now uses ValuePattern plus the Open split-button Invoke
  action. Output-folder verification checks actual shell folder identity as
  well as URL, and ignores individual closing/non-folder COM windows.

UI automation requires an idle unlocked desktop. An earlier rerun overlapped
other typing/window activity and failed; another exposed the native Open
split-button lookup error, which was corrected before the passing run. These
failed evidence directories remain; no assertion was removed or failure counted
as a pass. Global keyboard/real OLE drag checks cannot be made reliable while
someone else is interacting with the same desktop.

Redistribution follow-up
-----------------------

See the [release redistribution checklist](release-redistribution.md) for the
completed engineering checks and remaining owner/legal decisions. This work
does not authorize publishing a trial or paid release, select a source license,
add paid activation, change real trial state or install software.

Primary format references:
[Windows bitmap headers](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader),
[ImageMagick's pinned TGA coder](https://github.com/ImageMagick/ImageMagick/blob/fb965f1b54a65ddb633f8c2eac4452c782c66d7f/coders/tga.c).
