Context-Menu Simplification Goal
================================

Status: complete for the simplification scope; automated evidence passed and owner manual review accepted
Owner direction recorded: 2026-09-09

Objective
---------

Deliver the focused utility defined in
[decision 0018](decisions/0018-context-menu-utility-and-output-preference.md):
direct context-menu image commands, copies by default, and an explicit Settings
choice to replace originals safely on future commands. Remove the general
customer workspace and routine planners. Keep only necessary decisions,
read-only Analyze, compact progress/problems, Settings and License.

This takes priority over polishing the current general-purpose UI. The
[commercial release goal](commercial-release-candidate-goal.md) remains open;
this goal is not permission to ship.

Execution checklist
-------------------

- [x] Record the accepted scope, replacement consent and migration rules.
- [x] Add a versioned per-tool output choice; default/migrate to copies, never
  reinterpret legacy permission-only settings as standing replacement consent.
- [x] Simplify Settings opened from Convert/Optimize: clear copy/replace choice,
  safe consequence text, existing sound preference and access to License.
- [x] Make direct commands honor the captured output choice through existing
  validation/publication/recovery gates, retaining mandatory copy exceptions.
- [x] Remove manual picker/drop workspace, routine planners and unnecessary
  menu navigation from the customer surface. Direct app launch opens settings/help.
- [x] Retain small, relevant prompts for actual decisions and licensing blocks;
  preserve the chosen command/files across activation and cancellation.
- [x] Keep Analyze readable and progress/problems compact, with details and
  recovery paths available when useful. Preserve quiet success and process exit.
- [x] Update menu/schema compatibility, tests and customer/design documentation
  together; preserve supported image/DDS functionality and current fixed presets.
- [x] Verify automated settings migration, immutable batches, direct commands,
  output copies/replacement, collisions, changed sources, cancellation, failure
  recovery, access refresh and quiet lifecycle using isolated disposable inputs.
- [x] Verify the final customer surfaces visually and record keyboard,
  screen-reader, theme and DPI results separately. Installed Explorer acceptance
  remains subject to its existing explicit authorization boundary.
- [x] Refresh local candidate evidence for the simplified product without
  installation, registration or publication. Do not present older packages as
  containing this change.

Implementation and evidence (2026-09-09)
----------------------------------------

- Settings schema 2 uses explicit per-tool `ReplaceOriginals`. Copies remain the
  default; schema 1 permission-only flags never become overwrite consent. Invalid
  output-directory fields also disable replacement rather than redirecting it
  unexpectedly to the source folder. Loading does not rewrite stored settings.
- Settings has Create copies / Replace originals choices with standing-consent
  text. Commands capture that choice; alternate folders and Automatic metadata
  omissions retain copies. Publication, platform, source-change and recovery
  checks remain enforced. The approved PNG `fdEC` exception is unchanged.
- Removed manual picker/drop tools and the landing workspace. Standalone launch
  opens Settings/help. Direct Optimize never requests a planner; the legacy
  choose-preset command uses Lossless. Convert offers direct formats and DDS...
  instead of More options. Necessary conversion dialogs lock the menu target,
  retain matte/DDS previews and explicit DDS information handling, and omit
  ordinary quality/size/privacy and repeated replacement-consent controls.
- Licensing problems retain the failed command/files. License followed by Retry
  failed uses the same format/preset; already successful outputs are not repeated.
- Passed **484 foundation contracts**, **50 hidden view contracts**, **10 harness
  guards** and **12 isolated direct-command workflow checks**. The latter cover
  activation/deactivation/retry, copies, captured replacement settings, same-path
  backups, different-extension publication, cleanup refusal and DDS cancellation.
  The injected recycler retains files; this is not native Recycle Bin evidence.
- Passed **682 combined foundation/real-worker contracts** during implementation
  using the previously verified worker. After the final focused DDS changes,
  foundation, hidden views and direct-command checks passed again against current
  sources; the direct-command checks use the new staged worker below.
- Quiet full-app tests passed four Convert and eight Optimize activations over
  two rounds, including shared-router forwarding, unique outputs, unchanged
  originals, no early window and automatic exit. Evidence directories:
  `.codex-temp/quiet-smoke/b7ab419e9b83438993ef25419db9e439` and
  `.codex-temp/quiet-smoke/1f32a38bb1a648579c371d75dab4acdc`.
  The harness now uses repository-local activation fixtures and an injected
  test-only reader. Production activation parsing/ACL behavior remains unchanged;
  the first isolated-directory run failed while setting its ACL and is not a pass.
- Native shell/host contracts passed with DDS action/schema compatibility.
  Native builds can target isolated output; production staging now also builds
  its native shell separately instead of refreshing the registered development
  location. No installation or Explorer registration changed.
- Fresh Release production build and payload identity/allowlist verification
  passed with zero managed warnings/errors at
  `artifacts/production-staging/ad31a7f044784c0c8c28aee186321909`.
  It includes the simplified application and menu, plus a payload inventory.
  This is development staging, not a signed or released installer.
