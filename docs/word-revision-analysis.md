Word Revision Declaration Analysis
=================================

Analyzer `word-revisions-1` reports four bounded counts from recognized Word
packages: insertion, deletion, move-source and move-destination markers in the
main document XML. This supplies read-only evidence after the
[tracked-revision export experiment](word-revision-evaluation.md). It does not
enable Word conversion or select which revisions an exported PDF should show.

The reader traverses the main XML already parsed for package identification.
It adds no package reads or engine startup, and retains existing ZIP/XML byte,
depth and time limits. Both Transitional and Strict Word namespaces use the same
literal inspection. Namespace prefixes do not matter; foreign lookalikes and
case variants do not count. Cancellation is checked during traversal, and facts
are added only after the traversal completes.

| Fact suffix under document.word-revisions- | XML element counted |
| --- | --- |
| insertions | ins |
| deletions | del |
| move-sources | moveFrom |
| move-destinations | moveTo |

Microsoft documents the revision roles of
[inserted content](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.insertedrun?view=openxml-3.0.1),
[deleted content](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.deletedrun?view=openxml-3.0.1),
[move sources](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.movefromrun?view=openxml-3.0.1)
and [move destinations](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.movetorun?view=openxml-3.0.1).
References were consulted on 2026-09-11. The implementation and fixtures are
independently authored; no reference code or document content was imported.

The counts describe XML markers, not edits, changed characters, complete tracked
changes or schema-valid revision objects. Nested markers each count separately.
Revision IDs, authors, dates and document text are not returned by this reader.
Headers, footers, notes, comments, formatting changes, move-range endpoints and
display settings are outside its revision scope. Other analysis groups can read
selected parts for their own purposes without expanding this revision scan.
The accompanying scope fact explicitly states that zero does not mean the whole
document is revision-free. These counts are not conversion admission or a safety
decision, and say nothing about whether a renderer will display deleted text.

Compatibility elements or directives other than `mc:Ignorable` leave all four
counts unavailable with a scoped warning, retaining the recognized Word family
and other facts. The reader does not choose an AlternateContent branch or process
extension wrappers. Ignorable alone permits literal standard-namespace counts;
this is not complete markup-compatibility processing. Malformed or over-budget
main XML retains the existing generic package fallback with no revision counts.
Non-Word documents receive no invented zero values.


Verification
------------

All **2,266 foundation contracts pass**, including 23 new revision contracts.
They cover both namespaces, typed/nested counts, omitted markers, namespace/case
lookalikes, move-range and formatting exclusions, unsupported compatibility,
unselected-part scope, non-Word families, malformed/oversized XML, 10,000 markers,
cancellation and real-reader source preservation under a misleading image name.
The log is `.codex-temp/word-revision-analysis-foundation.log`.

The actual application file reader also inspected all four retained DOCX export
fixtures. Shown, hidden and unspecified revision settings each retain one
insertion and one deletion marker; the clean control has neither. All source
hashes and write times remain unchanged. A separate XML inspection agrees with
all four counts and the recorded hashes. This directly demonstrates why hidden
display must not be treated as removal of stored revisions. Evidence is under
`.codex-temp/word-revision-analysis-probe`, including `results.json` and
`independent-verification.json`.

Fresh isolated Release stage
`artifacts/production-staging/885c764010054136b49d8718a1b2fa09` builds with zero
warnings/errors and passes curated image/audio/PDF identities, notices,
dependencies, file allowlist and inventory checks. The build log is
`.codex-temp/word-revision-analysis-production-885c764010054136b49d8718a1b2fa09.log`.
It uses `-SkipShell`; no installation or Explorer registration occurred.
Public-source boundary, system-theme policy, 97 documentation files and both
repositories' whitespace checks pass.

No new Office export, audio/image/PDF worker matrix, benchmark, native shell,
visible layout/keyboard, screen-reader or theme/DPI acceptance is claimed.
Required Office conversion, isolation and broader fidelity remain incomplete.
