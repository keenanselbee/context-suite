Licensing Implementation And Verification
=========================================

Status: implemented locally; live Polar and interactive acceptance pending

The accepted policy is [decision 0017](decisions/0017-paid-access-and-release-candidate.md),
with seven-day trials and $5 CAD pricing under
[decision 0020](decisions/0020-seven-day-trial-and-pricing.md).
This is not commercial-release clearance or evidence of live entitlement delivery.

Seven-day trial verification
---------------------------

On 2026-09-13, `tools/Test-Foundation.ps1 -Configuration Release` passed 2,403
contracts. Coverage includes schema-1 trial records between days three and
seven receiving only the remaining time from their original start, restart
persistence, concurrency, exact 168-hour expiry, clock rollback and admitted-work
preservation. Expiry fixtures now advance beyond seven days across media flows.
This run did not use `-Integration` or perform installed desktop acceptance.
Live CAD pricing, delivery and production purchase behavior remain separate gates.

Customer workflow
-----------------

Open **License...** from the results window, Settings, Convert or Optimize.
Paste the purchase key and activate once. Keys are masked and cleared from the
entry control after submission. Closing the license window refreshes any open
planner's access status; reopening the app reuses the saved activation.

One purchase includes all future updates. The installation validates daily while
the app is running and keeps paid access for up to 30 days after successful
validation. A provider expiry can shorten that period. Network failures do not
revoke a good cache or extend its deadline. **Validate online** requests an
immediate check. Short quiet jobs allow a bounded in-flight check to finish while
hidden; media admission does not wait for background HTTP.

**Deactivate...** frees this installation after a confirmed server response.
If an activation change was interrupted, use the customer portal from the purchase
email, check/deactivate the installation there, then acknowledge local recovery.
Clearing local recovery neither revokes a remote activation nor resets the trial.
A damaged protected file is retained and blocks paid access; automatic reset would
risk allocating a second remote slot. Contact support for damaged-store recovery.

A definitive activation refusal lets the user correct the key without portal
recovery. Timeouts, malformed replies and interrupted activation remain uncertain
and retain the pending marker. Buttons follow the current state: an active key
offers validation/deactivation; an interrupted change offers portal recovery.
Recovery instructions are hidden for normal active and unactivated states. When
needed, a direct numbered panel explains how to check Polar before trying again;
its confirmation button remains disabled until the portal check is acknowledged.

Analyze, Settings and saved results remain available after expiry. Already
admitted media work finishes. Expired/revoked paid access does not silently start
a new trial. Normal unactivated admission retains the seven-day trial from its original start time.

Protection and boundaries
-------------------------

- Per-user files beside the existing trial record: `license-production.bin` and,
  only in test composition, `license-sandbox.bin`. Windows DPAPI CurrentUser
  protects keys/receipts; environment-specific entropy keeps the stores separate.
- Exclusive leases, bounded files, atomic encrypted writes and durable pending
  activation/deactivation markers. Corruption and linked storage fail closed.
- Background validation releases the storage lease while awaiting HTTP. A unique
  refresh identifier prevents a late response from undoing transfer or recovery.
- No merchant token, hardware fingerprint, selected filename, media bytes, or
  major-version condition is sent. Only the key, configured organization/benefit,
  random installation identifier and activation identifier are used as applicable.
- Production composition is fixed in private source, not a runtime setting.
  The test host has no real provider. The worker and Explorer receive no key.
- HTTPS customer endpoints, ten-second request deadline, 64 KiB response bound,
  cancellation, no redirects and no automatic mutation retries. Expected identity,
  activation, granted status, expiry and one-activation/no-usage limits are checked.
- DPAPI is local protection, not a provider-signed entitlement or tamper-proof
  anti-piracy system. A determined local user can bypass client enforcement.

Repeatable noninteractive checks
--------------------------------

```powershell
./tools/Test-Licensing.ps1 -Configuration Release
./tools/Test-Foundation.ps1 -Configuration Debug -Integration
./tools/Test-PrivateBoundary.ps1
dotnet run --project tests/ContextSuite.Application.TestHost -c Debug -- --view-contracts
./tools/release/Test-InnoInstaller.ps1
./tools/release/Test-InstallerRecovery.ps1
./tools/release/Test-ReleasePackaging.ps1
./tools/Test-Repository.ps1
```

Use an unregistered output configuration for integration builds; do not overwrite
an Explorer-registered payload without approval. Close Context Suite before router
contracts. Do not run multiple foundation suites in the same user session.

