WAV ID3 Artwork Conversion Verification
=======================================

Status: bounded embedded ID3 source handling implemented for WAV conversion,
2026-09-14. Candidate 1.0.4 passes isolated worker acceptance. WAV artwork
output and broader audio/document acceptance remain unfinished.


Format and preservation boundary
--------------------------------

WAV can carry ID3 metadata in a RIFF chunk. The
[Mutagen WAVE documentation](https://mutagen.readthedocs.io/en/latest/api/wave.html)
uses ID3 tags; [FFmpeg's WAV reader](https://www.ffmpeg.org/doxygen/trunk/wavdec_8c_source.html)
recognizes both `id3 ` and `ID3 ` chunks and attached-picture frames. The
[ID3v2.4 structure](https://id3.org/id3v2.4.0-structure) supplies tag framing and
footer rules. These are format references, not copied code or dependencies.

`WaveMetadata` has an explicit ID3-preservation option. It defaults off so callers
without a preservation plan retain the old unsupported-chunk result. Conversion
inspection and execution opt in. One complete tag may occur before or after the
audio data. Both chunk-name spellings use one shared count: duplicate chunks are
invalid. Tag size must match its chunk exactly, including any declared footer;
undeclared trailing bytes are refused. RIFF word padding stays outside the tag.

The reader reuses the existing MP3 ID3 frame/text/picture parser without adding
fake MPEG frames. ID3v2.2/v2.3/v2.4 supported text and PNG/JPEG picture fields
retain their existing encoding, unknown-frame, count and duplicate-description
rules. A tag has at most 2 MiB of frame payload, 4,096 frames and 31 pictures;
the existing text limits still apply. The outer 512 MiB WAV, 4,096 RIFF/INFO-chunk
and 256 KiB INFO limits remain separate. Parsing stays local, bounded and
cancellable and restores the caller's stream position.

WAV INFO and ID3 descriptive fields remain visible in the inventory. Repeated
field names across those stores block conversion, including agreeing values;
no store silently overrides the other. ID3 text uses its declared encoding,
while INFO retains its existing ASCII-only policy. Unknown chunks, cue points,
loops, broadcast metadata, iXML, unsafe frame features and linked artwork retain
their refusal behavior. The source native probe must agree with managed text and
audio properties before encoding; not every legacy encoding variant is admitted
merely because its tag can be parsed.

WAV artwork can convert to FLAC, MP3, Vorbis and Opus through their existing output
handlers. M4A also accepts representable pictures with Other designation and an
empty description; it refuses labels/descriptions it cannot retain. Encoded image
bytes, order and represented fields are preserved. Existing target-specific
geometry/description limits remain in effect. Output tags, picture inventories,
codec/rate/layout and decoded audio still require validation. Ordinary successful
commands use the existing quiet path; same-format WAV stays a byte-identical
no-op. No overwrite, trial or quality-consent policy changes are introduced.


Recorded evidence
-----------------

- **2,754 foundation contracts pass**, including 25 new WAV/ID3 checks for
  opt-in behavior, Unicode text, chunk spellings/placement, exact footer framing,
  pictures, duplicate stores/chunks, unknown frames, malformed sizes, byte limits
  and cancellation/caller position.
- **339 private artwork checks pass**, including **106 real conversions** from
  26 authored WAV inputs. Six ID3 version/encoding combinations each run with
  both chunk spellings and both placements. PNG/JPEG covers retain front/back
  designations and Unicode descriptions through four targets. Neutral and large
  512 x 512 RGBA covers run through all five non-WAV targets. Outputs retain
  inventoried tags/artwork and decode to 96,000 frames; FLAC retains exact decoded
  samples under the existing validation. A separately pinned decoder verifies
  PNG pixels/alpha and JPEG source-reference pixels. No-op bytes, original
  hashes/times, duplicate stores/chunks, cue refusal and cleanup are checked.
- Existing regressions pass **14 WAV-preservation**, **100 MP3-preservation**
  and **110 M4A-output** checks.
- **104 direct-worker checks pass** on the combined candidate. Five source
  fixtures exercise 22 target combinations, including three ID3 versions and
  large artwork. Checks cover quiet validated copies, exact published artwork
  and titles, collision-safe retries, source/output same-format no-ops without
  admission, unchanged originals, and malformed/unsupported metadata refusal
  before publication or admission.
- Release builds have zero warnings/errors. Combined candidate **1.0.4** has
  **116** verified image/audio/PDF/native-shell payload files. This is scoped
  local acceptance, not a final all-feature release test.

The new stage is
`artifacts/production-staging/424f0c8130054282a52a6545e0669689`.
Its inventory SHA-256 is
`AD78747D6377431BA09D0DC32DF25DE92AE8EAA0503790B02360E75C3DE6D1C2`.
Its reservation is `artifacts/production-version-receipts/1.0.4.json`; earlier
candidates and receipts remain unchanged.

Evidence is retained under `.codex-temp`. The main private report is
`wave-id3-regression-wave-artwork-d0d6ce91a5ca4e6c8db7730ec1ffa9d3/wave-artwork.json`.
The direct report is
`wave-artwork-direct-4c7c3ec67dea44cfae3ff4625c0aa687/wave-artwork-direct.json`.
Logs are `wave-artwork-direct.log`, `wave-id3-final-<suite>.log`, `wave-id3-foundation-final.log` and
`wave-id3-production.log`. An earlier smaller matrix passed 325 checks before
the large-cover and refusal fixtures were added. The first regression wrapper
assembled its argument array incorrectly and was refused by test-host argument
validation; `wave-id3-regression-wave-artwork.err` retains that failure.


Reproduction and remaining work
-------------------------------

Build the public foundation and private audio Release test hosts. Use absolute
paths to the independently pinned candidate and decoder and the generated
six-format fixtures from [audio evaluation](audio-engine-evaluation.md). Each
evidence directory must be new.

```powershell
dotnet artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0/ContextSuite.Audio.ContractTests.dll --wave-artwork '<candidate bin>' '<six-format fixtures>' '<new artwork evidence>' '<independent decoder bin>'
dotnet artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0/ContextSuite.Audio.ContractTests.dll --wave-preservation '<candidate bin>' '<six-format fixtures>' '<new regression evidence>'
dotnet artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.dll --wave-artwork-direct '<new direct evidence>' '<isolated worker executable>' '<artwork evidence>'
```

This checkpoint covers ordinary PCM16 stereo/48 kHz artwork fixtures. Existing
WAV precision/layout tests remain separate; the full cross-product was not rerun
with artwork. Additional ID3 variants, INFO/ID3 duplicate reconciliation,
non-ASCII INFO, WAV artwork output, listening/player compatibility and final
integrated acceptance remain open. Visible UI, screen readers, other themes/DPI
and installed Explorer were not exercised. Required Office conversion, isolation,
engine adoption/redistribution, signing, installer lifecycle and live commerce
remain separate gates in the [broad support goal](broad-file-support-goal.md).
