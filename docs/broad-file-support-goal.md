Broad File Support Goal
======================

Status: active; universal Analyze and optional audio/PDF candidates implemented;
required Office conversion and integrated acceptance remain incomplete
Owner direction: 2026-09-09

The latest [Office directory-boundary investigation](office-path-boundary.md)
identifies the startup failure and corrects the host manifest. Native path probes
pass 24 assertions and the updated host pin passes 30 runtime contracts. The
active interruption replay completes Word and Excel cancellation/recovery, then
fails during PowerPoint profile cleanup. Its retained profile was recovered with
file bytes and unrelated ACL entries preserved. The nine-cell matrix and
independent recovery-PDF acceptance remain incomplete.

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

On 2026-09-14, the [archive alias review](catalog-archive-alias-review.md) accounts
for all 50 associations across 30 archive/package records. Analyze retains Android
and Alpine alternatives for `.apk`, and RAR/Java resource-adapter alternatives for
`.rar`. The catalog now contains 244 records; no content detector is added.
The Release test host builds without warnings/errors and 2,822 foundation contracts
pass. Candidate 1.0.9 remains unchanged. The prepared Office network-event reader
is separate diagnostic work and awaits administrator approval; required Office
conversion and its isolation gate remain incomplete.

The subsequent [short-resampling check](short-resampling-verification.md) found
two adapter-admitted shortened Opus outputs caused by a shared resampler defect.
A source-rate duration check now refuses both before encoding. The full matrix
still fails 28 of 56 cases; correcting native resampling is required, not replaced
by the new refusal. The 222-case rate/layout regression and eight independent
resampling-source cases pass their stated checks. Candidate 1.0.9 is unchanged
and predates this guard; the next production candidate must use a new version.

The subsequent [finite-input resampling repair](finite-audio-resampling.md)
resolves the original 28 short cases through a bounded working extension and
encoding only the original time interval. It retains filter quality and metadata
validation. Boundary testing also corrects the duration guard to permit neighboring
whole-sample counts for fractional ratios, corroborated by independent SoX
resampling. Updated production staging, worker acceptance and wider audio gates
remain separate; candidate 1.0.9 still predates these changes.

Candidate **1.1.0** now includes the finite-resampling repair and 244-record
catalog. Its 116-file payload verifies, 40 isolated real-worker resampling checks
pass, and all twelve published copies independently decode within their expected
extents. All 2,822 foundation contracts pass; Release builds have zero warnings
or errors. Candidate 1.0.9 is preserved unchanged. See the
[candidate receipt and exact acceptance scope](finite-audio-resampling.md).
This does not close listening, wider recovery coverage, visible acceptance,
required Office conversion or the separate release gates.

