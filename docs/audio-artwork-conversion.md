FLAC Artwork Conversion Verification
===================================

Status: bounded FLAC-to-Vorbis/Opus artwork transport implemented and tested in
isolated production staging, 2026-09-13. Other cross-format artwork paths,
human listening and player acceptance remain open. The later
[MP3 artwork checkpoint](mp3-artwork-conversion.md) adds a separate ID3 picture
handler; the counts and limitations below describe this FLAC-source checkpoint.

Preservation boundary
---------------------

One FLAC audio stream with embedded pictures can convert to Ogg Vorbis or Opus.
The managed writer transports each complete FLAC PICTURE payload as a canonical
base64 `METADATA_BLOCK_PICTURE` comment, retaining order, image bytes, description,
type, media type and dimension fields. This follows
[Xiph's cover-art convention](https://wiki.xiph.org/VorbisComment#METADATA_BLOCK_PICTURE).
The pictures are not re-encoded. Ordinary descriptive tags retain their existing
explicit mappings and limits; picture-stream titles cannot replace album tags.

The candidate's full Ogg page inventory is validated before rewriting its
comment packet. Rebuilt pages use checked sequence numbers, continuation flags,
granules and CRCs. Identification, setup and audio packet bytes remain intact;
Vorbis setup sharing the original comment-ending page is retained. Opus padding
retains its original bytes. Final output requires exact original picture blocks,
the ordinary tag inventory and the existing native audio/signal validation.
Opus framing follows [RFC 7845](https://www.rfc-editor.org/rfc/rfc7845.html).

At most 31 embedded pictures and 8 MiB of aggregate base64 picture fields are
admitted. Only the explicitly authorized output comment packet gets the larger
packet limit; ordinary text remains limited to 256 KiB, other Ogg packets to
1 MiB and the file/page/deadline bounds remain unchanged. Linked pictures are
refused before native parsing and are never fetched. Reserved picture types,
duplicate icon types, malformed structure and oversized transport are refused.
Cuesheets, application blocks, unknown FLAC metadata and unsupported tag semantics
continue to require handlers. Same-format artwork inputs retain byte-identical
no-op behavior and do not require publication or trial admission.

Recorded checks
---------------

- **2,464 foundation contracts pass**, including 15 added artwork-plan and Ogg
  inventory checks. They cover the explicit preservation inventory, changed or
  missing pictures, large continued comments, unchanged ordinary text limits
  and caller-position restoration.
- **33 focused private checks pass**: 25 real-engine/artwork cases and eight
  structural writer cases. Independently authored PNG covers include two small
  front/back images with Unicode descriptions and alpha, plus a 512-square RGBA
  image whose base64 comment exceeds 1 MiB. Both output formats retain complete
  picture blocks, title/artist and 96,000 decoded audio frames. The independently
  pinned FFmpeg 9.0 runtime decodes every output cover to the exact authored
  RGBA bytes. This independent comparison covers images, not audio samples.
- Structural cases exercise final comment sizes 255, 65,025, 130,050 and 130,051
  bytes for both codecs, including the zero-length terminal lace on exact
  boundaries, continued original headers, shared Vorbis setup and Opus padding.
  Independent test packet extraction compares every non-comment packet byte.
  These synthetic setup/audio packets are structural fixtures, not decoded media.
- **36 direct-command checks pass** against the fresh worker, including 16
  artwork cases: quiet copy publication, complete published picture inventory,
  descriptive tags, same-format no-op, repeated-output collision protection and
  unchanged originals. These exercise application orchestration without opening
  windows or invoking Explorer.
- **52 audio conversion workflow checks pass** against the same stage, covering
  the existing six-format matrix, access admission/expiry, mixed failures,
  cancellation, source changes, output reservations and publication cleanup.

Both managed test hosts build with zero warnings/errors. Fresh combined image,
audio, PDF and native-shell staging passes payload identities, notices and file
inventory verification:
`artifacts/production-staging/1bcf6e8fd25f4f35bf1260c15a05e387`.

Retained evidence
-----------------

```text
.codex-temp/flac-ogg-artwork-foundation-2.log
.codex-temp/flac-ogg-artwork-foundation-2.exitcode
.codex-temp/flac-ogg-artwork-7de4daf2183d46d0b51d8aaa5c551010/flac-ogg-artwork.json
.codex-temp/flac-ogg-artwork-7de4daf2183d46d0b51d8aaa5c551010.log
.codex-temp/artwork-direct-27166ccde1834b608caf4449eb95b5ac/audio-conversion-direct.json
.codex-temp/flac-ogg-artwork-direct.log
.codex-temp/artwork-conversion-workflow-b6a398c946af4488a3e53c5d419d413c/audio-conversion-workflow.json
.codex-temp/flac-ogg-artwork-workflow.log
.codex-temp/flac-ogg-artwork-production.log
```

The direct-command run uses the generated covers from the earlier passing
25-case directory `flac-ogg-artwork-fea4796d71b1435bbfa152281d813ebb`.
Earlier failed invocations are retained: a relative evidence path violated the
adapter's absolute-path requirement; a test variable shadowed another local;
and a structural test incorrectly sent its deliberately long title through the
stricter conversion-tag mapping. The corrected tests pass without relaxing
production validation.

These counts are separate suites, not a claim that every previous release gate
was rerun. Broader MP3/M4A/Ogg-source artwork handlers, JPEG/other picture codecs,
hostile image decoder coverage, listening/player compatibility, visible UI,
installed-shell behavior and release adoption remain unverified by this slice.
No installation, registration, recycling, live licensing or publishing occurred.
