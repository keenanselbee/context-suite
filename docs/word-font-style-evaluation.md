Word Font Style Export Verification
==================================

Status: corrected and verified for the bounded source profile. All 80 application
checks, fifteen independent PDF checks and before/after pixel comparisons pass.
Broader source coverage and release acceptance remain open.

This matrix checks the [Word body font reader](word-body-font-inspection.md)
against actual application exports. Fifteen independently authored, passive
DOCX files use the same text, 12-point size and Letter page. Four documents select
Arial, Courier New, Times New Roman or the synthetic absent family directly;
the other eleven select their expected family through these mechanisms:

| Scenario | Expected family/control |
| --- | --- |
| Stored document defaults | Arial |
| Default paragraph style with a base style | Courier New |
| Character style with a base style over paragraph formatting | Arial |
| Direct literal over an inherited theme | Arial |
| Direct theme over an inherited literal | Times New Roman |
| Literal and theme on the same run-font element | Courier New |
| Explicit paragraph style using the major theme | Courier New |
| Unused paragraph and character styles naming the absent font | Arial |
| Deleted text naming the absent font | Arial |
| Saved old formatting naming the absent font | Arial |
| Used paragraph inheritance naming the absent font | Absent-family control |

The settings request visible tracked markup in the source; the application's
final-text export policy must still exclude deleted text and old formatting.
No source document is rewritten. Fixtures contain no fields, external links,
embedded objects or executable content.

The current deleted-text and old-formatting fixtures also contain an explicitly
related font table with Arial metrics and the absent family name. The original
verification below used the earlier fixtures without this table; its retained
results are historical evidence and are not relabeled as table coverage.

Application and independent checks
----------------------------------

The public source checks require one resolved final-text run, the authored family,
no unsupported-coverage claim and unchanged bytes/write times for every fixture.
The isolated application batch uses a fresh trial and the fixed PDF target,
publishes independently validated copies into an owned output folder, and retains
native ownership records long enough to verify profile/context removal.

The review oracle expects exactly the two actively missing-font documents to
request per-file review. The test explicitly accepts those generated documents.
Unused or historical declarations must not prompt. This is a test callback,
not visible-window or keyboard acceptance.

The separate inspector verifies original package parts against freshly authored
fixtures, publication identity and bytes, qpdf structure, PDFium page geometry
and extracted text, and source/output preservation. It records PDF BaseFont names,
corroborates the three available direct-font controls, and compares each style
case with its direct-font control using both font names and exact 96-DPI pixels.
Negative comparisons require the other three font controls to differ from Arial,
so an implementation rendering every document identically cannot pass.

The inspector verifies the independently prepared qpdf/PDFium inventories and
probe sources before execution and rechecks their hashes afterward. Native and
inspection wrappers bind their evidence to unchanged source and binary inputs.

Commands
--------

Use `tools/office-engine/Test-OfficeExecution.py --word-font-styles` with the
existing explicit disposable-profile option and selected engine directories.
This mode creates its own authored documents; do not supply `--fixtures`.
The default foundation suite also runs the 45 read-only authored-source and
revision-only report checks.

Use `tools/office-engine/Inspect-WordFontStyles.py` with the execution UUID and
independently prepared PDF directories to inspect the resulting application report.
An export or callback failure is not an independent PDF comparison pass.

Initial failure and correction
------------------------------

Execution `adfef56282bf404faa005437f93cc280` published all fifteen copies and
removed all fifteen profiles and contexts, with unchanged original and runtime
hashes. It failed the review oracle: the deleted-text case prompted alongside
the two actually missing-font controls. The failure is retained; it is not
counted as a successful matrix.

Its independent inspection, `style-inspection-1fcf8bb0faf4403c9e9520554fcad990`,
passes all fifteen text/geometry checks, the three available-font control names,
all eleven exact style/control comparisons and three negative controls. The
overall result remains failed because of the extra review. This separates an
unnecessary prompt from a rendered fidelity failure.

The correction retains source evidence in application preparation and filters
only positively established revision-only names after independent validation.
The [closed coverage rules](word-body-font-inspection.md#inactive-revision-reports)
keep unknown, active or ambiguous reports visible. All **3,867 foundation
contracts** pass, including the authored source matrix and exclusion guards.
Source-bound receipts and log use `.codex-temp/word-font-style-foundation-*`.

Verified correction
-------------------

Fresh execution `450015a0e7d541ada467d7079245512d` passes **80 application checks**.
All fifteen copies publish; only the two actively missing-font documents request
review. Original bytes and write times remain unchanged, every native profile
and context is removed, and source/fixture/engine/binary hash checks pass.
The worker and application test-host Release builds have zero warnings/errors.

Independent inspection `style-inspection-ab9c413568ab4dff90994afaef8e4894` passes
all fifteen text/geometry checks, three available-font control names, eleven
exact style/control comparisons and three negative controls. Its pinned PDF
readers and inspection sources remain unchanged. The receipt ends in
`c8e102d905a84b12a991c6bf2261842e` under the execution directory.

The retained `review-filter-comparison.json` additionally compares all fifteen
new PDFs with the first run: exact pixels, extracted text and normalized font
names are unchanged. The review count falls from three to two. This validates
the review correction without introducing a spacing tolerance or changing the
rendered result. The raw native callback and host/worker protocol are unchanged.

Plain font-table verification
-----------------------------

Execution `04f2dcae50ac4f6a8b20059604ded307` adds related plain font tables to
the deleted-text and old-formatting fixtures. All **80 application checks** pass:
only the two actively missing-font cases request review, all fifteen validated
copies publish, originals retain their bytes and write times, and all fifteen
native profiles and contexts are removed. Source, fixture, runtime and binary
hashes remain unchanged throughout the run.

Independent inspection `style-inspection-c005d4ca958c47b7815dd19ef3bbe420` passes
all fifteen text/page-geometry checks, three available-font controls, eleven
exact style/control comparisons and three distinct-font controls. Receipt
`style-inspection-receipt-8532fd5250f847899f435caaa11bdc5e.json` binds the passed
inspection to unchanged inputs and pinned PDF readers.

The first inspection refused the added eighth package part because its fixture
check hard-coded seven parts. That failed receipt is retained. The inspector now
requires the authored seven/eight-part count and still compares every part's
identity, size and bytes. The successful rerun inspects the same exports; no
native export was repeated to fix this inspection-only assumption.

The additional `font-table-comparison.json` verifies that only those two source
fixtures gain font-table content, relationships and content types. All fifteen
PDFs retain the earlier run's exact pixels, text and normalized font names;
the review count remains two. The reader's separate foundation guards retain
reports for aliases, embedded programs, table relationships, malformed and
oversized evidence. No broader alias or embedded-font support is claimed.

All **3,913 foundation contracts** pass with unchanged source inputs, including
46 added table/dependency guards. The final receipts and log use
`.codex-temp/word-font-table-final-foundation-*`. Worker, contract-test and
inspection-probe Release builds have zero warnings/errors. Repository boundary,
system-theme policy and 161-document checks pass. No new visible-window or
screen-reader acceptance was performed.

Remaining scope
---------------

These controls cover one supported main-story text run per document. Tables,
numbering, auxiliary stories, broader scripts, aliases, glyph coverage and
embedded-font fidelity still require work, as do Excel/PowerPoint font resolution
and broader layout acceptance. No general spacing tolerance is introduced.
Formal packaging, installed lifecycle and visible/accessibility acceptance remain
separate; the reserved 1.1.0 payload is unchanged.
