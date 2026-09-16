Office Direct PDF Command
=========================

Status: application integration and isolated execution verified; visible
acceptance remains pending. Optional Office engines remain
outside the reserved production candidate.

Convert > PDF routes `.docx`, `.xlsx` and `.pptx` selections to document preflight.
Extensions select the workflow; they do not establish conversion eligibility.
The application checks the exact content family and variant, source size and
fingerprint before admitting work. Mislabeled, malformed and unsupported
documents receive their own result while other eligible files can proceed.
Legacy, macro-enabled, template and OpenDocument conversion remain outside this
implemented variant set; broader launch coverage still requires a scope decision
or implementation and fidelity evidence.

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
This prompt does not settle the owner's still-pending preferred customer default.
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
installed shell, broader document fidelity and fresh expanded packaging remain
separate gates. Automated command tests are not visual acceptance or commercial
release clearance.
