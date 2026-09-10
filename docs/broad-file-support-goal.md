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
now exercise six-format conversion and lossless FLAC recompression with 77 passing
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
Audio worker transformation commands, full metadata/artwork admission, listening,
hostile-file/crash coverage, application publication/access/recovery, payload
review and actual customer UI acceptance remain pending. The candidate is not a
publishable receipt and adds no menu capability.

The [qpdf experiment](pdf-engine-evaluation.md) now measures a generated structural
rewrite with preserved inspected object graphs/decoded streams, encryption/error
behavior and 13 passing checks. It found a signature-summary blind spot; zero
reported signatures must not authorize rewriting. Optional private adapter/worker/
Analyze integration now passes 16 private adapter and 11 app-to-worker checks.
Bounded snapshots, locked-encryption facts, header fallback and cleanup are tested;
the separate PDFium evaluation now adds 12 passing generated-fixture checks,
including exact source/optimized rendered pixels and image alpha round trips.
Broader rendering/native failure coverage and all PDF transformations remain
pending. No qpdf or PDFium payload ships yet.

1,081 foundation contracts pass, including 30 audio-plan and 17 streaming sample
checks. The previous
76 hidden view contracts were not rerun for this non-UI audio candidate slice. The
[catalog inventory](file-type-inventory.md) separates descriptions from detectors.
The [source audit](catalog-source-review.json) records 223 references: 186 retrieved,
7 search-indexed and 30 unavailable for retrieval; factual/provenance review remains
open. These checks do not finish the catalog, detailed media/document analysis or
audio/document operations. A fresh isolated Release build at
`artifacts/production-staging/976f5e9e0e73485690dc211911281bc8` includes the private
audio candidates and existing optional PDF worker integration: zero warnings/errors, curated engine identities, file allowlist,
package dependencies and notice checks pass. `-SkipShell` was used; native shell
rebuild, installed lifecycle and visible acceptance were not performed. The
evaluation audio and PDF engines are not part of this normal payload. Manual review remains
pending; earlier image acceptance does not prove the expanded report UI.

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
| Images to PDF | Page size/order, image quality, one file versus per-input files, multi-output publication semantics |
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

No new commits, installed-app or Explorer-registration changes, purchases, live
Polar requests, publishing, private uploads, reference execution or unapproved
native recycling are authorized by this plan. Preserve both repositories and
use generated disposable fixtures in repository-local scratch directories.

Research starting points
------------------------

- [PRONOM/DROID identification](https://www.nationalarchives.gov.uk/information-management/manage-information/preserving-digital-records/droid/)
- [Library of Congress format descriptions](https://www.loc.gov/preservation/digital/formats/fdd/)
- [ffprobe structured analysis](https://ffmpeg.org/ffprobe.html)

These are research inputs, not selected production dependencies or automatic
permission to redistribute a dataset. Local reference projects remain read-only
behavioral research; independently implement useful ideas and test their limits.