Current focused verification: 99 synthetic HTTP transport contracts, 463
public foundation contracts (including paid storage/admission and UI-model checks),
25 mocked installer/input contracts, 52 mocked recovery checks, and seven
engine archive packaging checks. The Debug production build and payload allowlist
pass. The current isolated Release real-worker integration run passed 650 contracts. Forty-four hidden view
contracts also pass, including masked entry, live-status accessibility, Escape
and minimum-size layout; they do not show windows or establish visual acceptance.
The 650-contract integration run includes refusal/UI-state and recovery refinements
but predates the access-only Optimize refresh below. It did not overwrite the
registered development payload. The current
sandbox host also compiles with those refinements; owner-run sandbox results are
recorded below, separately from remaining acceptance.

Open-planner licensing regression checks use the actual conversion/optimization
models, license model, DPAPI manager and trial/access store with a synthetic
provider and clock. Twelve additional checks cover an expired trial, activation
enabling both existing plans, control notifications, preserved choices/consent,
expiry admission despite stale UI, validation recovery, rejection and deactivation
without a new trial. Optimize's repeated status initialization now updates only
access bindings; it does not rebuild the file list or plan. These checks do not
show windows or prove the license-window-close event visually. The current Release
application build has zero warnings/errors; 44 hidden view contracts pass.

Prepared opt-in sandbox test
---------------------------

`tools/Test-LicenseSandbox.ps1` builds only a private test host by default. It
reuses the actual license window, manager, DPAPI store and private transport but
fixes the environment to sandbox, has no media worker/Explorer router, and is not
packaged for customers. No key can be passed on its command line.

Only after explicit permission for a live sandbox/desktop run:

```powershell
./tools/Test-LicenseSandbox.ps1 -Launch -Profile A
```

Enter a sandbox key in the masked field locally, not in chat or a command. Profile
A persists at `.codex-temp/licensing-sandbox/A/license-sandbox.bin`. Close/reopen A
to test persistence and validate without allocating another slot. Open profile B
with the same command and `-Profile B` to check the second-installation refusal.
Deactivate A, then explicitly activate B to test transfer; deactivate B at the end.
Both profiles are isolated from production state, but their buttons perform real
sandbox API mutations. Do not delete an active profile's state before deactivation.
Use the purchase-email portal for uncertain activation recovery. Record outcomes
without screenshots containing keys. A successful standalone license test still
does not prove the full installed-app workflow or refund behavior.

Owner-run sandbox results (2026-09-09)
-------------------------------------

The owner performed these checks in the isolated A/B hosts and reported the
outcomes in chat. Screenshots support the displayed states; these are manual
observations, not independently captured API traces or installed-app evidence.
No customer key is stored in these notes.

- Profile A activation: active; recovery instructions hidden in the updated UI.
- Profile A explicit online validation: remained active (owner confirmed).
- Profile A close/reopen: activation remembered (owner confirmed).
- Profile B with the same key while A was active: activation declined. The generic
  refusal text alone does not identify the provider's cause; the controlled A/B
  sequence supports the expected one-installation behavior.
- Transfer: owner deactivated A, then activated B with the same key. Owner reported
  expected behavior and supplied B's active-state screenshot.
- Cleanup: B is last observed active; final B deactivation is not yet confirmed.

Isolated full-app workflow test (started, acceptance incomplete)
--------------------------------------------------------------

The owner opened the previous harness and reached its expired-trial conversion
window, then requested the UX overhaul. That is not a completed licensing/UI test.
The owner subsequently approved the refreshed isolated test. Desktop control
could not enumerate apps: `Computer Use native pipe is unavailable`, OS error 2,
persisted after a delayed retry and JavaScript-session reset/reinitialization.
No test window was opened or input sent during that attempt. The immediate blocker
is restoring the desktop-control connection, not missing owner permission.

`tools/Test-LicenseWorkflow.ps1` builds the actual application sources in the
public test host and runs ten noninteractive fixture/provider/isolation guards.
It does not open a window without `-Launch`. The simulated provider accepts only
`TEST-ONLY`, never calls Polar, and is not a shipping component. Each launch makes
a fresh `.codex-temp/license-workflow/<id>` profile with an expired trial and a
generated `fixture.png`. Trial/license/settings/publication files and stale-request
cleanup stay in that profile; the production cleanup default remains unchanged.
The full-app router is still shared per user: close other Context Suite apps and
do not invoke Explorer commands during this test. The script checks for running
app/worker processes and requires a verified worker in isolated production staging.

After explicit interactive-run approval:

