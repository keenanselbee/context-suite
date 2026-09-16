Document Font Reference Analysis
================================

Analyze now reports bounded font declarations from recognized OOXML packages.
The report separates literal names from unresolved theme references and labels
both as declarations. This is groundwork for required Office conversion and
useful read-only detail; it is not the missing-font detector or an enabled
Office-to-PDF action.

The later [Word body inspection](word-body-font-inspection.md) adds separate
source-use evidence to Office preflight for supported final-text runs, resolving
active style chains and Latin theme selections. It retains explicit gaps and
does not change this Analyze declaration scan or establish complete font fidelity.


Meaning and scope
-----------------

The optional scan selects XML parts through explicit content-type overrides for
Word, Excel, PowerPoint and shared themes. It recognizes the supported namespace
forms in both Transitional and Strict OOXML. It does not follow relationships,
resolve external targets or open embedded font files.

Observed fields are independently implemented from Microsoft's Open XML
documentation: Word [run font names and theme attributes](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.runfonts?view=openxml-3.0.1),
Word font-table names, spreadsheet font/rich-text names and scheme references,
and DrawingML typeface declarations. The corresponding
[spreadsheet font name](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.fontname?view=openxml-3.0.1)
and [DrawingML Latin font](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.latinfont?view=openxml-3.0.1)
references describe these as font selections, not proof of installed availability.
No SDK dependency, external schema or reference implementation is imported.

Names are deduplicated, sorted and quoted. Theme tokens stay separate rather
than being mistaken for literal font families. Empty observations say "None
observed in selected parts". The scope explicitly includes potentially unused
parts/styles; it does not resolve style precedence, theme inheritance, active
runs or compatibility branches. Content types supplied only through Default
declarations, extension-specific markup, legacy Office and OpenDocument font
references are outside this scan. Their existing analysis remains available.

Installed availability, glyph coverage, actual substituted fonts, embedded-font
rights and complete font validity are not checked. In the six retained
[substitution fixtures](office-font-substitution.md), the new reader correctly
reports the requested Arial or synthetic family, while the rendered PDFs use
the previously observed alternatives. That distinction is intentional.


Limits and failure behavior
---------------------------

The scan admits at most 32 selected parts, 64 distinct names/theme references
combined, and 128 characters per value. Control characters and whitespace-only
values are unsupported; empty optional values are ignored. Duplicate or unsafe
selected part names fail the optional scan. It shares the existing package limits:
256 KiB per compressed/expanded part, 1 MiB total expansion, 4 MiB additional
reads and XML depth 32. DTDs and external resolution remain disabled.

Facts are published only after the complete selected scan succeeds. Unsupported,
malformed or over-budget optional content produces unavailable font declarations
with a warning, retaining established document identity and basic facts.
Cancellation propagates rather than becoming an incomplete successful report.
No trial, engine, rendering, installation or transformation capability is added.


Verification
------------

The Release foundation suite passes **2,132 contracts**, including 30 new checks
for all three families in both namespace forms, separate theme references,
deduplication, source-preserving application reads, DTD/size/value/count limits,
duplicate/traversal declarations, namespace isolation and cancellation.
Log: `.codex-temp/document-font-foundation-unicode.log`.

A separate public-core inspection of the six retained Office substitution
fixtures verifies each requested family and unchanged source hash, with no
warnings. Evidence:
`.codex-temp/verify-document-fonts-8180234267b549ee93ecf005c3ce6b06/result.log`.
No Office engine was rerun for those read-only checks.

Fresh combined stage
`artifacts/production-staging/8a91b541545f435c87456f69d99aeea6` builds with zero
warnings/errors and passes image/audio/PDF candidate, dependency, notice and
file allowlist checks. Log:
`.codex-temp/document-font-production-8a91b541545f435c87456f69d99aeea6.log`.
The `-SkipShell` build does not establish fresh native-shell, installed or visible
acceptance. Full real-engine workflows were not rerun for this parser change.
