Curated Image Engine Evaluation
==============================

Date: 2026-09-07
Status: historical isolated-prototype evidence; prototype passed

The subsequent approved [production integration](bmp-tga-and-engine-integration.md)
now uses this candidate and adds bounded BMP/TGA conversion. Statements below
about unchanged stock production describe the isolated prototype stage, not the
current build. Commercial release clearance remains separate.

Recommendation: **GO to a separately reviewed production-packaging integration**.
The existing managed API and application behavior work with a small native link
overlay. No codec fork or application architecture change was required. Do not
treat this as permission to distribute either the candidate or the stock bundle.

Scope and dependency evidence
-----------------------------

The candidate retains ImageMagick/Magick.Native, JPEG including the 12/16-bit
build variants, PNG, WebP, Little CMS, zlib and libxml2. XML must remain for the
existing XMP metadata preservation contract. BMP/TGA built-in coders report read
and write availability; application support is still unimplemented.

The linker map identifies exactly those eight dependency libraries plus
MagickCore, MagickWand and coders. Other map owners are the checked Microsoft
runtime and Windows import-library set. Generated delegate defines agree with
the allowlist, including Windows GDI. No HEVC/H.264, RAW, SVG text-rendering or
liquid-rescaling dependency libraries enter this candidate's checked link map.
Built-in coders are not aggressively stripped. Runtime `gslib`/`ps` capability
strings remain because of external Ghostscript support paths; they do not mean
Ghostscript was linked or bundled. The application's restricted coder/delegate
policy remains necessary. This is not a sandbox or a general-format release.

Sources and maintenance
----------------------

- Magick.NET managed package: Q16-x64/Core 14.17.1, unchanged.
- Magick.Native source: `77c935e38d379b22d981770a91c466c16cba148b`.
- ImageMagick source: `fb965f1b54a65ddb633f8c2eac4452c782c66d7f` (7.1.2-31).
- Upstream static dependency artifact: `2026.09.01.0503`, Windows x64,
  no OpenMP, linked runtime. Configure release: `2026.08.23.0743`.
- Local build: Visual Studio 2026 Build Tools/MSBuild `18.9.1.35102`,
  MSVC toolset `14.51.36231`, Windows 11 x64.

The [recipe](../tools/curated-engine/README.md) and
[dependency source inventory](../tools/curated-engine/dependency-sources.json)
retain exact download hashes and component revisions. Native/ImageMagick sources
were built locally; dependency static libraries came from the hash-verified
upstream artifact. This is not an all-dependencies-from-source reproducibility
claim. Upstream's JPEG 12/16 repositories contain configuration referring back to
the same jpeg-turbo source, not separate unlicensed codec implementations.

Maintenance surface: removal of 29 `MAGICK_NATIVE_LINK_LIB` lines and a small
generated MSBuild/configuration overlay. No codec algorithms changed. Six unused
delay-load linker warnings remain (DNSAPI, IPHLPAPI, MSIMG32, ole32, SHELL32,
WS2_32); neither build failed. The curated notice records ImageMagick's generated
2026-09-06 date, whereas the stock notice used 2026-09-03. Pin source identity;
do not assume equal version labels mean identical builds.

Measured results
----------------

| Build | Native bytes | SHA256 |
| --- | ---: | --- |
| Stock production | 24,246,960 | `14A0992B54E236E37603DA18AE7B9936E3B3F490EADCC6D99BD13CCC1470B2C6` |
| Candidate `prototype-2` | 5,959,168 | `2B74DB7BA2F1B25BFFFDE9F89E09F9D3BE53C82FFBFF1A2F386CB8CB9DAE7637` |
| Fresh rebuild `repeat-1` | 5,959,168 | `5514110A62EE88E60B319E508BAD0F786DF3D3F1283BD9614D937C26A1BFEB9F` |

The candidate is about 75.4% smaller. Both fresh builds succeeded and each passed
235 real-image contracts. Different hashes mean **not byte-for-byte reproducible**;
timestamp/debug-path differences have not been exhaustively isolated. Neither
size reduction nor equal test results prove legal clearance.

