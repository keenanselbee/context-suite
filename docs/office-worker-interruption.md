Office Worker Interruption Investigation
=======================================

Status: interruption acceptance incomplete; ordinary exports and diagnostic host
verified, startup failures retained for investigation

The subsequent [directory-boundary investigation](office-path-boundary.md)
identifies the startup cause and corrects the native host manifest. The failures
below remain historical evidence; see that follow-up for current replay status
and the remaining Windows policy limitation.

The new opt-in harness prepares cancellation, worker-only termination and a
controlled application-deadline expiry for DOCX, XLSX and PPTX. Each stop requires
two increasing nonzero PDF lengths and a live native host whose executable,
creation time and AppContainer SID match the owned operation. Each successful
stop must release the worker/host and source/output handles, preserve originals,
remove its owned Windows profile, and permit a fresh export through the same
client. The deadline case fires the recorded 180-second application timer; it
does not wait 180 wall-clock seconds or change the system clock.

These are test requirements, not passed acceptance. Neither attempted matrix
reached its first growing-output stop point. No cancellation, worker-loss or
deadline cell is accepted from this investigation.


Observed results
----------------

- `e7b7cb182ac547b49e8879eafdf21a47` under `.codex-temp/office-worker` retains the
  failed matrix using authored 96-page fixtures and a direct adapter diagnostic.
  Word produced no candidate; the original host returned exit 2 with a generic
  failure. Both owned profiles were removed.
- `e5cea90fd3324136a37e127abe77e744` retains the failed matrix with separately
  generated twelve-page fixtures. Reducing the fixture did not establish a stop
  point. A later direct diagnostic with the instrumented host reported its last
  phase as engine initialization, before the document-loading phase was entered.
  That phase includes startup up to the engine-loop callback; it is not an exact
  native exception stack. Both profiles were removed.
- The worker stage `ce6f4c03432d41cb836192e492ac22d2/short` repeats that startup
  failure with a shorter context beside the retained worker. Its profile was
  removed. Context length alone is therefore not an established explanation.
- Sampled private memory in the instrumented twelve-page diagnostic peaked at
  196,366,336 bytes. This does not support a simple 512 MiB-limit explanation,
  but sampling is not a complete allocation trace or proof of cause.
- The same stage's `contracts-4` passes the existing **39 ordinary worker checks**
  with the new diagnostic host. All three owned profiles were removed. Its PDFs
  pass independent qpdf structure, authored-text, geometry and exact PDFium
  control-pixel inspection at `inspection-da972fefcf0a4751bfaf00b7d2ad8db6`.
- `control` in that stage also completes ordinary Word through the new direct
  diagnostic helper and removes its profile. This confirms the helper can export,
  without explaining the interruption-fixture failures.
- The updated host/runtime pin passes **30 runtime contracts** at
  `.codex-temp/office-runtime/51a78915961b42ff9e3f37f7df6e813b`. Native host,
  worker, private harness and inspection builds pass without warnings/errors.

The twelve-page fixture receipt is under
`.codex-temp/office-isolation/8f27bd8317bd49e7a285fc5758c3ecf3/office-fixtures`.
The original 96-page fixtures remain unchanged. The failures are not evidence
that either fixture size is generally supported, nor that document size causes
the startup exception. Do not weaken resource limits or add startup retries on
the strength of these observations.


Diagnostic host and repeatable commands
--------------------------------------

The host records fixed phase labels and distinguishes allocation/filesystem
exceptions without printing exception text that might contain document data.
Its new pinned binary is 104,448 bytes, SHA-256
`720ACB008C24C4AB7CB60BCF13790338F9E0093D5604B4BD973ABC3438AB1ED2`, built at
`.codex-temp/office-host/fdb8c15fcac144048aed1290c6211930`. The previous scratch
host and managed worker files are preserved in the worker stage's
`retry-f429dd01853a4ed4a3cff62d8c5981dd` directory. Runtime bytes and the complete
pinned inventory are unchanged. This is not a new production payload.

```powershell
dotnet run --project tools/office-engine/Probe/Office.Evaluation.csproj -c Release -- `
  --worker-stop-fixtures <fresh-office-isolation-fixtures-directory>

python tools/office-engine/Test-OfficeWorkerStop.py --create-disposable-profile `
  --worker <retained-worker.exe> --build-receipt <matching-worker-build.json> `
  --fixtures <ordinary-authored-fixtures> --large-fixtures <interruption-fixtures>
```

The fixture command creates twelve passive pages per family without executing
Office. The runner requires explicit profile opt-in, verifies retained worker
source/binary identity, builds only the harness and records fresh evidence.
Every actual export still verifies the complete pinned runtime. A failed run
stops and retains logs/profile receipts; inspect cleanup before any retry.
Successful future mode reports can use the existing independent worker PDF
inspector for their three recovery candidates.

Next, isolate the startup exception using an equivalent ordinary/interruption
control pair with better native exception evidence. Complete all nine active
stop/recovery cells only after reliable startup, then test production context
journals, application loss, independent validation and publication through their
actual boundaries. Broader Office admission, fonts, calculation, network policy,
runtime adoption and customer command remain open. No visible UI, installed
lifecycle, accessibility, commerce or release acceptance is claimed.
