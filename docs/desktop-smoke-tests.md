Desktop Smoke Tests
===================

Non-interactive view checks
--------------------------

Run `./tools/Test-ViewContracts.ps1 -Configuration Release` without reserving the
keyboard or mouse. It loads the actual four production windows into the isolated
test host, measures their minimum-size layout without showing them, checks the
UIA names/live settings of status controls, and checks collapsed advanced panels,
planner columns and Escape/confirmation wiring. It starts no worker or trial and
does not send desktop input, install packages or change the system theme.

The 2026-09-08 run passed 34 checks. These prove that the views load and expose
the checked properties, not visual fit, keyboard focus behavior or delivery to
a real screen reader. Interactive checks below are still needed. Visible status
updates now explicitly emit polite live-region events; hidden quick operations
do not announce messages or create windows for accessibility notifications.

Interactive checks
------------------

The public FlaUI.UIA3 5.0.0 harness drives the production WPF executable in
foundation mode and the exact app sources in an isolated test host in image mode.
The latter is a test assembly, not a review edition, fake codec or access bypass:
it enforces the same local trial using scratch storage and calls the real worker.
Neither test project requires private source to compile; real image execution
requires the built production worker. The host is excluded from production output.

Local verification (2026-09-06): WPF mode passed. Explorer mode is experimental
and has NOT passed end to end on this workstation. Runs reached Analyze, but
submenu UIA discovery and tabbed-window targeting were inconsistent; the latest
run timed out identifying the unique visible Explorer frame. Do not use this
mode as a release gate until it passes repeatably on an isolated desktop.
Failed automated runs are retained as evidence.

The extended WPF run also passed for both settings sections, pathless forwarding,
keyboard cancellation, unchanged queue, and unchanged persisted preferences.
Settings is an owned child window in the UIA tree; tests locate it under the app
rather than as a desktop-level sibling. The initial failed lookup is retained
alongside the corrected successful evidence in the active goal brief.

The subsequent user-provided screenshots confirm the manual classic
Explorer-to-WPF handoff: all three commands receive the complete three-file
selection, and one window accumulates 12 rows across four batches (Optimize,
Optimize, Convert, Analyze). The classic root menu shows icons, direct Analyze,
and submenu arrows for Convert and Optimize. This does not establish an automated
Explorer pass, modern-menu presentation, or working media processing. See
[foundation validation](milestone-1-validation.md) for the remaining checks.


Run Locally
-----------

Close Context Suite, unlock Windows, and avoid mouse/keyboard input while these
tests run. Do not run foundation integration tests at the same time.

```powershell
./tools/Test-DesktopSmoke.ps1 -Configuration Release
./tools/Test-DesktopSmoke.ps1 -Configuration Release -Images
./tools/Test-DesktopSmoke.ps1 -Configuration Release -Explorer -SkipBuild
```

The first command builds production and tests the WPF window via normal
activation-file entry points. SkipBuild skips the production build only; the
test project is still built. Image mode also builds the isolated host. Explorer mode additionally requires existing
healthy production shell registration for the same executable/configuration.
None of these commands installs packages, kills existing app instances, restarts
Explorer, or changes file associations. Missing prerequisites are not passes.

Experimental Explorer mode uses English menu labels and Shift+F10 to exercise the classic
keyboard context-menu path. Modern Windows 11 menu layout/icons/order remain
separate manual checks. The harness opens a unique fixture folder, selects the
three files, and invokes Analyze and each Convert/Optimize child action.
The first planning action in each flyout is traversed with Right/Enter because its UIA action
elements are not consistently exposed. Child layout is covered by native
contracts and manual review, not asserted by this keyboard traversal.
Explorer may reuse an existing tabbed window. The test intentionally leaves its
tab open rather than risk closing unrelated tabs; close the test tab manually.


Assertions And Evidence
-----------------------

- Analyze, Convert, and Optimize add three, six, then nine UI rows.
- Every foundation-mode row has the correct batch, operation, unique fixture
  path and terminal status: unsupported Analyze/Optimize or rejected empty-image
  Convert input. Counting rows alone is insufficient.
- Only one application owner remains after each handoff, and Explorer must
  launch the requested production build.
- Keyboard focus remains inside the application and the window can resize.
- Graceful close and reopening produce a fresh three-row batch.
- The three source files are unchanged, with no extra files in their folder.
- In WPF mode, Convert/Optimize Settings requests open the correct tab without
  changing rows; editing and cancelling does not persist preferences. Save and
  schema-failure behavior are covered by isolated settings contracts, not by
  overwriting the user's real preferences in desktop tests.

Each run retains fixtures and a result or failure report in a GUID directory
under .codex-temp/desktop-smoke/. Settings runs and failures capture window-region
screenshots and bounded UI-element listings. Capture failure is reported
separately. These are local diagnostics, not public CI artifacts: Explorer
screenshots can include sidebar locations and unrelated tab titles, and obscured
window-region captures can show overlapping applications. Review
before sharing. The test removes only its known activation request files and
stops only its own application processes. A file lock serializes desktop runs.

