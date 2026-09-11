Broad File Support Goal
======================

Status: active; first universal Analyze slice implemented, broader coverage pending
Owner direction: 2026-09-09

Objective
---------

Expand the completed image utility into a useful general file companion:
read-only Analyze for every readable regular file, a large offline catalog that
explains common file types, common audio analysis/conversion, and PDF/document
support. Preserve the simple context-menu UX and existing image behavior.
Follow [decision 0019](decisions/0019-broad-file-analysis-and-media-expansion.md).

This is an implementation and local acceptance goal, not permission to release.
Signing is deferred by the owner. The [commercial release gates](commercial-release-candidate-goal.md)
remain separate. Writing this plan does not complete this goal.

Commit checkpoints
------------------

On 2026-09-09 the owner requested regular DIFF/COMMIT checkpoints as part of this
goal. After a coherent implementation/verification milestone, pause new feature
work and review the complete pending changes in the public repository and its
separate private checkout. Propose sensible, independently reviewable commit
groups, with exact files and verification evidence, following each repository's
AGENTS.md command rules. Execute the authorized proposal before starting the next
major slice; a changed worktree requires a refreshed DIFF proposal first.

Include all approved work rather than leaving earlier slices indefinitely
uncommitted. Keep the repositories' histories separate, protect unrelated edits,
and exclude secrets and generated scratch/payloads. Parent-repository commit
authorization does not itself authorize private-repository commits. The owner
explicitly authorized these checkpoints for both public and private repositories
on 2026-09-09. The first checkpoint committed the accumulated work in four public
commits ending at `d5dff37` and four private commits ending at `82fb7e7`.
Repeat this review/verification/commit workflow after each coherent milestone.

Starting evidence
-----------------

The 2026-09-11 [audio packaging checkpoint](audio-distribution.md) connects the
verified audio review bundle to a fresh production build through an explicit
option. All 18 runtime/notice files are pinned, allowlisted and included in the
recursive production inventory. The packaged worker harness now uses that stage
directly, without engine injection. The full managed/native build, 18 packaging
acceptance/refusal checks and 116 actual packaged-worker checks pass. Default
release packaging continues to reject the audio candidate; redistribution
decisions, visible acceptance and the broader
goal remain open.

- Image conversion, fixed PNG optimization, safe output settings, licensing and
  the focused UI are implemented. The owner accepted the final simplification.
- Latest public implementation commits are `3d17298`, `f3d00b8` and `9b9174d`.
  The separate private repository still has staged/unstaged work; preserve it.
- Latest checks: 486 foundation, 73 hidden view, 10 isolated license-harness and
  13 direct-command workflow checks. Earlier engine/worker results have their
  own dates and scope; they are not evidence for the proposed new capabilities.
- Current development staging is
  `artifacts/production-staging/fc166b574b8d481bb90f618b3ca358c5`.
  It contains the image utility, not the expansion described here.
- At the start of this goal the dedicated Analyze execution read DDS headers. Audio and broad
  document analysis/conversion are not implemented. Recognition, analysis and
  transformation coverage must be reported independently.

Implementation progress (2026-09-09)
------------------------------------

The 2026-09-11 [Office PDF geometry inspection](office-engine-evaluation.md)
locates retained Excel/PowerPoint differences in page/text/shape coordinates and
PowerPoint structure roles, despite identical embedded TrueType bytes within
each pair. A repeatable read-only diagnostic verifies the pinned qpdf payload and
authored PDF identities; four invalid-input cases are refused. These findings
narrow the fidelity investigation without accepting a tolerance or completing
required Office conversion/isolation.

The 2026-09-11 [Office owner-crash preflight](office-isolation-evaluation.md)
verifies that an abruptly terminated launcher closes its job and stops two
identity-checked, previously live application descendants. The same fresh native
run passes the earlier resource/lifetime controls. It does not create an
AppContainer profile or execute Office; renderer isolation and required customer
conversion remain open.

The 2026-09-11 [Office resource preflight](office-isolation-evaluation.md) verifies
per-process/aggregate commit and helper-process limits with successful controls,
actual private-commit/handle measurements and complete owned-job cleanup. It
retains unexpected Windows high-water/process-accounting values instead of
mistaking them for strict ceilings. The native build and repository checks pass.
The AppContainer profile/access matrix remains pending; no Office document ran
under this launcher and no production isolation or conversion is claimed.

The 2026-09-11 [external relationship slice](document-design.md) adds read-only
OOXML link-declaration counts with explicit scope, no target resolution and
preserved document identity after optional scan failures. Twenty-two new contracts
include malformed/over-budget parts, root/orphan/relative links and cancellation;
all 1,790 foundation contracts pass. Fresh isolated staging
`8b3f443a25974f8aba523bea131182dc` passes build/payload checks with zero warnings
or errors (`-SkipShell`). This improves required document analysis; it does not
enable Office rendering or replace its isolation/fidelity requirements.

The 2026-09-11 [Office path comparison](office-engine-evaluation.md) separates the
profile from TEMP/TMP/APPDATA/LOCALAPPDATA. A long profile alone reproduces the
zero-exit/no-PDF failure; each individually long environment path works with a
short profile. Five PDFs pass independent checks and exact render/text control
comparison; two failed conversions remain recorded. This establishes a more
precise worker-profile requirement, not a universal path cutoff or completed
Office integration. Isolation and broader fidelity remain required.

The 2026-09-11 [audio distribution checkpoint](audio-distribution.md) adds verified
source/runtime review ZIPs, eight original build inputs, 1,014 collected notices
from 1,029 compiler inputs and ten archive integrity/tampering checks. A fresh
build from the extracted kit passes all dependency tests and FFmpeg native review;
all kit inputs remain unchanged. This establishes local rebuild evidence, not
identical binary reproduction or release clearance. Production adoption, source
delivery, modified-library use, product terms and remaining fidelity/UI acceptance
are still open.

