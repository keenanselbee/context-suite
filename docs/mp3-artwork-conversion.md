MP3 Artwork Conversion Verification
==================================

Status: bounded MP3-to-FLAC/Vorbis/Opus artwork transport implemented, with
isolated verification recorded below. This is one audio preservation slice;
remaining metadata, player/listening and release gates stay open.

Preservation boundary
---------------------

One MP3 audio stream with embedded PNG/JPEG covers can convert to FLAC, Ogg
Vorbis or Opus. The managed inventory reads ID3v2.2 PIC and v2.3/v2.4 APIC frames,
retaining image bytes, picture type, description and order. Version-specific
text encodings, tag/frame unsynchronisation and v2.4 data-length indicators use
the existing strict ID3 reader. Pictures never become ordinary title/album tags.
The underlying layouts are specified by
[ID3v2.2](https://mutagen-specs.readthedocs.io/en/latest/id3/id3v2.2.html#attached-picture),
[ID3v2.3](https://mutagen-specs.readthedocs.io/en/latest/id3/id3v2.3.0.html) and
[ID3v2.4](https://mutagen-specs.readthedocs.io/en/latest/id3/id3v2.4.0-frames.html#attached-picture).

PIC format codes PNG/JPG map to image/png and image/jpeg. APIC currently requires
those full MIME values; omitted media prefixes, aliases and other formats need
further review. Descriptions are limited to 64 UTF-16 code units for every admitted
version, a conservative implementation bound also applied to v2.4. Empty
descriptions are accepted; duplicate descriptions and duplicate icon types are
refused. Reserved picture types, mismatched signatures, incomplete descriptions,
invalid Unicode, linked images and oversized tags are refused before encoding.
The managed reader does not decode images or fetch links. Native source probing
still precedes the complete MP3 inventory; this is not a pre-probe refusal claim.

The complete ID3 tag remains bounded to 2 MiB and 4,096 frames, with at most 31
pictures. Descriptions share the 256 KiB text budget. The generated FLAC PICTURE
blocks carry zero dimension/depth/color declarations because ID3 does not provide
those fields; the encoded image retains its own information. File icons require
a declared 32-square PNG. This structural check is not full image validation.

FLAC output retains its encoded STREAMINFO and descriptive comments, adds the
picture blocks, then copies the encoded audio bytes. Unexpected encoder metadata
is refused rather than moved with potentially stale offsets. Output validation
compares all meaningful FLAC blocks byte for byte with the expected inventory.
Vorbis/Opus use the existing
[exact picture-comment transport](audio-artwork-conversion.md), following
[Xiph's convention](https://wiki.xiph.org/VorbisComment#METADATA_BLOCK_PICTURE).
Native output must also retain the image codec sequence, audio properties,
descriptive tags and decoded sample count within the existing signal bounds.

Same-format MP3 remains a byte-identical no-op. WAV and M4A artwork output still
require separate handlers. MP3-to-FLAC retains the explicit PCM24 precision
decision; lossy targets retain the lossy-transcoding decision, and Opus resampling
remains explicit where needed. Covers do not bypass these quality decisions.

Verification
------------

- **2,561 foundation contracts pass**, adding 59 structural picture checks and
  six plan/consent checks to the previous 2,496. Coverage includes every permitted
  description encoding, empty/Unicode descriptions, binary unsynchronisation,
  data-length indicators, malformed input, duplicate descriptions/icons, budgets,
  exact picture fields and caller-position restoration on failure/cancellation.
- **91 focused private artwork checks pass**. Six version/encoding fixtures
  carry front PNG and back JPEG covers; three further cases exercise binary
  unsynchronisation. All three targets retain exact picture blocks, ordinary
  tags when present, and 96,000 decoded audio frames. The pinned independent
  FFmpeg 9.0 runtime verifies exact output RGBA against authored PNG pixels
  (including alpha) and against its source JPEG decode. It independently encodes
  that JPEG from the authored PNG; JPEG is not compared to pre-lossy pixels.
  Same-format no-op, unsupported targets, linked/reserved pictures, original
  hashes and scratch cleanup are also checked.
- **100 existing MP3 preservation checks pass again**, covering older/modern
  text, ID3v1, mappings, no-op, gapless frame counts and unsupported metadata.
- **58 direct-command checks pass** through the freshly staged worker. They
  cover v2/v3/v4 inputs, the required quality prompt, declining without work,
  no-op without admission, validated copy publication with exact pictures,
  repeated-output collisions, unchanged originals and reservation cleanup.
  They do not open UI windows or invoke Explorer.

Both managed test hosts build with zero warnings/errors. Fresh combined image,
audio, PDF and native-shell staging passes payload identities, notices and file
inventory verification:
`artifacts/production-staging/747958a4173e4bf4b16ad92e9fd9c7a2`.

Retained evidence:

```text
.codex-temp/mp3-artwork-foundation-3.log
.codex-temp/mp3-artwork-foundation-3.exitcode
.codex-temp/mp3-artwork-8ad285a57a4943ac9518c2182c30fbf7/mp3-artwork.json
.codex-temp/mp3-artwork-native.log
.codex-temp/mp3-preservation-regression-8daa65bc4de8408fb677575ead11ad56/mp3-preservation.json
.codex-temp/mp3-artwork-preservation-regression.log
.codex-temp/mp3-artwork-direct-33ece7ea76364c92a8317dbc5bb533b5/mp3-artwork-direct.json
.codex-temp/mp3-artwork-direct.log
.codex-temp/mp3-artwork-production.log
```

The initial foundation run failed an existing native-open cancellation race
observation in `AnalysisIoContracts`, before reaching audio tests. Its log is
retained as `mp3-artwork-foundation.log`. The unchanged test then passed in the
second run (2,555 checks); the final run adds the six plan checks and passes
2,561. One test-host build also exposed a mistaken test-only `FileRow.SourcePath`
reference; the evidence report now uses its actual output/status fields.

Counts are separate suites. Remaining M4A/Ogg-source artwork, other image codecs,
APIC MIME variants, longer descriptions, hostile image decoder coverage,
independent audio fidelity/listening, players, visible UI, installed behavior
and commercial release clearance are not established here. No installation,
registration, native recycling, live licensing or publishing occurred. Fixtures
remain under ignored `.codex-temp`; no reference or customer images were used.
