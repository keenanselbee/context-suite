Combined Expanded Input Verification
====================================

Status: scratch composition and focused image/audio command replay pass,
2026-09-19. This is not the final versioned candidate or release acceptance.

The new scratch payload combines the existing image/DDS/PNG/palette, PDF and
Office engines with the reviewed HEIC/AVIF decoder, expanded AIFF/WMA audio
runtime and Windows Voice provider. Current application/worker binaries include
the interim Excel chart refusal. Reserved candidate 1.1.1 and its version receipt
remain unchanged; no installation or Explorer registration was performed.


Composition and identities
--------------------------

Run from the repository, using the retained review bundles and generated fixture
manifest described by the individual engine records:

```powershell
python -B .\tools\Test-CombinedInputs.py `
  --base-payload '<retained inventoried candidate>' `
  --heif-distribution '<reviewed HEIF bundle>' `
  --audio-distribution '<reviewed expanded audio bundle>' `
  --voice-distribution '<reviewed Windows Voice bundle>' `
  --audio-manifest '<retained expanded-audio fixtures.json>'
```

The harness verifies the baseline inventory and full engine allowlist before
copying. It omits old audio/HEIF directories and the old inventory only from the
new copy, stages complete reviewed bundles, refreshes managed binaries, verifies
the combined allowlist and writes a new inventory. The absent-Voice control is
a separate minimal worker. The actual candidate stays unchanged during command
execution. Original baseline, bundles, source files, manifest and control hashes
are checked afterward. Failures retain their scratch files; no version is reserved.

Passing evidence is
`.codex-temp/combined-inputs/e549e5e3509942b4b8ec8e0d6545f93a`, with the worker
under `combined/ContextSuite.Worker.exe`. Its inventory SHA-256 is:

```text
ABA4BCE94D364BC6327BB8545D884D636D0B2A31E031DEBF8186E414D10AE5A6
```

There are **19,467 files including the inventory**, totaling **1,583,367,075
bytes**. This passes the predeclared 1.65-GB engineering ceiling; owner acceptance
of the footprint remains pending.

| Group | Bytes excluding the top-level inventory |
| --- | ---: |
| Office host/runtime | 1,519,684,544 |
| PDF renderer | 16,605,058 |
| PDF structure engine | 9,119,512 |
| Combined-image PDF validator | 9,178,839 |
| Expanded audio and Voice | 6,448,013 |
| HEIC/AVIF decoder | 5,062,827 |
| Top-level image engines, managed binaries and notices | 13,392,840 |
| Shell assets | 11,133 |

The local ZIP measurement is **522,645,230 bytes** (522.6 MB decimal). All 19,467
archive members have the exact expected names, sizes and SHA-256 identities;
the source payload remains unchanged. Archive SHA-256:

```text
A28091100B5D4221555F17F8DB4875B9F3D0617160EFF10A45255A4B871D0F17
```

The measurement receipt is `archive-review.json` alongside the scratch payload.
Use `python -B .\tools\Measure-CombinedArchive.py '<completed combined evidence>'`
to create and check a local review ZIP. Existing archives are refused. This ZIP
is not an installer or published artifact, and its compressed size is not a
promise about the eventual installer/download.


Command replay
--------------

The exact combined worker passes **211 expanded-audio direct-command checks**:
seven AIFF/AIFF-C/WMA variants across six targets create 42 copies, with five more
from mixed-selection, retry and absent-provider cases. Existing quality consent,
access refresh, one batch admission, quiet success, duplicate-retry suppression,
copy naming, source preservation and publication cleanup remain covered. The
candidate's 20 audio/Voice files pass their pinned verification before and after.

The unchanged worker also passes **659 HEIC/AVIF interoperability checks** and
publishes **104 JPEG/PNG copies** in
`.codex-temp/heif-interop/7afd5e69448c40b6b6fef0b89ea7445b`. The receipt explicitly
records `PackagedExecution: true`. Ten upstream inputs and 42 authored SDR color
variants produce both targets; unsupported/HDR cases retain typed refusals and
same-worker recovery. Pixel/alpha/orientation, matte, precision/profile and
original-preservation checks remain those of the existing frozen corpus.
Inputs and full payload identities remain unchanged.

Application, worker and contract-host Release builds have zero warnings/errors.
`python -B .\tools\Test-CombinedInputsGuards.py` passes ten checks, including six
early-refusal cases:
outside-scratch and traversal paths, missing fixture manifest, changed baseline
bytes, duplicate inventory entries and extra baseline files. Archive controls
verify complete hashed membership, unchanged existing archives and refusal of
changed payload/inventory bytes, also with Python optimization enabled. Retained
guard evidence is `combined-input-guards/9657fb0068d54736ad2dcb54fd650ed6` under scratch.
These checks do not count as another general foundation or complete media replay.


Remaining acceptance
--------------------

This closes the missing combined HEIF/expanded-audio/Voice composition evidence.
It does not close all eight automated families in the
[local acceptance contract](local-candidate-acceptance.md). The
[mixed Office/image PDF replay](office-direct-command.md#expanded-combined-worker-replay-2026-09-19)
now passes 15 direct-command checks, five complete disposable profile retirements
and independent text/geometry/pixel comparison of the three normal Office PDFs.
The remaining Office/PDF regression suites, full expanded worker/metadata/recovery corpus,
human media/workflow/environment review, final performance replay and a fresh
formal versioned candidate remain required.

The [Excel chart guard](excel-layout-acceptance.md) prevents known incomplete
output but does not satisfy D07. Chart rendering/security, affirmative Office
network enforcement, PowerPoint font-size policy and approved real-file review
remain open. Source delivery, modified-library use, Microsoft eligibility and
other engine adoption decisions remain separate. No signing, live commerce,
installation, publication or private upload is implied by these results.