The 2026-09-11 [restricted FFmpeg work](audio-ffmpeg-build.md) now passes the real
generated-component gate with the complete audio/artwork selection. It adds
publisher-checksummed zlib 1.3.2 and a passing native codec example; ten source
archives verify. Seven configuration-gate checks pass. Full compilation exposed
native make path/quoting differences; the source overrides and tested script-file
mode address them. A fresh full build now succeeds with correct version identity,
unchanged upstream sources and a seven-file runtime. Six generated audio targets
pass candidate/independent decoding with exact frame counts; PNG artwork decodes.
The hardened follow-up now passes exact diagnostic and emitted PE checks, 298
private adapter checks and 116 isolated worker checks with the curated candidate.
Fresh isolated staging `5165575d4c9844bfbef0927dc20f88e0` builds with zero managed
warnings/errors and passed payload/native checks. Audio remains isolated in the
worker harness; normal staging still excludes it. Complete source/notice/runtime
distribution, remaining fidelity/compatibility cases and visible acceptance remain
open. This is local candidate evidence, not release clearance.

The next 2026-09-11 [audio build-tool checkpoint](audio-build-tools.md) builds GNU
make 4.4.1 and NASM 3.02 in repository scratch. Both pass their two native/workflow
tests, with 395/1,375 unchanged source files. Make has no compiler diagnostics;
NASM retains one reviewed Mach-O warning and has no errors. Wrapper fixes address
recursive make paths and NASM's SDK/inline-linkage compatibility; eight diagnostic
gate checks pass. No tools were installed. Complete FFmpeg composition and the
existing full audio matrix remain the next required work.

The subsequent 2026-09-11 [stable MP3 checkpoint](audio-dependency-builds.md)
builds LAME 4.0 from its original pinned release archive, with exact runtime
version, no compiler warnings/errors and 316 unchanged source files. Independent
decoding passes the authored stereo sample-count and signal-error checks. Nine
source archives now verify, including GNU make/NASM inputs for the remaining
build-tool work; six tar-reader test groups and four CLI refusals pass. Stable
LAME is the candidate for the curated build. FFmpeg composition, the full audio
matrix, source/notice inventory and production adoption remain open.

The 2026-09-11 [shared audio dependency builds](audio-dependency-builds.md) add
Ogg/Vorbis and the LAME core encoder while retaining the existing Opus command.
Fresh builds pass four Ogg/Vorbis tests, six Opus regression tests and authored
LAME encoding with no compiler warnings/errors or source changes. Independently
pinned FFmpeg decoding returns the exact generated stereo sample count and low
signal error. Two changed-fixture/engine refusal cases pass. The supplier LAME
source reports 4.1 alpha; evaluate the official stable 4.0 baseline before shipping.
This does not complete FFmpeg composition, adapter/worker replay or adoption.

The 2026-09-11 [Opus dependency recipe](audio-engine-curation.md) supplies explicit
source-version reporting and corrects the preliminary MSVC compiler flag issue
without changing upstream files. Its repository command builds in fresh scratch,
checks five upstream tests plus linked version identity, rejects compiler
diagnostics and records tool/source/output hashes. This advances the actual
curated dependency build. The final fresh run passes all six tests with no
compiler warnings/errors and 752 unchanged source files; two bad-input and three
launcher checks pass. The remaining codecs, FFmpeg composition, full audio
matrix, source/notice review and production adoption still need completion.

The next 2026-09-11 [audio source checkpoint](audio-engine-curation.md) adds LAME
SVN r6761: 418 file contents, a deterministic pinned ZIP and bounded HTTP export.
All six retained archives verify; a fresh LAME download reproduces its pinned ZIP,
and five invalid-path/existing-output cases are refused without writing.
A preliminary x64 static Opus source build
passes all five upstream tests without model downloads or source-tree changes.
Its version fallback and ignored compiler option still need correction; this is
not a production codec adoption or a replay of the Context Suite audio matrix.
The full curated build, toolchain/dependency inventory and adoption remain open.

The 2026-09-11 [audio curation checkpoint](audio-engine-curation.md) retains exact
FFmpeg, supplier recipe, Ogg, Vorbis and Opus source archives. The preparation
script passes a fresh download, five archive checks and four refusal cases.
It records unresolved LAME SVN source, Opus bootstrap input and toolchain/link
inventory before a bounded audio-only build. The evaluation bundle's compiled
video/network dependencies are not silently promoted into production. Existing
audio behavior, private code and production staging remain unchanged; actual
candidate build, matrix replay, adoption and broader acceptance remain required.

The expanded 2026-09-11 Word fixture verifies first/default headers and local
PAGE/NUMPAGES fields with deliberately stale caches. Both DOCX and generated DOC
exports pass the authored assertions and have identical page pixels. Their raw
text-extraction order differs, while the inspected structure dictionaries match;
[the evidence](office-engine-evaluation.md) keeps those observations separate
from untested screen-reader behavior. Two earlier modern runs have identical
uncompressed fixture parts and all five rendered pages, so no modern variability
was found in that control. Customer Office conversion and isolation are still
pending; no installed or production payload changed in this test-only slice.

