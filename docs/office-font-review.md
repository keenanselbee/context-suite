Office PDF Font Review
======================

Status: implemented in the optional Office candidate; 85 isolated execution
checks and seven independent PDF comparisons pass. Complete font fidelity and
visible acceptance remain open.

When the pinned renderer reports missing font families, Context Suite asks
whether to **Save PDF copy** or **Skip file**. The compact window identifies the
document and reported families and explains that the PDF may look different.
It starts with Skip focused; Enter has no default Save action. Escape, closing
the window and an unavailable review handler grant no permission to save.
Skipping affects only that file; later documents continue and require their own
choices. Retrying asks again. No preference stores substitution consent.

The original remains unchanged even with Overwrite originals selected. An export
without a missing-family report follows the existing quiet path; this is not a
claim of complete font fidelity. The
[callback evaluation](office-font-callback-evaluation.md) describes the renderer's
filtered substitutions and missing style/glyph/embedded-font coverage. Those
limitations remain release gates.

The later [Word style evaluation](word-font-style-evaluation.md) found that a
deleted-text font can produce a report even when the PDF matches the clean
control exactly. The app now removes only names that bounded, retained Word
source evidence proves exclusive to inactive revisions. Incomplete coverage,
uninspected settings/dependencies, active declarations and unknown report names
retain the existing prompt. Raw renderer reports remain intact; see the
[exclusivity rules](word-body-font-inspection.md#inactive-revision-reports).


Publication boundary
--------------------

The native host registers the pinned missing-family callback after document load,
copies its bounded data while valid, and unregisters after export. The terminal
reply follows engine shutdown and source/output identity checks. Version 2
requires an explicit font-report array; version 1 and omitted data are refused,
never treated as an empty report. The fixed export policy and ownership journal
policy remain unchanged so prior interrupted work remains identifiable.

The reply is bounded to 36 KiB. At most four callbacks can carry an aggregate
16 KiB of UTF-8 JSON, transported as hexadecimal strings to prevent raw engine
text from becoming terminal JSON syntax. The managed reader rejects malformed
encoding, unexpected/duplicate fields, invalid names and limits exceeded.
It allows at most 64 unique families, at most 128 UTF-16 characters per family,
and at most 16 KiB of family-name UTF-8. Repeated events merge names without
silently repairing an invalid individual report. Worker transport carries only
validated names to the application.

The app stops the native worker and removes its AppContainer profile before
independent PDF validation. Font review occurs after that validation and before
publication. While waiting, the output reservation remains Prepared; the app has
not handed its validation result to the publisher or permitted a commit. After Save, the app
rechecks the source/context and uses the existing transactional copy publisher.
Cancellation wins even if the review handler concurrently returns Save.

Skip, cancellation, invalid PDF and review failure abandon the reservation and
retire the owned temporary context. Existing cleanup failures retain the same
ownership/recovery evidence and block further Office work until resolved.


Verification commands
---------------------

Use a fresh scratch worker with the current pinned native host and full Office,
qpdf and PDFium payloads. Reuse the six generated fixtures from the callback
evaluation. Native profile creation requires the owner's explicit disposable
test authorization.

```powershell
python tools/office-engine/Test-OfficeExecution.py --create-disposable-profiles `
  --font-review --retained-worker '<current scratch worker executable>' `
  --fixtures '<generated callback fixture directory>'
```

The thirteen-export matrix covers three quiet controls, per-file Save/Skip,
retry, absent review, cancellation racing Save, review failure, corrupt PDF
refusal before review, and the direct application view-model event. The
independent probe command is:

```powershell
dotnet run --project tools/office-engine/Probe/Office.Evaluation.csproj -c Release -- `
  --inspect-font-publications '<completed execution results.json>' `
  '<verified qpdf executable>' '<verified PDFium probe executable>' `
  '<completed callback inspection results.json>'
```

The independent inspection checks every published copy against the corresponding
previously accepted fixture, including structure, exact text, page geometry and
pixels. It preserves the observed substituted appearance as a control; it does
not declare that appearance equal to the requested missing font.


Recorded results
----------------

The completed execution is
`.codex-temp/office-execution/f15c4c14fe5e47c9a327627cb4838411`.
All **85 checks** pass across thirteen started exports. The quiet controls save
without a font prompt. Skip preserves the original and permits the next file;
retry asks again. Missing review, cancellation racing Save, review failure and
corrupt PDF cannot publish. The direct view-model event preserves the next
document's PDF target and cached spreadsheet calculation choice. All original
bytes/modification times are unchanged. All thirteen profile folders, mappings,
contexts and journals are removed; no worker or temporary output remains.
The wrapper verifies unchanged source, fixture, binary and runtime hashes.

Independent inspection
`font-inspection-bc21792131aa48cfa399db2f374865a4` checks all **seven saved
PDFs**, covering all six fixtures. qpdf structure checks pass, and extracted text,
page geometry and pixels exactly match the previously accepted corresponding
control, across eleven page pairs. The missing-family controls still depict
substituted fonts; this does not accept unreported substitutions.

The native host build is
`.codex-temp/office-host/e9fff2e9d4904b4aa50d2821379d45d3`, with executable
SHA-256 `D8FE02337D606C3ECD17F3919F7D6F1208BB3085A142339A7ECFAC69100F5662`.
Its source-bound scratch worker is
`.codex-temp/office-execution/cd4db5e58f5f414599199aa6b5602677/worker`.
The complete 19,332-file Office runtime, inventory, host and exact directory
membership pass the staging verifier. The private checkout pins LF endings for
these native build inputs, so Git checkout cannot invalidate their source
hashes by converting them to CRLF. The reserved formal 1.1.0 package is unchanged.

The **3,701 foundation contracts** pass in
`.codex-temp/office-font-contracts-27ada30166944378b426101f8a114e29`.
All production input hashes still match that receipt. Later changes only extend
the isolated execution test and hidden view test; those pass separately.
The final **119 hidden view contracts** include a 64-family list at minimum
window size with scrolling and reachable actions. Native warnings-as-errors,
worker, test-host and independent probe Release builds pass.

The first execution attempt at
`.codex-temp/office-execution/baa57842ca374db7b147a253079eb3f0` is retained as
a failed test. The test tried reopening the live journal for writing; the
corrected test reads a detached copy. That failed run's six profiles/mappings,
contexts and temporary outputs are removed, with originals unchanged. It is not
counted as passing execution evidence.

Hidden XAML contracts cover explicit Save/Skip/close results and declared Escape
behavior. They do not prove visible layout, actual keyboard delivery, screen-reader
announcements, other themes or DPI levels. Manual acceptance must verify these
separately. No formal package, installed shell or release clearance is claimed.
