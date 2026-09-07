Image Output Safety And Settings Goal
====================================

Status: implemented and locally verified; bounded goal complete, media codecs remain next
Date: 2026-09-06


Objective
---------

Implement the shared settings, deterministic output naming, copy publication,
and verified optional source-replacement foundation for image conversion and
optimization, with automated filesystem/failure tests and a minimal WPF settings
surface. Follow [decision 0008](decisions/0008-output-naming-settings-and-replacement.md).

This is a focused Milestone 2 goal, not a claim to complete all execution
infrastructure or deliver codecs. It establishes the safety boundary before the
first real PNG/JPEG/WebP conversion slice; DDS conversion follows on that same
boundary. This document records the completed bounded implementation goal.


Implementation Evidence
-----------------------

- Typed settings, immutable batch snapshots, output-consent checks, and naming
  contracts are implemented. Local JSON saves preserve unsupported schemas and
  reject stale saves; failure tests cover locked destinations and cancellation.
- `Test-Foundation.ps1 -Configuration Release -Integration` passed 170 contracts;
  the production build completed with zero warnings/errors. These counts include
  existing foundation checks, settings, publication failures, forced termination,
  and typed per-file/aggregate results; native recycling is separately opt-in.
- WPF settings and pathless Convert/Optimize Settings activation are implemented.
  Native COM tests cover action/separator/Settings order, identities, enumeration,
  and settings activation without media paths. No packages were installed.
- The updated WPF smoke passed at
  `.codex-temp/desktop-smoke/d6e21ab9a3a341e886577f81b3ca3ce1`: both settings sections,
  unchanged queue, keyboard cancellation, and unchanged stored preferences.
- `Test-PublicationWindows.ps1 -Recycle` passed native same-path and
  different-extension publication, exact original readback from the Recycle Bin,
  permanent-delete proposal veto, cancellation, and changed-source rejection.
  Final native evidence:
  `.codex-temp/publication-windows/recycle-ba27883952114bcbb8f23a1e95812e46`.
  Only disposable test originals were recycled; no Recycle Bin contents were purged.
- `Test-PrivateBoundary.ps1` and
  `Test-ShellPrototype.ps1 -Configuration Release -SkipBuild` passed. Private
  composition remains required. No private files or installed packages changed.

| Acceptance surface | Verification |
| --- | --- |
| Settings defaults, safe saves, unknown schemas, snapshots and consent | `SettingsContracts.cs`; real locked settings plus cancellation and stale revision checks |
| Exact copy/DDS names and collisions | `SettingsContracts.cs`, `PublicationContracts.cs`; repeated inputs, case-insensitive names, shared destinations, actual long Unicode paths |
| Validated copies and source integrity | `PublicationContracts.cs`; wrong validation identity/digest, late source/output changes, racing destination, failed worker and cancellation |
| Replacement and cleanup failures | `PublicationContracts.cs`; actual `File.Replace`/locks, injected disk-full/permission/partial-replacement/cleanup failures, honest recovery-required outcomes |
| Native recycle-only behavior | Opt-in `WindowsRecycleContracts.cs`; actual Shell recycling/readback and native permanent-delete veto |
| Interrupted publication | `PublicationCrashContracts.cs`; six killed-child checkpoints from preparation through commit, exact original survival and non-destructive restart discovery |
| Settings activation and UI | Native COM enumeration/activation contracts, managed pathless activation, opt-in WPF smoke; queue and saved preferences remain unchanged |
| Result accuracy | Typed publication summary and WPF view-model contracts; warnings do not double-count completions and cancellation does not undo committed results |

Coverage And Remaining Boundaries
--------------------------------

Replacement availability is restricted to Windows base build **26200 x64** and
ordinary single-link files on fixed local NTFS volumes. The settings permission
defaults off separately for Convert/Optimize, and publication additionally
requires explicit per-batch consent. Other builds/locations stay copy-only.
Linked paths, cloud placeholders, and hard-linked inputs are rejected rather than
treated as verified ordinary files. Network/removable/cloud replacement and
other Windows builds are not certified. Disk-full and permission-denial coverage
is deterministic fault injection, not a physically filled disk or changed ACL.

The application owns publication and exposes actual output, retained-original,
and recovery-record paths in its results. Minimal JSON evidence under
`%LOCALAPPDATA%/ContextSuite/Publications` survives uncertain publication;
startup reports its presence without resuming work or purging artifacts. There
is no custom recovery browser, app Undo, or permanent-delete fallback. This does
not promise recovery after every power-loss/hardware failure or defend against
malicious same-user filesystem manipulation.

This foundation was verified using controlled test candidates, before real media
adapters were available, without a shipping access bypass. The subsequent
[conversion goal](image-conversion-goal.md) supplies real PNG/JPEG/WebP processing,
semantic validation, previews of output consequences and per-batch replacement
consent. The preference alone is not authorization to replace originals.