The 2026-09-11 [legacy Office PDF experiment](office-engine-evaluation.md)
exports both authored modern fixtures and generated DOC/XLS/PPT copies. All six
pass independent page/text/geometry/preservation checks; Word's rendered pages
match exactly, while Excel/PowerPoint have recorded pixel differences and
PowerPoint has a small slide-height change. These remain fidelity observations,
not accepted tolerances. Five legacy renders received visual review; no app UI
acceptance is implied. The next Office work must resolve those differences using
broader independent baselines alongside isolation and production integration.
No production payload, installed state or AppContainer profile changed.

On 2026-09-11 bounded [legacy DOC/XLS/PPT analysis](legacy-document-analysis.md)
adds likely content identity from root stream names and supported binary headers,
with useful compound facts and explicit unsupported/malformed fallback. It passes
1,768 foundation contracts, including 99 new legacy checks and 1,000 deterministic
mutations. Disposable LibreOffice-generated DOC/XLS/PPT copies also pass content
identity and original/copy preservation checks. Full isolated Release staging
`3880ef9136b34a9e810f13a322a416c7` passes payload checks with zero warnings/errors.
This adds no Office conversion command or engine dependency. Broader legacy
variants, required Office rendering/isolation, engine adoption and manual/release
gates remain open. The per-user AppContainer experiment still awaits its specific
authorization; this analysis work did not create a profile.

The Office follow-up isolates a profile/temp-root length dependency: fixed-parent
ASCII profiles of 90/110/130 characters produce validated PowerPoint PDFs, while
150/170-character profiles return zero without output. Short Unicode profiles
also pass. The [engine record](office-engine-evaluation.md) retains both controlled
matrices; the internal failing path and exact cutoff remain unknown. A separate
[native isolation preflight](office-isolation-evaluation.md) passes local control,
descendant cleanup, timeout and diagnostic limits. The registered AppContainer
file/network matrix is prepared but awaits specific per-user profile permission.
It has not replaced the Office smoke wrapper or enabled customer conversion.

The 2026-09-10 [Office engine evaluation](office-engine-evaluation.md) pins and
unpacks LibreOffice without installation. Three generated passive DOCX/XLSX/PPTX
fixtures pass independent PDF parsing, rendering, text/page geometry and original
hash checks. It records a silent PowerPoint failure with a deeper Unicode profile
and success with a shorter ASCII profile; the cause remains unresolved. This is
an evaluation checkpoint, not customer Office conversion. Isolation, broader and
legacy fidelity, runtime/font inventory, redistribution and implementation remain
required. No production build or installed change ran for this checkpoint.

The 2026-09-10 direct **Convert > PDF** checkpoint connects supported images to
one reviewed document, validated copy publication and whole-document retry.
Multiple images invoke the focused order dialog; retry retains that order and
uses current Convert output settings. Changed inputs require a new command, and
overlapping selections remain distinct retry jobs. Fresh evidence passes 20
direct workflow checks, the existing 56 combined worker/recovery checks, native
shell/host handoff contracts, 1,869 foundation/image-worker checks, 43 PDF-page
checks, 102 hidden-view checks and 10 licensing-harness checks. Full isolated Release
stage `ce26f4b0eb00464ba9c49f2782227db4` passes payload checks with fresh native
output and zero warnings/errors. See [evidence and limits](image-pdf-candidate.md).
Normal staging still excludes the optional validator; engine adoption, visible
acceptance, required Office transformations and remaining broad-file work are open.

The 2026-09-10 focused image-PDF order dialog now provides numbered filenames and
folders, keyboard move commands, a first-page-derived destination and access
refresh without losing order. The helper confirms one image directly and several
through one review; direct menu/combined-result retry integration remains next.
Fresh checks pass 1,669 foundation, 102 hidden-view and 10 isolated license-harness
contracts. Isolated Release staging `04a6c35e61904d87b21b19f93eb4003d` passes
payload checks with zero warnings/errors. The first build also refreshed the
ignored development payload because its staging ID was omitted; see
[the evidence and build deviation](image-pdf-candidate.md). Visible/accessibility
acceptance and optional-engine production adoption remain open.

The 2026-09-10 combined-PDF worker/publication checkpoint connects ordered images
to the optional validator and one transactional PDF copy under normal paid/trial
admission. The app locks and journals every original through final publication;
the worker independently verifies sources and candidate bytes. The new workflow
passes 56 checks, including five actual app-crash checkpoints, and the regression
passes 1,851 foundation/image-worker checks plus 25 PDF-page and 18 direct-page
checks. Fresh isolated Release stage `2cdfc9bd64a242f4ae0cd4321797a4c3` passes
payload checks using `-SkipShell` and matches the tested payload bytes. The order
dialog was added by the later checkpoint above. The direct Convert > PDF action
is still pending, as are native engine
adoption and broader acceptance. See [the detailed evidence](image-pdf-candidate.md).

The independent image-PDF validation checkpoint (2026-09-10) adds an optional
pinned native qpdf reader and managed reply verifier. It checks the fixed output
schema, ordered page geometry and exact independently decoded color/alpha/ICC
digests against original samples, with bounded streaming and no DPI resampling.
Fresh evidence passes 45 native validation checks, 133 writer checks and 1,628
foundation contracts. Isolated Release stage `7c787dc3faa24b4f813e0dd43951b67c`
passes payload checks using `-SkipShell`; the optional native validator is not
shipped in normal staging. The later checkpoint above adds worker/access/publication;
the order UI/direct command, wider acceptance and engine adoption remain open.
See [evidence and limits](image-pdf-candidate.md).

The 2026-09-10 [combined image-PDF checkpoint](image-pdf-candidate.md) implements
an immutable ordered plan and private fixed raster PDF writer candidate. The
owner selected one combined document; several images will require a focused
page-order review. The candidate retains full samples, orientation, ICC and alpha
and leases every original through serialization. It passes 133 independent
qpdf/PDFium/sample/source-safety checks on 23 combined pages and 1,587 foundation
contracts. Subsequent checkpoints above add output validation, worker execution
and all-source publication. Direct integration and the order dialog remain open.
This is not an enabled menu action
or completion of the required images-to-PDF scope.

