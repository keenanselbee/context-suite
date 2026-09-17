Combined Office Candidate 1.1.1
==============================

Status: fresh isolated image/audio/PDF/Office production build verified;
integrated workflow acceptance in progress. This is not release clearance.

The first formal combined candidate is
`artifacts/production-staging/3efa4342aed9435995d8f00b52fead96`, version **1.1.1**.
Its inventory contains **19,450 files totaling 1,573,914,125 bytes**, excluding
the 4,353,472-byte inventory itself. Inventory SHA-256:
`5C931EBBF0BBCB4DF1D576D4714628D420B9EB539956A7ED39589A9975AA497E`.
The exclusive version receipt records a verified build. The retained 1.1.0
candidate and all 117 of its files, including its inventory, remain unchanged.

The canonical production command builds the application, worker and native
shell into a fresh stage. It includes the existing pinned image engines, curated
audio bundle, independent PDF engines and full Office runtime. Complete file
allowlists, sizes, hashes, notices and version checks pass. Managed compilation
has zero warnings/errors; native compilation completes successfully. The build
receipt binds the source/configuration inputs and confirms they stayed unchanged.

Build evidence is `.codex-temp/combined-office-candidate.json`,
`.codex-temp/combined-office-build.log` and
`.codex-temp/combined-office-build-exit.json`. Input directories are recorded
there; each engine retains its own committed selection and verification policy.
No runtime is installed or registered, no files replace an earlier stage, and no
production license request is made.


Combined test entry points
-------------------------

The PDF and packaged-audio workflow scripts accept explicit
`-AllowOfficeCandidate` for this larger stage. Their default checks still reject
unselected Office files. The Office application harness accepts
`--production-stage <stage>` alongside its existing disposable-profile opt-in.
It verifies the complete combined payload and inventory before any native
profile creation, runs that stage's worker directly, and checks all retained
binary identities plus complete inventory membership afterward. It builds only
the test host; it neither rebuilds nor injects engines into the production stage.
Existing scratch-worker evaluation remains a separate supported mode.

The shared inventory verifier previously refused files above 1 MiB. The actual
Office-inclusive inventory is over 4 MiB. Its new limits are 16 MiB and 32,768
records; file hashing streams bytes rather than allocating each complete file.
All **22 audio payload checks** pass, including an 8,192-file synthetic inventory
above the old byte limit, corruption of its final file and both new bounds.
Evidence: `.codex-temp/audio-payload-tests-a4d8be272fa7449cbed8ae63fe376e4a`.

Four new Office invocation checks reject missing profile consent, conflicting
worker/engine choices, a directory outside isolated production staging and the
old stage without Office. All stop before profile creation. Evidence:
`.codex-temp/combined-office-guards-e6752b5546cf419c84d6d534ff4823f8`.

Host-only native checks also pass: three versioned identity manifests, three
valid batches, six mixed-availability cases and unknown-schema refusal. These
checks use the newly built native host and do not invoke COM or Explorer.


Foundation and image execution
-----------------------------

The final foundation run passes **4,330 checks**: 4,130 foundation contracts plus
200 checks using the actual staged image worker. It includes PNG/DDS conversion,
optimization, active cancellation, worker loss and recovery. Evidence is
`.codex-temp/combined-office-foundation-03ecb7b2cdf049d2a08797c49ae45ccc`.
`inputs.json` records the exact command and sources; `exit.json` records exit 0,
unchanged sources, successful full inventory verification and unchanged inventory
bytes. This run does not launch the shipping WPF application or change its profile.

