Word Body Font Inspection
=========================

Office preflight now carries bounded source font-family evidence for supported
final-text runs in an ordinary Transitional DOCX main story. This begins the
document-side counterpart to the [renderer font list](office-font-environment.md).
It does not add a customer prompt or suppress the existing
[renderer-reported substitution review](office-font-review.md).

The result separates available package inspection, counted text runs, resolved
runs, selected family names and explicit coverage issues. An available result or
an empty family list does not establish complete font fidelity. Ordinary Analyze
continues to expose the separately scoped [declarations](document-font-references.md).

Selection behavior
------------------

The reader follows the root office-document relationship and that main part's
styles/theme relationships, with agreeing content types. It resolves internal
relative package targets without filesystem extraction; external selected targets
are refused. Orphan styles/themes and font-table declarations cannot supply fonts.

For supported runs, it applies stored document defaults, the selected/default
paragraph style and its base chain, the selected character style and its base
chain, then direct run properties. A newer literal or theme selection replaces
the earlier pair for that font slot. A theme selection wins when both occur on
the same element. These rules follow Microsoft's
[style hierarchy](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.runstyle?view=openxml-3.0.1)
and [Word font-selection notes](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/aef3c9a6-5d6c-434b-90b7-85e761fd8e62),
independently implemented without an Open XML SDK dependency.

The current selection covers ASCII and supported Latin/punctuation ranges without
East Asian hints, plus an explicit complex-script override with a literal font.
Latin major/minor theme selections resolve through the active theme's Latin family.
Only families used by a fully resolved text run are returned. Unused styles and
unused font slots are not reported as used families. Missing application defaults
remain unknown; the reader never invents Calibri or Times New Roman.

Deleted and moved-from content and saved old formatting are excluded. Inserted,
moved-to and current formatting remain eligible. Paragraph-mark revisions leave
body text unresolved because accepting a deleted break can change the following
paragraph's inherited formatting. Paragraph-mark font properties alone do not
become text-run properties.

Coverage still required
-----------------------

Tables and numbering, fields, hidden text, compatibility/foreign wrappers,
headers/footers/notes and drawing text, broader script/locale/charset selection,
East Asian and complex-script themes, and more involved revisions remain
unresolved. Their presence is not mistaken for a complete successful scan.
Strict/legacy Word, Excel and PowerPoint used-font resolution remain separate work.

This is requested family evidence. Installed aliases, selected font faces,
metric substitutions, embedded programs/rights and glyph fallback still require
renderer and PDF evidence. Do not turn a partial result into an automatic warning,
approval, refusal or a promise that a document will retain its layout.

Limits and failure behavior
---------------------------

The reader shares the bounded document ZIP/XML reader: 4,096 ZIP entries,
256 KiB per compressed/expanded part, 1 MiB total expansion, 4 MiB reads and
XML depth 32. It adds at most 4,096 styles, 64 styles per active inheritance chain,
8,192 text runs and 64 distinct used families of at most 128 characters.
Duplicate/ambiguous identities, malformed text, unsupported dependencies, DTDs
and budget failures return unavailable evidence without leaking partial families.
Unsupported text contexts preserve other resolved-run evidence with coverage issues.

Cancellation propagates, including during dependency reads. The input remains open
and its original position is restored on success, unavailable results and
cancellation. The caller retains its read lease and deadline. No native engine,
network access, source rewrite, publication or admission decision occurs here.

Verification
------------

All **3,792 Release foundation contracts** pass, including 91 additional checks
for style/theme precedence, unused/cyclic styles, current/revised text, explicit
coverage gaps, package target safety, duplicate identities/properties, XML/run/
family limits, mid-read cancellation and source preservation. The application
preflight exposes the evidence without changing the existing conversion admission.
The evaluation probe, application test host and private worker Release builds
complete with zero warnings/errors; repository boundary, theme and 160-document
checks pass. No new visible or hidden-window acceptance run is claimed.

Log: `.codex-temp/word-body-font-foundation.log`. Its input/exit receipts bind
the run to unchanged core, application, shared and contract-test sources.

The read-only probe additionally checks two retained authored Word documents.
Each resolves its one explicitly styled Latin run to Arial or the authored absent
family while leaving its other four runs and ancillary stories unresolved. Source
hashes match the earlier native preflight; bytes and write times remain unchanged.
The source-family/list comparison agrees with all four retained normal/isolated
renderer reports, and all four retained PDF hashes still match their independent
inspection. This reuses earlier rendering evidence; no native export was rerun.

Receipt: `.codex-temp/word-body-font-retained.json`, based on isolation run
`b81a09fa93b943bb9b2d617d0464254d`. The reusable read-only probe mode is
`--inspect-word-body-fonts <owned-office-fixtures-directory>`.

The reserved 1.1.0 package and private native host remain unchanged. Fresh packaging,
broader Office fidelity and actual visible/accessibility acceptance remain open.