The direct PDF-to-PNG checkpoint (2026-09-10) adds PDFs to the existing **Convert >
PNG** command when the optional renderer is present. Mixed images/PDFs share one
admission; each PDF has one result with numbered page details. Partial cancellation
or failure stays visible, and in-session retry skips completed pages while using
current settings for unfinished pages. Changed PDFs require a new command. Fresh
evidence: 1,554 foundation, 25 page-publication, 18 direct PDF page, 86 hidden view,
10 simulated licensing-harness and 13 direct image checks, plus native shell
contracts. Full isolated Release stage `a768a20778d843ae90aa8090e5c4c8e7` includes
fresh native shell/host output and passes payload checks without installation.
Normal packaging still excludes the PDF renderer. Visible/accessibility acceptance,
engine adoption, broader fidelity, images-to-PDF and required Office-to-PDF remain
open. See [the document design](document-design.md) for evidence and limits.

The subsequent 2026-09-10 page-publication checkpoint connects PDF inspection and
per-page rendering to the optional worker. One conversion admission covers a
bounded page batch. Numbered PNG copies use the existing publication journals;
completed pages and originals survive later failure/cancellation. A scratch-lock
cleanup race found by regression testing is fixed with bounded, owned-directory
retries. Fresh verification: 1,752 combined foundation/image-worker checks
(1,552 public plus 200 real-worker checks), 25 PDF page-workflow checks and all
98 existing PDF worker/optimization/failure/direct checks. Final isolated Release
stage `7c3aa9f749f040d0a82fcba1035245ec` passes build/payload checks with zero warnings
or errors. Direct PDF-to-PNG UI and production renderer adoption are still open;
this does not complete the broad-file goal. Details and retained evidence are in
[the document design](document-design.md).

The 2026-09-10 PDF page-rendering checkpoint adds an optional isolated native
PDFium host, bounded public page protocol and private PNG validation adapter.
Fresh checks pass: 1,535 foundation, 24 raster adapter, 12 separate PDFium
evaluation, 292 audio and 27 structural PDF adapter. The isolated Release stage
`a2469abd2ce342c6aae6135f0983c8b7` passes normal payload checks with zero warnings
or errors. This is an adapter milestone: PDF-to-PNG worker dispatch, all-page
publication, context-menu integration and production engine adoption remain open.
See [the document design](document-design.md) for policy, evidence and limitations.
The broad-file goal remains active; this does not satisfy the required Office
conversions, complete document fidelity or independent release gates.

The first Analyze slice now supplies typed shared results, an unknown/empty-file
fallback, qualified content/filename evidence and an embedded 237-entry catalog.
DDS details are retained; initial image/document/container/text signatures share
the same read-only batch. The report shows a compact summary and collapsed
technical evidence. See [exact coverage and limits](file-type-coverage.md).

Bounded whole-file JSON/XML structure analysis now reports root and count facts;
specific application semantics remain unverified. Exact filenames, longest suffix
matching and ambiguous aliases retain explicitly qualified identification.

Bounded WAVE/FLAC header analysis now exposes declared audio properties with
qualified timing and metadata-block observations. It does not decode samples or
enable conversion. A [source-based engine comparison](media-engine-evaluation.md)
defines candidate experiments and fidelity matrices; no engine is adopted yet.

The [isolated audio evaluation](audio-engine-evaluation.md) now exercises all
36 required-format pairs on a generated stereo fixture. Corrected stream/global
mapping preserves four tested tags; decoded sample checks and process metrics
are retained. FLAC recompression preserved samples but dropped an application
block, so its preservation gate remains open. This is not worker, complete
metadata, listening or commercial-preset acceptance; no audio command is enabled.

The core FLAC metadata reconciler now preserves original descriptive block bytes,
rejects unsafe application/unknown/seek-table recompression, and verifies matching
audio declarations. Its isolated generated-media test produced a smaller FLAC
with exact samples, metadata and source preservation. Foundation coverage is now
919 passing contracts. Worker integration, seek-table/application handlers and
representative metadata acceptance remain open; this is not a shipping optimizer.

A private bounded pipe-probe component now parses all six generated audio formats
into public typed facts. Its 13 isolated checks cover engine identity leases,
input bounds, cancellation, protocol rejection and disabled engine-side report
files. The public parser has hostile/boundary coverage; the foundation total is
935 at that step. Optional worker registration and Analyze enrichment are now
implemented with 11 isolated application/worker checks and a fresh Release build
with zero warnings/errors. Richer/seekable probing and production audio packaging
remain pending. See the [audio evidence](audio-engine-evaluation.md) for the
important unknown-duration and isolation limits.

Bounded ZIP32/OOXML/OpenDocument analysis now identifies Word, Excel, PowerPoint
and OpenDocument text/spreadsheets/presentations from agreeing package evidence.
It reports declared sheet/slide counts and leaves rendered pages unavailable.
Its 47 contracts cover limits, malformed inputs, bounded seeking and source
preservation; see [document implementation and limits](document-design.md).
This adds no renderer or document transformation.