The earlier attempt at
`.codex-temp/combined-office-foundation-687959738113440e9ad3cb19da15c136` failed an
intermediate-event assumption in the existing synchronous I/O cancellation test,
before image integration. Its evidence is retained. The
[test correction](analyze-io-cancellation.md#cancellation-race-observation-2026-09-16)
requires the native aborted return and original token, with production code
unchanged. The passing run does not erase that failed attempt.


PDF execution
-------------

The combined staged worker passes **443 PDF checks**: 11 analysis, 17
optimization/publication, 51 failures, 19 mixed direct optimization, 25 page
publication, 20 page interruption, 18 direct page conversion, 56 combined-image
publication/recovery, 20 direct combined-image conversion and 206 page geometry.
The ten scenarios preserve their authored originals and use isolated output and
recovery paths. Full inventory checks before and after execution pass.

The source-bound wrapper evidence is
`.codex-temp/combined-office-pdf-7387db5bfa074a8d96deed957a18b228`; scenario results
are `.codex-temp/pdf-engine/production-ea1984d77b8a42408161105fe87af0ad`.
The command explicitly admits the Office-containing payload, but these scenarios
execute PDF/image/audio work only; they do not establish Office export acceptance.


Audio test reconciliation
-------------------------

The first packaged audio attempt at
`.codex-temp/combined-office-audio-00ae49bf689f46818d69e407001d4212` passes all 30
cross-format publications, then fails because its unknown-WAV-metadata fixture
is rejected during preflight. The old workflow test expected rejection later,
during execution. Current `AudioEncodingAdapter` intentionally refuses such
chunks in its source inspection; no production policy is changed to accommodate
the old expectation. The failed attempt retains an unchanged payload inventory.

The corrected test requires a stable unsupported preflight result, then uses
the actual direct-command coordinator for a mixed selection. It checks plain
audio guidance, no publication for the unsupported file, one admission for the
good file, a successful copy, worker continuity and unchanged originals. All
54 conversion-workflow checks pass on the rerun. The separate foundation/PDF
receipts precede this audio-only test correction; their production inputs and
staged worker remain identical.

The complete rerun passes **309 checks**: 11 Analyze, 14 FLAC publication,
16 direct optimization, 54 conversion workflow, 20 direct conversion,
96 conversion interruption, 24 FLAC optimization interruption and 74 publication
crash/recovery. Full inventory checks pass before and after execution, with
unchanged sources and inventory bytes. Source-bound wrapper evidence is
`.codex-temp/combined-office-audio-f913cc24330c465a87274d06bae10ec6`; scenario
results are `.codex-temp/audio-engine/worker-06a660080db048048975c472648fc5b6`.
The optional dedicated artwork fixture was not supplied to this broad replay;
its prior focused evidence is not counted as a fresh run here. Listening/player
acceptance and wider metadata/decoder matrices remain separate.


Office direct execution
-----------------------

The actual staged worker passes **15 direct-command checks** in
`.codex-temp/office-execution/751bc8070f334b25ae9423852b119d7e`, with source-bound
wrapper evidence at
`.codex-temp/combined-office-office-direct-30e8b8a62a5f45b496278402036ce197`.
One mixed selection publishes three Office copies and one ordered image PDF
through a shared admission. Existing output names are preserved. A deliberately
failed Word publication remains retryable and produces exactly one copy on retry.
All five disposable profiles, mappings and contexts are removed; image and
Office originals retain their bytes and write timestamps. Full payload inventory
and source/binary identity checks pass afterward.

Independent qpdf/PDFium inspection passes for all three ordinary Office outputs:
authored text, page geometry and exact retained control pixels match. Evidence is
`inspection-917ae4ca83ec4647a59760ecaaef18d6/results.json` under the Office run.
The retained control is `cs41` under the original Office isolation workspace;
its exact hashes are recorded in the comparison. This is generated-fixture
fidelity evidence, not acceptance of arbitrary documents or a visible UI review.


Office recovery and workbook capacity
-------------------------------------

The staged worker passes **43 Office execution/recovery checks** in
`.codex-temp/office-execution/8f27a275cc7240d69575d719b9dd5888`, with wrapper
evidence in
`.codex-temp/combined-office-office-recovery-47a3a2325a4d41b4b796c5d31606ac9c`.
The batch includes ordinary Word/PowerPoint documents and the authored 48-sheet
workbook. Checks cover numbered output collisions, publication failure,
cancellation before publication and retry of a post-publication cleanup warning
without duplicating the PDF. All six native contexts, profile folders and mappings
are retired; original bytes and timestamps, source identities and the full
production inventory remain unchanged.

Independent inspection of the workbook output and three retained controls passes
**144 page comparisons** across four 48-page PDFs. Authored text, page order,
geometry and exact rendered pixels match; all four inspected PDF hashes remain
unchanged. Evidence is
`contracts/capacity-inspection-97945dbfa7cd423fadf75b5c9021806f/results.json`
under that Office run. This is generated-fixture capacity and recovery evidence,
not arbitrary-workbook fidelity or visible acceptance. It does not close the
separately documented interruption-before-ZIP-completion gap.


Acceptance boundaries
---------------------

On 2026-09-16 the owner selected modern DOCX/XLSX/PPTX-only conversion, retained
Excel's explicit calculation choice and accepted withholding known incorrect
date results while deferring renderer correction. Legacy, template and
macro-enabled conversion is deferred. These decisions do not establish broader
font/layout or date fidelity.

The candidate retains the known early-1900 date guards, mandatory document
copies and fixed Office policies. Packaging does not correct Excel date
rendering, expand supported document variants or prove complete font/layout
fidelity. Network-isolation evidence, redistribution/adoption, human audio and
visible/accessibility acceptance remain open. Signing, installer lifecycle,
live commerce and publishing remain separate authorized release activities.
