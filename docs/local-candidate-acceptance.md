Local Candidate Acceptance Contract
===================================

Revision 1, 2026-09-19. Status: remaining acceptance is bounded below, not complete.
This contract implements Milestones A-D of the [active goal](broad-file-support-goal.md).
It preserves required features and distinguishes automated evidence, human
acceptance and commercial release gates. The final replay must use one fresh
combined candidate; reserved 1.1.1 remains the immutable earlier baseline.
The owner-approved 2026-09-22 execution goal prepares the candidate for review:
finish authorized engineering and automated checks, then deliver one review
pack and decision list. Human acceptance below remains required for release;
preparation does not mark those results passed. Required engineering failures
still prevent review readiness, and external evidence dependencies remain named.
The [combined expanded-input scratch checkpoint](combined-expanded-inputs.md)
passes focused image/audio command replay and package measurement. Its 1.583-GB
unpacked/522.6-MB compressed size is measured evidence; owner footprint acceptance
and the complete final versioned-candidate replay remain open.
Its [mixed Office/image PDF command replay](office-direct-command.md#expanded-combined-worker-replay-2026-09-19)
also passes, including publication retry, five profile retirements and independent
ordinary-document comparisons. This is a scoped A06/A07 result, not their full
suite completion or Office chart/layout acceptance.


Capability freeze
-----------------

The [launch matrix](launch-capability-matrix.md) owns fixed operations/targets.
These are admission boundaries, not proof of fidelity for every admitted file.
The [paired Excel application replay](excel-layout-acceptance.md#paired-application-acceptance-2026-09-23)
preserves all four authored charts. Ordinary internal charts are admitted;
newer/external charts and incomplete inspection remain refused. The paired
[48-sheet capacity replay](excel-calculated-date-guard.md#paired-source-built-capacity-replay-2026-09-23)
also passes; D07 still needs final-candidate and human acceptance. The paired
[earlier interruption matrix](office-worker-lifetime.md#paired-source-built-interruption-replay-2026-09-23)
exposed missing PowerPoint text-box fill. The [package repair](office-source-build.md#ooxml-shape-data-packaging-repair)
restores exact historical rasters without native recompilation. Its
[current interruption replay](office-worker-lifetime.md#reconstructed-runtime-interruption-replay-2026-09-23)
passes all 90 safety checks and exact control pixels for nine following PDFs.
[Application startup recovery](office-startup-recovery.md#reconstructed-runtime-application-recovery-2026-09-23)
also passes, including cleanup and independent following-output inspection.
Incomplete workbook-write, remaining resource/security and final-candidate gates
remain open. This is separate from the D09 typography decision.

The [packaged Office source record](office-production-payload.md#packaged-source-identity-2026-09-23)
now binds the reconstructed engine to its source kit, patches, configuration and
selected toolchain. Packaging refuses a missing or altered record. This advances
A08 preparation without closing redistribution/adoption or exact final-candidate
acceptance; no native rebuild or reserved-package change was needed.

| Family | Required candidate scope | Preservation boundary |
| --- | --- | --- |
| Existing images/DDS | Existing 27 conditional pairs and fixed PNG presets | Retain color, orientation, matte, DDS choices and smaller-result rules |
| HEIC/AVIF | Supported single SDR images and grids; 8/10/12-bit decoding; JPEG/PNG outputs | Preserve supported color/ICC/alpha/geometry; refuse HDR, collections/sequences, depth/unsupported auxiliaries and inconsistent alpha transforms |
| AIFF/AIFF-C | PCM8/16/24/32, NONE, sowt PCM16, big-endian float32/64, A-law, mu-law, IMA4 | Six existing targets with rate/layout/precision/metadata admission; no silent marker/loop loss or synthesized tails |
| WMA in ASF | Standard v1/v2, Pro, Lossless; separate Windows Voice provider | Complete inventory; refuse protection/additional streams; Voice mono at 8/11.025/16/22.05 kHz; retain all target consent/layout rules |
| Existing audio | WAVE PCM/float, FLAC, MP3, AAC-LC M4A, Ogg Vorbis/Opus | Existing six recipes, same-format no-op, sample-preserving FLAC optimization |
| PDF | Ordered combined images-to-PDF, all-pages-to-PNG at 150 DPI, structural Auto/Lossless | Copies, stored visible appearances and declared limits; no OCR/downsampling/signature-preservation claim |
| Office | Ordinary DOCX/XLSX/PPTX to PDF | Copies; Word final text without markup; Excel calculation choice and date guards; PowerPoint visible slide order without notes |
| Analyze | Every readable regular file and 245 reviewed descriptions | Qualified identity or useful unknown; filename hints separate from content; no license admission or execution |

ALAC evaluation, legacy/template/macro-enabled Office conversion, video, OCR,
archive extraction, new output formats and new Optimize presets are not required
additions. Existing generic analysis remains. Keep copies default and explicit
safe overwrite settings; never silently drop unsupported information.


Finite automated replay
-----------------------

Bind each replay to source revisions, executable identities, fixture manifests,
expected outcomes and candidate inventory. Check original/candidate identities
afterward. Preserve failed attempts. Never sum historical counts into a fresh pass.

| ID | Required suite family | Owning evidence |
| --- | --- | --- |
| A01 | Foundation and existing real image-worker integration | [Foundation/image acceptance](commercial-release-candidate-goal.md) |
| A02 | Analyze identities, misleading/unknown inputs, per-file failures and maximum selection | [Coverage](file-type-coverage.md), [performance](analyze-performance.md) |
| A03 | HEIC/AVIF authored bridge/output, upstream/color worker, loader and active-native recovery | [Color/alpha](heif-color-fallback.md), [packaging](heif-production-payload.md) |
| A04 | Existing audio conversion/FLAC, metadata/artwork, interruption and publication crash | [Combined baseline](combined-office-candidate.md), [policy](audio-conversion-policy.md) |
| A05 | Expanded 144-input worker, direct/metadata commands, recovery, 121-case surround/regression and Voice checks | [Worker](expanded-audio-worker-verification.md), [commands](expanded-audio-direct-verification.md), [recovery](expanded-audio-recovery.md), [surround](wma-pro-surround-repair.md) |
| A06 | PDF structure/appearance, ordered images, all-page export, cancellation and publication recovery | [Document policy](document-design.md), [combined baseline](combined-office-candidate.md) |
| A07 | Office direct/recovery, calculation/date, revisions, font decisions, capacity and independent PDF comparisons | [Combined baseline](combined-office-candidate.md), [font review](office-font-review.md), [date guard](excel-calculated-date-guard.md) |
| A08 | Engine/source correspondence, allowlists, absent/tampered providers, inventory and version reservation | [HEIF](heif-production-payload.md), [audio](expanded-audio-distribution.md), [Voice](windows-voice-distribution.md) |

These existing suite families are finite. Add cases for concrete defects or
missing listed coverage, not arbitrary test volume or unrelated formats.
Neighboring green checks cannot close missing evidence. Final command receipts
must say which exact worker ran; a freshly built substitute is not the staged worker.


Office content and layout corpus
-------------------------------

Keep existing regression sets and complete these twelve review groups. New
fixtures require fixed names/hashes, provenance or generation instructions,
expected text/page order and independent geometry before scoring export.

| ID | Review group | Required oracle |
| --- | --- | --- |
| D01 | Authored two-page Word: table, first/running headers, numbered footer, Latin Unicode | Exact text/two pages and expected element placement |
| D02 | Existing fifteen Word font/style controls | Existing text/geometry/font/pixel controls and correct font prompt selection |
| D03 | Existing Word revision/paragraph controls | [Paired application replay](word-revision-application.md) passes nineteen documents and independent fidelity checks; existing moved-text spacing allowance retained; final-candidate/human acceptance remains |
| D04 | Word combining accents, RTL/CJK and anchored image | [Authored export and independent inspection pass](word-layout-acceptance.md): exact characters/direction/placement, glyph/font programs and eight negative controls; human/final-candidate review remains |
| D05 | Existing five Excel print controls plus ordinary workbook | Areas, breaks, repeated titles, hidden cells/sheets, values and page order |
| D06 | Existing calculation/date controls under both modes | Correct values where admitted; known errors blocked; no external refresh |
| D07 | Excel chart, merged/wrapped cells and 48-sheet capacity | [Four authored chart/layout cases](excel-layout-acceptance.md#paired-application-acceptance-2026-09-23) and [all 48 capacity pages](excel-calculated-date-guard.md#paired-source-built-capacity-replay-2026-09-23) pass paired application/independent inspection; final-candidate and human acceptance remain |
| D08 | Ordinary, hidden/reordered and empty-visible-slide PowerPoint controls | [Paired slide replay](powerpoint-slide-evaluation.md#paired-source-built-renderer-replay-2026-09-23) passes visible order and note/hidden exclusion; earlier empty-deck refusal evidence retained; final-candidate/human acceptance remains |
| D09 | PowerPoint image, diagram/table and mixed typography | [Authored layout evidence](powerpoint-layout-acceptance.md) passes image/table placement and text; aggregate acceptance still fails the declared font-size tolerance |
| D10 | One approved real DOCX report | Owner/source reference for text, formatting and pagination |
| D11 | One approved real XLSX workbook | Owner/source reference for areas, displayed values and layout under chosen calculation modes |
| D12 | One approved real PPTX deck | Owner/source reference for visible content, fonts and layout |

Small character-position differences may preserve content, formatting and
pagination under the owner's decision. The existing 0.071-point move-fixture
allowance is not a blanket tolerance. Missing text, wrong dates, substituted
fonts, clipping or changed pagination fail. A per-document Save choice accepts
that explicitly reported font substitution; an empty callback is not proof of
complete style/glyph fidelity. D04/D07 have authored automated evidence; D09 and
real documents still need complete automated evidence. Human and final-candidate
review remain required; simpler controls do not establish their acceptance.


Human media and workflows
-------------------------

Prepare local artifacts without automatic playback. Record actual viewer/player,
display and audio device. Only an explicit human result closes listening or
owner acceptance. Do not reuse old review results for changed media outputs.

- **H01 audio:** fifteen comparisons from the existing three generated references;
  ten more from AIFF PCM, WMA Standard, Pro, Lossless and Voice, each to WAV/Opus;
  nine from three approved real recordings (speech, acoustic/sustained music,
  percussion/dense music), each to MP3/AAC/Opus. Total: **34 comparisons**. Review
  starts/ends, transients, noise and channel placement. Open encoded files in an
  intended player; decoded-WAV playback does not prove encoded-player support.
  The original fifteen and [expanded ten](expanded-audio-listening-review.md)
  generated comparisons are prepared. All human results and the nine
  real-recording comparisons remain open.
- **H02 images:** four scenes (photograph, oriented geometry, transparency,
  high-precision SDR gradient), each in HEIC and AVIF, each to JPEG/PNG:
  **16 outputs**. Compare appearance, orientation, alpha/matte and banding against
  retained source references.
  The [sixteen-output pack](heif-image-review.md) is prepared and passes scoped
  automated image checks. Human visual/display acceptance remains open.
- **H03 documents:** D01-D12 plus PDF text/vector, scanned, rotated/cropped and
  transparent-image controls. Review actual pages alongside automated checks.
  The [document worksheet](document-review.md) prepares 58 historical authored
  cases across D01-D09 plus four fresh PDF appearance controls, preserving five
  failures and two withheld date PDFs. The scan is synthetic. Real documents,
  actual human review and the final-candidate replay remain open.
- **H04 workflows:** eight scenarios: mixed Analyze with unreadable input; quiet
  image conversion; matte choice; audio quality choice; image-PDF order; Office
  calculation/font choices; cancellation/failure/retry; Settings/License changes
  and copy/overwrite preferences. Check names, originals, status, keyboard focus,
  Tab/Shift+Tab and Escape using simulated access and disposable files.

The [manual workflow harness](manual-workflow-review.md) prepares eight reviewed
command selections plus standalone Settings for these groups. Fresh profiles use
copied fixtures and TEST-ONLY access. Headless admission passes; visible H04
results remain pending while desktop control is unavailable.

Record light/dark, 100%/150%/200% scaling, two-monitor movement and screen-reader
announcements separately. Unavailable checks stay explicitly unverified release
gates. Hidden-window checks or earlier Tab behavior do not prove these results.
Installed Explorer acceptance retains its separate authorization boundary.

The historical worksheets are also collected by
[Prepare-OwnerReview.py](../tools/Prepare-OwnerReview.py). It requires a
`HistoricalDraft` manifest with four `Packs` entries (`audio`, `expanded-audio`,
`images`, `documents`), each declaring its scratch `Directory` and complete
`Files` map of relative paths to `Bytes` and `Sha256`. It verifies every artifact
and local worksheet link, then copies unchanged worksheets into a fresh scratch
collection. It opens no viewer and does not perform media conversion.

The retained manifest `.codex-temp/owner-review-inputs-01.json` assembles 288 files
(340,485,067 bytes): 25 generated audio comparisons, 16 image outputs and 62
document entries. Document statuses remain 50 passes, five failures, five controls
and two refusals. The draft index explicitly leaves human and final-candidate
acceptance unset. [Test-OwnerReview.py](../tools/Test-OwnerReview.py) passes eight
assembly/identity/refusal checks at
`.codex-temp/owner-review-checks/c80cc5e0323442708084890ad05713d7`; the collection is
`.codex-temp/owner-review-draft/6bb4ef7de745406090b6347fa613c702/index.html`.
Run either tool with the manifest path as its sole argument. These checks do not
prove visual presentation, worksheet interaction or media fidelity. The final
candidate still needs refreshed comparisons; this draft cannot clear a gate.


Performance and package review limits
------------------------------------

Declare these limits before the next measurement, using the recorded Windows 11
x64 i9-9900K/32-GiB host or documenting a changed machine. A miss requires
investigation and an explicit decision; do not loosen a limit after failure and
relabel that run passed. Correctness/resource caps remain separate.

| Workload | Review limit | Method |
| --- | --- | --- |
| Existing twelve-case Analyze corpus | Retain [existing limits](analyze-performance.md), including 5 s repeated p95 for 4,096 paths | Three fresh processes, first call plus ten repeats each; allocations and bounded inspection |
| Optional audio/PDF Analyze on ordinary small fixtures | 2 s first-call median; 1 s repeated p95 per file | Five fresh sessions plus five repeats each, including worker startup/probe |
| 12-megapixel SDR HEIC/AVIF to JPEG/PNG | 10 s median; 20 s maximum per operation | Five runs per pair including startup, validation and publication |
| 60-second stereo audio at 44.1/48 kHz to each target | 15 s median; 30 s maximum per operation | Five runs per target including inspection, encoding, validation and publication |
| Ten-page text/vector PDF to PNG; ten images to PDF | 20 s median; 40 s maximum per batch | Five runs each including validation and all publications |
| Ordinary two-page DOCX, one-page XLSX, two-slide PPTX to PDF | 45 s median; 75 s maximum per document | Five runs each including runtime hashing, profile lifecycle and PDF validation |
| Complete candidate | Provisional engineering ceiling 1.65 GB decimal unpacked | Exact file/per-engine/total bytes and archive size; owner acceptance of the approximately 1.58-GB footprint remains pending |

Record individual timings, hashes, machine/runtime and peak process/job memory
where obtainable. Keep caches and machine settings unchanged; fresh-process
observations are not cold-storage measurements. These local review thresholds
are not customer latency promises. Never reduce validation or isolation to meet
them. Office dominates package size; do not remove arbitrary pinned runtime files.

The [audio timing harness](audio-performance.md) measures both declared stereo
rates and all six targets in sixty fresh-worker operations. Its isolated results
remain separate from final combined-candidate replay and human listening.
The [PDF timing harness](pdf-performance.md) passes both ten-page/image workloads
in ten fresh-worker operations against preserved candidate 1.1.1. All fifty PNG
and five PDF copies pass the scoped completeness checks; original and candidate
identities remain unchanged. Final expanded-candidate replay remains required.
The [optional Analyze benchmark](optional-analyze-performance.md) passes the
seven declared audio/PDF cases with 35 first calls and 175 repeats against the
exact 1.1.1 worker, retaining bounded facts and read-only behavior. Final-candidate
replay and environment/human checks remain separate.
The [HEIC/AVIF timing harness](heif-performance.md) passes the four declared
12-megapixel input/target pairs in twenty fresh-worker commands against the
isolated combined HEIF payload. Maximum median/maximum are 7.297/8.344 seconds;
all originals and payload identities are unchanged. This closes the isolated
image timing baseline, not human review or final expanded-candidate replay.
The [Office timing harness](office-performance.md) passes fifteen commands against
the preserved 1.1.1 worker. All three ordinary document types meet their fixed
45/75-second limits, preserve originals and remove disposable native profiles.
This closes the isolated Office timing baseline; chart fidelity, typography,
network enforcement, human review and final expanded-candidate replay stay open.


Safety and completion gates
---------------------------

The [Excel layout matrix](excel-layout-acceptance.md) retains the original silent
chart-loss failures. The paired source-built renderer now passes four application
publications and independent PDF comparisons with active-content restrictions
enabled. Native safety, source-build replay, broader Office regressions and the
final candidate remain separate required evidence; these four PDFs do not close
the complete Office fidelity/security gate.

The [Office isolation record](office-isolation-evaluation.md) still has unresolved
loopback/deadline observations. File/token checks and successful exports do not
prove network denial. Resolve this with a positive ordinary-process control and
affirmative enforcement evidence for the restricted process, including IPv4/IPv6
and cleanup. Preserve the original assertions and failed observations.
The 2026-09-19 fresh probe repeats the timeout result and now binds PID/time/ports.
Its fourteen query-planner checks do not close this gate; attended administrator
event/filter collection remains pending.

The final audit identifies the candidate, each A/D/H result, performance samples,
source/runtime delivery and adoption decisions, preserved originals and reviewed
public/private commit checkpoints. Required media-fidelity gaps, missing engines
or unresolved safety behavior leave the local goal incomplete. Explicitly
untested environment checks remain named release gates, never aggregate passes.

Signing remains deferred. Native installer lifecycle, live commerce, installation,
Explorer registration, publication and private uploads retain their separate
authorization requirements. This contract grants none of those approvals.