The [typed audio policy and private encoding candidate](audio-conversion-policy.md)
now exercise six-format conversion and lossless FLAC recompression with 292 passing
combined checks. Seekable inherited input preserves MP3 gapless sample counts;
the native process joins its Windows job before parsing. Tests cover wider
precision, surround, explicit Opus resampling, metadata reconciliation and running
child cancellation. The 36 format pairs include six unchanged same-format cases.
The file API now validates with fixed-size buffers and owned decoded scratch,
returning a read-leased candidate with source/output digests. A five-minute PCM24
round trip retains exact samples beyond the old array limits; running-operation
cancellation, artifact disposal and low managed allocation pass. Resampled output
now has time-aligned sample-error measurements, still requiring independent
fidelity and listening acceptance.
WAV conversion now inventories the complete RIFF chunk chain before encoding,
with bounded reads that seek past sample data. Admitted INFO fields must survive
native probing and actual output. Unhandled markers/loops, broadcast/unknown
metadata, repeated fields and ambiguous text stop conversion. The private
candidate records source metadata coverage separately from sample validation;
FLAC conversion also inventories original blocks/comments, validates complete
frames and seek tables, and requires exact source/output tag values. Unicode and
multiline comments pass native transport; Unicode-to-WAV, repeated or semantic
comments, artwork/application/unknown blocks require further preservation policy.
Vorbis/Opus conversion now inventories complete Ogg page framing and original
comments with bounded storage. Native facts must agree, and every admitted
descriptive value must survive output probing. Chained/multiplexed streams,
corruption, semantic tags and nonzero Opus playback gain prevent conversion;
MP3 now inventories supported ID3v2.3/v2.4 text and complete MPEG frame boundaries;
explicit admitted tags compensate for a pinned native unsynchronisation parsing
defect. ID3v1.0/1.1 text, track and genre plus numeric ID3v2 genres now pass
preservation checks. Agreeing v1/v2 fields retain richer values; contradictory
values block conversion. APE, composite/unreviewed genres, language-specific/named
comments, artwork and other frame handlers remain pending. M4A now inventories one local AAC-LC track,
sample tables, ordinary tags and simple priming edits before cross-format native
probing. External references, artwork/freeform tags, timestamps, extended headers,
complex edits and other unsupported structures require further handlers.
Vorbis output now flushes packet pages after generated short clips exposed
incorrect decoded lengths with the pinned default packing. Strict sample counts
and signal-error checks remain; generated extreme impulse/out-of-band LFE
signals are still refused. Normal six-channel Vorbis/Opus round trips pass.
Native FLAC optimization now retains embedded cover images and ordered duplicate
comments, with generated RGBA/audio equality evidence. Bounded descriptive
inventory distinguishes linked artwork, which still prevents rewriting.
Analyze reports explicitly attached artwork through its optional worker probe;
14 isolated worker/Analyze checks pass. Cross-format artwork, other container
metadata and FLAC application handlers remain pending. Seek tables now rebuild
from verified source/output frame indexes; a generated fixture retains exact
audio and matches linear source samples at four actual seek positions. Native
changed-frame-boundary and long-seek coverage remain open.
FLAC worker commands and sequential application publication/access now pass 14
isolated workflow checks: validated smaller named copies, collisions, original
hashes, admission across expiry, new-work denial, no smaller result, changed and
corrupt sources, cancellation and refusal of nonempty/hard-linked reservations.
Every FLAC optimization validates complete source/output frame indexes and CRCs,
including files without seek tables. Audio conversion now has typed file-based
worker commands and sequential application publication: 50 isolated checks cover
all 30 cross-format pairs, six same-format no-ops, metadata/refusal behavior,
collisions, expiry, cancellation, changed sources and unsafe reservations. The
confirmed batch combines required quality choices; paid/trial admission applies
once, and encoding must verify the source metadata before publication. Direct
audio Convert menu routing and a compact quality prompt now pass 20 application
workflow checks through the real worker. All six fixed targets retain the existing
quiet workflow. Required quality changes need one explicit decision; activation,
deactivation, final admission, cancellation and retries preserve the batch contract.
Full metadata admission, listening, broader hostile-file/crash/recovery coverage, payload review
and actual customer UI acceptance remain pending. Existing Auto/Lossless actions
now dispatch FLAC when the optional verified encoder is present, with 16 passing
direct-audio checks. Mixed PNG/FLAC batches share one admission across expiry,
retain settings/actions on retry and finish through the existing quiet workflow.
Balanced/Smallest, misleading extensions and missing engines explain their limits
before admission. Normal packaging still excludes the evaluation audio payload.

The [qpdf experiment](pdf-engine-evaluation.md) now measures a generated structural
rewrite with preserved inspected object graphs/decoded streams, encryption/error
behavior and 13 passing checks. It found a signature-summary blind spot; zero
reported signatures must not authorize rewriting. Optional private adapter/worker/
Analyze integration now passes 16 private adapter and 11 app-to-worker checks.
Bounded snapshots, locked-encryption facts, header fallback and cleanup are tested;
the separate PDFium evaluation now adds 15 passing generated-fixture checks,
including exact source/optimized rendered pixels and image alpha round trips.
The first structural optimization adapter candidate now passes 27 private checks
and 32 public graph/preservation contracts. It rejects signature/encryption and
uninspected revision history, compares rooted and ordinary unreferenced content,
and returns only smaller outputs. Independent PDFium rendering matches both
generated candidate pages. Transformation worker/access/publication, broader
rendering/native failure coverage and all other required PDF/Office actions remain
pending. No qpdf or PDFium payload ships yet.

