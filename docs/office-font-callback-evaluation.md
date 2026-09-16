Office Renderer Font Callback Evaluation
========================================

Status: pinned API reviewed; twelve isolated/control exports and independent
callback/PDF comparisons pass.
This is evaluation tooling, not a customer missing-font detector or release
acceptance. The [font fidelity gap](office-font-substitution.md) remains open.


Candidate API and limits
-----------------------

The pinned LibreOffice 26.2.6.3
[callback API](https://github.com/LibreOffice/core/blob/libreoffice-26.2.6.3/include/LibreOfficeKit/LibreOfficeKitEnums.h)
defines missing-font callback 57. A document callback can report font-family
names; it cannot identify a missing style. The
[document ABI](https://github.com/LibreOffice/core/blob/libreoffice-26.2.6.3/include/LibreOfficeKit/LibreOfficeKit.h)
provides callback registration. The probe independently declares the required
prefix and verifies its size and function pointer against the loaded runtime;
it does not import upstream headers or depend on a reference checkout.

The pinned
[implementation](https://github.com/LibreOffice/core/blob/libreoffice-26.2.6.3/desktop/source/lib/init.cxx)
tracks font mappings while loading a document and supplies the retained missing
family list on callback registration. It filters same-family matches and several
substitutions it considers metrically compatible, including Arial/Liberation Sans
and Calibri/Carlito. Some glyph substitutions also fall within its heuristics.
Therefore an empty callback cannot establish identical font programs, styles,
glyph coverage or complete export-time fidelity. The API is a possible positive
warning signal, not proof that unreported fonts were preserved.

The exact tagged sources were retrieved independently into
`.codex-temp/office-font-api-cdb3b85321434a559a690ee51a61e307` for read-only
inspection. Header SHA-256 identities are recorded in `sources.json`. The pinned
implementation is 318,803 bytes with SHA-256
`44F1C7F4F9EAB03657BD0AA5C48531786912BD79AAAA95CA970735AEDAE2EC57`.
Web search's cached header omitted the callback; the directly retrieved pinned
header includes it. Source review and actual runtime observations must agree
before using it in production.


Isolated comparison
-------------------

```powershell
.\tools\office-engine\Test-OfficeIsolation.ps1 -CreateDisposableProfile `
  -PreparedOfficeDirectory '<independently prepared pinned Office runtime>' `
  -FontCallbacks
```

This explicit mode creates six fresh, independently authored Word/Excel/PowerPoint
fixtures using the existing Arial and synthetic `ContextSuiteAbsentFont9361`
comparison. Each is loaded and exported in a normal and AppContainer process,
for twelve runs. The same fixed PDF and active-content settings remain in use.
The wrapper leases and preflights all six original fixtures through completion.
Only disposable input copies are presented to the renderer.

The font mode uses the real local-app-data base for native profile derivation,
matching the production environment correction. It retains separate explicit
engine profiles, temporary paths and restricted DLL lookup. It does not install
a font or inherit the user's PATH. The older access-environment experiment and
ordinary embedded-export modes retain their previous environment behavior.

The callback is registered after document load and removed after PDF export.
Only event 57 is recorded, with a limit of four events and 32 KiB per payload;
overflow refuses the case. Raw data is diagnostic evidence from authored fixtures,
not text safe to insert into a customer window. Independent inspection must
validate its JSON and compare it with the generated PDFs and cleanup records.

Inspect a completed run with the separately pinned qpdf/PDFium tools:

```powershell
.\tools\office-engine\Inspect-OfficeIsolationExports.ps1 -StagingId '<run GUID>' `
  -PdfPreparedDirectory '<prepared qpdf directory>' `
  -PdfiumPreparedDirectory '<prepared PDFium directory>' -FontCallbacks
```


Observed result
---------------

The completed run is
`.codex-temp/office-isolation/d005a1aad8c343febbd0090c48594f4b`.
All **twelve exports** complete with bounded output, clean shutdown and zero
active child processes after job cleanup. Six Arial cases emit no missing-font
event. Each of the six synthetic-family cases emits exactly one event identifying
`ContextSuiteAbsentFont9361`. Normal and AppContainer observations agree for all
three document families. The disposable native profile folder and registry
mapping are absent, and no owned test process remains.

Independent inspection
`inspection-0a444e753fe64fc485c78836b4f28a07` validates all twelve PDFs: qpdf
structure, authored text, page counts/geometry, fixed settings and unchanged
source/output identities. All **six normal/isolated pairs** have equal text and
exactly equal pixels across ten page pairs. The missing-font versus Arial
comparison retains equal text but reproduces the earlier visual differences:

| Document | Different pixels per page at 96 DPI |
| --- | --- |
| Word | 6,268; 0 |
| Excel | 2,806 |
| PowerPoint | 11,648; 11,618 |

These differences are observed substitutions, not an accepted typography
tolerance. The unchanged second Word page remains a useful control. The ordinary
six-export inspector regression also passes against retained stage
`1a880d93be994b9a9a3467bb77aa1ba3/cs41`, in
`inspection-00901d935689485a8098313cc920805b`; those older exports were not rerun.

After native execution,
[Verify-OfficeIsolationCopy.py](../tools/office-engine/Verify-OfficeIsolationCopy.py)
rechecks the canonical pinned inventory, exact file/directory membership and all
19,332 vendor file sizes/hashes. The runtime is unchanged. Four refusal controls
reject the wrong stage root or altered archive, destination and inventory member
before accepting any runtime. `runtime-after.json`, `copy-verifier-guards.json`
and `native-profile-absence.json` retain this evidence. The final wrapper includes
that post-run runtime check; it was invoked separately for this completed run.
The native probe builds with warnings treated as errors, and the managed probe
builds in Release without warnings/errors. Native-run inputs were unchanged;
later edits add inspection/reporting only.


Next implementation
-------------------

The production native host, runtime pins, protocol and customer UI are unchanged.
The next change should transport validated family names through the fixed private
host/worker reply, retain independent PDF validation, and require a compact
explicit choice before saving a PDF when the renderer reports substitutions.
Keep the candidate unpublished until that choice; cancellation must retire the
same owned context and preserve the original. Do not add a general planner or
insert raw callback JSON into a window.

This positive warning signal still needs a defensible policy for unreported
style/glyph/embedded-font cases. Callback absence is not permission for quiet
success with unverified fidelity. No production conversion, visible dialog,
keyboard/screen-reader, theme/DPI, new formal package or installer acceptance is
claimed by this evaluation.
