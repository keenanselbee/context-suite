Context-Menu Utility And Output Preference
=========================================

Status: accepted; implemented with automated evidence and owner manual acceptance
Date: 2026-09-09

Scope update: [decision 0019](0019-broad-file-analysis-and-media-expansion.md)
broadens the image utility to file analysis, audio and document support while
retaining all customer-surface and safe-output decisions below.

Decision
--------

Context Suite is a context-menu image utility. The owner approved removing the
general-purpose customer workspace and routine planning windows. Select files,
right-click, choose Convert and a format or Optimize and a preset, then continue
working. Keep existing fixed recipes, media capabilities and validation.

Customer surfaces are limited to:

- Direct Convert and Optimize commands, with no ordinary success window.
- A read-only Analyze report for the selected files.
- Compact cancellable progress and actionable problems/recovery details.
- A focused prompt only for a necessary decision, such as a transparency
  background or explicit DDS interpretation. Retain required choices without
  presenting an unrelated general planner.
- Settings and License. Direct Start-menu launch opens a small settings/help
  surface, not a file picker, drag-and-drop workspace or batch dashboard.

Remove the manual processing hub and routine file/format/preset planners from
the customer workflow. Reuse internal planning, execution and result models;
this decision does not require deleting tested infrastructure or expanding DDS.

Output preference
-----------------

Convert and Optimize each retain a final Settings command. Each tool defaults
to **Create copies** and offers **Overwrite originals** as an explicit persistent
choice for future commands. Saving that choice is standing replacement consent;
ordinary actions must not reopen a planner for the same consent every batch.
Explain the consequence beside the setting before it is saved. Switching back
to copies affects future batches, not queued or admitted work.

Version the stored choice separately from the existing permission-only
`AllowReplacingOriginals` flag. That old flag authorized an additional per-batch
choice, not automatic replacement. Legacy, invalid and missing settings must
therefore default to copies until the new choice is explicitly saved.

Replacement retains the existing safety contract:

- Capture immutable settings for each batch and honor final access admission.
- Encode to a temporary file, validate, and publish transactionally before
  recycling any source/backup. Never overwrite an unrelated destination file.
- Same-format optimization uses recoverable same-path replacement. Conversion
  to another extension publishes the new-format file before recycling its
  unchanged original; it does not write a new format under a misleading extension.
- No permanent-delete fallback. Preserve and report recoverable files when
  recycling/publication fails. No smaller result keeps the source unchanged.
- Retain platform/path restrictions and source-change checks. Alternate output
  folders and automatic metadata omissions that require preservation of the
  original remain copy-only. Explain an applicable exception without offering
  an unsafe override. The approved PNG fdEC exception remains unchanged.

The choice authorizes application behavior for future customers. It does not
authorize changing this machine's installed settings, processing personal files,
or recycling non-disposable files during development.

Consequences and supersession
----------------------------

This supersedes decision 0008's per-batch replacement-consent requirement and
decision 0015's always-copy quick actions and general advanced in-app planner.
Their naming, publication, recovery, quiet lifecycle and media guarantees remain
in force. No licensing, signing, installation or release gate is waived.

Prefer fast automated model, native-menu and isolated workflow tests. Reserve
visible review for layout, focus, accessibility and real Explorer acceptance;
record these separately instead of treating hidden contracts as visual proof.

Track implementation and exit criteria in the
[simplification goal](../context-menu-simplification-goal.md).