Stable AutomationId values identify the window, result grid, summary, and cancel
button. Polling waits for expected conditions, with UIA call and condition
timeouts. Provider/desktop failures must remain explicit failures or NOT RUN,
never silently converted to successful tests.


Image Conversion Mode
---------------------

`-Images` passes 26 checks locally (2026-09-06). It uses independently generated
GDI+ PNG fixtures and a deliberately damaged JPEG file. The real planner enforces
target/matte/metadata choices, consequence acknowledgement and consent reset.
Transparent JPEG confirmation also requires a current matte preview.
It renders encoded before/after previews, cancels without starting a trial,
queues repeated normal activations, produces actual WebP/JPEG copies and verifies
resize through an independent decoder. It then expires only the isolated trial
record and verifies blocked conversion, available settings, and unchanged sources.
Native file picking and retrying a completed row both open fresh plans for the
original source. Cancelling those plans publishes nothing and leaves the trial
start unchanged. The native picker filename is set/read back through ValuePattern
and its Open split-button is invoked through UIA, avoiding fragile filename
keyboard focus. Earlier failed automation attempts remain separate evidence.
An owned external WinForms drag source exercises real OLE file-drop into blank
window space and verifies a Copy result and a fresh plan. Output-folder opening
is verified in Explorer after expiry. The test leaves its own Explorer tab open
instead of risking other user tabs. Synthetic drag-source foreground failures
remain separate evidence; the passing harness verifies focus before mouse input.
The test explicitly selects metadata removal through the UI. Physical-resolution
preservation, including fractional PNG resolution and EXIF round trips, is covered
separately by the adapter matrix.

The test-only entry point reads `CONTEXTSUITE_TEST_ROOT` and
`CONTEXTSUITE_TEST_WORKER` in its own assembly and injects normal application
paths. Production reads neither variable and exposes no trial override flag.
The root must be under `.codex-temp`; no real trial is read, consumed or reset.
This is evidence for the actual windows/view models with isolated composition;
the distinct production-executable smoke still covers non-converting activation,
settings and lifecycle. Do not claim live paid licensing or complete media coverage.

Successful initial image evidence is recorded in the [completed goal](image-conversion-goal.md).
The [BMP/TGA integration](bmp-tga-and-engine-integration.md) adds three planner
checks for BMP matte/preview consent and TGA preview without trial consumption;
all 29 image UI checks pass on an idle unlocked desktop. Output-folder checks
also compare actual shell folder identity and tolerate individual closing COM windows.
The dark before/after preview was visually reviewed. The test waits for preview
layout before taking its capture; earlier failed owner-window lookup/focus runs
are retained instead of relabeled as passes.


Future Coverage
---------------

The DDS extension passes all 33 image UI checks in
`.codex-temp/desktop-smoke/images-883ea7a9ede344d6b640bfce8519623c`.
It adds explicit source interpretation, actual DDS preview before confirmation,
no trial consumption from preview, and invalidation after storage changes.
The DDS controls and shared before/after captures were visually reviewed in dark
mode. See the [completed bounded DDS goal](dds-conversion-goal.md) for independent
codec/worker evidence and the deliberately deferred formats/structures.

Keep most checks in fast contracts. Add real media fixtures and semantic output
checks alongside each adapter; retain source-preservation and collision tests.
Keep the empty-file foundation assertions distinct from image-mode checks.
Extend focused UI coverage for long-running cancellation and failure recovery.
Keep the large media edge-case
matrix in adapter/worker contracts rather than driving every case through Explorer.

Public CI should compile this harness without private source. Interactive runs
are opt-in local checks initially, not assumed available on hosted CI. A future
dedicated Windows VM must isolate desktop access and private production builds;
never give untrusted public pull requests a private self-hosted runner.

Visual clipping, icon appearance, modern-menu ordering, high-DPI behaviour, and
screen-reader usability still require a brief manual review. UIA focus and
resize assertions do not certify accessibility or visual quality.

Application appearance follows Windows through application-level WPF Fluent
`ThemeMode="System"`; no registry polling or separate theme preference is used.
The repository check guards that setting and prevents per-window overrides.
WPF owns live app-mode/accent updates and contrast-theme resources (see
[Microsoft's Fluent guide](https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md)).
The dark-mode desktop run passes and its main/settings captures have been visually
reviewed. This is not proof of a live Windows theme transition. To finish manual
theme coverage, keep both windows open and change Windows Settings >
Personalization > Colors > app mode between Light and Dark, then try a contrast
theme under Accessibility. Confirm controls, selection, focus, text, and scrollbars
remain legible without restarting; restore your preferred Windows appearance.
Automated tests do not change the user's system-wide appearance. Native Windows
message boxes remain OS-owned dialogs rather than WPF-themed content.

References: [FlaUI](https://github.com/FlaUI/FlaUI) and
[Microsoft UI Automation testing](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-usefortesting).
