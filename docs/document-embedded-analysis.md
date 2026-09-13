Office Embedded Reference Analysis
=================================

Status: read-only declaration counts implemented for recognized OOXML documents;
2,496 foundation contracts pass, 2026-09-13.

Analyze now reports three additional facts from the relationship files it already
reads for external links. No additional part reads, extraction, renderer or
native probe are introduced.

| Fact | Counted declarations |
| --- | --- |
| Image references (internal) | Exact Transitional/Strict Office `image` relationships whose TargetMode is omitted or Internal |
| Embedded-object references (internal) | Exact Transitional/Strict Office `oleObject` and `package` relationships with internal targets |
| VBA project references (internal) | Microsoft's exact `vbaProject` relationship type with an internal target |

Microsoft documents the [VBA project relationship](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-offmacro/183324c8-2ba3-4574-be14-3b5be3d0deea)
and the [embedded object reference model](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.oleobject?view=openxml-3.0.1).
The relationship inventory counts declarations, including potentially unused
parts and repeated references to the same target. It does not count verified
images, live objects, executable macros or unique embedded files. External
targets remain in the separate external-link count, even when they declare one
of these types. Target paths are not displayed, opened, fetched or validated.

A matching filename, type-name suffix or different case cannot impersonate an
exact relationship type. Missing targets still have declarations; malformed or
unsupported relationship files instead make all relationship counts unavailable.
No partial count is shown after a later read/parse/budget failure. Cancellation
continues to propagate. The existing package/XML/deadline budgets are unchanged.

The displayed scope explains that document fields and embedded contents were
not scanned and zero is not a safety verdict. An unscanned ODF/legacy document
does not receive a zero OOXML count. Macro-enabled document identity remains a
separate format declaration, not proof of a VBA project or conversion admission.

Verification
------------

The 32 added contracts cover Word/Excel/PowerPoint in both namespace families,
omitted/explicit internal and external modes, repeated/missing targets, exact
type matching, unused relationships, later malformed/DTD/over-budget failures,
an opaque 8 MiB member and ODF scope. Six actual application file-reader cases
retain source bytes and modification times. Equal-size known/unknown relationship
types produce identical read accounting, demonstrating no added reads for the
new classification. Existing cancellation and aggregate-budget contracts pass.

The complete foundation log is `.codex-temp/document-embedded-foundation.log`
with exit zero. Fresh combined image/audio/PDF/native-shell staging is
`artifacts/production-staging/394a9b5bb92b45a5ae3b6ec3b5c73f46`; it passes Release
builds and payload/notice/dependency inventories. Its log is
`.codex-temp/document-embedded-production.log`.

No Office engine was added to the product. Visible report, keyboard, themes/DPI,
screen-reader delivery, installed-shell and broader release acceptance were not
tested in this checkpoint. The required converter and its unresolved execution
boundary remain under the [document design](document-design.md) and
[broad-file goal](broad-file-support-goal.md).
