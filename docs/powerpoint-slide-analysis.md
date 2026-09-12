PowerPoint Slide Visibility Analysis
===================================

Analyzer `slide-visibility-1` adds visible and hidden slide counts to the existing
read-only PowerPoint package report. It also reports how many referenced slides
omit the visibility setting. The original declared slide count remains separate;
none of these facts promises a PDF page count or approves a conversion.

Microsoft's [slide description](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.presentation.slide?view=openxml-3.0.1)
specifies that an omitted `show` attribute defaults to true. The visible count is
marked derived when it uses that default; explicit true/false and 1/0 declarations
are distinguished from omitted settings. The
[slide relationship documentation](https://learn.microsoft.com/en-us/office/open-xml/presentation/how-to-get-all-the-text-in-all-slides-in-a-presentation)
describes resolving the presentation's slide-list references to slide parts.
These primary sources were consulted on 2026-09-11. No SDK, renderer or reference
implementation was added to the parser.


Selection, bounds and failure behavior
-------------------------------------

After identifying a supported Transitional or Strict presentation package, the
reader follows its slide list through the presentation's internal relationship
part. It requires distinct slide IDs and targets, the matching slide relationship
type, one agreeing slide content-type override and the corresponding XML root.
Absolute package paths and relative paths are resolved within ZIP names only;
no filesystem path or external target is opened. Escaped, protocol, query,
fragment, backslash and package-escaping references are unsupported.

The optional scan selects at most 128 slides. Existing limits remain shared with
other document inspections: 256 KiB per part, 1 MiB total expanded part bytes,
4 MiB additional reads, 4,096 directory entries and XML depth 32. DTDs and external
XML resolution remain disabled. The file reader's existing cancellation/deadline
token is checked during part reads and XML/list traversal. Repeated reads count
against the shared budget; an expensive slide scan can leave later optional font
details unavailable rather than extending the budget.

Visibility is reported only after the complete selected slide list succeeds.
Malformed or missing parts, ambiguous IDs/types, invalid Boolean values,
unsupported compatibility processing and exhausted limits make all three new
counts unavailable, with a scoped warning. Already established identity and
declared slide count remain available. Cancellation propagates; partial counts
are never presented as totals. Empty lists produce zero counts. All-hidden decks
produce zero visible slides without guessing what a renderer would export.

The inspection concerns referenced slide XML declarations. It excludes unused
slide parts, custom shows, note content, master/layout interpretation, animations,
rendered appearance and export settings. It does not validate the complete slide
schema or establish absence of active content. Compatibility elements/directives
other than `Ignorable` leave visibility unavailable; no alternate branch is chosen.
The [export experiment](powerpoint-slide-evaluation.md) remains separate evidence
about the candidate renderer's behavior on four generated inputs.


Verification
------------

All **2,320 foundation contracts** pass, including 54 new slide checks. They cover
both OOXML namespaces, Boolean spellings/defaults, empty/all-hidden lists,
internal target normalization, ambiguous or external references, content types,
malformed/DTD/oversized XML, unsupported compatibility, the 128/129 boundary,
shared expanded-byte limits, cancellation during a slide read and real-reader
source preservation under a misleading filename. The final log is
`.codex-temp/slide-visibility-foundation-final.log`.

The application file reader also inspected the four retained PPTX fixtures from
`evaluation-6250d8cefed041728dfece8ba0e5a290` under the prepared Office scratch.
The reordered and two notes cases report three visible, zero hidden slides; the
hidden-ends case reports one visible and two hidden. All four explicitly encode
visibility, so their default-setting count is zero. Source bytes and write times
remain unchanged. Separate Python XML inspection agrees with every count and
source hash. The probe, results and independent comparison are retained under
`.codex-temp/slide-visibility-probe/`.

Fresh Release staging at
`artifacts/production-staging/dc5a863d901b4eb89f94b943682511aa` builds with zero
warnings/errors and passes the curated image, optional audio/PDF payload,
dependency, notice and file-inventory checks. The log is
`.codex-temp/slide-visibility-production-dc5a863d901b4eb89f94b943682511aa.log`.
The build used `-SkipShell` and did not install or register anything.

No new Office engine execution, worker/native integration, visible UI, keyboard, screen-reader,
theme/DPI, installed-shell or installer-lifecycle acceptance is claimed by these
analysis tests. Office-to-PDF implementation remains required and unfinished.
