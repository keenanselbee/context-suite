Office Output I/O Failure Checks
================================

Status: 65 checks passed on 2026-09-23 with the selected reconstructed runtime04
and a fresh managed worker. All three following PDFs pass separate authored
text, geometry and exact historical pixel comparisons. This is scoped output
creation and recovery evidence, not the final versioned-candidate replay.

[Test-OfficeIoFailures.py](../tools/office-engine/Test-OfficeIoFailures.py) builds
only the managed harness/worker and copies the selected pinned Office/PDF engines
into new repository scratch. It creates nine disposable profiles sequentially.
Originals never enter a writable grant. Each source snapshot remains read-only.
No renderer compilation, installation or machine setting changes are involved.

For each ordinary DOCX, XLSX and PPTX fixture:

1. Grant the isolated native host read access to its empty output directory,
   with normal write access to its disposable profile/temp directories. Call the
   production file adapter and require a diagnostic identifying the actual PDF
   export phase, with no successful completion, PDF or calculated workbook.
2. Repeat through the actual worker. Require its existing sanitized failure,
   no accepted candidate/output, and successful profile/mapping cleanup.
3. Export in a fresh normal context using the same worker. Require successful
   completion, independent PDF-reader validation and an exact reservation copy.
   Inspect the retained PDF independently against the original authored controls.

This changes only the grants to the disposable AppContainer SID. It does not
remove or change the user's permissions. Native phase diagnostics establish
where export failed; their trailing Windows error value is not used as proof of
the filesystem error code. The engine can leave an earlier loader error there.
The worker keeps that diagnostic private and returns its existing failure enum.


Evidence and reproduction
--------------------------

The passing source/engine/fixture/binary-bound run is
`.codex-temp/office-io/fed0d333fc8d457ebce5adce58437fdd`.
Its `contracts/results.json` records 65 checks, six refused exports and three
successful following exports. All nine profile directories and registry mappings
are absent; all 5,036 runtime/context ACL entries were checked for all nine SIDs.
Original hashes/timestamps and source snapshots remain unchanged. The worker
stopped, source leases were released, and no native host remained for this
isolated runtime. Both Release builds had zero warnings/errors.

The separate inspection at `inspection-dfb55fdbb7d84bc48695a44a4fb83f15`
passes all three PDF structure, text, geometry and exact control-pixel checks.
Its retained oracle differs from the earlier paired-stop inspector only in the
accepted scratch root; no PDF assertion was relaxed. The same run also passes
25 compiled-profile checks. `independent-replay-01/result.json` binds that
inspection and unchanged inputs.

~~~powershell
python -B tools/office-engine/Test-OfficeIoFailures.py --create-disposable-profiles `
  --office-engine <selected-repository-scratch-engine> `
  --pdf-engine <selected-repository-scratch-qpdf> `
  --pdf-renderer <selected-repository-scratch-renderer> `
  --fixtures <ordinary-authored-fixtures>
~~~

The opt-in is required before building or creating profiles. Supply exactly one
ordinary fixture of each supported Office family. Engine sources and copied
payloads are hash-checked before execution and after the run.


Coverage boundaries
--------------------

These checks complement [preparation interruption and cleanup](office-context-preparation.md),
[publication failure](office-pdf-validation.md) and
[launcher resource enforcement](office-resource-enforcement.md). Existing
publication contracts inject disk-full and permission exceptions into the move
boundary and require preservation of the original; that historical foundation
coverage is not counted as a new run here.

This test neither fills a volume nor simulates a physical disk disappearing.
It proves actual renderer output-creation refusal and following worker recovery;
it does not prove every storage device/error mode, a live disk quota, publication
on a full volume, or a full mid-write disk. The host's PDF/workbook size caps
remain post-export validation bounds. Final A01/A06/A07 replay still needs the
exact new packaged bytes.
