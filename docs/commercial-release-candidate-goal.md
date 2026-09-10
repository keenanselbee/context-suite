Image Commercial Release Candidate
==================================

Status: active; not commercial release clearance

Current product priority: complete the owner-approved
[context-menu simplification](context-menu-simplification-goal.md) before final
UI and package acceptance. Decision 0018 removes the general customer workspace
and makes safe replacement an explicit persistent Settings choice, with copies
as the default. Existing test/package evidence below predates that change.

Implement the approved [paid-access policy](decisions/0017-paid-access-and-release-candidate.md),
integrate it into the existing quiet image workflow, and produce verifiable
release-candidate evidence without expanding image recipes or adding audio.

Execution checklist
-------------------

- [x] Record owner policy, publisher and private sandbox configuration.
- [x] Typed licensing transport and deterministic policy/failure contracts.
- [x] Protected per-user storage, activation recovery, refresh and transfer (synthetic provider).
- [x] Public activation UI and shared trial/paid admission for both planners and quick actions.
- [x] Offline, expiry, revocation, clock changes and admitted-batch regression tests.
- [x] Packaging/installer integration and local candidate verification (unsigned, uninstalled).
- [x] Review progress, error recovery and accessibility wiring (source and hidden contracts).
- [ ] Complete interactive licensing, focus, scaling and screen-reader acceptance.
- [x] Consolidated customer instructions, component inventory and local release evidence.

Local evidence
--------------

The Debug production build and payload guard pass with the separate private
`ContextSuite.Commercial` assembly. WPF still has no image-engine dependency.
Latest UX integration evidence: **672 foundation contracts with the real worker**
against isolated Release staging `01fcbe432b93490f8c5788f5cb9f0e2e`, including automatic
metadata copies, cancellation, crash recovery and unchanged original-file checks.
This run did not include an application-executable startup argument. The earlier
650-contract run included WPF startup; current XAML has separate hidden checks.
Current focused verification: **942 image-engine/adapter contracts**, **99 synthetic
Polar transport contracts**, **25 mocked installer contracts**, **52 mocked
installer-recovery checks**, and **7 engine archive packaging checks**.
Missing-private builds still fail clearly. No live key, purchase, registration,
certificate or original user image was changed by these runs.
Fifty-one hidden view contracts pass, including the license window's minimum-size
layout, bounded masked key entry, live status and Escape close behavior. Public
source/theme/link validation passes for 53 documentation files.

The license window is wired into Results, Settings and both planners. Its model
has busy/error checks and an existing planner refreshes access after the window
closes. Interactive focus, accessibility, scaling and live-provider acceptance
are not yet verified. See [licensing verification](licensing-verification.md)
for customer steps, storage/recovery behavior and the remaining acceptance list.

Release inventory requires the commercial assembly and checks recorded app/worker
dependencies against the packaged manifests. Release-candidate builds now use a
fresh `artifacts/production-staging/<id>` directory and reject reuse before building.
An unsigned archive and Inno installer were built without refreshing the registered
Release payload; before/after hashes confirmed it unchanged. No installer was run.

Current local artifacts (dirty working sources, test identities, not distributable):

The current archive and installer include the activation-refusal, state-aware
actions, access-only Optimize refresh and automatic-metadata/compact-UI follow-up. They
remain unsigned internal packaging evidence, not an approved customer download.

- Archive: `artifacts/release-candidates/98b4129e00c94b0c94398e92b1803c26/ContextSuite-win-x64-unsigned.zip`
  with 42 inventoried files; extracted archive revalidation passed.
- Archive SHA256: `45EE60D297660FCED7EE08D0EB87DF4331E5200F3A6FA9678FACFD01937402C8`.
- Identity receipt: `artifacts/identity-candidates/b3c99dbc864d46b690acd0a861eccdfb/identity-candidates.json`.
- Installer receipt: `artifacts/installer-candidates/674a678a529c4b5ca241bf74afdd71a9/installer-receipt.json`.
  Its compiled EXE hash is `46D893A468755115AF641850C0B84EA46D337AD84772FBB39D584A182598B54B`.
  `ContextSuite.PackagingTest`, `CN=UnsignedPackagingTest`, and version `0.1.0.0`
  are internal evidence identities, not final product identity decisions.
- Current archive and extracted payload both passed release verification; 25
  installer/input checks and 17 packaging checks passed against the current inputs.
  Earlier artifacts passed 42 lifecycle and 52 recovery checks with mocks.
- Fresh source receipt: `.codex-temp/fresh-source-build/5cb1cc59154c47e8adb78d2b93f3d004/verification.json`.
  It records the actual working-source files and a successful Release build,
  synthetic licensing tests, 121 DDS and 942 image-engine/adapter contracts,
  including the automatic-metadata/compact-UI follow-up. This uses
  installed SDKs/NuGet cache and pinned imported palette assets, not a clean machine
  or an independent palette rebuild. Scratch evidence is intentionally retained.

Public CI now includes hidden view checks; private CI stages pinned palette assets
and runs synthetic licensing tests. No hosted workflow or asset upload occurred.
The [customer quick-start draft](customer-quick-start.md) covers the current image
workflow and licensing recovery; interactive verification remains required.

