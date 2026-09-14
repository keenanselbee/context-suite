WAV Output Artwork Verification
===============================

Status: bounded WAV output artwork implemented, 2026-09-14. Private real-engine
and candidate 1.0.5 staged-worker checks pass. This is
a scoped audio checkpoint; the broader support goal remains incomplete.


Preservation boundary
---------------------

Convert to WAV now preserves representable PNG/JPEG covers from FLAC, MP3,
AAC-LC M4A, Ogg Vorbis and Opus. The existing WAV same-format action remains
unchanged without publication or paid admission. This completes the basic artwork
routes among the six formats subject to each target's existing preservation limits;
it does not establish support for every metadata, codec or container variant.

The private writer adds one lowercase `id3 ` RIFF chunk containing ID3v2.4 APIC
frames to a fresh encoded candidate. It updates the RIFF length and adds odd-byte
padding while retaining every prior chunk byte, including samples, fmt/fact and
INFO. Unknown chunks or an existing ID3 tag are refused before writing. Caller
position is restored and cancellation remains active. Output must remain below
the existing 512 MiB limit.

The shared ID3 picture projection retains image bytes, picture type, description
and order. It verifies redundant PNG/JPEG geometry and uses the existing 31-picture,
2 MiB and distinct-description limits. Unsupported image types, conflicting
geometry or duplicate descriptions are refused. M4A's unnamed multiple covers
therefore need a representable ID3 description policy before WAV conversion; the
application does not invent labels. Complete output metadata, audio properties,
frame count and exact decoded samples are validated before publication. A lossless
target does not restore detail lost in the source.

WAV descriptive INFO text still requires ASCII. At this checkpoint Unicode picture
descriptions used ID3, but descriptive ID3 output was unfinished. The subsequent
[WAV text checkpoint](wave-text-conversion.md) adds that explicit encoding policy.
The encoder's `ILNG` field is inventoried as language, following
[FFmpeg's RIFF metadata mapping](https://www.ffmpeg.org/doxygen/trunk/riff_8c.html).
Its exact value must agree with the native probe. Unknown INFO identifiers and
duplicate stores remain refused. The [WAV ID3 source record](wave-id3-artwork.md)
documents chunk parsing and the underlying RIFF/ID3 references.


Recorded evidence
-----------------

- **2,772 foundation contracts pass**, including the full six-format artwork
  policy matrix, target-specific quality consent, exact decoded WAV policy and
  language mapping/probe-disagreement checks.
- **103 private checks pass across 22 real conversions**. FLAC, MP3, Vorbis and
  Opus each exercise PNG, JPEG, mixed covers and a large 512 x 512 RGBA image.
  M4A exercises single PNG/JPEG/large covers with both movie-atom placements.
  Checks retain exact projected picture inventories, titles, 96,000 decoded
  frames, original hashes/timestamps and scratch cleanup. A separately pinned
  decoder verifies PNG pixels/alpha and JPEG source-reference pixels.
- Writer checks cover odd/even padding, unchanged prior RIFF bytes, caller
  position, pre-cancellation and refusal of pre-existing ID3 without output.
  Duplicate picture descriptions and conflicting geometry are refused.
- Existing regressions pass **339 WAV-source artwork**, **90 MP3 artwork**,
  **77 Ogg artwork**, **112 M4A artwork**, **29 FLAC-to-Ogg artwork** and
  **14 WAV-preservation** checks. Obsolete WAV-output rejection expectations
  were removed; metadata that remains unrepresentable still has refusal checks.
- **37 direct-worker checks pass** on candidate 1.0.5. Seven source fixtures
  exercise quiet validated copies, exact published artwork/title inventories,
  collision-safe copies, same-format no-ops without admission and unchanged
  originals. Duplicate descriptions and conflicting geometry are refused before
  admission or publication. No image planner or audio-quality prompt opens for
  these sample-exact WAV targets. This is automated orchestration evidence,
  not visible desktop acceptance.
- Release foundation and production builds have zero warnings/errors. Candidate
  **1.0.5** has **116** verified image/audio/PDF/native-shell payload files.

The stage is `artifacts/production-staging/c2eca21b98aa42398081720617b6ec3f`.
Its inventory SHA-256 is
`79146B1860CBADB5CA459AE18C7A8757D86119F7A193D03927B03E9B0FB267E5`.
The reservation is `artifacts/production-version-receipts/1.0.5.json`; earlier
stages and receipts remain unchanged. No software or Explorer package was installed.

The private report is retained at
`.codex-temp/wave-output-regression-wave-output-artwork-58c8c7d7e9e6487e8b0e641b14eb4f76/wave-output-artwork.json`.
Regression logs use `.codex-temp/wave-output-regression-<suite>.log`; the foundation
logs are `.codex-temp/wave-output-foundation-fixed.log` and
`.codex-temp/wave-output-foundation-final.log`. The direct report is
`.codex-temp/wave-output-artwork-direct-0b46e82394b74478b1a8213092ae1781/wave-output-artwork-direct.json`;
the corresponding log is `.codex-temp/wave-output-artwork-direct.log` and the
production log is `.codex-temp/wave-output-production.log`.

During review, the `AudioConversionContracts` and `AudioBatchContracts` rejection
helpers were found to catch their own failed assertions as `InvalidOperationException`.
Both now assert failure outside the exception-catching region. Earlier foundation
totals do not prove their old negative cases, including stale MP3/Ogg-to-M4A
rejection expectations. Those expectations now match the implemented artwork
matrix and the corrected suite passes. This finding does not invalidate the
separate real-engine evidence or establish that every test helper has been audited.

An initial real conversion correctly refused an unreviewed `LIST/INFO/ILNG` emitted
for M4A-to-WAV. The diagnostic run identified that field; the explicit mapping and
agreement checks resolved it without dropping metadata. Earlier failure logs
`wave-output-artwork.err` and `wave-output-artwork-diagnostic.err` remain retained.


Reproduction and remaining work
-------------------------------

Build the public foundation and private audio Release test hosts. Use absolute
paths to the pinned candidate, independently pinned decoder and generated
six-format fixtures from [audio evaluation](audio-engine-evaluation.md). Use fresh
evidence directories for every invocation.

```powershell
dotnet artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0/ContextSuite.Audio.ContractTests.dll --wave-output-artwork '<candidate bin>' '<six-format fixtures>' '<new artwork evidence>' '<independent decoder bin>'
dotnet artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.dll --wave-output-artwork-direct '<new direct evidence>' '<isolated worker executable>' '<artwork evidence>'
```

These fixtures use ordinary stereo 48 kHz audio and do not cover the entire
rate/layout/precision cross-product with artwork. Rich metadata, non-ASCII INFO,
additional ID3 variants, listening/player compatibility and integrated acceptance
remain open. Visible UI, screen readers, themes/DPI and installed Explorer were
not exercised. Required Office conversion, engine adoption/redistribution,
signing, installer lifecycle and live commerce remain separate gates in the
[broad support goal](broad-file-support-goal.md).