```powershell
./tools/Test-LicenseWorkflow.ps1 -Launch -WorkerPath 'C:\Repositories\Context Suite\artifacts\production-staging\3a4b8c388052414ebfbcf004f6fcb85b\ContextSuite.Worker.exe'
```

The script prints the generated image path. Windows have a **SIMULATED LICENSE
TEST** title. Use only that image and `TEST-ONLY`, not a customer key.

1. In More tools, choose Convert files and select the generated image. Choose
   TGA. Automatic metadata should need no routine acknowledgement; expired trial
   must keep conversion disabled and offer an activation action. Files/details
   and preview should start collapsed.
2. Open License from the planner, activate `TEST-ONLY`, then close License.
   Conversion should become available without losing the format/consent. Convert
   the disposable image; check the original remains and a named output appears.
3. Open Optimize PNG for the original generated image and choose a preset. From
   that planner, open License and deactivate the simulated installation. Close
   License: optimization must be disabled again because the test trial is expired.
4. Reactivate `TEST-ONLY` and close License. The same preset/list should remain,
   and optimization should be available. A small fixture may correctly produce
   no smaller output.
5. Check keyboard focus, readable status, Escape behavior and screen-reader
   announcements; report those separately from the functional results. Close the
   test app. All test files are retained for review, not installed or published.

The test is prepared with a clean Release build and ten passing harness checks;
those do not count as completion of this interactive sequence or Polar acceptance.

Interactive isolated workflow results (2026-09-09)
-------------------------------------------------

Computer Use connected successfully in a new session. The prior native-pipe
failure did not recur; no configuration or helper changes were made. No Context
Suite processes were running before launch. The authorized workflow script
verified staging `3a4b8c388052414ebfbcf004f6fcb85b`, built the Release test host
with zero warnings/errors, and passed its ten isolated harness checks.

The actual visible-window sequence used only `TEST-ONLY` and the generated image
in `.codex-temp/license-workflow/c69431f84f4048969bbae697ea084573`:

- Convert: selected TGA. Routine metadata required no acknowledgement. Size/quality,
  saving/privacy, files/details and preview started collapsed. Expired access
  disabled Convert and displayed a clear Activate license action.
- Activated the simulated license and closed License with Escape. The existing
  planner retained TGA and its one input, and Convert became enabled.
- Conversion completed and created `fixture - Converted.tga` (9,234 bytes).
- Optimize: chose Balanced for the original fixture and selected its file row.
  Deactivated through License and confirmed the test-only deactivation. Closing
  License disabled Optimize while preserving the preset, file and selected row.
- Reactivated `TEST-ONLY` and closed License. The same plan became usable, with
  the preset, file, selected row and scroll position preserved. Tab reached
  Optimize with a visible focus outline; Enter ran the operation.
- Optimization created `fixture - Optimized.png` (184 bytes). The visible final
  aggregate status reported two completed operations and 18 bytes saved (8.9%).
- The original `fixture.png` remained 202 bytes. Its SHA-256 before conversion,
  after conversion and after optimization was identical:
  `165BB2F002C56A59FC0C188316870FCB0D22872C6447AD1322749B583E4945D9`.
- The test app was closed, the launch script exited with code zero, and a process
  check found no remaining Context Suite app or worker. Test files and simulated
  activation state are retained in the isolated profile for review.

Visible usability findings at the existing dark appearance and display settings:

- Expanding More tools at the initial landing-window size left the tool buttons
  partly clipped until scrolling. An automation click on the clipped Convert
  element opened Settings; Escape dismissed it without saving, then scrolling
  made the intended action accessible. This does not establish a miswired button.
- The expired Convert window partly clipped the collapsed Preview header.
- Optimize's access explanation was below the visible area at its initial size.
  After deactivation the visible summary still said one file was ready while
  Optimize was disabled; scrolling revealed the expiry explanation. That text
  says "New conversions are unavailable" inside the Optimize window.
- Final results retained the earlier instruction to review the planning window
  even though both operations had completed.
- License text and actions were readable. The test key was masked and cleared
  after activation. Tab/Enter activation and Escape close worked; returning from
  License to Optimize visibly restored focus to its License button. These are
  bounded keyboard observations, not full keyboard acceptance. Initial typing
  required an explicit refocus during automation; initial-focus reliability is
  not established by this run.

Screen-reader speech/live announcement delivery, other themes, DPI levels and
full focus traversal remain unverified. The fixture does not establish visual
fidelity across the image corpus. No live Polar request, customer key, Explorer
command, installation, registration, signing, publishing or commit occurred.
The functional isolated sequence passed; visual/accessibility acceptance remains
open because of the findings and untested coverage above.