Latest recovery/UX review: definitive provider activation refusal now clears only
its pending local attempt so the user can correct the key. Uncertain requests still
require recovery. State-aware actions prevent redundant activation, and closing
the window cancels work while preserving uncertain mutation evidence. Explicit
revocation is honored even if its activation is absent. Focused verification passes
463 public contracts, 99 simulated transport checks and 44 hidden view checks.
The isolated private sandbox host compiled and profile A was launched after
explicit owner approval. On 2026-09-09 the owner confirmed A's online validation
and restart persistence, observed B's refusal while A was active, then deactivated
A and successfully activated B. Screenshots show the expected active/refused UI
states. These are owner-run sandbox observations, not independent API traces or
installed-app acceptance. B's final cleanup, rotation and portal/refund checks
remain pending. The desktop helper's native pipe was unavailable after its
documented retry/reset sequence.
Owner feedback prompted hiding recovery for normal states and replacing its
dropdown with direct numbered instructions only when recovery is needed.
See the opt-in commands in licensing verification.

Follow-up planner checks use actual planner/license/access models with a synthetic
provider, expired trial and controlled clock. They verify activation enables open
plans, expiry/rejection prevents new admission, validation restores access and
deactivation does not grant a new trial. Optimize now preserves its file-list and
plan objects when refreshing licensing. The 463-contract suite and current Release
application build pass; the older 650-contract packaged integration evidence above
predates this small refresh change. No installed application or live Polar state
was changed by these checks.

The full-app interactive harness is now prepared: `tools/Test-LicenseWorkflow.ps1`
uses the real App/windows with a test-only, network-free `TEST-ONLY` provider,
expired isolated trial and generated PNG. Ten fixture/isolation/provider guards
pass, and the Release app/test-host builds are clean. Startup stale-request cleanup
now accepts its directory from composition so the test host does not clean the
production activation directory; production defaults are unchanged. Test workflow
guards are included in public CI configuration, not claimed as a hosted run.
The harness is not launched without explicit approval. See licensing verification
for the bounded manual sequence and shared-router precaution.

Interactive follow-up on 2026-09-09: Computer Use connected, and the authorized
isolated full-app sequence passed using `TEST-ONLY`. Activation enabled the open
TGA plan; deactivation blocked the open Balanced optimization plan; reactivation
restored it without losing the preset, input or selected row. Both operations
created named copies and left the original hash unchanged. The app exited cleanly.
The initial-window clipping, hidden Optimize expiry explanation with conversion
wording, and stale post-completion planning notice received local fixes. The
landing/conversion windows open taller; Optimize access status stays above its
buttons; blocked access offers activation; supported files are no longer called
ready; accepted selections clear the old notice. The follow-up passes 475
foundation and 52 hidden view contracts. A visible retest is pending because the
Computer Use connection check again failed with native-pipe OS error 2 before
launch; no helper/configuration change or test-app launch followed that failure.
Bounded Tab/Enter/Escape checks passed, but full focus traversal, screen-reader
delivery, other themes and DPI acceptance remain open. See the recorded
[interactive results](licensing-verification.md#interactive-isolated-workflow-results-2026-09-09).
This run did not contact Polar or change installation/registration state and does
not close the commercial-release goal or its external gates.

Progress and recovery source review
-----------------------------------

`QuietWorkflow` and its PNG contracts cover the two-second progress boundary,
exactly one completion sound, muted operation, and no success sound for warning,
failure or cancellation. `App` wires those outcomes to delayed progress and an
attention window with the first problem selected. `MainViewModel` and its foundation
contracts cover aggregate results, original/output paths, post-publication
cancellation safety and retries that exclude completed outputs and duplicate clicks.
`StatusTextBlock` supplies polite, coalesced announcements only for visible loaded
controls; hidden view contracts verify accessible names and live text, not actual
screen-reader speech. Source review is complete; interactive acceptance remains
explicitly open above, including the owner's recovery-panel feedback follow-up.

External gates (not implied by this goal)
----------------------------------------

- Signing account, billing, identity verification and final signatures.
- Explicitly authorized interactive desktop and installed-registration changes.
- Independent Windows lifecycle acceptance; mocked tests are not native evidence.
- Owner approval for commits, pushes, private artifact uploads and hosted CI.
- Live purchases/refunds, customer uploads and publication.
- Final pricing, source/product/support terms and tooling/redistribution approval.

Continue independent local work while these gates wait. Never mark an unavailable
check passed or remove a safety admission gate to make a candidate appear ready.

Context-menu simplification checkpoint (2026-09-09)
-------------------------------------------------

The [simplification goal](context-menu-simplification-goal.md) now has an
implemented direct-command utility, safe saved output preference, and passing
local automated evidence. Fresh production development staging is
`artifacts/production-staging/fc166b574b8d481bb90f618b3ca358c5`, including the
owner's window sizing, Settings, retry-result, equal-width wrapped columns and
validation-message refinements. Build and payload checks passed, followed by
10 isolated licensing-harness and 13 direct-command workflow checks. Older
installers and archives do not contain these changes. The owner confirmed the
final column/message refinements work well, closing the focused simplification
review. Automated desktop control remains unavailable; screen-reader delivery,
other themes/DPI and installed Explorer still need separate acceptance. Signing,
installed lifecycle acceptance, live commerce and the remaining release gates
remain independent and open. No installation, registration, commit or publication
was performed for this checkpoint.
