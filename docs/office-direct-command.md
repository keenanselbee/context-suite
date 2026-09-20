Office Direct PDF Command
=========================

Status: application integration and isolated execution verified, including the
expanded combined scratch worker on 2026-09-19. Visible acceptance remains
pending. Office engines are included in reserved candidate 1.1.1 and the later
[combined expanded-input payload](combined-expanded-inputs.md); final versioned
acceptance remains incomplete.

Convert > PDF routes `.docx`, `.xlsx` and `.pptx` selections to document preflight.
Extensions select the workflow; they do not establish conversion eligibility.
The application checks the exact content family and variant, source size and
fingerprint before admitting work. Mislabeled, malformed and unsupported
documents receive their own result while other eligible files can proceed.
Legacy, macro-enabled, template and OpenDocument conversion remain outside this
implemented variant set. The owner selected ordinary modern DOCX/XLSX/PPTX only
for the first release; the [document design](document-design.md) records that
scope and the remaining fidelity requirements.

Each Office document produces its own validated PDF copy. Images in the same
selection form one combined PDF, with a focused page-order review for multiple
images. The image subset retains its own reviewed order. Office documents are
not inserted as pages in that combined image PDF. One admission covers the
confirmed image and Office plans; expiry after admission does not split the
batch into differently licensed work. Originals remain mandatory copies even
when Settings requests overwrite.

For spreadsheets, the focused **Spreadsheet formulas** window asks for saved
values or local recalculation. Neither policy is automatically selected. Cancel
or closing the window cancels unfinished work before this command executes.
Retries retain the selected calculation policy and use current output settings.
The owner selected retaining this explicit choice with no silent default.
Word retains final text with tracked-change markup hidden under the fixed export
policy. PowerPoint retains the previously verified visibility policy.

The application and command share one startup recovery task. New Office work
waits for it; incomplete scans or records needing review block Office execution
with the retained recovery location. Unrelated image actions and Analyze keep
their existing queue behavior. A long-lived executor retains cleanup ownership
through the view model's lifetime. Shutdown retries cleanup and leaves durable
records for the next process if it cannot finish. No pending journal is discarded
to allow another export. Retention of incomplete preparation and interrupted
exports without retirement intent remains unfinished release work.

Office failure guidance
-----------------------

Office worker exceptions and final results use fixed document-specific messages.
The shared failure categories and transport are unchanged. A native export failure
does not establish that the document is damaged; its message recommends opening
the document in its usual application and exporting there. PDF-validation refusal
instead states that the generated PDF could not be validated and was not saved.
Timeout, unexpected worker exit, file access and processing limits have distinct
guidance. Known early-date fidelity and cleanup/recovery explanations remain.
Native exception text, paths and document metadata never supply the user message.

The result window previously used a generic Office explanation. The misleading
"damaged image" wording came from the shared worker exception in diagnostics;
it was not evidence that the result window displayed that text. Both surfaces
now use the Office wording, with the executor preserving the failed operation's
phase. Other media operations retain their existing exception messages.

All **4,130 foundation checks** pass, including ten additional message/privacy
and compatibility checks. The current source-bound log and receipt use
`.codex-temp/office-failure-final-foundation`. All **eight actual-worker checks**
pass in `.codex-temp/office-failures/1718430225754f75ad4154ac7381674a`: invalid
contexts, a missing directory, unavailable validation inputs and fabricated
completion identities receive safe typed explanations; the worker remains usable,
leaves authored bytes unchanged and releases its scratch directory. The test
worker has no Office engine and creates no Windows profile or native Office
export. Worker, application and private harness Release builds have zero warnings
or errors. Visible result-window acceptance is not inferred from these checks.

The first worker attempt, `b0331010d6334c02b475301847a24ea0`, stopped on an
incorrect test expectation: missing native directory handles currently map to
the generic engine-failure category, not file access. The corrected test preserves
that production classification. The failed process exited, inputs were unchanged
and no native Office profile was created. It is not counted as a passed run.

Run the focused contract host after a Release foundation build with
`--office-worker-failures <scratch-worker.exe> <fresh-repository-evidence-directory>`.
Use an evidence directory below `.codex-temp/office-failures/` and a scratch worker
with the pinned PDF readers. This mode creates only repository-local fixtures.


Verification
------------

The final orchestration run passes all **3,472 foundation contracts** in
`.codex-temp/office-preparation-foundation-eb06d0a8899b4bef9c3a563cc3facccb`.
Eleven new checks cover absent engines, denied admission, explicit calculation,
cancelled choice, retry/settings retention, duplicate/malformed/mislabeled
documents, mixed row grouping and unresolved recovery. Presence markers are
non-executable test fixtures: these checks launch no Office engine or native
profile.

The real-engine direct workflow is available through
`tools/office-engine/Test-OfficeExecution.py --direct` with explicit disposable
profile authorization and the three Office/PDF runtimes plus the combined-image
validator. It exercises one mixed command and one failed-publication/retry pair,
retaining source hashes, per-file results and completed journal copies. A passing
test requires all five native profiles and generated contexts to be removed.

The run at `.codex-temp/office-execution/43228ce148d441f181061d07a4a1f6ff`
passes **15 actual command checks**, exits zero and verifies unchanged inputs.
It publishes one combined image PDF and three separate Office copies, preserves
an existing output name, and retries a deliberately failed Word publication
exactly once. All five native profiles/mappings and generated contexts are
absent; original hashes and modification times remain unchanged.

