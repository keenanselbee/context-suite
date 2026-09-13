Context Analyzer Design
=======================

Status: generic Analyze fallback, initial header identification, DDS details,
bounded JSON/XML structure analysis, WAVE/FLAC/AIFF/AU header facts, document package and
legacy compound analysis, and a 243-entry descriptive catalog are
implemented. The [coverage inventory](file-type-coverage.md) records exact
facts, limits and tests; broader catalog/family analysis remains in progress.

The [file I/O cancellation scope](analyze-io-cancellation.md) now covers synchronous
opening and metadata operations as well as the asynchronous read budget. Local
blocked-open tests pass; driver-dependent completion and remote-storage acceptance
remain explicit limitations.

The [common image header reader](image-header-analysis.md) now adds content-based
JPEG/GIF/BMP/WebP identification and declared dimensions within the same 64 KiB
prefix. It does not decode pixels, apply orientation or establish complete validity.

Optional deeper audio probing is now connected to the worker and Analyze. It
enriches collapsed details from a bounded byte snapshot and retains the header
report on probe failure. The reviewed audio payload is available in explicit
isolated production staging; default release adoption and wider acceptance remain pending.

Optional [PDF probing](pdf-engine-evaluation.md) is also connected. It reports
pages, encryption and document inventories from a complete bounded snapshot,
retaining unavailable content facts for password-required inputs and header
fallback on failure. Zero reported signature fields never means unsigned. Its
candidate engine is available in [explicit combined staging](pdf-production-payload.md),
alongside optional PDF-to-PNG, PDF optimization and combined image-to-PDF.
Default release adoption and required Office conversion remain pending.

Bounded [OOXML/OpenDocument package inspection](document-design.md) identifies
supported document families and reports declared sheet/slide counts. OOXML also
adds [PowerPoint visibility counts](powerpoint-slide-analysis.md) from referenced
slide settings, distinguishing defaults and incomplete scans. These counts do
not predict rendering or a custom slide show. OOXML
reports external-link declarations from bounded relationship-file inspection,
with an explicit scope and unavailable counts after partial/unsupported scans.
The same scan reports [internal image, embedded-object and VBA references](document-embedded-analysis.md)
as declarations, keeping them separate from verified contents and unique files.
It never opens those targets or treats a zero count as safety approval. Encrypted,
unsupported and over-budget parts retain basic facts. This does not enable
document transformation or validate rendered layout. Bounded
[legacy DOC/XLS/PPT inspection](legacy-document-analysis.md) adds supported binary
header facts and likely content identity, retaining fallback for unsupported variants.

Implementation follows the [broad file support goal](broad-file-support-goal.md)
and [decision 0019](decisions/0019-broad-file-analysis-and-media-expansion.md).
Every readable regular file receives basic facts and an honest result, even when
its type is unknown. A reviewed offline catalog explains common uses; it does not
claim what a particular file contains or enable unsupported transformations.
Extension hints remain distinct from signature/container evidence. Show confirmed,
likely, ambiguous or unknown identity without invented confidence percentages.
Deeper image, audio, PDF and Office/OpenDocument analysis augments this fallback.
Analyze remains local, read-only and independent of paid transformation admission.


Purpose
-------

Context Analyzer explains how a file is stored and which properties matter for
later conversion or optimization. Analysis is strictly read-only and should
make uncertainty visible rather than infer facts the file does not encode.


Explorer Experience
-------------------

**Analyze** is a top-level Explorer command alongside **Convert** and
**Optimize**. It has no Explorer submenu: invoking it passes the complete
selection to the host and opens the Analyzer details surface directly.

Explorer performs no full parse or property-summary work. Analysis happens out
of process after activation. For multiple selected files, the details surface
can show a comparison table, aggregate facts, and individual warnings without
launching a separate window for each file.


Analysis Result Model
---------------------

An analysis result should include:

- Source identity and detected media family.
- Detected format and raw format identifier.
- Facts grouped into stable sections such as storage, image, audio, metadata,
  and compatibility.
- Warnings for malformed, unsupported, contradictory, or incomplete data.
- A state for each fact: explicit, derived, unknown, not encoded, or unavailable.
- The analyzer and schema versions needed to understand cached results.

Parsers return typed facts. They do not construct menu text, windows, or message
boxes. Presentation layers decide which facts fit a compact snapshot and which
belong in details.


DDS Version 1
-------------

The implemented public parser reads at most 148 header bytes and reports:

- DDS header type: legacy or DX10 extended.
- Exact known format plus raw FourCC or DXGI value.
- Compression family, including BC1 through BC7 where represented.
- Whether linear or sRGB is explicitly encoded, typeless, unknown, or not
  recorded by a legacy header.
- Width, height, depth, and mip count.
- Texture type, cube-map state, array size, and volume state.
- Alpha mode when encoded and a qualified description when format-dependent.
- File size and structural warnings.

Legacy DXT headers do not encode enough information to prove linear versus sRGB
usage. Analyzer must report that limitation rather than guess from the filename
or common engine practice.

The application processes an Analyze selection as one read-only batch and shows
per-file details on row selection. Malformed files do not stop later items.
No worker, media engine, trial admission or network request is needed. Checked
payload accounting covers known mip/array/cube/volume layouts; unknown formats
retain their raw identifiers without claiming a payload size. This is structural
analysis, not validation of compressed pixels. See [DDS evidence](dds-conversion-goal.md).


Later Image Analysis
--------------------

Common image analysis may report:

- Container and encoded pixel format.
- Dimensions, bit depth, channel count, and alpha presence.
- Animation and frame count.
- Color profile or declared color space.
- Orientation metadata and whether display dimensions differ from stored
  dimensions.
- Relevant metadata groups without exposing private values unnecessarily.
- Compression mode and quality indicators when the format makes them reliably
  available.

Analyzer must distinguish an absent property from one that the selected parser
cannot determine.


Later Audio Analysis
--------------------

Common audio analysis may report:

- Container and codec.
- Duration, sample rate, bit depth where meaningful, and channel layout.
- Bitrate and whether it is constant, variable, lossless, or unknown.
- Tags, embedded artwork presence, and gapless or loop-relevant information when
  reliably encoded.
- Warnings for misleading extensions, unsupported streams, or malformed data.


Performance And Safety
----------------------

- Treat extensions as routing hints and validate signatures or container data.
- Use bounded reads and checked arithmetic for sizes and offsets.
- Place limits on allocation, recursion, frame counts, metadata lengths, and
  decompression work.
- Do not invoke external media engines during Explorer menu enumeration.
- Do not modify access, write, or creation timestamps intentionally.
- Do not retain file contents or sensitive metadata in logs or caches.
- Key cached results by sufficient file identity and invalidate them when the
  file changes.


Validation
----------

Each analyzer requires:

- Unit fixtures for every advertised format variant.
- Malformed, truncated, oversized, and unknown-value cases.
- Tests proving that unknown values remain reportable rather than crashing.
- Tests separating encoded facts from inferences.
- A read-only contract test that detects unexpected file changes.
- Snapshot selection tests covering one file, compatible batches, mixed types,
  and an unavailable parser.


Non-Goals
---------

Analyzer does not repair files, edit metadata, decode complete media merely to
show a header summary, or make conversion and optimization decisions on the
user's behalf. It may explain or recommend another tool, but that operation must
remain a separate explicit command.