- Current direct-workflow evidence:
  `.codex-temp/license-workflow/direct-2c6a38611cc840aa96f7e83b39418337`.
  Disposable originals, retained backups and generated outputs remain for review.
- Computer Use failed its connection check with native-pipe OS error 2. No visual
  test was launched, no helper/configuration was changed, and no repeated retries
  were attempted. Final visual layout, keyboard/focus, screen-reader delivery,
  themes and DPI acceptance remain open. Earlier screenshots concern the old UI.

Run automated direct-command checks with `tools/Test-LicenseWorkflow.ps1
-DirectCommands -WorkerPath <verified-staged-worker>` and quiet checks with
`tools/Test-QuietOptimization.ps1 -WorkerPath <verified-staged-worker>` (add
`-Convert` for conversion). The interactive `-Launch` licensing workflow now
starts direct TGA conversion; activate through License and use Try again.
`-Launch -Settings` opens the standalone Settings/help surface. Check Computer
Use connectivity and absence of other Context Suite processes before launching.
Do not use Explorer commands concurrently because the router is shared per user.

Completion-audit follow-up (2026-09-09)
--------------------------------------

The audit found that the old image desktop harness still expected manual file
picking, drag/drop, editable quality/resize controls and routine planners. It is
now rewritten for direct DDS/JPEG/WebP commands, necessary prompts, queued
forwarding, immutable source bytes, expired access, cancelled output preferences
and keyboard close. Image mode requires a verified staged worker and keeps all
activation fixtures inside the repository. It no longer refreshes the installed
or development production payload. The test project builds with zero warnings
and errors, but its new interactive assertions are **not yet run**.

The compact report now has an explicit Close button wired to Escape through the
existing graceful cancellation/shutdown handler. All 50 hidden view contracts
pass, including this wiring; actual keyboard/focus delivery remains unverified.
This closes a source-level gap without substituting hidden tests for visible
acceptance.

A fresh Release build including this Close action passed with zero warnings/errors
and verified payload/inventory at
`artifacts/production-staging/558b233d150446efb743150d7a935c35`. This supersedes
`ad31a7f044784c0c8c28aee186321909` as current development staging; neither is a
signed release. The connection was checked once again after this follow-up:
Computer Use still reports native-pipe OS error 2. No interactive app was launched
and no helper/configuration change was attempted. Visual acceptance remains the
specific unresolved gate; additional successful hidden tests cannot close it.

Blocked audit: the same native-pipe OS error 2 was confirmed across three
consecutive goal turns. The latest staged application, worker, shell and inventory
remain present, and no Context Suite process is running. Independent implementation,
test-harness and documentation work is complete for this scope. Resume final
visible/keyboard acceptance when desktop control is available; do not change the
fixed pipe configuration or launch the helper manually. The goal is blocked,
not complete, and no commercial release gate is waived.


User screenshot feedback and fixes (2026-09-09)
-----------------------------------------------

The owner manually tested the isolated application and supplied five screenshots.
They show successful activation and TGA conversion, a too-tall/narrow License
window, cramped initial results and Settings, clipped output choices, and stale
failed-attempt rows after successful retry. This provides actual visible feedback;
it does not establish full keyboard, screen-reader, theme or DPI acceptance.

Changes responding to that feedback:

- Results default to 720 x 540 and Settings to 680 x 660. License is wider and
  sizes its height to content, with scrolling bounded for recovery instructions.
- Settings presents Create copies / **Overwrite originals** first with readable
  explanations. Copy location/names are secondary. One body scrolls while Save,
  Cancel and the status remain outside it; no inner tab viewport clips a radio.
- The overwrite text accurately explains recycling after validation and renamed
  backups for same-format overwrites. Serialized `ReplaceOriginals` is unchanged
  so existing version-two preferences retain their meaning.
- **Try again** replaces Retry failed. It is offered for retryable unfinished
  work, skips successful outputs, and no longer leaves a stale queued notice.
  Once a retry is accepted, its older failure is excluded from displayed rows
  and aggregate counts; internal attempt history is retained. Successful retry
  selects its current result rather than showing the expired-access failure.
- Inspection confirmed the owner's separate Settings test profile
  `40b3f9d7d57649b18531e4d7a5091095` saved Convert `ReplaceOriginals: true`.
  The conversion screenshot profile `814af30fe8494d949129474dd1e767d5` has no
  settings file and contains both original PNG (202 bytes) and output TGA
  (9,234 bytes). Each harness launch creates a fresh isolated profile, so the
  saved choice was not expected to affect that other session or the installed app.

Verification passed: **485 foundation contracts**, **57 hidden view contracts**,
**10 licensing harness guards**, and **13 direct-command workflow checks**.
New checks verify that both Settings tabs show their output choices without
scrolling at initial size, Save remains outside the body at minimum size, License
has bounded natural content height, and a successful retry shows only the current
result. Desktop-harness build passed with zero warnings/errors; its interactive
sequence remains unrun. These fixes still need a fresh visible review.

