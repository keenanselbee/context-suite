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

The expanded audio replay at
`.codex-temp/candidate-112-expanded-audio-e042cbedf18440799c119c2f638bba42`
passes 1,008 worker checks across 144 inputs and six targets, with 796 validated
copies. The remaining target decisions are the declared layout/rate or excessive
signal-error refusals; no quality threshold changed. It also passes 173 metadata
command checks and 265 recovery checks, including all 30 observed native-phase
interruptions and successful same-source retries. The same production-build host
and exact release worker ran these suites; package, host and fixture identities
and package/host membership are unchanged afterward.

Direct-command evidence at
`.codex-temp/candidate-112-audio-direct-4114eb4f7d7c43ba8b9366220bb1fff4`
passes 211 checks, including mixed selections, access refresh/retry and absent
Voice-provider refusal. A test-only optional argument supplies the disposable
missing-provider worker path. Successful commands still use the release worker;
no negative-control files are written into the candidate. The test host was
rebuilt with zero warnings/errors for this argument and paired, in a disposable
copy, with the exact shipped Core DLL. Production build-source hashes are
unchanged. Its first preparation stopped before execution because the newly
compiled Core recorded the later documentation commit in its product version;
that attempt is retained, not counted as a product failure or pass.

The direct evidence also retains 78 independent supplier-decoder checks on the
metadata replay's 42 outputs: descriptive values and exact authored RGBA pixels
in all 36 covers, including partial transparency. The supplier runtime is checked
against its pinned archive before and after verification. These are scoped
generated-fixture checks, not human listening acceptance.

The exact packaged audio executables pass the existing 121-case decoder matrix
at `.codex-temp/candidate-112-surround-e3b9e478c1c044e1bbe6b60200f78fff`:
29 surround comparisons meet the unchanged per-channel limits, and 92 regression
outputs remain byte-identical. This native decoder replay is distinct from the
worker suites and does not approve FFmpeg's known Voice waveform behavior; the
application uses its separate Windows Voice provider. Candidate and reference
identities remain unchanged. An earlier wrapper retained an obsolete executable
path and failed before launching the decoder; its evidence is preserved.

The remaining baseline audio metadata/artwork replay at
`.codex-temp/candidate-112-audio-artwork-18188557b52b4b3ba147bce1b3d0d42d`
passes 588 checks across nine suites. These cover WAV text, WAV/MP3/M4A/Ogg
artwork input, WAV/MP3/M4A artwork output and the FLAC/Ogg direct-command matrix.
They use the original production-build host and exact release worker; all
captured package, host and fixture identities are unchanged afterward.

The sequential media performance replay at
`.codex-temp/candidate-112-media-performance-c978b199d8694144beb5b2d86400ed66`
passes all 90 declared operations on Windows 11 build 26200, an i9-9900K and
approximately 32 GiB RAM:

| Workload | Operations | Largest group median | Largest individual time | Median / maximum limit |
| --- | ---: | ---: | ---: | ---: |
| 60-second stereo audio, both rates and six targets | 60 | 4.972 s | 5.027 s | 15 / 30 s |
| Ten-page PDF export and ten-image combined PDF | 10 | 3.304 s | 3.783 s | 20 / 40 s |
| 12-megapixel HEIC/AVIF to JPEG/PNG | 20 | 7.161 s | 7.476 s | 10 / 20 s |

Each group contains five fresh-worker operations with application validation
and publication. The existing workload definitions and budgets are unchanged.
The reports retain individual samples, output/source hashes and managed-worker
peak memory; they do not measure whole native-job memory, cold storage, visible
UI responsiveness or human acceptance. All captured package, host and retained
fixture identities and package/host membership match afterward. The first
wrapper attempt omitted the audio fixture directory and stopped before media
conversion; its failed preparation receipt remains retained.

Analyze performance and bounded-read acceptance passes at
`.codex-temp/candidate-112-analyze-2fb82033bb764dbf8e42f69b9400554a`.
The original twelve generic cases run in 36 fresh processes with eleven samples
each, using the unchanged production-build host and the existing fixture/assertion
code with only its host path redirected. All identity, per-file failure, original
preservation, inspection, timing and allocation checks pass. The maximum
4,096-file selection has a 3.802-second first-call median and 3.023-second
repeated p95, within the existing 8/5-second review budgets. Repeated median
allocation is 260,350,136 bytes, below the 384-MiB review trigger; this is
cumulative managed allocation, not retained memory.

