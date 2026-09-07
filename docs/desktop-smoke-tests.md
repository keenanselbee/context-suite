Desktop Smoke Tests
===================

The public FlaUI.UIA3 5.0.0 harness drives the real production WPF executable.
It does not implement a second application, fake codec, or licensing bypass.
The UI test project builds without private source; running it requires a built
production application. The fixture expectations intentionally describe the
current foundation, whose media catalog is empty.

Local verification (2026-09-06): WPF mode passed. Explorer mode is experimental
and has NOT passed end to end on this workstation. Runs reached Analyze, but
submenu UIA discovery and tabbed-window targeting were inconsistent; the latest
run timed out identifying the unique visible Explorer frame. Do not use this
mode as a release gate until it passes repeatably on an isolated desktop. The
manual Explorer smoke remains outstanding. Failed runs are retained as evidence.


Run Locally
-----------

Close Context Suite, unlock Windows, and avoid mouse/keyboard input while these
tests run. Do not run foundation integration tests at the same time.

```powershell
./tools/Test-DesktopSmoke.ps1 -Configuration Release
./tools/Test-DesktopSmoke.ps1 -Configuration Release -Explorer -SkipBuild
```

The first command builds production and tests the WPF window via normal
activation-file entry points. SkipBuild skips the production build only; the
test project is still built. The second command additionally requires existing
healthy production shell registration for the same executable/configuration.
Neither command installs packages, kills existing app instances, restarts
Explorer, or changes file associations. Missing prerequisites are not passes.

Experimental Explorer mode uses English menu labels and Shift+F10 to exercise the classic
keyboard context-menu path. Modern Windows 11 menu layout/icons/order remain
separate manual checks. The harness opens a unique fixture folder, selects the
three files, and invokes Analyze and each Convert/Optimize child action.
The single-child flyouts are traversed with Right/Enter because their UIA action
elements are not consistently exposed. Child layout is covered by native
contracts and manual review, not asserted by this keyboard traversal.
Explorer may reuse an existing tabbed window. The test intentionally leaves its
tab open rather than risk closing unrelated tabs; close the test tab manually.


Assertions And Evidence
-----------------------

- Analyze, Convert, and Optimize add three, six, then nine UI rows.
- Every row has the correct batch, operation, unique fixture path, and completed
  unsupported status. Counting rows alone is insufficient.
- Only one application owner remains after each handoff, and Explorer must
  launch the requested production build.
- Keyboard focus remains inside the application and the window can resize.
- Graceful close and reopening produce a fresh three-row batch.
- The three source files are unchanged, with no extra files in their folder.

Each run retains fixtures and a result or failure report in a GUID directory
under .codex-temp/desktop-smoke/. On failure, the harness attempts window-only
screenshots and bounded UI-element listings. Capture failure is reported
separately. These are local diagnostics, not public CI artifacts: Explorer
screenshots can include sidebar locations and unrelated tab titles. Review
before sharing. The test removes only its known activation request files and
stops only its own application processes. A file lock serializes desktop runs.

Stable AutomationId values identify the window, result grid, summary, and cancel
button. Polling waits for expected conditions, with UIA call and condition
timeouts. Provider/desktop failures must remain explicit failures or NOT RUN,
never silently converted to successful tests.


Future Coverage
---------------

Keep most checks in fast contracts. Add real media fixtures and semantic output
checks alongside each adapter; retain source-preservation and collision tests.
Update these foundation-only status assertions when capabilities actually ship.
Add focused UI tests for planning choices and result presentation rather than
driving every media edge case through Explorer. Cancellation correctness remains
in worker contracts until the UI has a real long-running operation to cancel.

Public CI should compile this harness without private source. Interactive runs
are opt-in local checks initially, not assumed available on hosted CI. A future
dedicated Windows VM must isolate desktop access and private production builds;
never give untrusted public pull requests a private self-hosted runner.

Visual clipping, icon appearance, modern-menu ordering, high-DPI behaviour, and
screen-reader usability still require a brief manual review. UIA focus and
resize assertions do not certify accessibility or visual quality.

References: [FlaUI](https://github.com/FlaUI/FlaUI) and
[Microsoft UI Automation testing](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-usefortesting).