1,507 foundation contracts pass, including 30 audio-plan, 17 streaming sample,
29 FLAC description/artwork, 22 seek-table, 24 batch/IPC/access, 34 WAV inventory,
26 FLAC/shared conversion metadata, 47 Ogg inventory, 86 MP3 inventory and 53 M4A
inventory checks, 35 audio conversion batch/IPC/access checks and two additional
paid-audio-admission checks, 14 direct-action/quality-decision checks and 32 PDF
rewrite checks and 19 PDF batch/access checks. The preceding 292 combined private audio checks and 113 real-worker
audio checks were not rerun for the PDF-only change. This slice passes 27 private
PDF adapter checks (preceding candidate milestone), 11 PDF Analyze worker checks,
17 PDF optimization worker/publication checks and 19 new direct PDF/mixed-family
checks, plus 51 new native failure/recovery/long-path checks. The preceding 15
independent PDFium checks were not rerun for path and failure-test changes.
The earlier shared
reservation-handle refactor's 942 image-engine checks pass again after correcting
long-path native reads. The 86 hidden view contracts, 13 image direct-command checks and
native shell contracts pass after PDF dispatch integration. They cover the expanded Convert menu's complete
multi-file activation without registration or installation. The
[catalog inventory](file-type-inventory.md) separates descriptions from detectors.
The [source audit](catalog-source-review.json) records 223 references: 186 retrieved,
7 search-indexed and 30 unavailable for retrieval; factual/provenance review remains
open. These checks do not finish the catalog, detailed media/document analysis or
audio/document operations. A fresh isolated Release build at
`artifacts/production-staging/8243dabf3d574224b31dd0d0aa0b12d7` includes the private
audio candidates, direct quality window and existing optional PDF worker
integration and direct structural PDF optimization. Release compilation, curated engine identities, file allowlist,
package dependencies and notice checks pass, with zero compiler warnings/errors.
This path/failure stage used `-SkipShell`; the preceding dispatch stage built
native shell into isolated scratch with updated preset descriptions. Hidden UI,
native shell and image direct-command suites were not repeated for this private
path change. See [dated audio evidence](audio-engine-evaluation.md).
Installed lifecycle and visible acceptance were not performed. The
evaluation audio and PDF engines are not part of this normal payload. Manual review remains
pending; earlier image acceptance does not prove the expanded report UI.

The PDF executor now admits one batch, uses the existing sequential worker and
publishes validated smaller copies through the shared transaction journal. PDF
copies are mandatory even with overwrite selected; unchanged results publish no
duplicate. Generated checks cover collision naming, expiry during/after a batch,
changed sources, signature/encryption refusal, cancellation around reservation
and between files, nonempty/hard-linked outputs and cleanup. Independent PDFium
rendering matches the source for both published generated pages. Auto/Lossless
now dispatch PDF alongside PNG/FLAC under one admission and one quiet completion.
Generated direct checks cover trial expiry across families, activation retry,
mandatory PDF copies, unchanged results, missing engines, misleading extensions,
protection refusal and cancellation between families. New failure checks observe
a live owned qpdf process before cancellation, deadline expiry and worker-only
termination, then verify cleanup and successful work through a fresh worker.
Publication move errors and application exits preserve exact originals and
appropriate candidate/journal evidence. Restart discovers retained records; it
does not silently restore or delete files. The tests exposed and corrected native
long-file-path and process-working-directory limits, with successful local source,
snapshot and output paths beyond MAX_PATH. Broader fidelity, UNC/long engine-install
paths, visible recovery acceptance and all other document actions remain open.

Product boundaries
------------------

- Keep three peer commands: Analyze, Convert and Optimize. No general workspace,
  arbitrary command editor, resident scanner, cloud analysis or automatic uploads.
- Analyze accepts ordinary selected files regardless of extension. Unreadable,
  empty, unknown, misleadingly named or partially supported files get an honest
  per-file result. One problem does not stop later files in the selection.
- "Every file" means useful fallback for each readable regular file, not an
  exact decoder for every possible format. Locked files, devices, folders,
  offline placeholders and unsupported virtual items receive specific handling.
  Do not download cloud placeholders or recursively traverse folders implicitly.
- Keep basic analysis and format explanations available without paid admission.
  Transformations use the existing trial/paid access boundary.
- Recognizing an extension must never enable an unimplemented transformation.
  Explorer uses cheap capability metadata; actual probing stays out of Explorer.
- Copies remain default. Existing explicit per-tool overwrite settings retain
  their meaning. New transformations must preserve publication, source-change,
  metadata, recovery and immutable-batch guarantees; otherwise force a copy or
  withhold the action, never weaken a safety gate.
- General image/audio metadata handling must not silently become a promise to
  remove personal information. Never infer quality, safety or provenance merely
  from a filename, codec, bitrate or extension.

Coverage to deliver
-------------------

| Family | Analyze target | Transformation target |
| --- | --- | --- |
| Existing images and DDS | Reuse verified facts; expose dimensions, representation, color/orientation, alpha and relevant limitations | Preserve existing verified menu formats and fixed PNG presets |
| Common audio | Container/codec, duration, channels, sample rate, meaningful bit depth/bitrate, tags/artwork presence | WAV, FLAC, MP3, M4A/AAC, Ogg Vorbis and Opus through an explicit tested input/output matrix; lossless FLAC recompression |
| PDF | Identify PDF and report available version, pages, encryption and document properties with availability states | Images-to-PDF, PDF pages-to-images and PDF optimization; exact variants and policies pending |
| Office/OpenDocument | Identify Word/spreadsheet/presentation families and report safely available package/document properties | Word/Excel/PowerPoint-to-PDF required; OpenDocument conversion not selected |
| Text, source and structured data | Identify likely text encoding and recognizable structures without inventing semantics | No new editing or transformation implied |
| Archives, executables, fonts, disk images, databases, video and common 3D/game assets | Catalog description, signature/container identification and bounded structural facts where implemented | Identification does not add extraction, execution, video conversion or arbitrary asset conversion |
| Unknown, extensionless or ambiguous files | Size/attributes, observed evidence, qualified type candidates or unknown result | No guessed conversion |