The seven optional audio/PDF cases pass 35 fresh sessions and 210 Analyze
commands against the exact packaged worker. The largest first-call median is
0.635 seconds; the largest repeated p95 is 0.043 seconds, below the respective
2/1-second limits. Retained generated fixtures are checked against their earlier
receipts before copying. Package, host and captured input identities remain
unchanged. Generic analysis does not start a media worker; optional analysis
does. Neither benchmark flushes caches or establishes visible UI responsiveness.
An initial prerequisite check ran before the preceding media wrapper had written
its final hash-verification receipt and stopped without starting Analyze; the
completed replay began only after that wrapper exited successfully.

The fifteen Office performance operations pass at
`.codex-temp/office-performance/695432bb24a34eeeba84bf9698109e01`:

| Ordinary document | Median | Maximum | Median / maximum limit |
| --- | ---: | ---: | ---: |
| Two-page DOCX | 6.593 s | 8.567 s | 45 / 75 s |
| One-page XLSX, saved values | 6.974 s | 7.154 s | 45 / 75 s |
| Two-slide PPTX | 6.585 s | 6.784 s | 45 / 75 s |

All fifteen PDFs pass the existing page geometry and authored-text checks.
Every original retains its bytes and timestamp. Each native profile, registry
mapping and owned working directory is removed; publication and worker scratch
are empty after disposal. The exact release worker and original production-build
host are unchanged, together with the captured build sources and package
membership. Timings include isolation, runtime hashing, validation and
publication; they do not close broader Office fidelity or network acceptance.

The authored Office fidelity replay is complete at
`.codex-temp/candidate-112-office-fidelity-215ad0a72310449bac2b8611b22d0aac`,
but its overall result remains **failed** because D09 still exceeds the existing
typography tolerance. All 165 application checks and 43 profile/context removals
pass. Independent qpdf/PDFium inspection reports:

| Suite | Documents | Independent result |
| --- | ---: | --- |
| Word font/style controls | 15 | Text, font faces, exact style controls and distinct controls pass |
| Word tracked changes | 13 | Text and accepted fidelity pass; the existing moved-text exact-pixel difference remains recorded |
| Word paragraph revisions | 6 | Text, paragraph layout and exact final/control pixels pass |
| Word multilingual and anchored image | 1 | Combining accents, CJK/RTL placement, font programs, image and negative controls pass |
| Excel chart/merged/wrapped layout | 4 | Text, chart values/labels, geometry and negative controls pass |
| PowerPoint slide order/visibility | 3 | Independent slide-content and ordering checks pass |
| PowerPoint image/table/typography | 1 | Text, placement and image checks pass; font-size and aggregate negative-control acceptance fail |

D09 still measures `20.01300048828125` points for a 20-point run, beyond the
unchanged 0.01-point limit. The first two pages pass every recorded layout check;
the third fails `FontsMatch`. The negative-control aggregate also remains false
because its baseline must first satisfy the same oracle. No tolerance is relaxed,
and no full Office fidelity pass is claimed. The narrow 0.015-point proposal
remains an owner decision, separate from the existing Word spacing allowance.

The original production-build host executes the exact release worker. The
independent managed inspector was built separately with zero warnings/errors;
its qpdf/PDFium binaries and probe sources were checked against their retained
identities. Candidate, host, inspector, selected system fonts and captured source
identities remain unchanged. No native engine build or product change occurred.

The direct-command and recovery/capacity replay passes at
`.codex-temp/candidate-112-office-commands-b046669959ab441eab89930809fa012c`.
The mixed image/Office command suite passes 15 checks and retires all five
profiles. Independent inspection of its three ordinary Office PDFs passes
structure, authored text, geometry and exact retained control pixels. The
43-check recovery suite passes output collisions, publication failure,
pre-publication cancellation and cleanup-warning retry without duplicate
publication. All six profiles/contexts are retired and originals are unchanged.
Its 48-sheet workbook passes independent comparisons with three retained
controls: all four PDFs preserve the authored labels, page geometry and exact
pixels, with 144 page comparisons. Candidate, fixtures, inspection inputs and
captured source identities remain unchanged. This does not repeat or close the
separate unfinished-write, managed-validation or native network checks.

