Foundation Fixture Provenance
============================

Audio header fixtures are independently authored in
[AudioAnalysisContracts.cs](../ContextSuite.Core.ContractTests/AudioAnalysisContracts.cs).
The harness retains silent PCM WAVE and STREAMINFO-only FLAC under its
`audio-analysis` scratch subdirectory. The FLAC fixture contains no audio frames;
it is not a valid complete recording or real-engine acceptance. Truncations,
hostile lengths, unknown codecs and deterministic mutations test bounded fallback.

[FlacMetadataContracts.cs](../ContextSuite.Core.ContractTests/FlacMetadataContracts.cs)
authors metadata-only FLAC prefixes for framing and byte-preservation contracts.
Picture/cuesheet payloads in those tests are placeholders, not rendering or full
semantic fixtures. The separate [audio experiment](../../docs/audio-engine-evaluation.md)
authors real encoded media for sample equality and comment preservation evidence.
The private audio encoding contracts additionally author PCM8/16/24/32 and
float32/64 WAVE fixtures, signed full-scale values, an extensible 5.1 declaration,
low-rate mono, FLAC padding/application canaries and a disposable native child.
A generated five-minute PCM24 stereo signal exercises file-based encoding and
decoding beyond the earlier array limits; streaming contracts also use fragmented
sample streams, non-finite values and framing/length mismatches.
The private artwork fixtures independently encode two 2-by-1 RGBA PNG covers,
including one translucent pixel, and package them into FLAC with authored
duplicate artists, lyrics, Unicode descriptions and ReplayGain text. They contain
no downloaded artwork or music. Public descriptive-metadata tests use placeholder
image bytes for framing only; their passing results do not prove image decoding.
The private seek fixture re-encodes generated audio at level 0 with 1,024-sample
blocks and inserts authored seek points/placeholders and zero padding. It compares
seeked output with linear source samples. Public synthetic frame indexes test
boundary changes and malformed tables without claiming native decode acceptance.
These are generated signals and process fixtures, not representative music/speech
or a listening-quality corpus; see [the policy](../../docs/audio-conversion-policy.md).

The managed contract harness creates a small UTF-8 text file under the supplied
repository-local scratch directory and removes it in `finally`. Its complete
content is authored in `tests/ContextSuite.Core.ContractTests/Program.cs`.
It is deliberately not media. No conversion or file-analysis success is inferred
from this fixture; it tests selection, transport, and lifecycle contracts only.

No third-party fixtures or assets are redistributed by these tests. DDS and
image fixtures, provenance, and semantic expectations belong to their later
implementation milestones.

[DocumentAnalysisContracts.cs](../ContextSuite.Core.ContractTests/DocumentAnalysisContracts.cs)
authors minimal ZIP/OOXML/OpenDocument declaration fixtures, hostile variants and
an unrelated large member using the framework ZIP writer. No document renderer
is used; missing target sheets/slides in these minimal fixtures are intentional
because the reader reports declared lists, not full document validity. A generated
Word-family package is retained in `.codex-temp/foundation-tests/documents-*`
for the real-reader byte/timestamp preservation check. These fixtures do not
establish Office application, PDF conversion, font or layout acceptance.

Broad Analyze fixtures are independently authored in
[AnalysisContracts.cs](../ContextSuite.Core.ContractTests/AnalysisContracts.cs).
They cover signatures and their truncation, generic/Unicode files, declared header
fields and malformed offsets. Signature-only fixtures establish identification
behavior, not validity of complete media or documents. Generated disk fixtures
are retained under `.codex-temp/foundation-tests/analysis-*` for inspection; tests
record source preservation, bounded reads and mixed-batch behavior. No fixture
content is taken from reference projects or third-party datasets.

[PdfProbeContracts.cs](../ContextSuite.Core.ContractTests/PdfProbeContracts.cs)
authors structured probe JSON and hostile schema/size variants. Real generated
PDFs are authored separately by [the PDF evaluation harness](../../tools/pdf-engine/README.md),
including small image/mask bytes, document structure and invalid signature
placeholders. These do not establish cryptographic signatures. The separate
PDFium evaluation compares rendered pixels for the generated two-page source
and its optimized copy; broader document fidelity remains unverified. No customer
or reference documents are used.

[PdfAnalysisContracts.cs](../ContextSuite.Core.ContractTests/PdfAnalysisContracts.cs)
checks typed enrichment, the actual 16 MiB framed payload boundary, original read
leases and over-budget/header fallback with authored callback facts. These do not
pretend that the header fixture is a valid PDF. The separate
[PdfWorkerContracts.cs](../ContextSuite.Core.ContractTests/PdfWorkerContracts.cs)
uses real PDFs from the evaluation harness for app-to-worker, source preservation,
paid-access independence and scratch cleanup checks.