The same candidate subsequently passes **20 finite-resampling interruption
checks**: cancellation, client timeout and owned-worker termination after its
real working file appears. Temporary files and journals are removed, originals
and prior copies survive, and each same-source retry publishes through a fresh
worker. All four committed copies independently decode to 1,536 finite stereo
frames. See the [scoped recovery evidence](finite-audio-resampling.md#finite-path-interruption-and-retry).
This adds no product payload change or visible acceptance claim.

Current completion path (2026-09-12)
------------------------------------

Use this summary for current priority; the dated evidence below records individual
checkpoints and is not a cumulative release-test result. An unchecked milestone
can contain implemented work: check it only when its whole stated obligation and
exit evidence are satisfied. Do not repeat a passing suite merely to add another
checkpoint; rerun it for relevant changes, a failure or the final integrated build.

| Area | Implemented or measured | Work required to close the area |
| --- | --- | --- |
| Universal Analyze | Shared results, generic/header fallback, bounded family readers, per-row availability after admission, synchronous I/O cancellation and refreshed mixed-batch performance baseline | Finish the advertised regular-file acceptance matrix; test remaining driver/network and metadata-stall cases; accept the visible report |
| Offline catalog | 244 records, schema/version validation, source references, reviewed current MIME claims, reconciled typical-use descriptions and current detector-route provenance | Finish wider alias/variant review and additional meaningful MIME coverage; preserve the distinction between descriptive and detected coverage. More records are not a substitute for finishing that review |
| Audio | Six conversion targets, lossless FLAC optimization, optional curated payload, real-worker/interruption evidence and a rate/layout matrix with corrected Opus surround mapping | Close remaining input/metadata variants, independent decoding and human listening/player checks, redistribution/adoption review and integrated UI/recovery acceptance |
| PDF | Optional structural optimization, all-page PNG conversion and combined image PDF with explicit order review | Close documented fidelity/unsupported-input and recovery gaps; accept the order dialog and outputs visibly; review redistribution and adopt the selected payload |
| Office to PDF | Independently sourced uninstalled candidate; generated Word/Excel/PowerPoint experiments, owned evaluation jobs and prepared isolation probe | Verify the isolation boundary, settle rendering policies, implement the required converter, then pass fidelity, hostile-input, cancellation and publication acceptance |
| Integration | Isolated combined staging and dated contract runs | Build and verify the final selected payload, run relevant regression suites together, update customer capability claims and record actual manual acceptance separately |

The owner authorized the disposable Office isolation profile test on 2026-09-14.
The [registered-profile results](office-isolation-evaluation.md) verify actual
AppContainer execution, exact permitted content, withheld-file/write denial and
profile cleanup. IPv4/IPv6 attempts reach observation deadlines while adjacent
controls connect; the combined test remains failed. Network capability diagnosis
does not explain the result, and reading scoped filtering events requires
elevation. Authorization for this test is resolved; enforcement evidence and
Office execution inside the boundary remain unfinished.

The subsequent [explicit-environment check](office-isolation-evaluation.md#explicit-environment-and-redirected-storage-2026-09-14)
verifies exactly eight variables and actual writes/readback through five scratch
storage variables in the AppContainer. It accounts for Windows' local/temp path
redirection, rejects three changed-environment controls and independently verifies
disposable profile cleanup. Network enforcement and actual Office renderer
compatibility remain open; the combined isolation matrix still fails.

The [Office version-only follow-up](office-isolated-startup.md) now starts the
pinned engine normally and inside an actual AppContainer, with matching build
strings and zero remaining owned processes. A separate 19,332-file runtime copy
keeps its access grants apart from the retained candidate. This establishes
version reporting only; profile/document initialization, conversion and network
enforcement remain unverified. The full isolation matrix still fails.

The subsequent [full initialization/export attempt](office-isolated-startup.md#initialization-failure-and-environment-control-2026-09-14)
exposes a further integration blocker: ordinary Word/Excel/PowerPoint exports pass
independent structure/text/page checks, but all AppContainer initializations fail
before export. Word exits 1; Excel/PowerPoint reach the 60-second deadline. An
ordinary control using the same redirected environment initializes successfully,
so those paths alone do not explain the failure. Job/profile cleanup passes;
restricted initialization and network enforcement both remain unresolved.

The [focused startup diagnostics](office-isolated-startup.md#owned-startup-diagnostics-2026-09-14)
retain owned child exits and window observations. Ordinary initialization passes;
restricted initialization alternates between natural exit 1 and a 60-second
timeout, with empty engine logs. A visible LibreOffice window provides no useful
error text through the scoped accessibility inspection. Jobs and profiles are
cleaned up. The cause is still unknown; further execution needs a new diagnostic
hypothesis, and no customer Office capability or release pass is claimed.

The subsequent [profile-copy experiments](office-isolated-startup.md#profile-copy-hypotheses-2026-09-14)
eliminate two simple explanations: permitted file copying succeeds in the
AppContainer, and seeding the completed ordinary profile still does not permit
restricted startup. All fifteen seeded profile files and disabled-content settings
match the control; owned profiles/jobs are cleaned up. The specific failure cause
and network enforcement remain unresolved.

The [runtime lookup and IPC investigation](office-isolated-startup.md#runtime-lookup-and-ipc-diagnosis-2026-09-14)
now identifies a directory-parent access defect and corrects the evaluation layout
to `runtime/office`. Scoped directory lookup passes while surrounding/withheld
lookups remain denied. The earlier UNO path exception disappears, but startup
still stalls. The stock engine's standard pipe namespace conflicts with the
AppContainer requirement, reproduced by a bounded standard/LOCAL pipe comparison.
Evaluate a compatible embedded/source integration next; additional profile flags
cannot resolve that demonstrated namespace mismatch. No customer converter or
production payload is changed.

The subsequent [embedded startup check](office-isolated-startup.md#embedded-startup-2026-09-14)
successfully initializes and destroys the pinned engine inside the AppContainer,
using its API path that omits desktop IPC. Ordinary and restricted controls exit
zero, and owned jobs/profiles are cleaned up. This clears an initialization
obstacle; the next step is authored Word/Excel/PowerPoint export through that API,
with independent fidelity and recovery checks. Network enforcement and required
customer conversion remain incomplete.

The [authored embedded export check](office-isolated-startup.md#authored-embedded-exports-2026-09-14)
now passes the Word fixture in both ordinary and AppContainer processes, including
independent PDF structure, text, page geometry and exact rendered-pixel comparison.
Excel and PowerPoint initialize but time out while loading in both controls. The
full matrix remains failed; investigate the embedded loader/main-loop interaction
before expanding the document matrix or enabling customer conversion.

The [Windows embedded lifecycle correction](office-isolated-startup.md#windows-main-loop-and-input-copy-correction-2026-09-14)
now passes all six authored Word/Excel/PowerPoint exports, ordinary and restricted,
with independent structure/text/geometry and exact rendered-pixel comparisons.
It waits for the actual main loop on the initializing thread and gives each case
a fresh read-only input copy. Earlier ordinary failures were also affected by
retained lock files from interrupted tests. Broader fidelity, hostile-content and
recovery checks, network enforcement and customer conversion remain open.

The [passive Office failure/recovery matrix](office-embedded-recovery.md) records
three truncated-input refusals, six marker-confirmed forced stops and 24 passing
normal exports afterward, with independent output checks and file release.
The renderer accepts all three zero-byte Office inputs as new documents; this
keeps source admission explicitly open. Existing analysis distinguishes those
inputs from content-identified packages, but is not complete rendering or safety
validation. Mid-render cancellation, owner crashes and customer integration remain
separate work.

The [initial Office source preflight](office-embedded-recovery.md#initial-source-preflight)
now rejects empty/unidentified inputs and distinguishes ordinary Open XML main
types from templates, slide shows and macro-enabled variants. The isolated export
wrapper retains source read locks through its native test. Verification passes
2,866 foundation contracts and eight wrapper cases, including seven refusals
before a launch sentinel and all source handles released. This is bounded source
identification for the experiment, not complete document validation or customer
conversion admission; legacy/other variants, content/network isolation and
application integration remain required.

The [active embedded export interruption experiment](office-embedded-recovery.md#active-export-interruption)
now verifies three stops while PDFs are growing, with failed operations and
incomplete outputs retained only in scratch. Six large 96-page controls pass
independent structure/text checks; six normal exports after the confirmed stops
pass independent structure/text/geometry/pixel checks. Three earlier missed stops
and their following controls remain recorded. All 24 attempts preserve sources
and settings, release files and remove their disposable profiles. Application
cancellation, owner crashes, same-profile recovery, publication and content/network
isolation are still separate requirements.

The [separate embedded owner-crash experiment](office-embedded-recovery.md#owner-crash-during-export)
verifies three abrupt job-owner exits during PDF growth, retained engine handles
signaling exit, all six recorded job members no longer live, profile cleanup and
27 exclusive file opens. Six following normal exports pass independent checks.
Stopped engines report exit zero despite incomplete PDFs, so future orchestration
must require an explicit successful owner reply and validated output. Context Suite
application/worker crash recovery, same-profile retries and publication remain
unimplemented/unverified for Office.

The [private Office host candidate](office-host-candidate.md) adds a fixed-purpose
isolated export process and strict completion protocol. Three actual host exports
pass independent PDF/text/layout/pixel checks; six malformed/context/path refusal
cases and ordinary-token refusal pass. The foundation suite now passes 2,986
contracts. Production launcher/worker/publication integration remains next;
this component checkpoint does not enable customer Office conversion.

The [managed Office launcher](office-managed-launcher.md) connects that native
host to a capability-free suspended launch, verified job limits, bounded pipes,
deadline/cancellation and descendant cleanup. Clean-environment testing also
corrects the host's CRT environment initialization. App-owned profile/grant
composition, worker requests, independent validation and publication remain to
be integrated before enabling the customer command.

The [application profile/grant owner](office-profile-ownership.md) now passes
45 native ownership/access checks, including worker loss, actual grant revocation,
preserved unrelated ACL changes and link/collision refusals. The test exposed and
corrected an attributes-only directory handle that failed to prevent renaming;
three Office export regressions pass with the stronger native handle. Production
context construction, worker routing, validation/publication and persisted
application-crash recovery remain open.

The [Office runtime verifier](office-runtime-verification.md) pins and holds the
host and all 19,332 candidate runtime files, with explicit empty-directory
membership. Thirty checks pass for altered/missing/extra entries, write/rename
sharing and cleanup. This supplies the payload lease needed for worker integration;
it does not yet add worker routing, PDF validation or customer Office conversion.

The following [Office worker export checkpoint](office-worker-export.md) now
connects typed requests, pinned runtime leases and the sandbox launcher. Three
actual worker exports pass 39 checks and independent PDF/text/geometry/pixel
inspection; the foundation suite passes 3,023 contracts. Real Office cache paths
exposed a cleanup limit, now covered by 50 ownership checks and successful actual
cleanup. Production context journals, PDF validation/publication, cancellation and
loss acceptance through the complete workflow, and customer enablement remain open.

The [Analyze mapped-file checkpoint](analyze-mapped-files.md) adds 37 local NTFS
checks for read-only/copy-on-write admission and writable-view refusal after the
original handles close, mixed-batch guidance and post-release recovery. The
foundation suite passes 2,809 contracts. Product code and reserved candidate 1.0.5
are unchanged; driver/network, metadata-stall and visible acceptance remain open.

The [audio catalog alias review](catalog-audio-alias-review.md) supplies provenance
for all 33 currently listed extension associations across eighteen Audio records.
It resolves the `.kar` naming gap without changing recognition or expanding
conversion claims. Other-family aliases, competing meanings, wider variants and
additional meaningful MIME coverage remain open.

The [document catalog alias review](catalog-document-alias-review.md) reconciles
all 58 current extension associations across 31 document, spreadsheet and
presentation records. It supplies separate evidence for the `.ical` convention
and clarifies template, macro-enabled and slideshow groupings. The catalog and
reserved payload are unchanged; this does not satisfy required Office conversion
or complete other-family alias and deeper variant review.

The [image catalog alias review](catalog-image-alias-review.md) reconciles all 65
current extension associations across 30 Image records. It closes the existing
`.jfif`, `.rgbe` and `.targa` naming evidence gaps while retaining shared HEIF/AVIF
and `.ase` alternatives. Catalog bytes and candidate 1.0.7 are unchanged; wider
family aliases, competing meanings and exact-variant acceptance remain open.

The [PDF page geometry matrix](pdf-page-geometry.md), extended by the
[typed limit follow-up](pdf-page-limits.md), passes 59 actual worker/publication
checks on its dated combined stage. It covers
crop/rotation pixels, 16-million-pixel pages, maximum width/height, out-of-bound
refusal and later valid work. Native page-size refusals now use a typed limit
category and clear guidance, distinct from malformed input. Native allocation
failure, inherited/other page geometry and visible acceptance remain open.

The [Office evaluation lifetime checkpoint](office-process-lifetime.md) passes
sixteen helper checks and three real passive exports through creation-time
Windows job ownership. It closes the evaluation launcher's descendant/diagnostic
cleanup gap without establishing filesystem/network isolation or implementing
the required customer converter. Two real engine startup-interruption cases also
pass cancellation/owner-crash cleanup and reuse of the same disposable profiles;
no document was open during those startup interruption tests. The later
[Office export-interruption checkpoint](office-export-interruption.md) adds three
96-page completed controls and six interruptions after actual temporary PDF growth
across Word, Excel and PowerPoint. All recover through valid same-family exports
using the same disposable profiles; bounded file-release waits handle a measured
post-exit sharing violation. The separate access-matrix authorization,
rendering policies and production integration remain pending.

The [isolation preflight](office-isolation-evaluation.md) now verifies actual
input/output bytes and token capability counts; it fixes a control that previously
truncated the generated input while only testing handle access. The registered-profile
access matrix still awaits its separate approval.

The [Office embedded-image checkpoint](office-image-evaluation.md) verifies
six passive modern exports, exact retained PNG resolution/visible samples/alpha,
three effective downsampling controls and independently rendered patches. It
advances document fidelity evidence while the required customer converter,
isolation authorization and other rendering decisions remain outstanding.

The [Office embedded-font inspection](office-font-program-inspection.md) adds
internal name/table/hash evidence for eight programs in the six retained font
exports, ten bounded parser tests and four workflow guards. Identical Arial name
tables accompany different program bytes, so a simple name/hash lookup would
not settle font fidelity. Production font resolution, isolation and required
Office conversion remain unfinished; this inspection leaves the staged product unchanged.

The owner subsequently selected final text with tracked-change markup hidden
for Word-to-PDF. The [explicit Word export policy](word-revision-evaluation.md)
passes eight real exports: four final-text pages match the clean control's pixels,
and positive controls demonstrate the option's effect. Sources retain bytes,
write times and revisions. The evaluation implements the default; customer
integration, complex revision cases and isolation remain unfinished.

The [structural Word revision matrix](word-structural-revisions.md) adds twelve
exports with passing text/source checks and exact formatting/table controls.
Moved text differs by 151 pixels and up to 0.07001 PDF points in horizontal
character position, with identical embedded font bytes. The exact-pixel matrix
remains failed; this is a recorded fidelity gap, not an accepted tolerance.

The latest combined production stage is candidate 1.0.9 at
`artifacts/production-staging/4798b0329381494f8db92a8441aa2ef0`.
The [short-MP3 correction](mp3-short-padding-verification.md) restores exact
duration without changing encoded audio bytes. The original 90-cell short-audio
matrix, a separate 90-cell MP3 rate/length matrix, ten candidate guards, 30 direct
worker checks and 2,813 foundation contracts pass. One tiny MP3 fails automatic
format detection in the independent runtime while decoding correctly with MP3
selected; player compatibility remains open. Required Office conversion and
broader acceptance remain unfinished.

The preceding combined production stage is candidate 1.0.8 at
`artifacts/production-staging/056d900511c1404eab414951c4d49e0f`.
The [short-audio checkpoint](short-audio-verification.md) fixes FLAC encoding below
sixteen samples without padding. All fifteen tested FLAC lengths match the original
samples through independent decoding. The complete 90-cell matrix retains five
unresolved MP3 padding failures; it is not a passing all-audio matrix. Twenty direct
worker/publication/recovery checks and 2,813 foundation contracts pass. Short MP3,
broader audio acceptance and the required Office converter remain unfinished.

The preceding combined production stage is candidate 1.0.7 at
`artifacts/production-staging/19e12a8a4db142e3bb73df3518c67243`.
The [WAV Unicode-tag checkpoint](wave-text-conversion.md) preserves supported
descriptive values from the other five audio formats using explicit ID3 encoding
alongside representable ASCII INFO. It passes 155 focused checks, existing audio
regressions, 2,812 foundation contracts and 66 staged-worker direct checks.
The thirteen-case independent decode/probe matrix includes text with artwork;
player/listening compatibility, broader variants and integrated acceptance remain
open. Candidate 1.0.6 is retained but superseded by the corrected typed-frame
syntax policy. The required Office converter remains unfinished.

The earlier combined production stage is candidate 1.0.5 at
`artifacts/production-staging/c2eca21b98aa42398081720617b6ec3f`.
The [WAV output-artwork checkpoint](wave-output-artwork.md) preserves representable
PNG/JPEG covers from the other five audio formats while retaining exact decoded
samples and existing quality policies. It passes 103 private artwork checks across
22 conversions, 37 direct-worker checks and 2,772 foundation contracts. The record
also explains two corrected assertion helpers and the limits of earlier negative
test evidence. The combined stage has 116 verified files. Broader metadata,
listening/player compatibility, integrated acceptance and the required Office
converter remain open; this does not establish commercial release clearance.

The preceding combined production stage is candidate 1.0.4 at
`artifacts/production-staging/424f0c8130054282a52a6545e0669689`.
The [WAV ID3 artwork checkpoint](wave-id3-artwork.md) admits bounded WAV source
tags/covers through existing FLAC, MP3, M4A and Ogg output handlers. It passes
339 private artwork, 104 direct-worker and 2,754 foundation checks; the checkpoint
records the scoped evidence and remaining work. The stage has 116 verified payload files.
WAV output was still pending at that checkpoint and is covered by the later
1.0.5 record. Further metadata/fidelity, listening and integrated acceptance
remain open alongside the required Office converter and other release gates.

The preceding combined production stage is candidate 1.0.3 at
`artifacts/production-staging/f1f21b2f1e63471b9f2b5a44442a90b5`.
The [M4A output-artwork checkpoint](m4a-output-artwork.md) adds representable
PNG/JPEG covers from FLAC, MP3, Vorbis and Opus, preserves movie placement and
repairs audio chunk offsets. It passes 110 private, 59 direct-worker and 2,729 foundation
checks; its scoped acceptance and remaining limits are recorded in that checkpoint.
WAV artwork, further fidelity, listening/player and integrated acceptance remain
open. This combined payload has 116 files and its own reserved version receipt.

The preceding combined production stage is candidate 1.0.2 at
`artifacts/production-staging/76ffc1a76c854a6896e4d5ab832f40bc`.
The [PDF page-scale checkpoint](pdf-inherited-geometry.md) fixes UserUnit sizing,
adds inherited geometry acceptance and verifies scaled limits during inspection
and rendering. It passes 206 geometry/publication, 29 private raster, 12 comparison,
25 page workflow, 20 interruption, 18 direct-command and 21 payload checks, plus
2,696 foundation contracts. Its combined payload has 116 verified files; these
scoped runs do not establish final all-feature or visible acceptance.

The preceding combined production stage is candidate 1.0.1 at
`artifacts/production-staging/a932bd2a928c48d5b708d407d892693a`.
It adds [MP3 output artwork preservation](mp3-output-artwork.md) to the preceding
combined capabilities: FLAC, Vorbis, Opus and supported M4A covers retain image
bytes and represented picture metadata. The current foundation run passes 2,696
contracts; the new slice passes 94 focused private and 56 direct-worker checks.
Existing artwork regressions pass 114 M4A, 81 Ogg-source and 31 FLAC-to-Ogg checks.
These are separate suites, not an all-feature release run. WAV artwork outputs,
further metadata variants and listening/player acceptance remain open.

The preceding M4A combined production stage is
`artifacts/production-staging/bcc65f0acd2f47258b2b165707dbb53f`.
It includes catalog revision 2026-09-13.1, the prior Analyze and combined-PDF
failure handling, corrected Opus surround mapping, separate trial-policy edits,
the ID3v2.2 reader and bounded FLAC-to-Vorbis/Opus artwork transport, with the
native shell build included. It also includes the new
[Office embedded-reference analysis](document-embedded-analysis.md), which
classifies image, OLE/package and VBA relationships without reading their targets.
It now also includes the [MP3 artwork handler](mp3-artwork-conversion.md) for
PNG/JPEG covers to FLAC, Vorbis and Opus, and the
[Ogg artwork source reader](ogg-artwork-conversion.md) for FLAC and cross-codec
Vorbis/Opus outputs. It now includes the [M4A cover-data handler](m4a-artwork-conversion.md)
for those three targets. That checkpoint's foundation run passes 2,663 contracts,
including 37 new M4A picture/admission checks. The M4A checkpoint passes 116
focused private artwork, 31 existing preservation and 115 direct-worker checks
on this stage. The preceding Ogg checkpoint passes 85 focused private artwork,
51 existing preservation and 57 direct-worker checks on its own stage.
The preceding PDF follow-up passes 25 private raster, 12 evaluation, 59 direct/
worker, 20 interruption and 19 payload checks. The earlier MP3 checkpoint passes
91 focused private artwork, 100 preservation and 58 worker/direct checks on its
own dated stage.
Cover bytes, descriptions, order and independent image decoding are verified;
quality decisions, copy defaults and original-file protection remain intact.
The earlier [FLAC artwork checkpoint](audio-artwork-conversion.md) passes 33
focused private, 36 direct-command and 52 audio conversion workflow checks.
Picture bytes, descriptions and order survive those target formats; remaining
artwork paths and human listening/player acceptance remain open. This fresh staging is
not an all-feature regression or commercial release clearance.
The earlier FLAC
checkpoint passes 24 optimization interruption, 96 conversion interruption and
11 normal worker checks on stage `dc5a863d901b4eb89f94b943682511aa`.
These are separate runs, not evidence that every release suite
has passed on a final payload.

The [ID3v2.2 checkpoint](audio-engine-evaluation.md#id3v22-text-preservation-2026-09-13)
adds bounded older-MP3 text admission and passes 100 focused native checks,
including metadata transport, decoded lengths, safe refusals and no-op bytes.
It closes that text-header variant; artwork, other metadata conventions,
independent fidelity/listening and integrated payload acceptance remain open.

The [catalog purpose reconciliation](catalog-purpose-review.md),
revision 2026-09-12.1, reviews the final 34 descriptions and improves fifteen
descriptions/references. All 243 current IDs have a purpose review, without gaps
or duplicate counting. The catalog changes no recognition or capability fields.
Alias/variant and MIME review remain open, as do the required Office converter and
integrated gates. The [detector-route reconciliation](catalog-detector-review.md)
accounts for all 34 content-identifiable IDs and 209 filename-hint records. It
corrects the inventory's MP3 optional-probe description without changing runtime
behavior, aliases, confidence or capabilities. Format conformance and broader
resource/visible acceptance remain separate from this provenance inventory.
That earlier purpose review passed 2,367 foundation contracts and 104 documentation files, with
an independent check that only purpose/source fields and revision changed and
that every current ID occurs once in the purpose-review partition. The successful
foundation log is `.codex-temp/catalog-remaining-foundation-retry.log`; the first
run ended without a terminal result and is not counted as passing evidence.

The later [audio rate/layout checkpoint](audio-rate-layout-verification.md) passes
300 private adapter checks and 2,386 foundation contracts. Its 222-cell generated
matrix contains 151 successful conversions, 37 unchanged WAV cases and 34 expected
refusals. It corrects Opus 5.0/6.1 channel order and declines speaker layouts the
pinned presets cannot preserve before encoding. A subsequent explicit native/Xiph
Opus comparison confirms all 33 outputs' complete frame counts and seven layouts'
source-channel identities, but exits 1 for unresolved surround sample differences.
The matrix record retains the failed comparison. A follow-up Xiph spectral
comparison passes 72 speaker comparisons and two negative controls, while four
scores still require listening review. Independent Opus fidelity, listening and
complete source-container/rate/layout acceptance remain open.
Stage `4921457d351a4daf85270a7f6672b542` passes 11 packaged audio checks, 52 conversion/publication
checks and 20 direct-conversion checks; logs and exact scope are in the matrix
record. Build, payload and source/notice inventories pass. No installed or visible
acceptance is inferred from those automated checks.

The later [combined-image resource checkpoint](image-pdf-candidate.md) passes five
real writer/validator cases: maximum pixel count and width, oversized output and
pixel-budget refusals, and successful reuse after refusal. Source hashes, write
times, released leases and scratch cleanup pass. The host peaks around 818 MiB;
this excludes validator children and is not whole-worker memory acceptance.
The later PNG precision matrix below covers large alpha/16-bit samples; native
resource failures and visible acceptance remain open. Its follow-up tests the
actual worker and publisher: a large page publishes
identically, output-cap failure cleans its reservation/journal, and a later image
completes in the same worker. The writer now reports the existing resource-limit
category and the app advises selecting fewer or smaller images. Three workflows
pass on the latest stage above, with full payload inventory verification. Worker
and validator memory measurements are scoped to these generated opaque BMPs;
no native-allocation failure or universal memory ceiling is established.

The full regression initially stalled in a rejected test-oplock cleanup. The
[test-helper fix](analyze-io-cancellation.md) tracks actual pending requests,
adds two refused-request regressions and bounds setup-only retries. Five isolated
runs pass 36 checks each, then the complete foundation passes 2,391. Production
Analyze cancellation is unchanged; the initial stalled run remains failed evidence.

The [MIME registration/variant review](catalog-mime-variant-review.md) checks all
59 current claims across 36 records and corrects seven alias associations in four
existing records. Shared HEIF/AVIF suffixes retain multiple candidates; XHT and
SPX remain subject to existing content/filename distinctions. The catalog stays
at 243 records, with unchanged MIME descriptions and detectors. All 2,403
foundation contracts pass, including 12 new confidence-boundary checks. The
current staging predates these aliases; their inclusion remains part of the next
integrated payload. Additional meaningful MIME coverage and wider aliases remain open.

The [large PNG precision matrix](image-pdf-candidate.md#large-png-precision-and-transparency-2026-09-13)
now passes nine writer/independent-validator cases and nine actual-worker copy
workflows. All eight RGB/grayscale, 8/16-bit and opaque/alpha combinations succeed
at 16 million pixels, with exact sample/profile preservation, followed by small
work in the same host/worker. Original/lease/journal/scratch and worker-exit checks
pass. Cumulative host and worker memory measurements exclude validator children;
native-allocation failures, wider large-input variants and rendering/visible
acceptance remain open. The existing stage is verified unchanged; no new
production payload or broad regression run is claimed for these test additions.

Prioritize the remaining work in this order:

1. Resolve the [Office isolation evaluation](office-isolation-evaluation.md).
   The owner authorized the disposable profile test; file restrictions, token,
   explicit environment and cleanup have passed their recorded checks. Actual
   embedded initialization/shutdown and authored Word/Excel/PowerPoint exports now
   pass in both controls. Broader fidelity, hostile-content/recovery and network
   enforcement remain open. The
   separate elevated read-only network-event query still awaits authorization.
   Initial source preflight, active-render interruption and evaluation-owner
   crash tests now have component evidence. Connect those policies and the
   managed launcher to production app/worker ownership, complete source
   admission, independent PDF validation and transactional publication. The
   small authored matrix does not clear the remaining isolation or adoption gates.
2. Settle the concrete Office policies exposed by experiments:
   [Excel calculation](excel-calculation-evaluation.md),
   [early date systems](excel-date-system-evaluation.md),
   [missing fonts](office-font-substitution.md). Word's final-text default is
   owner-selected; extend its [verified inline-revision policy](word-revision-evaluation.md)
   to the remaining revision cases before customer integration. Use the existing
   [print-layout](excel-print-layout-evaluation.md) and
   [slide/notes](powerpoint-slide-evaluation.md) fixtures as acceptance controls.
   Record exact supported variants and refusals; implement Word/Excel/PowerPoint
   conversion only with a defensible isolation and fidelity policy.
3. In independent work, close known Analyze resource/admission gaps and finish
   review of the current catalog inventory. Add a parser field or catalog entry
   only to close a declared capability gap or defect, rather than continually
   expanding the completion target.
4. Finish the selected audio/PDF acceptance matrices and arrange manual review
   of listening/player behavior, Analyze, necessary prompts, page order and
   recovery. Record keyboard, screen reader, themes and DPI individually; an
   unavailable test stays unverified.
5. Freeze the local candidate, run the relevant integrated regressions and fresh
   packaging verification, reconcile customer claims with evidence, and perform
   the final public/private DIFF/COMMIT checkpoint. Apply the same checkpoint
   workflow after each coherent implementation milestone above.

Required Office conversion cannot be replaced with more document analysis.
Signing, native installer lifecycle, live commerce and publication remain separate
commercial release gates under their existing authorization boundaries.

Starting evidence
-----------------

The 2026-09-11 [Analyze failure-guidance follow-up](analyze-io-cancellation.md#failure-guidance-follow-up-2026-09-11)
keeps timeout, missing-file, access-denial and sharing-conflict causes visible in
the result model. All 2,367 foundation contracts pass, including actual denied-read
and timed-out mixed batches with original/ACL preservation. Fresh combined staging
passes with the latest catalog. Visible and assistive-technology delivery remain
unverified; these are result-model checks.

The 2026-09-11 [remaining image catalog review](catalog-image-review.md) checks
26 purposes beyond the earlier four common-image records and improves twelve
descriptions/references. Catalog revision 2026-09-11.11 retains 243 entries with
unchanged identification and operation fields. This finishes the current Image
family's purpose review, not exact-variant, decoder or transformation acceptance.

The 2026-09-11 [Analyze admission follow-up](analyze-io-cancellation.md#admission-follow-up-2026-09-11)
removes selected-file availability queries from Analyze's managed/native admission.
Missing/non-file members receive individual results without rejecting valid files.
All 2,358 foundation contracts and native host-only validation pass; a fresh stage
includes the native shell build and passes payload checks. COM invocation, installed
Explorer routing and visible acceptance were not performed.

The 2026-09-11 [Analyze I/O cancellation checkpoint](analyze-io-cancellation.md)
starts the read deadline before file setup and requests cancellation of synchronous
native operations without abandoning them. All 2,349 foundation contracts pass,
including 29 blocked-open, race, preservation and retry checks. The refreshed
benchmark meets existing review budgets and fresh combined staging passes with
zero warnings/errors. Driver/network, admission and visible acceptance remain open.

The 2026-09-11 [FLAC optimization interruption matrix](audio-interruption-verification.md)
passes 24 new checks for live-encoder cancellation, client timeout and worker
termination with smaller validated retry copies. The shared conversion harness
also repeats all 96 interruption checks and 11 normal worker checks. Separate
inspection verifies six originals, 21 committed copies, cleanup and process exit.
This covers encoding, not every optimization phase or listening/UI acceptance.

The 2026-09-11 [video catalog review](catalog-video-review.md) checks all ten Video
entries and improves seven descriptions/references. Catalog 2026-09-11.10 retains
243 records and unchanged identification/operation fields. Mobile/Flash variants
and container-versus-codec distinctions are clearer; complete variant coverage
and other remaining family reviews are separate work.

The 2026-09-11 [PowerPoint visibility analysis](powerpoint-slide-analysis.md)
adds bounded visible/hidden/default counts from referenced slide XML, retaining
basic facts when the optional scan fails. All 2,320 foundation contracts pass,
including 54 new checks; four retained PPTX inputs also match independent XML
counts through the real file reader with original bytes/times preserved.
This does not implement the still-required Office converter.

The 2026-09-11 [PowerPoint slide/notes evaluation](powerpoint-slide-evaluation.md)
adds four generated PPTX exports with saved order, hidden end slides and an
identical-input positive notes control. All ten parsed/rendered PDF pages match
the tested text policy; independent source/PDF/relationship checks pass and three
altered evidence copies are rejected. Required Office implementation, isolation
and broader fidelity decisions remain open.

The 2026-09-11 [data catalog review](catalog-data-review.md) checks all 37 Data
records and improves fifteen descriptions/references. Revision 2026-09-11.9 keeps
243 records with unchanged recognition/operation fields. Feather versions and
pickle loading have clearer explanations; complete variant/alias and remaining
family review remain required.

The 2026-09-11 [Word revision analysis](word-revision-analysis.md) adds bounded
insertion/deletion/move-marker counts from the already parsed main XML, with
explicit scope and unavailable results for unsupported compatibility processing.
All 2,266 foundation contracts pass; the four retained export fixtures also match
an independent XML count with originals preserved. Fresh isolated stage
`885c764010054136b49d8718a1b2fa09` passes packaging checks. Required Office export
and complete revision/fidelity acceptance remain open.

The 2026-09-11 [Word revision evaluation](word-revision-evaluation.md) exports
four passive DOCX cases and records deleted text in PDFs for shown/unspecified
revision settings, but not hidden settings or the clean control. Independent
source/PDF/declaration inspection and two altered-evidence refusals pass. This
adds a concrete revision-policy requirement before Word-to-PDF adoption; it does
not implement the converter or resolve isolation and broader fidelity.

The 2026-09-11 [five-target audio interruption matrix](audio-interruption-verification.md)
passes 96 checks across FLAC, MP3, M4A/AAC, Vorbis and Opus encoding. All 15
target/fault pairs preserve originals and earlier copies, clean unfinished work,
and complete validated same-target retries. The 11 normal audio-worker checks
and payload inventory checks also pass. Other phases, listening/player acceptance
and release adoption remain separate work.

The 2026-09-11 [source/configuration review](catalog-source-code-review.md) checks
all 49 records in those families and improves twenty descriptions/references.
Catalog revision 2026-09-11.8 retains 243 entries and unchanged recognition fields.
Source, scripts, type declarations and configuration have clearer purpose text;
full variant/alias and remaining-family review is still required.

The 2026-09-11 [archive/package catalog review](catalog-archive-review.md) checks
all 29 records in the two archive/package families and improves sixteen records'
wording/references. Catalog revision 2026-09-11.7 retains 243 entries and unchanged
recognition/operation fields. Full variant, alias and remaining-family review is
still required; this does not add extraction, mounting or installation actions.

The 2026-09-11 [Excel print-layout evaluation](excel-print-layout-evaluation.md)
matches all five passive workbook cases across eight PDF pages: manual breaks,
repeated title rows, fit-to-page, disjoint areas and hidden cells. Independent
source/PDF/hash inspection and two altered-evidence refusal checks pass.
These saved-print findings do not enable required Office conversion or resolve
isolation, calculation/date/font handling and broader layout acceptance.

The 2026-09-11 [audio publication crash checkpoint](audio-publication-crash-verification.md)
passes 74 checks across ten abrupt application-layer terminations, plus 11 normal
audio-worker checks. WAV-to-FLAC and FLAC optimization preserve originals,
candidates/copies and journals; restart discovery and fresh operations pass.
The unchanged combined stage retains its verified inventory. Manual recovery,
native overwrite and other release gates remain separate.

The 2026-09-11 [audio interruption checkpoint](audio-interruption-verification.md)
passes 20 encoder-phase cancellation/timeout/worker-crash checks plus 11 ordinary
audio-worker checks on the existing combined stage. Native CPU and candidate
growth are observed before each fault; cleanup, original/committed-copy safety
and fresh-worker retries pass. Other crash phases and release gates remain open.

The 2026-09-11 [workbook settings checkpoint](workbook-settings-analysis.md) adds
typed date/calculation declarations with scoped optional failure and no new I/O.
All 2,243 foundation checks pass, including 61 new checks, and seven retained
Office fixtures match independent source-XML inspection. Fresh combined staging
passes with zero warnings/errors. Office rendering policy, isolation and actual
conversion remain required; declarations do not prove formula/date fidelity.

The 2026-09-11 [document catalog review](catalog-document-review.md) checks 31
document-related purposes and improves fourteen descriptions/references. Revision
2026-09-11.6 preserves all 243 records and their recognition fields. Required
Office conversion, remaining catalog families and variant acceptance stay open.

The 2026-09-11 [AIFF/AU header checkpoint](aiff-au-header-analysis.md) adds bounded
read-only content identification and audio declarations without a worker or
conversion permission. All 2,182 foundation checks pass, including 50 new cases;
fresh combined staging passes with zero warnings/errors. A discovered image-test
thread-affinity issue is fixed separately; visible/installed acceptance remains open.

The 2026-09-11 [audio catalog purpose review](catalog-audio-review.md) checks all
eighteen Audio descriptions against primary documentation, clarifies fourteen
records and replaces broad references. Catalog revision 2026-09-11.5 retains all
243 IDs and recognition fields. Alias/variant and other-family review remains
open; descriptions grant no new conversion or parsing capability.

The 2026-09-11 [Excel date-system experiment](excel-date-system-evaluation.md)
completes three passive exports but finds six early-1900 display mismatches
across 21 numeric date/time observations. Modern/fractional dates, elapsed hours
and all tested 1904-system values match. Independent source/text/hash inspection
confirms the finding; date-aware handling remains required before Office adoption.

The 2026-09-11 [independent FLAC decoder checkpoint](audio-independent-flac.md)
verifies eighteen real-adapter conversion/optimization outputs with publisher-
verified Xiph FLAC 1.5.0. PCM16/24/32 mono/stereo/six-channel samples match exactly,
including signed extrema and partial final blocks; corrupt/truncated controls
fail. Older players, listening and redistribution approval remain separate gates.

The 2026-09-11 [document font reference checkpoint](document-font-references.md)
adds bounded optional OOXML font-name and theme declarations while preserving
basic identity on optional failure. All 2,132 foundation contracts pass, plus
read-only verification of the six retained Office font fixtures. This is declared
reference analysis, not installed-font resolution or a customer Office converter.

The 2026-09-11 [Office font substitution evaluation](office-font-substitution.md)
compares six authored exports. Missing requested fonts silently become Bodoni MT
Black in the Word title and DejaVu Sans in Excel/PowerPoint on this host. Page
counts/text still pass, while four paired page renders differ. Source/PDF hashes
and font-only source differences are verified. Explicit missing-font handling,
broader layout fidelity and required Office conversion remain open.

The 2026-09-11 [font header checkpoint](font-header-analysis.md) reviews five
catalog records and adds likely content identification with bounded header facts.
All 2,102 foundation contracts pass, including 218 new checks and actual-reader
source preservation. Fonts are never installed, rendered or decompressed;
complete font validity and wider catalog review remain unclaimed.

The 2026-09-11 [PDF page interruption checkpoint](pdf-page-interruption.md)
passes twenty actual staged-worker checks for cancellation, client deadline
expiry and worker termination after observing the owned native renderer alive.
Originals remain unchanged, owned temporary state is cleaned and a fresh worker
publishes both retry pages. The actual adapter deadline and wider fidelity/UI
acceptance remain separate gaps.

The 2026-09-11 [Analyze performance baseline](analyze-performance.md) measures
three fresh processes and thirty repeated samples per case on a recorded
Windows host. The 128 MiB binary reports 64 KiB inspected; a DOCX with a 64 MiB
unrelated member reports 131,093 bytes. The eight-file mixed selection, including
a locked item, has 6.02 ms repeated median and 11.51 ms p95, with originals
unchanged. Provisional reference-machine review budgets are recorded. Disk-cache
cold, optional-worker, visible UI, broader hardware/storage and larger-selection
acceptance remain open.

The 2026-09-11 [MIME catalog checkpoint](catalog-mime-descriptions.md) adds 59
registered descriptive identifiers to 36 catalog records, with per-identifier
provenance. Schema 1 remains compatible with records that omit MIME metadata.
Analyze exposes these only in qualified technical details, independent of
identity confidence and operation support. All 1,884 foundation contracts and
fresh combined application/payload staging pass; broader MIME and factual review
and actual visible acceptance remain open.

The 2026-09-11 [Office profile correction](office-profile-settings.md) reapplies
typed settings after initialization and verifies their saved declarations after
rendering. Seventeen guard contracts and authored Word/Excel/PowerPoint PDF
exports pass, with seven required declarations retained in each profile and
originals unchanged. This closes the observed profile-seeding gap for the tested
settings, not the separate engine-enforcement or AppContainer isolation gates.

The 2026-09-11 [Excel calculation experiment](excel-calculation-evaluation.md)
adds twelve isolated exports across saved/missing formula values and three
requested profile settings. Fresh-profile overrides were initially discarded;
initializing first makes the explicit recalculation setting persist and change
stale results. Forced recalculation also overrides manual workbook mode, while
"never" still computes missing caches. A launch calculation policy and effective
profile/update restrictions remain to be resolved; this does not enable Office
conversion or prove arbitrary-document isolation.

The 2026-09-11 [catalog alias review](catalog-alias-review.md) adds six missing
meanings for shared extensions and corrects the static/import-library wording.
The catalog now contains 243 records. Undecoded shared suffixes retain ambiguity;
content evidence still takes precedence. Broader factual/provenance review and
MIME coverage remain open.

The 2026-09-11 [common image Analyze checkpoint](image-header-analysis.md) replaces
filename-only JPEG/GIF/BMP/WebP identity with bounded content/header evidence,
adds declared dimensions and keeps actual decoded/media properties unavailable.
Four catalog descriptions/detectors were reviewed against primary specifications;
JPEG's description now covers lossless variants. All 1,837 foundation checks pass,
including 47 new header cases, 1,200 mutations, independent Windows-encoded images
and actual-reader original preservation. Broader catalog review remains open.

The 2026-09-11 [Office IPv6 preflight](office-isolation-evaluation.md) adds a
separate IPv6-only loopback endpoint to the prepared isolation matrix. The
unrestricted generated child connects to both IPv4 and IPv6 successfully; native
lifetime and resource checks still pass. Actual isolated denial, profile cleanup
and Office execution inside that boundary remain pending explicit disposable
AppContainer-profile approval. This is preparation, not customer Office conversion.

The 2026-09-11 [PDF packaging checkpoint](pdf-production-payload.md) connects
qpdf/PDFium and the authored renderer/validator to one isolated production build
with images/audio. The 45 pinned PDF files include full retained notices and
adjacent Microsoft runtime dependencies; the renderer previously relied on
runtime DLLs outside its evaluation payload. The full managed/native build,
19 packaging checks, 217 actual packaged PDF workflow checks and 116 audio
workflow checks on the same stage pass. Default release adoption and required
Office conversion remain open.

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
fallback, qualified content/filename evidence and an embedded 243-entry catalog.
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
  implemented optional PDF candidates and the still-unimplemented Office converter.
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
