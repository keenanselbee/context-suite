Office Embedded Failure And Recovery Evaluation
===============================================

Status: passive malformed-input and forced-stop observations are complete for
the three authored OOXML fixtures. All 24 following normal exports pass independent
checks. Zero-byte inputs are accepted by the renderer and require application
admission checks. Production cancellation, hostile-content denial and Office
conversion remain unverified/unimplemented.


Scope and method
----------------

This extends the [working Windows lifecycle](office-isolated-startup.md#windows-main-loop-and-input-copy-correction-2026-09-14).
It uses only generated Word DOCX, Excel XLSX and PowerPoint PPTX fixtures, fresh
read-only input copies, disabled-content profiles and the existing AppContainer
token/job restrictions. There are no executable macros, external references or
customer documents in these cases. Network enforcement remains a separate gate;
these passive cases do not authorize hostile-document execution.

An independently built scratch variant adds four fixed faults. The empty case
truncates each owned copy to zero bytes; the ZIP case retains its first 64 bytes.
For interruption, the child reports a confirmed document-loaded or exported
marker, then waits at an explicit test barrier. The parent observes that marker
and terminates the owned job. It records the requested stop separately from a
timeout or diagnostic-output limit. This tests termination at known boundaries,
not cancellation during active rendering or an application-owner crash.

Each fault batch runs three restricted processes and is followed by six normal
exports: one ordinary and one restricted for each format. Every attempt gets a
fresh profile/input/output directory. Each native invocation removes its owned
Windows profile before the following batch begins. The retained earlier fixture
lock files are not removed or reused as inputs.


Observed results (2026-09-14)
-----------------------------

| Fault batch | Three restricted outcomes | Following normal batch |
| --- | --- | --- |
| `cs27`: empty copies | All return success and create PDFs despite having no source document bytes | `cs28`: all six pass independent checks |
| `cs29`: 64-byte ZIP prefixes | All refuse loading, exit nonzero, shut down and leave no PDF | `cs30`: all six pass independent checks |
| `cs31`: stop after load | All stop at the confirmed marker, exit nonzero and leave no PDF | `cs32`: all six pass independent checks |
| `cs33`: stop after export | All stop at the confirmed marker and exit nonzero; PDFs remain in scratch, but the operation remains failed | `cs34`: all six pass independent checks |

The 24 normal outputs pass qpdf structure, expected text, page count/geometry and
exact ordinary/restricted rendered-pixel comparisons. Their input hashes,
timestamps, read-only attributes and one-file input directories remain unchanged.
All 36 attempts retain their intended input bytes and seven profile settings.
All jobs reach zero members and all disposable Windows profiles are removed.
After cleanup, 102 exclusive read opens succeed across inputs, profiles and
existing outputs, confirming those files are no longer held open incompatibly.

The three PDFs created from zero-byte inputs and the three retained after export
interruption also pass qpdf structure checks. This does not make their source
valid or their interrupted operation successful. Output existence, parseability
and even renderer success cannot replace source admission and completed-operation
requirements.

The existing [header/package analyzer](../src/ContextSuite.Core/Analysis/DocumentAnalysis.cs)
is separately exercised against three valid packages, three empty copies and
three truncated copies. Only the valid packages receive a matching content-based
Office identity. All nine inputs retain their bytes/timestamps. This is useful
preflight evidence, not proof that the analyzer validates an entire document or
establishes its rendering, active-content or external-link safety.


Retained evidence
-----------------

All native cases are beneath
`.codex-temp/office-isolation/1a880d93be994b9a9a3467bb77aa1ba3`.
`embedded-fault-matrix-7e4f53613d31469d9a211313ae5aaa16/build.json` binds the
independently compiled source variant; `matrix.json` and per-case logs/results
retain every outcome, including the unwanted empty-input successes. The variant
is based on public commit `031ab163ab13d9fa7557a11a02e148738dac3185`; fault hooks
remain evaluation-only scratch code.

Independent normal-output results are:

- `cs28`: `inspection-c5ca501bd064445faab9401bb01fba8e/results.json`
- `cs30`: `inspection-e89abb9ec9fd403da230c57e01f676db/results.json`
- `cs32`: `inspection-8408b74d73704832bb2b2b4d24f7103d/results.json`
- `cs34`: `inspection-223080782d504c93ab62aff579d1c152/results.json`

`fault-qpdf.json` retains checks for the empty-source and interrupted PDFs.
`fault-input-analysis.json` retains the nine content-identification observations;
`.codex-temp/office-fault-input-analysis/build-proof.json` binds its authored
probe, referenced Core assembly and result. The final
`.codex-temp/office-embedded-fault-verification.json` reconciles source/build
identity, all 19,332 runtime members in source and copy, exact copied membership,
original fixture hashes, profile removal, input/profile preservation, file release
and the 24 successful following exports. It explicitly leaves the renderer's
malformed-input-refusal gate failed because of empty-file acceptance.


Initial source preflight
------------------------

The follow-up adds a separate bounded Open XML source preflight and connects it
to `Test-OfficeIsolation.ps1 -EmbeddedExports`. Before launching the native test,
the wrapper locks all three authored sources for reading, checks their contents
and exact main content types, and retains those locks until the test finishes.
Empty or unidentified files, another document family in the expected fixture
slot, and unsupported variants stop the launch. Analyze retains its existing
read-only identity and fallback behavior.

This initial preflight recognizes ordinary DOCX/XLSX/PPTX package declarations,
including strict/transitional namespaces. It distinguishes templates, slide shows
and macro-enabled types from those ordinary variants. It does not inspect every
part, prove absence of active content or authorize arbitrary rendering. Legacy
Office/OpenDocument and additional variants still need an explicit policy; this
initial scope does not remove them from the broader evaluation requirements.
Direct native diagnostic invocations remain separate from this managed preflight.

Verification passes 2,866 foundation contracts, including 44 new source-preflight
checks. Eight tests of the wrapper's actual preflight/lease block use a launch
sentinel: three empty and three truncated sources plus one mismatched family
prevent invocation; the valid case invokes once with three confirmed write-denying
source locks. All eight preserve input bytes/write times and release all 24 read
handles. No Office process or Windows profile was created by these wrapper tests,
and earlier real-engine results were not rerun or reclassified.

The wrapper evidence is retained under
`.codex-temp/office-isolation/preflight-bfbd734e96834152b533df86cdea1f03/verification.json`,
with per-case `source-preflight.json` records. The Release evaluation build has
zero warnings/errors. `.codex-temp/office-source-foundation.log` retains the
passing foundation run. Its earlier redirected PowerShell invocation stopped on
expected malformed-client stderr; the completed rerun captures stdout/stderr
outside PowerShell and preserves the real exit status.


Required next work
------------------

Office conversion admission must reject zero-byte or structurally unidentified
Office inputs before invoking the renderer. A filename hint or successful engine
exit is insufficient. Keep useful read-only Analyze fallback separate; its bounded
package identity must not be presented as complete validation or a safety verdict.
The initial preflight above is necessary identification, not complete production
admission. No customer Office conversion path is implemented by this experiment.

Continue with actual mid-render cancellation, owner/worker crash recovery, larger
resource/disk-failure cases, broader format/fidelity coverage and application-owned
publication. Resolve calculation/font policies and network/content isolation
before customer conversion. No visible UI, screen-reader, theme/DPI, installed
shell, live licensing or commercial-release acceptance is claimed here.
