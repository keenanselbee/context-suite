M4A Artwork Conversion Verification
==================================

Status: bounded AAC-LC M4A PNG/JPEG cover conversion to FLAC, Vorbis and Opus
implemented and verified in isolated staging, 2026-09-13. Other artwork targets,
metadata variants, human listening/player acceptance and shipping adoption remain
open.


Preservation boundary
---------------------

The existing complete M4A inventory can explicitly admit `covr` image data.
The reader accepts both multiple `data` children and repeated `covr` atoms,
retaining their encountered order. JPEG type 13 and PNG type 14 must match their
image signatures, and locale must be zero. These typed data values follow
[Apple's metadata types](https://developer.apple.com/documentation/quicktime-file-format/well-known_types);
the `covr` convention and image list are also documented by the
[Mutagen maintainers](https://mutagen.readthedocs.io/en/latest/api/mp4.html).
These are format references, not new application dependencies.

Each admitted image becomes a complete FLAC PICTURE payload with its exact bytes
and media type. M4A does not declare a front/back designation, description or
dimensions in these data atoms. The synthesized fields therefore use Other,
an empty description and zero width/height/depth/color declarations. The reader
never decodes an image or fabricates a front-cover label. The existing private
FLAC reconciliation and Ogg comment writer preserve these blocks unchanged.

The existing 16 MiB movie, atom, sample-table, media-extent, local-reference,
timing, AAC-LC and text limits remain. Artwork adds a 31-picture count limit and
an 8 MiB aggregate picture transport budget, including synthesized block fields
and base64 expansion. That budget applies across all cover atoms. Empty covers,
truncated atoms, unknown data types, mismatched image signatures, localized
pictures and external/unknown children are refused. Pictures never consume the
ordinary text budget or authorize other unsupported metadata.

The preservation option defaults off. Only FLAC, Vorbis and Opus targets enable
it in conversion inspection and execution. Complete managed M4A inventory occurs
before native inspection for these cross-format paths. Same-format M4A requests
remain byte-identical no-ops; WAV/MP3 artwork conversion remains unsupported.
No external audio/image reference is followed. Existing duplicate-tag, freeform,
timestamp, playback/edit and codec restrictions remain unchanged.

Output still requires the exact picture inventory, descriptive tags, decoded
audio extent and existing sample validation. FLAC from lossy audio requires
precision consent, and lossy-to-lossy conversion retains its separate consent.
Resampling also requires consent when applicable. No lossless-target restoration
claim is made.


Recorded evidence
------------------

- **2,663 foundation contracts pass**, including 37 new picture-reader and
  admission checks. Cases cover ordered bytes/types, neutral fields, repeated
  cover atoms, multiple data children, caller position, count/aggregate byte
  limits, cancellation and malformed/unsupported inputs. Reader-only byte
  fixtures test inventory, not image decoding.
- **116 private artwork checks pass** with generated valid PNG/JPEG covers.
  Cases include one atom with two covers, two separate cover atoms, and a
  512 x 512 RGBA noise cover. All three shapes run with both a trailing movie
  atom and a movie atom moved before media with adjusted chunk offsets.
  Each converts to FLAC, Vorbis and Opus, retains exact ordered picture blocks
  and title, and decodes to 96,000 audio frames after AAC priming. An independent
  pinned decoder compares output PNG pixels/alpha to authored samples and JPEG
  pixels to its independently encoded/decoded source reference. No-op, declined
  quality consent, unsafe metadata, external references and cleanup also pass.
- **31 existing M4A preservation checks pass**, including text, AAC configuration,
  media extents, priming, fast-start, malformed metadata and external references.
- **115 direct-worker checks pass** on fresh combined stage
  `artifacts/production-staging/bcc65f0acd2f47258b2b165707dbb53f`.
  All six source variants publish validated copies to the three admitted targets.
  Required prompts, declined consent without admission/publication, no-op without
  admission, exact published artwork/title, collisions, original hashes and
  modification times, and reservation cleanup are checked.
- Release test hosts build with zero warnings/errors. Combined image/audio/PDF
  and native-shell staging passes pinned payload and inventory verification.
  Earlier Ogg, MP3, PDF and interruption checks remain their own dated runs.

Evidence is retained under repository `.codex-temp`: foundation log
`m4a-artwork-foundation.log`; private report
`m4a-artwork-95ad254963ac4f07a5b6cf82019b42dd/m4a-artwork.json`; existing regression
`m4a-preservation-35f7b8a2e73e4886a2b36326165fa0b0/m4a-preservation.json`; direct
report `m4a-artwork-direct-6c35ebe5ba074d2da61e0dbce9e41719/m4a-artwork-direct.json`.
Logs are `m4a-artwork-native.log`, `m4a-artwork-regression.log`,
`m4a-artwork-direct.log` and `m4a-artwork-production.log`.


Reproduction and remaining work
-------------------------------

Build the private audio and public foundation Release test hosts from the parent
repository. Use independently pinned candidate/decoder paths and generated
six-format fixtures from [audio evaluation](audio-engine-evaluation.md):

```powershell
dotnet artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0/ContextSuite.Audio.ContractTests.dll --m4a-artwork '<candidate bin>' '<generated six-format fixtures>' '<new absolute artwork evidence>' '<pinned independent decoder bin>'
dotnet artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0/ContextSuite.Audio.ContractTests.dll --m4a-preservation '<candidate bin>' '<generated six-format fixtures>' '<new absolute regression evidence>'
dotnet artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.dll --m4a-artwork-direct '<new absolute direct evidence>' '<isolated worker executable>' '<artwork evidence>'
```

Close Context Suite before direct/router checks. These workflows require no
installation, Explorer registration, live licensing, native recycling or
reference executable. They do not establish visible UI, screen-reader, theme/DPI,
human listening or player compatibility acceptance. Wider M4A metadata/input
variants and artwork output handlers remain under the
[broad file support goal](broad-file-support-goal.md).