Fresh Release production staging and payload verification passed with zero
warnings/errors at
`artifacts/production-staging/89a35bc321684308a7ae5cfd3e0181c4`, superseding the
previous staging for this UI revision. Direct workflow evidence is retained at
`.codex-temp/license-workflow/direct-637197f448c24a32923e47bec308c9dd`.
No installed settings, Explorer registrations, private source, live provider,
commits or published artifacts were changed.

Final harness alignment (2026-09-09)
-----------------------------------

The screenshot-driven revision hides Cancel work while idle and Try again for
non-retryable unsupported input. The image desktop harness now treats an absent
idle control correctly and exercises Try again on an expired command instead of
trying to invoke it for an unsupported damaged file. It still verifies that
completed outputs are not repeated. The revised harness builds; interactive
assertions remain unrun. Production staging is unchanged by this test-only fix.

Verification approach
---------------------

Final acceptance (2026-09-09): following the final column/layout and validation
wording changes, the owner confirmed "works well." This closes the focused
manual review and the simplification goal. Tab and Shift+Tab were explicitly
reported working earlier. The review used the owner's dark-mode configuration;
Escape was not separately reported. Screen-reader delivery, other themes,
DPI levels, monitor-edge behavior, installed Explorer and native installer
lifecycle acceptance remain unverified by this review and are separate release
checks. Automated desktop control remained unavailable; no automated visual
pass is claimed. The final quick start was corrected to describe direct commands,
Try again, and saved overwrite consent instead of the removed manual workspace.
The earlier pending/blocked entries below are historical checkpoints.

Second manual Settings review (2026-09-09): the owner confirmed Tab and Shift+Tab
work in the reviewed UI. Dark-mode screenshots showed weak tab distinction and a
large blank area after closing Copy location and names. This is user-reported
keyboard evidence, not screen-reader, Escape, theme-transition or DPI acceptance.

The follow-up uses shared content-height sizing for results, Settings, License
and necessary decision windows. Opening/closing inline sections and switching
tabs restores height fitting after a manual resize. Width stays user-controlled;
height is bounded by the current monitor's work area, with scrolling for excess
content. Settings has stronger tab indicators and a heading identifying the
selected tool. Its overwrite explanation starts "After the new file is validated"
and describes recycling and copy fallback without backup mechanics.

The updated test host builds and **69 hidden view contracts** pass, including expansion/collapse,
tab switching, compact content height and reachable Save at minimum size. The
new appearance and native monitor-bound resizing still need visible review.
The existing staged worker can be reused: Test-LicenseWorkflow rebuilds the
current test application before launching it. Production staging above predates
this latest presentation-only revision.

Third manual review (2026-09-09): the owner reported the revised UI otherwise
working well, with remaining feedback limited to results-column sizing and
license wording. This is positive visible feedback for the reviewed configuration;
it does not establish screen-reader, other-theme, DPI or monitor-edge acceptance.
Results now use equal-width columns and wrap long text, with full text on hover.
Successful validation explicitly says "License validated online" only after a
matching online receipt is saved; cached access during an outage does not claim
validation succeeded. Expiry explanations retain "Analyze remains available"
and omit the sentence about existing results. These final refinements await
visible confirmation. **486 foundation contracts** and **73 hidden view contracts**
passed, including distinct validation/outage messages and long text at two window
widths. No live provider, installed settings or registration was touched.

Fresh production staging (2026-09-09) includes all three manual-review follow-ups
at `artifacts/production-staging/fc166b574b8d481bb90f618b3ca358c5`. Release build,
curated payload identity/allowlist/notices and file inventory passed; the managed
build reported zero warnings/errors. This supersedes `89a35bc321684308a7ae5cfd3e0181c4`
as current development staging. The new worker passed 10 isolated license-harness
guards and 13 direct-command workflow checks. Evidence is retained at
`.codex-temp/license-workflow/direct-337dccdfb8664d4f9ca0e179d72c5c4e`.
No installer was produced or installed, and no Explorer registration changed.

Use the repository's foundation, hidden-view, shell and isolated real-worker/
workflow harnesses as primary regression checks. Update the old licensing harness
sequence to enter through direct actions instead of the removed manual hub.
Use Computer Use only for focused visible review when its connection is working;
do not make repeated helper failures a prerequisite for independent coding/tests.
Native replacement/recycling checks are limited to explicitly authorized
disposable fixtures and the documented Windows safety workflow.

Exit requires working direct commands under both output choices with no routine
planner, no general customer workspace, passing relevant automated checks, and
honestly recorded visible acceptance or remaining gaps. Hidden tests alone do
not close visual/accessibility acceptance.

Boundaries
----------

Preserve existing changes in both repositories. No commits, installation,
Explorer-registration changes, live Polar calls, signing purchase or publication
without separate authorization. Do not add codecs, audio, new presets, permanent
tray infrastructure, cloud processing or a replacement bypass. Keep originals
and recoverable files safe even when overwrite is selected. Do not mark the
commercial-release goal complete when this simplification is finished.