Audio format names are launch targets, not current capability claims. Distinguish
container from codec: M4A can contain AAC or ALAC; Ogg can contain different codecs.
Record exactly which combinations can be read and written. Investigate AIFF,
ALAC, WMA and APE as additional common inputs; they are not required output
promises until evaluated. Do not quietly remove a target because an engine
experiment fails: report the gap and select another approach or resolve scope.

Milestone 0: scope and evidence contracts
---------------------------------------

- [x] Record the broader launch direction and preserve the completed image scope.
- [ ] Produce a versioned capability matrix distinguishing catalog recognition,
  signature identification, detailed analysis, conversion and optimization.
- [ ] Freeze the first audio input/output pairs and metadata/fidelity policies.
- [x] Resolve the document transformation decision below with concrete options.
- [ ] Define fixture ownership/provenance and parser resource budgets before
  importing datasets or adopting additional engines.

Exit: an explicit matrix has no ambiguous "supports PDF" or "supports audio"
entries. Pending operations are visible and cannot appear as menu capabilities.

Milestone 1: universal Analyze vertical slice
--------------------------------------------

- [x] Introduce a shared typed analysis result: basic file facts, format identity,
  evidence, typical-use description, grouped details, warnings and per-fact
  availability. Preserve unknown raw values and distinguish derived facts.
- [ ] Add a generic fallback for unknown/empty/extensionless files and clear
  outcomes for access denial, source changes, cancellation and read limits.
- [x] Separate extension hints from signature/container evidence. Show confirmed,
  likely, ambiguous and unknown results without invented confidence percentages.
- [x] Route the existing DDS analyzer through this result model without losing
  its detailed texture facts or making it depend on a general media decoder.
- [ ] Enable Analyze for regular file selections in shell/host capability rules;
  keep mixed selection and one shared application/worker model.
- [ ] Present a compact summary: "What it is", "Commonly used for", key facts,
  then collapsed technical details. Handle multiple files in one usable report.

Example: "DOCX - Word document. Commonly used for editable reports and letters."
That description concerns the format. It must not claim the particular file is
a resume or report unless its contents explicitly support that conclusion.

Exit: known DDS, PNG, PDF, text, an executable, an unknown binary and a mixed
selection all produce useful read-only results. Misleading extensions do not
override stronger content evidence. Analyze does not start a trial or network call.

Milestone 2: reviewed offline format catalog
------------------------------------------

- [x] Use a versioned, reviewable data file packaged with the app; no database
  service or mandatory online lookup. Define stable IDs and a migration policy.
- [ ] Store display name, aliases/extensions, family, common uses, MIME identifiers
  where meaningful, evidence rules, source references and description provenance.
  Keep engine capabilities separate from format descriptions.
- [ ] Cover images, audio, documents, text/code/data, archives, executables,
  fonts, databases, disk images, video, 3D and commonly encountered game assets.
  Initial planning target: at least 200 distinct useful catalog records, reviewed
  against a concrete common-file inventory. Aliases do not inflate that count;
  coverage gaps matter more than hitting a number.
- [ ] Research established registries; review reuse terms before importing data.
  Write concise descriptions appropriate to ordinary users and retain sources.
- [ ] Implement deterministic conflict handling, including shared ZIP/OLE
  containers, ambiguous extensions, polyglot/contradictory evidence and plain
  XML/JSON whose application-specific type cannot be established.
- [ ] Validate IDs, aliases, duplicate rules, references, schema/version handling
  and coverage. Every detector needs positive and misleading/truncated fixtures;
  extension-only catalog entries must remain explicitly tentative.

Exit: published coverage inventory separates descriptive entries from validated
detectors. Unknown files still work. Catalog updates cannot silently change
transformation permissions or add executable logic.

Milestone 3: deeper analysis across families
-------------------------------------------

- [ ] Surface existing common-image probes without unnecessary full decoding.
- [ ] Add structured audio probing with bounded output and explicit container,
  stream and metadata handling; report duration/bitrate as estimated when needed.
- [ ] Add bounded PDF and Office/OpenDocument analysis. Distinguish encryption,
  unsupported parser features and missing properties. Do not execute macros,
  scripts, embedded applications, external links or document rendering merely
  to report basic facts.
- [ ] Add selected small analyzers for text/structured data, archive directories,
  executable headers and fonts where their facts are useful and independently
  testable. Unsupported details must leave the generic result usable.

Exit: each advertised property is supported by a fixture or returned as unknown,
not encoded or unavailable. Every parser has explicit byte/time/memory/recursion
limits and malformed-input tests. Hashing or exhaustive scans are optional work,
not mandatory for a quick summary. No unbounded archive extraction.

Milestone 4: audio conversion and bounded optimization
-----------------------------------------------------

- [ ] Evaluate an independently sourced, pinned FFmpeg/ffprobe build or narrower
  alternatives. Record actual codecs, dependencies, notices, deployment size,
  runtime requirements and update process before production adoption.
- [ ] Implement structured commands and machine-readable probe/progress output
  in the existing on-demand worker. No arbitrary arguments or media protocols.
- [ ] Add fixed reviewed presets for the agreed audio targets. Retain source
  channels/sample rate/precision where appropriate; no silent stereo-to-mono,
  normalization, trimming, resampling or unsupported metadata loss.
- [ ] Define tags, artwork, multiple streams, channel layouts, gapless playback
  and loop metadata policies. Handle unsupported preservation explicitly.
- [ ] Require a clear decision for lossy-to-lossy changes; keep it compact and
  separate from routine successful conversion. Lossless output never implies
  restoration of information lost in the input.
