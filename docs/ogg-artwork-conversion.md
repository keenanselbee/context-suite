Ogg Artwork Conversion Verification
==================================

Status: bounded Vorbis/Opus source artwork conversion implemented and verified
with generated files in isolated staging, 2026-09-13. Human listening/player
acceptance and production engine adoption remain pending.


Preservation boundary
---------------------

Vorbis and Opus sources with embedded PNG/JPEG covers can convert to FLAC or
the other Ogg codec. Each complete picture block retains image bytes, type,
description, media type, dimension fields and order. FLAC output reuses the
picture-block reconciliation; Ogg output reuses the reviewed comment writer.
No image is re-encoded. Same-format requests remain byte-identical no-ops.
WAV, MP3 and M4A targets with artwork still require preservation handlers.

The source reader accepts case-insensitive `METADATA_BLOCK_PICTURE` field names
and canonical base64 complete FLAC PICTURE payloads. This follows
[Xiph's picture convention](https://wiki.xiph.org/VorbisComment#METADATA_BLOCK_PICTURE)
and [RFC 4648's canonical encoding rules](https://www.rfc-editor.org/rfc/rfc4648.html#section-3.5).
Required padding, no whitespace and zero pad bits are checked. Output field-name
case is canonicalized; the decoded picture payload is unchanged.

Existing bounds remain: 31 pictures, 8 MiB aggregate base64 picture fields
including names, 4,096 comments, 256 KiB ordinary comment text, and the separate
picture-description text bound. Full Ogg framing, sequence, CRC, packet, page,
file and deadline checks still apply. Linked images, reserved picture types,
duplicate icon types, malformed structures and noncanonical encodings are
refused. Links are never fetched. The native source probe precedes the managed
Ogg inventory; this is not a claim of pre-probe artwork refusal.

Ordinary tags retain their explicit mappings. Duplicate descriptive tags,
legacy `COVERART`, replay gain, R128 gain and nonzero Opus output gain remain
unsupported rather than being omitted. The shared FLAC structure reader also
refuses an empty MIME string, although Xiph permits it; generic MIME variants
and other image codecs are not established by this PNG/JPEG matrix.

Quality policies are unchanged: FLAC from decoded lossy audio requires precision
consent; cross-codec lossy conversion requires its own consent, plus resampling
when applicable. Final outputs still require exact picture inventory, descriptive
tags, native image/audio interpretation and the existing sample validation.
This does not claim that FLAC restores lost quality.


Recorded evidence
------------------

- **2,626 foundation contracts pass**, including 43 added source-reader and
  admission checks. Cases include exact ordered blocks, case-insensitive names,
  malformed/truncated structures, trailing bytes, missing padding, nonzero pad
  bits, count/byte bounds, icon rules, gain/tag refusals and complete continued
  Ogg comment packets.
- **85 focused private checks pass** for Vorbis and Opus sources to FLAC and
  the other codec. Authored fixtures contain ordered PNG/JPEG covers with Unicode
  descriptions or a 512 x 512 RGBA noise cover whose comment spans many pages.
  The independent pinned decoder compares PNG pixels/alpha with the authored
  samples and JPEG pixels with its independently encoded/decoded reference.
  Complete picture bytes/fields, title/artist and 96,000 decoded audio frames
  survive every accepted conversion. Refusal, no-op and cleanup cases also pass.
- **51 existing Ogg preservation checks pass**, covering ordinary metadata,
  multichannel layout, short audio extent, framing corruption and unsafe tags.
- **57 direct worker checks pass** on fresh combined stage
  `artifacts/production-staging/b40f5ba06ef84842a2e7504d97cde70a`.
  They cover both sources and both picture sizes, exact published covers/titles,
  quality prompts, declined consent without admission/publication, no-op without
  admission, collision-safe copies, original hashes/modification times and
  reservation cleanup. These exercise application orchestration, not visible UI.
- Release test hosts build with zero warnings/errors. Combined image/audio/PDF
  and native-shell staging passes its pinned payload and inventory verification.
  Earlier PDF, MP3 and interruption results remain separate dated evidence.

Foundation log: `.codex-temp/ogg-picture-foundation.log`. Final private evidence:
`.codex-temp/ogg-artwork-09ee238aceba4909a1160108e8260eae/ogg-artwork.json`, with
log `.codex-temp/ogg-artwork-native-final.log`. Existing regression evidence:
`.codex-temp/ogg-preservation-5e78b2c820564cc1a7c87f34dd92fc47/ogg-preservation.json`.
Direct evidence:
`.codex-temp/ogg-artwork-direct-be6012b44ef240ab894471c73ad52e6a/ogg-artwork-direct.json`,
with log `.codex-temp/ogg-artwork-direct-final.log`. Stage log:
`.codex-temp/ogg-artwork-production.log`. Earlier generated evidence is retained;
the final fixture corrects the authored JPEG's declared depth to 24 bits.


Reproduction and remaining work
-------------------------------

From the parent repository, build the Release private audio and public foundation
test hosts. Use the independently pinned candidate/decoder directories documented
in [audio evaluation](audio-engine-evaluation.md), and a fresh evidence directory:

```powershell
dotnet artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0/ContextSuite.Audio.ContractTests.dll --ogg-artwork '<candidate bin>' '<generated six-format fixtures>' '<new absolute artwork evidence>' '<pinned independent decoder bin>'
dotnet artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0/ContextSuite.Audio.ContractTests.dll --ogg-preservation '<candidate bin>' '<generated six-format fixtures>' '<new absolute regression evidence>'
dotnet artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.dll --ogg-artwork-direct '<new absolute direct evidence>' '<isolated worker executable>' '<artwork evidence>'
```

Close Context Suite before direct/router checks. No installation, registration,
live licensing, native recycling or reference executable is needed. M4A artwork,
other metadata variants, independent audio fidelity/listening, player
compatibility and visible/keyboard/theme/DPI acceptance remain open under the
[broad support goal](broad-file-support-goal.md).
