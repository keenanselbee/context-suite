Simple Context-Menu Workflows
=============================

Status: accepted; initial quiet Optimize slice implemented, broader acceptance pending
Date: 2026-09-08

[Decision 0018](0018-context-menu-utility-and-output-preference.md) further narrows
the accepted customer UI and replaces the always-copy quick-action rule with
copies by default and explicit persistent replacement consent in Settings.
Its implementation is pending; the lifecycle and safety principles below remain.

Decision
--------

Context Suite is a utility for a broad, nontechnical audience. The primary
workflow is select files, right-click, choose an understandable action, and
continue working. Successful routine operations should not require the user to
open, configure or dismiss an application window. Advanced controls are secondary.

This direction takes priority over adding codecs, presets or technical controls.
It changes the intended UX, not the existing safety and commercial-access gates.

Optimize Menu
-------------

The target menu, in authored order, is:

```text
Optimize > Auto
           Lossless
           Balanced
           Smallest
           ----------------
           Settings...
```

Remove Choose preset from the ordinary Explorer workflow. Each preset directly
submits the entire selection as one batch with immutable settings. Quick actions
remain copy-only and preserve Windows-style output names. Settings opens an
intentional settings surface without starting work. Advanced planning remains
available inside the application, not as a mandatory intermediate window.

Convert should follow the same principle with supported destination formats.
Necessary choices such as a transparent image's JPEG background remain explicit;
never choose destructive defaults just to avoid a window. Analyze intentionally
opens readable information because that information is the requested result.

Best-Effort Presets
------------------

The candidate-comparison strategy below records the initial implementation.
[Decision 0016](0016-fixed-optimization-recipes.md) supersedes it with implemented
fixed recipes and one safety fallback. The quiet UI and preservation principles
here remain in force; see [current evidence](../fixed-preset-integration.md).

Auto is the first and recommended choice: select a high-quality, worthwhile size
reduction for each image without asking the user to choose an encoder. It is not
permission for aggressive loss, metadata deletion, resizing or format conversion.

Treat each preset as a maximum loss budget, not a mandatory encoding method.
Compare validated candidates, including a lossless baseline, and retain the
smallest eligible result. An unsuitable lossy representation should fall back to
supported lossless processing, not block the whole file. Never use Smallest's
weaker limits to rescue a failed Balanced or Auto candidate.

If no eligible result improves the source, leave it alone. User wording should
say No smaller result, rather than claim the file is mathematically optimal.
This is a normal completion, not a failure or reason to open a window.

The initial Auto policy should conservatively reuse reviewed candidates; do not
introduce an unbounded search or new engine. Its exact quality/benefit thresholds
must be versioned and tested before enabling the action. Auto does not claim to
find a universal perceptual optimum. See the [implementation plan](../quiet-first-ux-goal.md).

Quiet Completion And Exceptions
-------------------------------

- A fast successful batch opens no window and plays one completion chime, not
  one sound per file. Successful includes a safe No smaller result outcome.
- Use the installed Windows `Media/chimes.wav` (normally
  `C:\Windows\Media\chimes.wav`). Do not copy/distribute that Windows asset.
  Sound failure or an absent asset must not turn successful media work into an
  error. Respect system audio settings and provide a simple sound toggle.
- Longer or larger batches may show one compact progress surface with overall
  progress, current activity and Cancel. Do not bring the old detailed grid into
  focus for every item. Avoid brief window flashes on fast work.
- Full success closes automatically any progress surface opened automatically;
  leave a user-opened window alone. Do not steal focus on completion.
- Partial failure, unsupported input with no safe fallback, retained-backup
  warnings, required consent, trial expiry and licensing issues receive one
  concise actionable UI. Never hide them behind a success sound.
- Cancellation is neither success nor failure: no success chime, no discarded
  completed output, and no error popup merely because the user cancelled.
- The app must exit when its quiet queue is complete and no user-owned surface
  needs it. Retain the single-session router and on-demand worker; do not add a
  resident service, permanent tray process or sound inside Explorer.

UI Direction
------------

Redesign the current engineering-oriented main window and planners. Show the
task, a plain-language outcome/progress summary, and only useful next actions.
Move paths, engine identifiers, policy versions, per-file technical fields and
recovery diagnostics into expandable Details. Preserve keyboard access,
accessible status announcements and automatic Windows light/dark/contrast themes.
Do not create a second dashboard or redundant navigation layer.

Safety And Compatibility
-------------------------

Best effort is not best effort at preserving user data: preservation and
validation remain mandatory. Unknown metadata is not silently disposable.
Investigate common chunks such as fdEC, preserve them only under verified rules,
or obtain explicit permission to remove them from new output copies. Signed
provenance, color, alpha and animation need their own correct handling.

No change to trial duration, paid access, replacement authority, media-upload
policy or release clearance is implied. Shell installation/registration is a
separate explicit validation step, not a side effect of documenting or building.

Supersedes the mandatory planner/default presentation aspects of decisions 0013
and 0014, and 0014's no-lossless-fallback rule. The initial replacement is recorded
in the [quiet-first UX evidence](../quiet-first-ux-goal.md).