Usability fixes after the interactive run (2026-09-09)
----------------------------------------------------

The landing and conversion windows now open taller (540 and 620 device-independent
units respectively) to give the expanded manual tools and collapsed conversion
sections more room. Their minimum sizes and scrolling behavior remain available.
Optimize's access explanation is now in the fixed footer above its buttons, with
an Activate license action when access is blocked. The file summary says supported
instead of ready, and trial-expiry wording covers both conversion and optimization.
Accepted manual selections clear the input notice; progress and results continue
to use the current batch summary, without a stale instruction to review a planner.

Verification passed: 475 foundation contracts and 52 hidden view contracts in
Release. New checks cover the blocked/active Optimize action and footer geometry
at minimum size. A test-fixture binding error encountered during the first hidden
run was corrected before the passing run. These checks do not establish visual
usability. Before launching the interactive retest, Computer Use failed its
connection check with `Computer Use native pipe is unavailable`, OS error 2.
No retest app was launched and no helper/configuration changes were made. The
earlier interactive evidence above applies to the pre-fix UI; these layout changes
still require a visible-window retest. Initial focus, screen-reader, theme and
DPI acceptance remain open.

Context-menu simplification follow-up (2026-09-09)
-------------------------------------------------

The manual workspace and routine Optimize planner are now removed. Standalone
launch opens Settings/help; direct commands show problems and useful progress.
The old interactive sequence above is historical evidence, not acceptance of
these new surfaces. The updated harness launches a direct TGA command with the
same isolated expired trial and TEST-ONLY provider. License then Retry failed
preserves the requested target/files; the model workflow also verifies blocked
Optimize, reactivation and retry of the original preset. DDS retains a focused
choice dialog whose access state refreshes when License closes.

Passed 484 foundation, 50 hidden view, 10 harness-guard and 12 direct-command
workflow checks; the latter exercise the real staged worker and simulated
licensing with no native recycling or provider requests. See the
[simplification evidence](context-menu-simplification-goal.md) for exact scope,
quiet tests and new staging location. Final Computer Use connection check failed
with native-pipe OS error 2; no visible retest launched. Final focus, screen-reader,
theme and DPI checks remain open. No live commerce/release gate is closed.

Owner screenshot review (2026-09-09)
------------------------------------

The owner supplied screenshots of simulated activation, completed TGA output,
window resizing and Settings. They identified cramped Settings/results, excessive
License height, unclear Retry failed wording and a retained failure after success.
The [simplification goal](context-menu-simplification-goal.md) records the fixes,
verified isolated saved preference and latest staging. The action now reads Try
again; a successful retry replaces its older failure in the display. Current
checks passed: 485 foundation, 57 hidden views, 10 harness guards and 13 direct
workflow checks. Final visible review of these changes remains open.

Remaining release checks
------------------------

- Sandbox key rotation, portal recovery and actual refund-to-revocation behavior;
  finish by deactivating the remaining test installation.
- Interactive license-window focus, keyboard/screen reader, dark/light/high
  contrast, 100/150/200% scaling, and activation followed by direct-command retry
  or resuming a necessary conversion prompt.
- Signed installed build upgrade/repair/uninstall, protected-state persistence
  and portal recovery on another Windows user or machine. Mock Appx tests are
  not native installation evidence.
- Final signed release archive, customer installer delivery, terms/pricing,
  support process and production checkout verification. Never commit customer
  keys or use them in ordinary tests; screenshots are not API acceptance evidence.

Manual Settings follow-up (2026-09-09): the owner confirmed Tab and Shift+Tab
navigation in the isolated test. Further screenshot feedback prompted clearer
tool tabs, content-height window resizing and simpler overwrite wording. These
latest presentation changes await manual review. This does not certify Escape,
screen-reader delivery, other themes or DPI levels; no live Polar call was made.

The owner subsequently reported the revised UI otherwise working well and
requested clearer validation feedback. Successful online refresh now returns
"License validated online. All future updates included." after saving the
matching receipt. Cached or failed validation retains its existing status and
does not claim online success. Expiry copy says "Analyze remains available"
without mentioning existing results; access behavior is unchanged. The latest
run passed 486 foundation and 73 hidden view contracts. The final copy/table
refinements still need visible confirmation; live commerce and broader
accessibility acceptance remain separate.

Final owner confirmation (2026-09-09): "works well" after the final table and
validation-message revision. This closes that focused visual review. Earlier
pending references are historical; live commerce, screen-reader delivery,
other themes/DPI and native installation acceptance remain open. Tab and
Shift+Tab were explicitly confirmed; no separate Escape result was reported.