- [ ] Add lossless FLAC recompression only when decoded samples and required
  metadata remain equivalent and the validated result is smaller. Bitrate
  reduction of MP3/AAC/Opus is not a lossless optimization.
- [ ] Verify target container/codec, decoded duration/sample count, channels,
  metadata and output usability. Use exact sample comparison for lossless paths
  and appropriate signal checks plus human listening for lossy presets.

Exit: all agreed pairs pass real-engine fixtures, mixed batches, cancellation,
source-preservation, access expiry/retry and output/recovery checks. Presets remain
fixed; no new general planner or arbitrary quality-search loop.

Milestone 5: deliver selected document actions
------------------------------------------------

Document identification and analysis are required. On 2026-09-09 the owner selected
PDF tools plus Word/Excel/PowerPoint-to-PDF for launch. Analysis must not substitute
for those actions. OpenDocument analysis is required; its conversion is not selected.

Evaluate engines, exact variants and policies for these selected actions:

| Action | Main questions to settle |
| --- | --- |
| Images to PDF | One combined PDF selected; focused page-order review, physical sizing, image fidelity and all-source transactional copy publication |
| PDF pages to images | All pages versus selected pages, resolution, transparency and predictable page names |
| PDF optimization | Preserve text, vectors, links, forms, accessibility tags and signatures; distinguish image downsampling from structural recompression |
| Word/Excel/PowerPoint to PDF | Installed Office dependency versus standalone rendering, font substitution, pagination, workbook print areas and fidelity |

- [x] Record owner-selected actions and explicitly deferred actions.
- [ ] Evaluate engine fidelity, isolation/cancellation, offline operation,
  package size, maintenance and redistribution. Do not adopt installed Office
  automation or a large rendering dependency merely because a reference uses it.
- [ ] Create [document design](document-design.md) implementation decisions and
  representative fixtures before enabling actions. The linked design records
  the current decision boundary, not an implemented converter.
- [ ] Implement selected actions using fixed policies and copies by default.
  Review multi-page/multi-output atomicity and recovery before allowing overwrite.
- [ ] Test encrypted, malformed, signed, scanned and text/vector documents,
  missing fonts, embedded content, multiple pages, cancellation and access blocks.

Exit: selected actions meet declared fidelity and safety requirements. PDF-to-image
rendering is not labeled document-preserving PDF optimization. Required conversion
actions cannot be dropped from launch without a new owner decision.

Milestone 6: integrated acceptance and packaging
-----------------------------------------------

- [ ] Run relevant public contracts and real-engine integration for every new
  capability, retaining before/after hashes and semantic results.
- [ ] Test unknown/malformed files, misleading extensions, huge declarations,
  archive bombs, locked files, Unicode/long paths, concurrent file changes,
  cancellation, engine crash, failed publication and one-bad-file mixed batches.
- [ ] Benchmark cold/warm Analyze on a recorded Windows machine, including large
  files and selections; set release thresholds from measured budgets. Avoid
  whole-file reads when only identification or header facts are requested.
- [ ] Verify image regressions, quiet success, sound, retry, selected settings,
  trial/paid admission and process exit across all supported families.
- [ ] Manually review the changed summary, menus and necessary prompts. Record
  keyboard/Escape, screen-reader, themes, DPI and multiple-monitor results
  individually. Hidden tests never imply visual or assistive-technology delivery.
- [ ] Update customer quick start, exact capability matrix, catalog provenance,
  notices/inventory and engine records; build fresh isolated production staging.
- [ ] Verify the new payload and unsigned packaging evidence without installing,
  registering Explorer, altering trust, contacting Polar or publishing.

Exit: new capabilities have authoritative evidence and the owner has accepted
the changed UI. Explicitly untested environment/accessibility/installed-lifecycle
checks stay recorded as release gates. No unsupported entry is marketed as tested.

Required deliverables and completion audit
-----------------------------------------

The goal is complete only when the capability matrix and catalog are implemented,
universal fallback and richer analysis work, agreed audio paths and FLAC
optimization pass, document transformation scope is resolved and delivered,
and Milestone 6 evidence/documentation is current. A plan or a long extension
list alone is not completion. Do not count existing image tests as new coverage.

Deliver the format catalog with provenance, shared analysis model/UI, tested
family analyzers, audio adapter/presets, selected document handlers, fixtures,
capability coverage report, verification records and isolated candidate inventory.
Use the existing repository test scripts and add focused commands only where the
new capability needs them. Re-run only checks justified by changes or failures.

Independent release decisions
-----------------------------

Signing remains deferred. Permanent installer identity/version policy, disposable
Windows install/upgrade/repair/uninstall acceptance, live commerce/portal/refund
checks, pricing, support/refund terms, source/product terms and redistribution
review are independent gates. Keep them visible without blocking read-only
analysis or independent implementation work unnecessarily.

The standing commit checkpoint authorization above covers both repositories.
Installed-app or Explorer-registration changes, purchases, live Polar requests,
publishing, private uploads, reference execution and unapproved native recycling
still require separate authorization. Preserve both repositories and
use generated disposable fixtures in repository-local scratch directories.

Research starting points
------------------------

- [PRONOM/DROID identification](https://www.nationalarchives.gov.uk/information-management/manage-information/preserving-digital-records/droid/)
- [Library of Congress format descriptions](https://www.loc.gov/preservation/digital/formats/fdd/)
- [ffprobe structured analysis](https://ffmpeg.org/ffprobe.html)

These are research inputs, not selected production dependencies or automatic
permission to redistribute a dataset. Local reference projects remain read-only
behavioral research; independently implement useful ideas and test their limits.
