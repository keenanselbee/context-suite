Audio Source and Runtime Review
================================

Reviewed locally: 2026-09-11. The restricted audio candidate now has verified
source/runtime review archives and a successful rebuild from the extracted source
kit. Production adoption and redistribution approval remain open. This work does
not install tools, change Explorer, publish artifacts or select product terms.


Reproduce the review
--------------------

From the repository root, with the retained source cache and accepted candidate:

```powershell
python -B tools/audio-engine/Prepare-AudioDistribution.py `
  --candidate '<completed curated FFmpeg workspace>' `
  --sources '<retained source cache>'
python -B tools/audio-engine/Verify-AudioDistribution.py '<printed review directory>'
python -B tools/audio-engine/Test-AudioDistribution.py '<printed review directory>'
```

Preparation requires the exact seven files in
[curated-candidate.json](../tools/audio-engine/curated-candidate.json). It reruns
native review, verifies dependency source/recipe/test receipts, and compares all
10,422 FFmpeg source files with the pinned original archive. It creates fresh
repository scratch output with explicit archive membership. Private application
source, supplier binaries and installed state are not inputs to either archive.

The verifier reads the ZIPs without extracting or executing them. It checks
membership, lengths, hashes, original source pins, current authored recipes,
runtime identity, retained evidence and notice bytes against the source archives.
The inventory is an engineering record, not a signed provenance attestation or
proof that the notice review is legally complete.


Contents and evidence
---------------------

The reviewed output is
`.codex-temp/audio-distribution-7348685650fd480e971d36d58aa45ce5`.

| Archive | Bytes | Entries | Contents |
| --- | ---: | ---: | --- |
| `audio-source-review.zip` | 39,454,989 | 29 | Eight original archives, 20 authored recipe/tool files and build instructions |
| `audio-runtime-review.zip` | 2,323,655 | 18 | Seven runtime files, seven root licenses, IJG and collected source notices, product notice and runtime inventory |

Source ZIP SHA-256:
`CDEEDA647FA2D461161B6AA7A0010FC76C60E7A721374E012723BD0BA81FB39D`.
Runtime ZIP SHA-256:
`727696DF4390A905D929F6010FD637BBAA08BF6E1D78AB344BAFC0283E43A5B4`.
The uncompressed seven-file runtime remains 4,844,544 bytes; its exact identities
and 298 adapter/116 worker results are in [the build record](audio-ffmpeg-build.md).
Those media suites were not rerun for this archive-only checkpoint.

The source kit retains FFmpeg, Opus, Ogg, Vorbis, stable LAME, zlib, GNU make and
NASM archives, totaling 39,417,954 bytes. The ten-input research manifest also
records the historical supplier recipe and alpha LAME SVN export. Neither is
needed by this selected build. `Prepare-AudioSources.ps1 -BuildInputsOnly` verifies
the eight selected archives; its default still verifies all ten research inputs.

Build/configuration/version/source records and six dependency manifests, source
inventories and test logs remain beside the ZIPs under `evidence/` (27 files).
The inventory records their hashes. Evidence contains local build paths and is
not automatically added to either deliverable archive.

Ten distribution checks pass at
`.codex-temp/audio-distribution-tests-eb250c9137da4138b0eb736225008a92`.
They accept the original and reject a missing license, extra synthetic private
path, changed runtime/source/recipe/notice bytes, duplicate entry, changed build
evidence and changed notice inventory. Archive mutations recompute their outer
hashes to exercise the trusted membership/content checks. No private source is
used by the canary. The original bundle passes again afterward.


Rebuild from the extracted kit
-----------------------------

The source ZIP was checked entry by entry and extracted to
`.codex-temp/rb-56b63af4`. Its own
[Rebuild-AudioSource.ps1](../tools/audio-engine/Rebuild-AudioSource.ps1) built all
six dependency groups, ran their tests, built FFmpeg and passed native review.
No previously compiled codec libraries were copied into that kit.

The completed FFmpeg workspace is the kit's
`.codex-temp/audio-ffmpeg-56e493a8c74d4cb5aad27e645fbbaf54`.
Its `build-review-2c0c8c83228f410b8c3e68f058316a63` accepts the eleven reviewed
diagnostics and seven hardened PE files. The command log is
`.codex-temp/audio-source-kit-rebuild-56b63af4.log` in the main repository.
All 29 extracted kit inputs remain byte-identical afterward. The source ZIP used
for this build is byte-identical to the current reviewed source ZIP above.

This verifies that the retained inputs and recipes can rebuild in the existing
Windows tool environment. It does not promise identical binaries: configure paths
and PE build metadata can differ. It does not establish a clean-machine build or
repeat the adapter/worker media suite against the rebuilt identity. The private
runtime pin still selects the previously tested candidate.


Notice review and remaining decisions
------------------------------------

The runtime includes original FFmpeg LGPL 2.1 and LICENSE texts, Xiph notices,
LAME's GNU Library GPL version 2 text, zlib's license, and the IJG source notice
with an authored accompanying acknowledgment. FFmpeg and LAME source headers
permit their stated later license versions; selecting the disabled GPL/nonfree
build options does not remove LGPL obligations. FFmpeg's own
[distribution guidance](https://ffmpeg.org/legal.html) calls for corresponding
source, build information and appropriate notices/terms, including linked external
libraries. The local kit supplies engineering inputs for that review.

Compiler dependency records identify 1,029 source/header inputs. Collection
retains 1,014 comment notices, including notices after include guards. Fifteen
inputs have no collected notice: nine FFmpeg shared-implementation wrappers,
LAME `id3tag.h`, Opus `static_modes_float.h` and `mlp_data.c`, and zlib's
`crc32.h`, `inffixed.h` and `trees.h`. The complete original files, root licenses
and inventories remain available; absence of a collected comment is not a
conclusion about licensing. Collection is a review aid, not a complete legal
classification of every source file.

Opus includes its ordinary retained generated analysis tables, including
`mlp_data.c`. No separately downloaded DNN/DRED/OSCE/deep-PLC model archive is
needed by this build. Do not describe the compiled codec as containing no models
or neural analysis code.

Before distribution, settle the public build scripts' license, product terms,
source hosting/delivery, user notice access, modified-library use and Microsoft
tool/runtime eligibility. The application currently checks exact runtime hashes;
the rebuilt executables work independently but are not automatically admitted by
the app. Review that design against the applicable license obligations before
adoption; this checkpoint does not change runtime trust policy. Complete the
file-specific notice and intended-market review as well. No public source URL or
codec/patent clearance is implied. See [release redistribution](release-redistribution.md)
and the kit's [build instructions](../tools/audio-engine/distribution/BUILD.md).
