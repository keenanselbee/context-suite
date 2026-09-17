WMA Conversion Adapter
======================

Status: isolated WMA v1/v2 conversion verified, 2026-09-16. Customer commands,
actual-worker admission/publication, representative timing and listening
acceptance, remaining WMA variants and combined payload adoption are pending.
The installed application and reserved combined 1.1.1 stage are unchanged.

This uses the exact expanded seven-file runtime from the
[restricted input evaluation](audio-expanded-input-evaluation.md). Only that
evaluation identity admits ASF input; existing production and supplier adapter
identities retain their previous input allowlists. No decoder, encoder, native
binary, protocol or output format was added in this adapter checkpoint.


Inventory and conversion boundary
---------------------------------

`AsfMetadata` walks the complete bounded ASF object chain before native
conversion/probing. It checks header/file/data identities, object counts and
extents, one ordinary local audio stream, fixed packet size/count, codec list,
header extension and optional simple-index references. Header/index storage is
bounded to 16 MiB per object; the aggregate object and metadata-descriptor limits
are 4,096. Audio packets are skipped without allocating their extent. The reader
restores caller stream position after success, refusal or cancellation.

Content Description, Extended Content Description and supported Metadata/Library
records retain documented Unicode fields, including title, artist, album, year
and numeric track. Managed inventory owns output tags; native inherited tags
are disabled. PNG/JPEG WM/Picture records retain bytes, type and description
through the existing bounded picture handlers. Existing per-target restrictions
still apply: a labelled cover cannot silently lose its designation in M4A.
Unknown tags/objects, language attribution, scripts, markers, multiple streams,
unsupported timing offsets, broadcast and variable-packet profiles require a
preservation policy. Unsupported meaningful information is a refusal.

Base/extended/advanced encryption objects, encrypted-stream flags and protection
metadata are refused. The reader does not follow URLs, execute script commands
or acquire licenses. Native conversion is never used to remove protection.

The reader identifies five WMA codec tags, but planning admits **wmav1 and
wmav2 only** at this checkpoint. WMA Pro, Lossless and Voice still require
representative fixtures and fidelity evidence; compiled decoders do not establish
conversion support. The six existing targets and consent rules are unchanged:
lossy-to-lossy conversion requires acknowledgement; FLAC uses acknowledged 24-bit
precision; floating WAV preserves the decoded WMA samples exactly.

ASF packet timestamps use a millisecond clock. In the generated stereo 48 kHz
fixture, direct native conversion writes 47,104 WAV sample frames but a fact
count of 47,096; AAC and Opus also fail existing timing/sample-extent validation.
Changing copy/start timestamps or encoder timebase does not fix that case.
The adapter now streams its already validated float64 reference behind an
80-byte WAV header and encodes at the exact sample clock. Samples are neither
added nor removed. This adds at most the existing 1 GiB decoded budget plus
80 bytes of owned scratch, without a full-file memory allocation. Header checks
cover supported channel masks, exact sample extent and the maximum byte budget;
positive native WMA evidence currently covers stereo at 48 kHz only.

This correction preserves the selected decoder's sequence. It does **not** prove
that discontinuous ASF packet timelines, damaged payloads or all encoder delay
conventions are represented correctly. Those are required fidelity/refusal gates
before customer admission. The original 48,000-frame source tone produces a WMA
that both candidate and pinned supplier decode to 47,104 frames; conversion
preserves those 47,104 decoded frames, not the pre-encoding tone's length. Do not
describe that agreement as gapless WMA or independent codec proof.


Evidence and reproduction
--------------------------

```powershell
dotnet run --project proprietary/tests/ContextSuite.Audio.ContractTests/ContextSuite.Audio.ContractTests.csproj -c Release -- `
  --wma-inputs '<absolute expanded engine bin>' '<absolute native fixture directory>' `
  '<absolute new evidence directory>' '<absolute pinned supplier bin>'
```

Final focused evidence:
`.codex-temp/wma-regression-wma-918835d50577482e9dbc02e12d81c907` passes
**283 checks and 36 outputs**. Each WMA version converts to six targets as plain
audio, Unicode text and text plus an unlabelled PNG. WAV sample comparisons are
exact; FLAC maximum error is at most 1/8,388,608; the tone's lossy outputs meet
the declared RMS limit of 0.02. The supplier checks decoded extent, mapped text
and exact artwork RGBA pixels. Source hashes/timestamps and scratch cleanup pass.
Protection refusals precede native conversion, and quality consent remains
required. Fifteen checks cover the decoded WAV transport and bounds.

The public `--asf-metadata` mode passes **74 checks** covering framing, metadata,
picture transport, indexes, protection, unknown content, typed planning and
caller-position recovery. Its minimal PNG header tests transport only; native
picture decoding is covered by the actual adapter matrix.

Retained development evidence includes:

- `wma-adapter-1623f55f8b9140458ff959024fb08c51`: invocation rejected relative
  engine paths before native work; use the absolute paths required by the API.
- `wma-adapter-77075895a13c4122b6e9307e698d4446`: 18 outputs pass and 18 fail
  strict timing/sample validation before the decoded transport correction.
- `wma-timing-82038ee5a89d4e41a5fdb283996697b4`: pinned-runtime frame timestamps
  and four direct-WAV experiments identify the sample/fact mismatch.
- `wma-adapter-9aa377b22e0947aab0ce5a272473e602`: 268 checks/36 outputs pass
  after the correction, before the additional transport boundary checks.

The AIFF regression at
`.codex-temp/wma-regression-aiff-7449a3d1f08e4b98b8f496e3ddb2d366` passes
**365 checks/68 outputs**. Its obsolete ASF-probe-refusal check was removed as
WMA gained its own inventory and conversion tests; no AIFF fidelity check was
removed. The existing audio suite passes **368 combined probe/encoding checks**
at `.codex-temp/wma-regression-baseline-69c4ef9ad22c46ef9778399a1dbaed74`.
The complete `Test-Foundation.ps1 -Configuration Release` run passes **4,307
contracts**, including all 74 ASF checks, in
`.codex-temp/wma-foundation-eb39ce7caf8d4802b6707fa75eef769e.log`.
The managed Release build has zero warnings/errors. Public boundary,
system-theme, 176-document and whitespace checks pass. No visual,
screen-reader, installed-app or Explorer acceptance is claimed by these tests.


Specification provenance and remaining work
-------------------------------------------

The implementation is independently authored from Microsoft's
[ASF Specification](https://download.microsoft.com/download/7/9/0/790fecaa-f64a-4a5e-a430-0bccdab3f1b4/ASF_Specification.doc)
and [ASF file structure](https://learn.microsoft.com/en-us/windows/win32/medfound/asf-file-structure).
The retained specification has SHA-256
`88370623AA0B5347E7A6E1EA86C7B1C766BD79DAF1A37EBF8560A5657665797B`.
It was read as research data, not opened through Office or executed. Selected
pinned upstream parser sources were inspected to check metadata interpretation;
no upstream source or reference binary was copied into production.

Before enabling commands, finish representative and hostile ASF timelines and
payloads, codec configuration variants, rates/layouts, broader metadata/artwork,
WMA Pro/Lossless/Voice decisions, size/time/memory/disk budgets and real-worker
publication/cancellation/recovery. Complete listening and application usability
acceptance, redistribution review and a fresh combined versioned payload.
ALAC remains separate. These tests do not clear commercial release gates.