Installed Explorer Settings-menu layout, modern-menu visual verification,
high-DPI/accessibility review, and hosted CI remain separate checks. Native COM
tests do not certify installed menus. Scratch logs, crash artifacts, and UI
screenshots remain in `.codex-temp` for diagnosis; screenshots can include
overlapping desktop windows and should be reviewed before sharing.


Scope And Order
---------------

1. Extend existing public policies with immutable output intent and per-batch
   settings snapshots. Implement versioned local JSON storage and safe saves.
2. Implement Windows-style names, batch reservation, path validation, and
   publication that cannot overwrite an unrelated destination after a race.
3. Add application-owned temporary-output publication with explicit validation
   evidence. Test adapters can supply controlled outputs only in test assemblies;
   do not add fake production capabilities or a shipping access bypass.
4. Prototype same-path replacement with a unique backup and different-extension
   publication followed by recycling. Verify actual Windows behavior, partial
   failures, and cancellation before exposing replacement as available.
5. Add the minimal shared Convert/Optimize settings surface and final Explorer
   **Settings...** entries. Preserve Analyze direct activation, icons, selection
   batching, and bounded menu enumeration. Settings activation must not enqueue a
   media operation or require trial/purchase access.
6. Integrate explicit outcomes into the existing result model: completed copy,
   completed replacement, original retained, backup retained, publication failure,
   and pre-publication cancellation. Keep aggregate totals truthful.

Use current projects and feature folders; do not add an assembly per service.
Public code owns policies, settings, naming, publication, Windows recycling, UI,
and tests. No private media implementation changes are required for this goal.


Acceptance Tests
----------------

- Exact names for conversion, optimization, and typed DDS representations;
  numbering from `(2)`; preserved ` - Copy` and existing operation suffixes;
  Unicode, long names, case-insensitive collisions, repeated inputs, and files
  with identical basenames in different source directories.
- Default copies leave source bytes unchanged. A file appearing after planning
  is not overwritten; existing files and batch reservations are both respected.
- Invalid output, failed validation, worker failure, cancellation before commit,
  locked files, denied permissions, and disk-full conditions cannot discard the
  original or publish an invalid result.
- Successful same-path replacement preserves a byte-identical original in the
  backup before recycling. Different-extension publication succeeds before any
  attempt to recycle the original. Changed sources and unsafe linked paths are
  rejected rather than retired.
- Recycle failure, cancellation, unavailability, and a proposed permanent delete
  retain the original/backup and produce a visible warning. Verify actual Shell
  integration separately from injected failure contracts.
- Forced termination between publication steps leaves identifiable originals;
  restarting and normal temporary cleanup do not delete them. Partial Windows
  replacement failures are accounted for without assuming all-or-nothing API
  behavior. No guarantee against hardware failure or all power-loss cases.
- Missing, malformed, unsupported-version, and partially written settings fail
  safely; failed saves retain previous valid settings. Never rewrite an unknown
  newer schema silently. Edits do not change already captured batch settings.
- Replacement is off by default, tool-specific, and requires a per-batch choice;
  quick actions and alternate output folders remain copy-only. Incomplete or
  unverified replacement support is unavailable, not silently enabled.
- Settings are keyboard-accessible, open the correct section, and never create
  media jobs. Existing activation, single-window, private-boundary, and shell
  contracts remain valid.

Keep real filesystem/Shell test inputs disposable and under `.codex-temp`.
Native recycling tests must be explicit opt-in, record their own test artifacts,
and never empty the user's Recycle Bin or touch unrelated files. Use deterministic
fault injection for cases not reproducible on the available local filesystem;
report untested removable/network/cloud behavior rather than treating mocks as
platform evidence.


Verification And Handoff
------------------------

Extend the existing foundation contract harness and documented scripts instead
of adding a parallel test framework. Run public contracts, a full production
build, affected shell contracts, and the opt-in WPF smoke checks as applicable.
Document any new narrowly scoped Windows filesystem/recycle test command in
[development](development.md). Builds must not install packages or restart
Explorer. Installed-menu verification remains separately authorized.

Record commands, pass/fail results, platform coverage, and remaining manual
checks. Do not mark replacement ready on platforms without evidence. If the
Windows spike exposes an unsafe assumption, retain copy-only operation and bring
the specific decision back; do not substitute permanent deletion or build a
backup manager without a new decision.


Next Media Goal
---------------

The next [conversion goal](image-conversion-goal.md) implements real PNG/JPEG/WebP
batch conversion using Magick.NET-Q16-x64 14.17.1, with transparency/matte,
orientation, metadata/color, validation, and mixed-batch tests. Record the actual
resolved dependencies and notices before shipping an adapter. DDS BC conversions and linear/sRGB behavior are a
first-class follow-up, not an implication that Magick.NET supports every DDS
combination. See [Converter design](converter-design.md).

The current application still has no production media-admission provider.
[Decision 0009](decisions/0009-first-image-engine-and-trial.md) now settles the
minimal local trial to implement alongside conversion. Tests remain isolated
without a website account system or shipping bypass; paid Polar access is deferred.