The three normal Office outputs pass independent structure, authored text,
geometry and exact control-pixel comparison in that run's
`inspection-8b3bd9381da44a03838328bd9169e6e4` directory. The initial inspection
stopped before checking PDFs because it expected sibling outputs; the inspector
now also accepts the exact test-owned selected output folder. It does not accept
arbitrary output paths.

The [native profile environment follow-up](office-app-preparation.md) reruns all
15 direct checks with the corrected adapter in
`.codex-temp/office-execution/cb8320577db3481aabe99f9fb1fcb3c0`. Its
`inspection-5e585ae2cf144f05a9e5343d51a13c20` independently verifies the three
normal Office PDFs against the same text, geometry and exact pixel controls.
Inputs remain unchanged and all five profiles/contexts are removed. That
follow-up also passes 55 actual-App checks across interrupted preparation,
forwarded Analyze, a following Word export and blocked-cleanup guidance; it
does not establish visible spreadsheet-prompt or broader fidelity acceptance.

All **110 hidden view contracts** pass, including the new window's layout,
explicit button policies and cancellation declaration. The window's minimum
height was finalized after engine execution; rendering and dispatch code did not
change. These tests never show the window or send keyboard input.

The follow-up at
`.codex-temp/office-direct-regression-eb59bf1250b443a6b181da9b8d519597` passes
**20 direct image-PDF regressions** and the native shell/host contracts. The
actual app lifecycle run at
`.codex-temp/office-app-lifecycle/a2183aa49d5a46d4badc230139a96e33` passes **42
checks** across absent/completed storage, retained review, forwarded Analyze and
shutdown waiting for a locked record. These existing lifecycle scenarios use
authored records and image conversion; they do not exercise an actual Office
command or the new spreadsheet window through the application dispatcher.

Visible sizing, keyboard/Escape and screen-reader delivery of the spreadsheet
choice window have not yet been accepted. Theme/DPI, actual Office command
shutdown/restart,
installed shell, broader document fidelity and complete final-candidate replay remain
separate gates. Automated command tests are not visual acceptance or commercial
release clearance.


Expanded combined worker replay: 2026-09-19
------------------------------------------

`Test-OfficeExecution.py` now accepts `--scratch-payload` as an alternative to
`--production-stage`. Both modes verify the full engine allowlist and inventory,
include HEIF verification when present, and execute the supplied worker directly
without rebuilding or copying it. The scratch mode is restricted to repository
scratch and explicitly identified in the final receipt; it does not turn the
payload into a formal versioned candidate. Separate worker/engine overrides
cannot be mixed with either packaged mode. Disposable native profile use still
requires its explicit opt-in argument.

```powershell
python -B .\tools\office-engine\Test-OfficeExecution.py `
  --create-disposable-profiles --direct `
  --scratch-payload '<verified combined scratch payload>' `
  --fixtures '<retained ordinary Office fixture directory>'
```

The owner's existing disposable-profile authorization covers this run. Evidence
`.codex-temp/office-execution/36e3d5fd591740899dd9763805983ea0` passes all **15 direct
execution checks** against the exact worker in
`combined-inputs/e549e5e3509942b4b8ec8e0d6545f93a/combined` under scratch. The initial
mixed selection publishes three Office PDFs plus one combined image PDF with
one access admission and the explicit spreadsheet/image-order choices. Existing
output names are preserved. An injected Word publication failure produces no
published file, remains retryable, and a double retry queues exactly one new
attempt and copy. Originals retain bytes and timestamps. All five actual Office
exports retire their context directories, native profile folders and registry
mappings; no worker remains. The complete payload inventory and recorded inputs
are unchanged afterward.

Independent inspection `inspection-441a1dc285534743a4500d03c7b115ac` under that
execution directory passes qpdf structure, authored text, declared geometry and
exact retained-control pixels for the three normal Office PDFs: two Word pages,
one Excel page and two PowerPoint pages. The source Open XML parts match the
earlier reference fixtures byte-for-byte. Control PDF identities match the
previous accepted comparison under `office-isolation/1a880d93be994b9a9a3467bb77aa1ba3/cs41`.
This is a regression comparison for these ordinary documents, not broad Office
fidelity or proof of actual page ordering for distinct images in the mixed PDF.

The contract host builds with zero warnings/errors. Eleven selection guards at
`.codex-temp/office-execution-guards/8a9628e207eb434e998b18bd362fe642` verify exact
scratch selection, HEIF allowlist selection, path boundaries, required members,
explicit profile opt-in and incompatible arguments before native work. Run them
with `python -B .\tools\office-engine\Test-OfficeExecutionGuards.py`.
The harness takes 138.56 seconds for the five Office exports and its checks;
this is not a new performance sample or replacement for the declared benchmark.
The previous 4,439 foundation result was not rerun for this test-tool change.

The [Excel chart restriction](excel-layout-acceptance.md) remains in force; ordinary
chart-free Excel conversion passes this replay. D07 charts, D09 typography,
affirmative network enforcement, real documents, visible UI and full A01-A08
acceptance remain open. Successful disposable profile cleanup is not proof of
network denial, and hidden command execution is not human usability acceptance.
