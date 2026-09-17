HEIF Source And Runtime Review
==============================

Status: local engineering evidence, 2026-09-16; commercial redistribution and
production adoption remain open. The [input evaluation](heif-input-evaluation.md)
owns media boundaries. These bundles do not replace reserved candidate 1.1.1.


Retained material
-----------------

The [review tool](../tools/heif-engine/Review-HeifDistribution.py) prepares exact
source/runtime ZIP membership from verified local build inputs. It retains the
original libheif 1.23.4 and libde265 1.1.3 source archives, the local libheif patch,
four build-recipe files, selected AOM artifacts and the pinned AOM source archive.
Private source, test fixtures and fixture encoders are absent. The private bridge
binary appears only in the runtime review bundle.

Both library archives retain their original COPYING files with LGPL/GPL texts.
The library source headers identify LGPL version 3 or later; the permissive
example/wrapper licenses do not describe the whole library. AOM's supplier notice
does not include its separate PATENTS document, so the runtime review bundle
also includes the original AOM LICENSE, PATENTS and AUTHORS files. The source
archive is pinned to ImageMagick/aom revision
`79de389e4abced53cb6e85b7574826dbc227cd9c`, alongside the selected 3.15.0 artifact.
See [source identity](../tools/heif-engine/distribution-inputs.json) and
[runtime identities](../tools/heif-engine/distribution-candidate.json).

The collected notices cover 1,664 source files and license documents from the
archives. This deliberately inclusive collection is not a precise linked-object
inventory or a completed file-by-file redistribution assessment. It collects
license files and qualifying C-style comment blocks; the original archives
remain authoritative for all source contents and notices.

Local bundle evidence: `.codex-temp/heif-distribution/94369d7ff2d04bd2b49178cdff27f2dd`.

| Archive | Files | Bytes | SHA-256 |
| --- | --- | --- | --- |
| `heif-source-review.zip` | 23 | 21,666,444 | `6D8520C71C56597010F7BA6409D184D0B206B841B08D146ED26DCEB63716C084` |
| `heif-runtime-review.zip` | 12 | 1,565,244 | `4D5EECAC5ACA27A22E08347C7E9FC7EF0C1F7271389064C4A235F48EE41831C6` |

Verification recomputes membership, binary/source identities and derived notices
against the repository's pinned inputs rather than trusting the editable bundle
inventory. It refuses duplicate, traversing, linked and oversized ZIP entries.
The [mutation suite](../tools/heif-engine/Test-HeifDistribution.py) passes all
14 checks at `.codex-temp/heif-distribution-tests/630df9de43104733b459fc9696c78a01`.
Changed binaries, archives, recipes, AOM artifacts, notices and patches, missing
licenses, extra synthetic private-path content and unsafe membership are refused
even when the editable inventory is rewritten. Verification executes no DLLs
and extracts no archive entries; the original bundle remains unchanged.


Rebuild and media verification
-----------------------------

The build recipe now supports `--libraries-only`, omitting private-source reads
and the bridge build. The [source-kit runner](../tools/heif-engine/Rebuild-HeifSource.py)
first verifies the bundle, extracts its exact members into fresh repository
scratch, rebuilds both LGPL DLLs, and checks that all kit inputs remain unchanged
and no private checkout is present. It retains commands, exits and logs.

The rebuild passed at `.codex-temp/hr-52e7c0331bcd`; its nested build is
`heif-engine/141200c0af844486ba88d47d5cf038e2`. AOM's pinned static artifact is
reused, not rebuilt. This proves a library-only build from the kit, not media
acceptance of those rebuilt identities or byte-for-byte reproducibility.
Compiler/path/build metadata can change DLL identities.

The full candidate was separately rebuilt with the current recipe at
`.codex-temp/heif-engine/d515d8f8c6c94713b3a4d61e88d19aea`. Its 884 source files
match the original/patch inventories. Direct DLL dependencies remain the other
listed decoder DLLs and KERNEL32. It retains the upstream C4805 bool/byte warning
in libheif `context.cc`; managed builds have zero warnings/errors.
The private loader and runtime review pins select this full candidate:

| File | Bytes | SHA-256 |
| --- | --- | --- |
| `ContextSuite.Heif.Native.dll` | 130,048 | `55AC6DF9292D876C5689627563E846DD1F21A1CBCF5F4799879CFF8D9F71CF09` |
| `heif.dll` | 2,950,144 | `9231D312757F12681C94458F8073EE6B1392C7177384C4E84E080B20CC33B526` |
| `libde265.dll` | 556,032 | `A8F37EDFA42FAC0427A62E6A827DC032CE7DA0BD40AE3BC96E0E5B41245A0124` |

| Verification | Evidence under `.codex-temp/` | Result |
| --- | --- | --- |
| Bridge and resource-limit recovery | `heif-bridge-tests/7563c1dbd6ef47a5bb2d4150ae8f3082` | 3,634 checks |
| Worker/application publication | `heif-worker/7adc2218de034d1f866d8fdc4281e4b8` | 1,181 checks; 336 outputs |
| Active-native interruption | Same worker run, `interruption/results.json` | 38 checks across six cases |
| Loader ownership/refusals | `heif-loader/525dffc93032424a8bb10fb1ab5215b0` | 25 checks |
| Managed conversion and SDR appearance | `heif-conversion/bfbeb4fa719e40d8a7e2d394129d4748` | 2,819 checks; 336 outputs |

The existing 942 image, 4,130 foundation and 15 Office lease checks were not rerun
for this recipe/pin checkpoint. Broader realistic-file interoperability, visible
acceptance, budgets and production packaging remain required.


Remaining release decisions
---------------------------

The source kit does not settle modified-library use with the application's
current hash checks. Corresponding-source delivery, product/source license terms,
customer notice access, complete linked-component review, Microsoft tooling
eligibility and intended-market patent questions remain explicit release gates.
No public source URL or commercial clearance is implied. Continue the shared
[redistribution checklist](release-redistribution.md) before distribution.
