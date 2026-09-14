PDF Page Limit Results
=======================

The later [page-scale checkpoint](pdf-inherited-geometry.md) applies these limits
after UserUnit scaling and supplies inherited-box/rotation acceptance. Its
206-check matrix and versioned 1.0.2 stage supersede those specific open geometry
items below; the historical results here retain their original scope.

PDF-to-PNG now distinguishes recognized size/page limits from malformed input.
The direct result says:

> This PDF exceeds a size or page limit for PNG conversion. Try a smaller PDF or fewer pages. The original was kept.

A rendering-stage limit also retains specific processing-limit guidance and the
status of originals and completed page copies. Other malformed/protected input
keeps its existing failure path. The fixed output policy and all limits are
unchanged; no quality settings or routine planner were added.

Protocol and packaging
----------------------

The native renderer returns one complete 32-byte CPR1 failure frame for its
explicit source-byte, physical-size, pixel-dimension, pixel-area or page-count
guards. Reply kind 3, code 1 means a recognized processing limit; all reserved
fields and the payload length are zero. The normal process exit indicates a
complete reply, not successful rendering. These guards run before output of any
inventory or pixels. Other exceptions retain the nonzero native exit path.

Both inspection and page decoding validate the entire failure frame and map it
to the existing typed `ResourceLimit` category. Unknown codes, malformed or
trailing data stay invalid rather than becoming limit responses. The managed
adapter also classifies the 128 MiB source bound as a resource limit before
hashing or native execution. Empty input remains invalid.

The authored renderer was rebuilt in a fresh prepared directory, preserving the
old candidate and receipts. Its 21,504-byte executable now has SHA-256
`C916AF8BDF3BAA2D6839D55F5DF6325A56D112AC0595D20CE61D4049C616991C`.
Adapter identity and the public payload manifest pin that same binary and its
current source. The PDFium runtime, command arguments, pixel ABI, rendering
policy, process ownership and deadlines are unchanged. Failure kind 3 is a
fail-closed extension: an older decoder rejects it as an unknown reply, and
production composition still requires the paired pinned adapter/host.

Verification
------------

- **2,583 foundation contracts pass**, including 22 added inspection/render
  failure-frame checks. Truncation, unknown/oversized fields, reserved values
  and trailing payloads cannot become successful results.
- **59 worker geometry/publication checks pass** on the final combined stage.
  The previous exact cropped/rotated and maximum-sized pixel results remain
  unchanged. Dimension, area and a valid 4,097-page document return typed limits.
  A forged bounded planning inventory cannot bypass native render remeasurement.
  Its failed rendering publishes nothing, and the executor retains specific
  guidance. Direct mixed-batch rows distinguish oversized from malformed PDFs
  while allowing a later valid document to publish. These are model/status tests,
  not visible or screen-reader acceptance.
- **25 private raster checks and 12 separate evaluation checks pass**. Existing
  form/annotation rendering, alpha/color/profile, PNG density, malformed/protected
  input, source changes, long paths and engine identity refusals still work. An
  additional call through the existing owned-process launcher verifies that the
  native executable independently returns the complete source-size refusal.
- **20 actual renderer interruption checks pass again**: cancellation, client
  deadline expiry, worker death, child termination, publication cleanup and reuse.
  This does not test the native adapter's actual 60-second wall-clock deadline.
- **19 PDF payload checks pass**, including changed/missing files, dependency
  closure and default-release entry-point refusals. The fresh combined image,
  audio, PDF and native-shell build passes its normal inventories and notices.

Final stage:
`artifacts/production-staging/14e7cec5306a4660a6fedeb7f5922c3c`.
The native and managed builds have zero warnings/errors. The PDF test assembly
uses the same internal-access convention as the image/audio test assemblies to
exercise the existing owned launcher; no public execution API was added.

Retained evidence:

```text
.codex-temp/pdf-limit-foundation.log
.codex-temp/pdf-page-geometry-64a820a2aefb444e88861ae2e1c3896d/pdf-page-geometry.json
.codex-temp/pdf-limit-geometry-final.log
.codex-temp/pdfium-engine/370a295da0054e118c43f3a4040e7dc2/raster-adapter-7d607b850e7f49d5bde242dac3a9f319/raster-adapter.json
.codex-temp/pdfium-engine/370a295da0054e118c43f3a4040e7dc2/raster-reference-7f039080bfc2458383796c083878f334/
.codex-temp/pdf-limit-native-source-check-2.log
.codex-temp/pdf-limit-interruption.log
.codex-temp/pdf-payload-tests-fc498de3a10e45fbaa66c5ec6f5c173f/results.json
.codex-temp/pdf-limit-production-final.log
```

Earlier passes on intermediate stage `cf39c10350984972b1b73e87ea2db8bc` are
retained separately. One test build initially lacked internal access to the owned
launcher; that failed invocation is retained as `pdf-limit-native-source-check.log`.
The final foundation count predates the last wording simplification, which is
verified by the final direct-row checks. Other media suites were not rerun for
this PDF change.

Native allocation failure, non-limit renderer crashes, actual adapter deadline,
inherited/UserUnit geometry, broader fidelity, visible/assistive acceptance and
release adoption remain open. No installation, Explorer registration, native
recycling, live licensing or publishing occurred.