The primary candidate passed 280 foundation/integration contracts and 26
conversion UI contracts with isolated trial state. The separate module probe
verified the loaded DLL hash and PNG/JPEG/WebP/BMP/TGA coder availability.
Nine packaging contracts pass: valid candidate acceptance; rejection of stock,
nested stock, extra known-style library, arbitrary external library, extra
delegate, absent notice, stock notice and modified native bytes.

Later scripted reruns again passed 235 engine and 280 integration checks, but
the UI did not repeat cleanly: one run timed out focusing the native file-name
field; another passed that step and timed out recognizing the completed output
folder in Explorer. These failures are retained and not counted as passes. The
earlier complete 26-check run establishes bounded compatibility, not reliable UI
automation. Recheck these interactive steps before promotion; the newer combined
run must not be described as green. No UI assertions were weakened or skipped.

Local evidence is intentionally retained in ignored `.codex-temp/curated-engine/`:

- `prototype-2/preparation.json`, `build-result.json`, `*-build.log`, native
  `.map`, generated configuration and source license files.
- `prototype-2/coder-probe.json` records the actual loaded module.
- `prototype-2/results/engine-d80d58e440c5462f8686a44212e089d5` and
  `repeat-1/results/engine-8ee762f8d06a4d06bde4812ea2033321` are passed engine runs.
- `prototype-2/ui-results/images-22265e78504b483d9a7ef3c0d4def47d`
  retains the passed conversion UI run.
- `prototype-2/guard-tests-d0af99d128f844de9957d27a3b64a1b5/result.txt`
  records the nine guards with the final notices.
- `verified-output` under each successful build contains the final isolated
  payload and curated notices, including the pinned WebP patent grant.
- `verification-*` directories retain subsequent scripted suite logs and
  successful summaries; missing summary means a run did not finish successfully.
  Specifically, `verification-b91cbb38447f4785b9e2965b6d7d1bcf` and
  `verification-4bdfb8daee0a46c19c88d9443d919c66` retain the two UI failures above.
  `verification-e5691ed5301b4fcf8e4e53592bb1a9ad/result.txt` records the final
  successful engine/integration-only run (235/280), not a UI pass.

Earlier failed/invalid experiments are retained, not counted as passes:
`prototype-1` first loaded a nested stock NuGet native copy; that initial green
test run is invalid as curated evidence. After correcting native selection,
omitting XML failed XMP handling. The final recipe flattens the payload and
retains XML; no metadata test or application behavior was weakened to pass.

Redistribution review and remaining gates
----------------------------------------

Generated curated notices retain ImageMagick, Magick.Native/managed Apache
licensing, JPEG's mixed BSD/IJG/zlib notices, Little CMS MIT, libpng, WebP BSD,
libxml2 notices and zlib. The separate pinned
[WebP patent grant](https://github.com/ImageMagick/webp/blob/b981ef267195cb12f2cb97e4dd23e12a1323a4ce/PATENTS)
is included without treating it as universal patent clearance.

This dependency selection removes the stock bundle's unused codec families from
the inspected link inputs; it does not waive obligations for retained code.
No GPL/LGPL component appears in this bounded inventory. Before distribution:

1. Review the exact retained source/file notices, generated notice completeness,
   attribution and modified-build disclosure. Archive the pinned inputs and recipe.
   Any source/rebuild obligations must be assessed from the actual components;
   do not assume every dependency requires an LGPL relinking kit or none could.
2. Confirm Microsoft toolchain/static-runtime redistribution rights under the
   applicable installed terms. Framework-dependent .NET distribution is separate
   from bundling its runtime. Inventory the full application, not just Magick.
3. Resolve any applicable codec/patent questions for the intended distribution.
   This engineering report is not legal advice or counsel sign-off.
4. Integrate a reviewed candidate and its notices explicitly into production
   packaging, with a complete file inventory and build-evidence hashes. Retain
   the stock development fallback; do not silently change the shipped payload.
5. Rerun existing safety/media/UI tests against that final package, check CI,
   signing/update/install behavior, and finish paid activation separately.

DDS engine selection and BMP/TGA variant policies remain separate decisions.
No new formats, production binaries, private code, real trial state, Polar state,
installed shell registration or release artifacts were changed by this prototype.