The date and font-decision replay passes at
`.codex-temp/candidate-112-office-decisions-db7192f5044040c18fc3b5bb6a731c3e`.
The date suite passes 32 application checks across six exports: saved values and
the admitted 1904 calculation result publish, while the two known early-1900
calculation errors remain withheld. Independent calculated-workbook and PDF
inspection passes. The font-choice suite passes 85 checks across thirteen
exports, with independent inspection of accepted publications and retained
decline/refusal behavior. All nineteen native profiles and contexts are removed.
The original production-build host and exact release worker are used; captured
package, fixture and inspection identities remain unchanged.

The [five Excel print cases](excel-print-layout-evaluation.md#final-candidate-application-replay-2026-09-23)
also pass exact-worker publication, source preservation and profile cleanup at
`.codex-temp/candidate-112-excel-print-b6475b7d4e06401da620548316239d3f`.
Independent inspection passes complete text/page order, repeated titles,
hidden/out-of-area exclusions and geometry across all eight PDF pages, with
negative controls. The existing four-chart inspector regression also passes.
Test-only modes were added to the managed command host and inspector, both built
without warnings/errors. The disposable host uses the shipped Core DLL; no
product or native-engine change occurred. All captured identities and candidate
membership remain unchanged. These results close the authored D05 print replay
and D06 date replay, not real-workbook or human acceptance.

The exact release worker also passes all 71 Office PDF validation checks at
`.codex-temp/candidate-112-office-validation-37fcd1e943d44b03837ca261d9df6211`.
These cover three retained runtime04 exports, fourteen refusal cases, active
cancellation/worker loss, empty failed reservations and successful following
requests. The retained XLSX calculated copy is hash-bound with its source and
PDF. A freshly built private contract host uses the shipped Core DLL; all
candidate files, test inputs and membership remain unchanged. No Office export
or profile is created by this validation-only replay. Native interruption,
startup recovery and resource evidence retain their separately recorded scope.

The packaged-library resource and exact-worker output-denial replay passes at
`.codex-temp/candidate-112-office-safety-e3c31cf536a047b5bd8744ba6ff059be`.
All 61 launcher pressure checks use the shipped Private/Core DLLs with the
retained authored helper. The 65 I/O checks include six real write refusals,
three following exports and removal of all nine profiles/mappings; 5,036
runtime/context ACL entries contain none of their SIDs. Independent inspection
passes the three following PDFs and 25 compiled-profile controls. No rebuild was
needed, and all captured inputs and candidate membership remain unchanged.

The [native interruption replay](office-worker-lifetime.md#exact-candidate-interruption-replay-2026-09-24)
passes 90 checks against the release worker. Nine following PDFs pass exact
independent comparisons; eighteen profiles/mappings are absent and 5,809 owned
ACL entries are clear of test SIDs. Its final receipt is
`.codex-temp/candidate-112-office-interruptions-70100b6c17d44a5e94989bc0755455d5/verification-02/results.json`.
The first independent inspection used the wrong profile variant; its failure is
retained. The existing updater-free inspector completed verification of the same
outputs without another native export or a relaxed assertion.

The [release/source verification](candidate-distribution-review.md#decisions-and-unfinished-work)
passes seventeen packaging checks and a separate reserved-version refusal at
`.codex-temp/candidate-112-release-acceptance-9eac27ed9acc4ac9aec9894e9500e131`.
All 104 companion files, nine source archives, 64 packaged notice/index matches,
reserved production inventory and prior version receipts remain unchanged.
Missing/altered files, duplicate/unsafe inventory entries and attempted staging
reuse are refused. This establishes those A08 packaging/source-binding results,
not recipient delivery or owner adoption. Two preparation-only wrapper errors
are retained separately; neither changed the candidate or source packet.

Exact A01-A08 completion, consolidated media review, real documents
and recordings, D09 typography, affirmative attended network evidence, UI/theme/
DPI/screen-reader observations and separate installed lifecycle/commerce gates
remain open. Signing stays deferred. The active goal is not complete.
