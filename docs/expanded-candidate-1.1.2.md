Expanded Candidate 1.1.2
========================

Status: unsigned candidate built and archive verified on 2026-09-23; exact-package
execution and performance acceptance are in progress. This is an internal owner
review candidate, not release clearance, installation or publication.


Candidate identity
------------------

| Item | Value |
| --- | --- |
| Version | 1.1.2; managed/file/package metadata 1.1.2.0 |
| Public revision | `abf0ab8` |
| Private revision | `8e70ce3` |
| Production stage | `artifacts/production-staging/5d97eab3d78e4e6199717450ad835264` |
| Release directory | `artifacts/release-candidates/901e6ef81e1c4a7b96910857e3bd2214/ContextSuite` |
| ZIP beside release directory | `ContextSuite-win-x64-unsigned.zip` |
| ZIP bytes | 241,265,726 |
| ZIP SHA-256 | `D54741155C60413DB13DF81A6D0D4E91CFF0ECA058EA85303549DA825F855F35` |
| Application payload bytes | 611,834,443 |
| Complete extracted candidate bytes | 613,033,493 |
| Production inventory SHA-256 | `76304F87AD3BC9A5F47C1CABCB2962BF31BDA783EA3DB85D87B87D7869E5E8ED` |
| Release manifest SHA-256 | `FBDCB86EB7A8AEA83FEB9E7D5185D7FBC88263F9E59274FB6E9BA7AA917ADA25` |

The exact build inputs, command, output and receipt are retained at
`.codex-temp/expanded-candidate-112-9e6a6dc159944beca7b495de616e2bcb`.
The canonical release workflow ran with all seven explicit engine inputs and
`-AllowDirty`: unrelated pre-existing worktree changes are preserved, and the
manifest truthfully records dirty provenance. The preparation record hashes the
actual build sources and verifies them unchanged afterward; the candidate is
not represented as a clean-checkout release.

The build completed in 304.4 seconds with zero managed warnings/errors.
Managed output/intermediates and shell
output used fresh directories. The completed Office, DDS and other selected
native engines were reused after their source/hash guards passed. All prior
version receipts and the reserved 1.1.1 inventory remain unchanged. No earlier
candidate was repaired or overwritten.

The manifest selects expanded audio, Windows Voice, PDF, Office runtime04 and
HEIF in addition to the existing image engines. It includes the supported local
HEIF/audio library-replacement implementation. Official package verification
still rejects recipient override manifests. It checks the original selected
libraries, not recipient-modified copies.

Production allowlists, dependency separation, engine identities, source records,
all 63 notice links and product metadata passed. The release wrapper compressed,
extracted and verified the archive again. This establishes package integrity;
it does not replace execution, performance or human acceptance.


Source companion and acceptance
--------------------------------

The [consolidated source/notice packet](candidate-distribution-review.md) contains
nine unchanged archives totaling 1,343,773,485 bytes, including duplicate source
content. It remains separate from the application download. Its standalone
instructions and manifest helper document compatible local library replacement.

`source-packet-binding.json` in the build evidence binds all 104 packet files,
the nine archive identities and this candidate's archive/manifest hashes.
All 63 notice files plus their offline index match the packaged application
byte-for-byte. Source hosting, product/source terms, Microsoft eligibility and
engine/market adoption are still owner decisions; no delivery approval is inferred.

The [finite acceptance contract](local-candidate-acceptance.md) remains the
completion boundary. Execution must identify the worker inside this release
directory. Existing source-linked application contract hosts are bound to the
same production build; they do not establish visible WPF acceptance. Historical
green results are not counted as fresh A01-A08 results.

The first core acceptance preparation stopped before tests because its wrapper
incorrectly expected a referenced WPF application DLL in the contract host.
The existing host compiles selected application sources directly and references
Core. The corrected wrapper verifies the matching Core DLL and unchanged build
sources, then runs the exact release worker. It changes neither the application
nor test assertions. Its first unused scratch directory remains retained.

The first exact-package replay passes all 19 core commands. Evidence is
`.codex-temp/candidate-112-core-3024b4821927443f963c7401c4f3b991`:

| Scope | Fresh result |
| --- | --- |
| Foundation and existing image-worker integration | 4,705 checks, including worker loss/interruption and recovery |
| Baseline audio analysis, FLAC, conversion/direct commands, interruption and publication crash | 309 checks across eight suites |
| PDF analysis, optimization, failure/direct/page operations, combined images and geometry | 443 checks across ten suites |

The command receipt identifies the worker under the release directory, not a
rebuilt substitute. Package membership, every candidate file, test-host binaries
and original fixture files match their captured identities afterward. The
source-linked host came from the same production solution build, with an exact
matching Core DLL. These results cover A01 and the baseline A04/A06 workflows;
they do not close expanded audio, additional metadata/artwork matrices, Office,
performance, visible UI or the whole acceptance contract.

The HEIF replay at
`.codex-temp/candidate-112-heif-2d1736b0eeba4cdc92b3c2cba7172203` also passes:
1,181 authored worker/application checks with 336 outputs, 659 upstream/color
checks with 104 outputs, and 38 interruption/recovery checks across six observed
native codec executions. Successful work runs through the exact archive worker.
Missing/corrupt-provider controls use separate disposable copies; the candidate
is never damaged. All bound inputs and package files are unchanged afterward.
Standalone native/loader evidence remains separately scoped, not falsely counted
as a fresh packaged-worker run.

Exact A01-A08/performance completion, consolidated media review, real documents
and recordings, D09 typography, affirmative attended network evidence, UI/theme/
DPI/screen-reader observations and separate installed lifecycle/commerce gates
remain open. Signing stays deferred. The active goal is not complete.
