Output Naming, Settings, And Replacement
=======================================

Status: accepted and implemented; native replacement verified on Windows build 26200 x64, local NTFS
Date: 2026-09-06


Context
-------

Convert and Optimize need predictable copies and an explicit replacement option.
The product should remain simple: use familiar Windows-style output names and
the Recycle Bin rather than building a backup manager or application Undo system.
Recovery must not depend on a successful conversion alone.


Decision
--------

Create new files and preserve sources by default. Use the following names in the
source folder, or in an explicitly selected output folder:

| Operation | First output | Collision |
| --- | --- | --- |
| Ordinary conversion | `gamma - Converted.png` | `gamma - Converted (2).png` |
| Optimization | `gamma - Optimized.webp` | `gamma - Optimized (2).webp` |
| DDS conversion to an explicit representation | `gamma - BC7-sRGB.dds` | `gamma - BC7-sRGB (2).dds` |

Use space-hyphen-space, capitalized operation labels, and numbers immediately
before the extension. The first output is unnumbered; collisions start at 2.
Preserve the complete source basename, including intentional ` - Copy` or other
suffixes. Do not strip or repeatedly interpret existing names. Ordinary outputs
do not contain timestamps, GUIDs, or redundant format labels. DDS suffixes reflect
the actual requested compression and applicable color interpretation; unsupported
combinations must not acquire a plausible-looking label.

Allocate names against both existing files and destinations reserved by the
batch. Recheck at publication and never overwrite a competing file. Replacement
permission covers only the selected source, never another destination. Do not
add customizable filename templates or punctuation preferences initially.


Settings And Consent
--------------------

- Add a final **Settings...** item after a separator in both Convert and Optimize
  submenus. It opens the corresponding section of one shared settings window;
  Analyze remains a direct action with no submenu.
- Store typed, versioned JSON in local application data. Validate fields, use
  safe defaults for invalid values, and save without truncating the previous
  valid settings on failure. Keep credentials and trial state separate.
- Keep **Allow replacing originals** off by default, separately for each tool.
  Enabling it exposes an explicit per-batch replacement choice and confirmation;
  it does not turn quick actions into silent replacement actions.
- Quick actions create copies. A chosen alternate output folder also keeps
  sources. Show actual output paths and consequences before confirmed replacement.
- Capture an immutable settings snapshot for each admitted batch. Later settings
  edits affect future batches, not queued or active files.


Same-Path Replacement
---------------------

1. Produce, close, and validate a unique temporary output on the source volume.
2. Confirm that the selected source is still the file admitted by the plan;
   serialize conflicting jobs and reject a changed source or unsafe path.
3. Use Windows replacement with an explicit unique sibling backup path, such as
   `gamma - Original <guid>.webp`. Do not delete or recycle the source first.
4. Confirm publication before attempting to recycle the backup.
5. If recycling fails or is unavailable, retain the backup and report its exact
   location. This is a completed replacement with a cleanup warning, not a reason
   to re-encode or discard the backup.

`ReplaceFileW` provides named backups and requires the source, temporary output,
and backup on the same volume. Its documented partial failures require inspecting
the surviving files; an error is not proof that no rename happened. Do not ignore
metadata/ACL failures to force a replacement through.


Different-Extension Conversion
------------------------------

Publish the validated output under an unused destination name first. In explicit
replace-source mode, prefer the original basename with the new extension, using
collision numbering if necessary. Then recycle the unchanged original under its
original name; no GUID rename is needed. If recycling fails, retain both and
report **Converted; original retained**. Never remove a valid published output
merely because source cleanup failed.


Failure And Recovery Contract
-----------------------------

- Request recycling through Windows Shell operations, never ordinary permanent
  deletion. Abort any proposed permanent-delete fallback. Verify per-item results
  and cancellation, not just an overall success code.
- Before enabling replacement, prove the no-permanent-fallback behavior and
  interrupted-publication handling on supported Windows/filesystem combinations.
  Unsupported locations remain copy-only; do not silently weaken the policy.
- Preserve recoverable originals on interruption. Startup/temp cleanup must not
  delete original backups or unclassified replacement artifacts. Keep only the
  minimal operation evidence needed to identify interrupted publication; do not
  introduce persistent job resumption or a backup database.
- Cancellation stops uncommitted work. Once a file is committed, report it as
  completed, retain any pending backup, and do not label it cancelled or undo it
  as part of batch cancellation.
- Report whether the original was recycled, retained as a backup, or retained at
  its original path. Never promise permanent recovery, Explorer Ctrl+Z, or
  one-click app Undo. Restoring a GUID-named backup may require manual renaming.
- No recovery browser, automatic backup retention/purging, or custom recycle
  storage in the initial implementation. Retained backups are ordinary files
  whose location is available to the user. Recycled originals still consume disk
  space until Windows or the user removes them.


Consequences And Alternatives
----------------------------

Default copies need no recovery mechanism. Optional replacement uses a temporary
backup only while publishing and recycling, leaving it behind when safety needs
it. This reduces maintenance compared with an app-managed recovery store, at the
cost of manual recovery and no guaranteed retention period.

Rejected alternatives: overwriting directly, recycling before publication,
automatic permanent deletion when recycling fails, and a custom backup manager.
The earlier dot/hyphen-only and plain-space naming proposals are superseded by
the Windows-style convention above.

See the [implementation evidence](../image-output-safety-goal.md) for the test
gate and coverage limits. The current implementation rejects linked paths and
multi-link files and keeps replacement unavailable on unverified Windows builds
or storage types. It does not certify removable, network, or cloud-backed paths.
Do not expand the platform allowlist without equivalent native evidence.

Minimal per-operation JSON records live at
`%LOCALAPPDATA%/ContextSuite/Publications`, recording paths, source/output
fingerprints, and the last publication stage. Startup reports retained records
without purging artifacts or reconstructing a job queue. Recovery is manual.
Production recycling checks the native pre-delete recycle flag and exact original
identity and requires per-item recycle confirmation. It never requests a permanent
fallback. Tests separately exercise an actual permanent-delete proposal and prove
that the production callback vetoes it with the test original intact.


Sources
-------

- [Windows replacement and partial failures](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew)
- [Windows Shell operation flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)
- [Shell execution and cancellation results](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-performoperations)
