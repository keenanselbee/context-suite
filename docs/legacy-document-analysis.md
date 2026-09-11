Legacy Office Analysis
======================

Status: bounded read-only DOC/XLS/PPT identification implemented; conversion,
rendered fidelity and broader legacy variants remain separate work.

Scope
-----

Analyzer `compound-1` enriches a content-identified compound file under the
application's existing read lease and five-second content deadline. A filename or
compound signature alone cannot identify an Office family. Only root-level stream
names agreeing with supported binary headers establish a **likely** content match.
Embedded documents cannot identify their outer container. Conflicting families,
unsupported declarations and exhausted limits retain basic compound-file facts.

| Family | Required declarations | Facts reported |
| --- | --- | --- |
| Word DOC/DOT | WordDocument FibBase magic, supported base nFib and selected 0Table/1Table | Raw base version, template and encryption/obfuscation flags, declared main stream bytes |
| Excel XLS/XLT | Workbook or Book stream with BIFF8 workbook-globals BOF | BIFF8 and declared workbook stream bytes; sheets and encryption unavailable |
| PowerPoint PPT/POT/PPS | Current User record/version/token and a supported leading document record when applicable | Storage version, whether the encrypted-document token is declared, main stream bytes; slides unavailable |

Word base nFib accepts C1/D9/101/10C/112 (hex); a later nFibNew override is not
inspected. Earlier Excel BIFF versions and standalone worksheet BOFs remain
unsupported. PowerPoint's ordinary token is not proof that all content is
unencrypted. Its current-edit offset is range-checked but the edit/persist graph
is not traversed. User-name text is not decoded or reported. EncryptedPackage and
EncryptionInfo names alone report only their presence, without guessing an OOXML
family or validating encrypted content.

All families leave rendered pages, macros and embedded-content presence
unavailable. The implementation uses managed byte reads, without COM storage,
rendering, decryption, reference resolution or execution. It neither enables an
Office transformation nor requires a paid license or private engine. Header-only
operation probes retain their existing behavior.

Bounds and limitations
----------------------

- CFB versions 3 and 4, with 512-byte and 4,096-byte sectors; whole physical
  sectors, bounded directory graph and checked selected allocation chains.
- At most 2 MiB additional reads (repeat reads count), 1,024 FAT sectors, eight
  DIFAT sectors, 4,096 directory entries, graph depth 64 and 65,536 chain steps.
- Mini-FAT at most 128 KiB, root mini-stream at most 2 MiB. Selected stream
  headers read at most 4 KiB each; unrelated payload contents are skipped.
- Detect cycles, out-of-range references, ambiguous sibling names and shared
  sectors among inspected allocation structures/selected streams. Complete
  unrelated allocation, red-black tree ordering/balance and document semantics
  are not validated. Version 3 ignores the high stream-size DWORD as recommended
  for older writers; version 4 checks the 64-bit declaration.
- Inspected-byte reporting counts the union of physical reads, including the
  original header. A separate fact reports additional read traffic. Caller
  cancellation propagates; an internal deadline returns basic information.

These limits describe supported analysis, not universal format validity or safety
certification. Large, unusual, signed and application-produced variants need
broader acceptance. Existing file-opening/network-filesystem limitations in the
[coverage record](file-type-coverage.md) still apply.

Verification
------------

The independently authored structural fixtures cover all three families, both
sector sizes, mini/normal storage, fragmented chains, nested documents, renamed
inputs, encryption declarations and conflicting headers. Negative cases exercise
allocation graphs, range/size/read limits and cancellation. One thousand
deterministic mutations return bounded reports. An unrelated 8 MiB stream is
skipped; actual application-reader checks preserve source bytes and timestamp.
These structural fixtures are not complete renderable Office documents.

The separate [Office evaluation runner](../tools/office-engine/README.md) offers
`-LegacyAnalysis`: fixed LibreOffice exports of authored passive DOCX/XLSX/PPTX
fixtures into disposable DOC/XLS/PPT copies, then analysis by the production Core
reader with source/copy hash checks. It accepts no arbitrary customer input. This
interoperability check does not establish Microsoft Office layout fidelity,
hostile renderer isolation or customer legacy conversion acceptance.

On 2026-09-11 the Release foundation suite passed **1,768 contracts**, including
99 legacy-analysis checks. LibreOffice 26.2.6.3 generated all three test copies;
each retained likely content identity and unchanged source/copy hashes:

| Generated copy | File bytes | Unique inspected bytes | Export time |
| --- | --- | --- | --- |
| DOC | 9,216 | 9,216 | 9,021 ms |
| XLS | 6,144 | 6,144 | 8,652 ms |
| PPT | 460,288 | 70,144 | 8,719 ms |

These timings measure export, not parser performance. Retained evidence is
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-b6d46db764df4d2bacacbbac552a4d27`,
including `legacy-analysis.json`, per-family conversion/analysis reports and
generated files. The first run stopped while serializing an optional uninitialized
catalog array in the evidence harness; the fixed report projects only hint ID/name.
That earlier run is not counted as a pass.

Full isolated Release staging
`artifacts/production-staging/3880ef9136b34a9e810f13a322a416c7` includes the parser
and freshly built native shell/host files. Its build and payload/dependency/notice
checks pass with zero build warnings/errors. No installed payload was refreshed
and no Explorer registration changed. Optional audio/PDF/Office engines were not
adopted. Existing worker/hidden-view suites were not rerun for this managed
Analyze-only change. Visible, screen-reader, theme/DPI and installed-shell
acceptance remain separate and unverified by these runs.

Implementation sources
----------------------

Independently implemented from Microsoft's [CFB header](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/05060311-bfce-4b12-874d-71fd4ce63aea),
[directory entries](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/60fe8611-66c3-496b-b70d-a504c94c9ace),
[Word FibBase](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/26fb6c06-4e5c-4778-ab4e-edbf26a545bb),
[Word version mapping](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/fe661052-9c88-4ae1-aec4-44799b2b4777),
[Excel BOF](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-xls/4d6a3d1e-d7c5-405f-bbae-d01e9cb79366),
[PowerPoint CurrentUserAtom](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-ppt/940d5700-e4d7-4fc0-ab48-fed5dbc48bc1)
and [PowerPoint record types](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-ppt/38fb1fa5-0a62-477a-8b14-178df22de812).
No reference source, binaries or documents were copied into the implementation.
