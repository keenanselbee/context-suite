Office Renderer Font Environment
===============================

The pinned renderer exposes the same **313 font-family names** in twelve
authored normal/AppContainer export cases. Each list is unchanged between document
load and PDF export. Arial is listed and the synthetic
`ContextSuiteAbsentFont9361` is absent in every case. This supplies renderer-side
family-list evidence for further font-resolution work; a listed family is not
proof of its actual font program, style, embedding rights or glyph coverage.

This is evaluation tooling. The production host, its font-review protocol and
customer flow are unchanged by this checkpoint.


Fixed read-only query
--------------------

The independently declared pinned document ABI prefix now includes
`getCommandValues`. Only the fixed `.uno:CharFontName` query is called; there
is no caller-supplied engine command or editing action.
The exact tagged [API declaration](https://github.com/LibreOffice/core/blob/libreoffice-26.2.6.3/include/LibreOfficeKit/LibreOfficeKit.h)
and [implementation](https://github.com/LibreOffice/core/blob/libreoffice-26.2.6.3/desktop/source/lib/init.cxx)
show that this query reads the current document's font list. Its legacy response
repeats standard font sizes for each family. These are size choices, not evidence
that a particular font style or glyph exists.

The query result is allocated by the engine; the pinned engine's free function
releases it. The probe bounds each response to 256 KiB and writes it only to its
fresh owned case directory as `fonts-before.json` and `fonts-after.json`.
Only byte counts enter the ordinary bounded diagnostic channel. Engine shutdown,
job cleanup, read-only original leases and disabled active-content settings
retain the existing callback-evaluation workflow.

The legacy writer treats periods in names as property-tree separators. The
observed `Modern No. 20` therefore appears as nested `Modern No` / ` 20`
keys. The independent reader reconstructs those separators within a depth limit;
it does not drop the name or count an intermediate key as a family. It rejects
malformed JSON/Unicode, duplicate properties, unknown fields, invalid size/name
values and excessive input. Its limits are 4,096 families, 256 characters per
name, 64 size choices and eight legacy nesting levels.

The tagged implementation also has a `compact_fonts` option returning separate
font-name and size arrays. That representation has in-memory reader coverage
but was **not selected or tested natively** here. A future production use should
evaluate that representation and its compatibility explicitly.


Evidence and reproduction
-------------------------

The native run is
`.codex-temp/office-isolation/b81a09fa93b943bb9b2d617d0464254d`.
All twelve Word/Excel/PowerPoint control/missing-family exports complete, with
unchanged recorded inputs and clean shutdown. The full 19,332-file runtime and
exact directory membership remain unchanged. The disposable profile folder and
registry mapping are absent; no owned test process remains.

`font-environment-ec4985304e19430e80db6818a9ce590f.json` records all 24 query
responses, their hashes, the reconstructed lists and six normal/isolated
comparisons. Recorded native byte counts match both retained responses in every
case. All lists are equal across export and isolation. The Arial/missing
callback observations remain unchanged.

Independent PDF inspection
`inspection-2cd52a99528747b7b4ff5c6e29ed337a` passes all twelve structure,
authored text, page geometry, settings and preservation checks. Six
normal/isolated pairs have exact pixels. The earlier observed font-substitution
pixel differences are retained; the query is not a typography acceptance rule.

Run the native evaluation with the existing
[callback command](office-font-callback-evaluation.md#isolated-comparison).
Then run:

```powershell
python -B tools/office-engine/Inspect-OfficeFontEnvironment.py '<completed isolation stage>'
python -B tools/office-engine/Test-OfficeFontEnvironment.py
```

The reader passes 25 in-memory checks. The native probe builds with warnings
treated as errors. The first read of the real legacy JSON correctly refused its
nested name until the reader accounted for the pinned writer's period behavior;
no native rerun or altered original was needed for that correction.


Remaining resolution work
--------------------------

Reconcile actually used document fonts with the renderer's list, including
effective styles/themes and name aliases. Do not treat every font-table or
unused-style declaration as a font used by a printed page. Account separately
for missing styles, embedded fonts and per-script/per-character fallback.
The current [font-review window](office-font-review.md) handles reported
substitutions, while callback filtering and broader fidelity remain open.
This experiment does not prove a portable inventory on other machines, complete
font preservation, visible accessibility or commercial release readiness.
