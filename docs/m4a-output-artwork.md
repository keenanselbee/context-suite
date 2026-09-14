M4A Output Artwork Verification
===============================

Status: supported PNG/JPEG covers from FLAC, MP3, Vorbis and Opus now reach M4A
through the bounded output writer. Candidate 1.0.3 passes isolated worker
acceptance. This does not complete audio fidelity or commercial release gates.


Preservation boundary
---------------------

The M4A `covr` convention represents an ordered list of images. It does not
represent a per-picture designation or description. See the
[Mutagen maintainers' format documentation](https://mutagen.readthedocs.io/en/latest/api/mp4.html)
and [Apple's typed metadata values](https://developer.apple.com/documentation/quicktime-file-format/well-known_types).
These are format references, not copied code or new dependencies.

The output policy accepts embedded PNG/JPEG pictures whose designation is Other
and whose description is empty. Front/back/icon labels and any description are
refused. Core diagnostics recommend FLAC or Ogg; the direct command retains its
existing unsupported-feature message and keeps the original. Pictures retain their encoded bytes,
media types, count and order, including duplicate images. Nonzero width, height,
depth and palette declarations must agree with the image header before the M4A
inventory uses zero for those absent fields. This reuses the MP3 output geometry
check; it does not claim full image decoding or PNG CRC validation.

The existing 31-picture and 8 MiB aggregate picture-transport limits apply,
including synthesized FLAC PICTURE fields and base64 transport overhead. The
16 MiB movie and existing overall audio-file limits also apply. Linked pictures,
unsupported types, malformed extents and inconsistent declarations stay blocked.
No artwork omission or extra quality consent is introduced. Same-format M4A
requests remain byte-identical no-ops.

The private writer operates only on a fresh encoded candidate after the complete
managed M4A inventory has admitted it without existing artwork. It appends one
`covr` atom to the existing metadata list and updates enclosing lengths. Movie
placement stays unchanged. Both 32-bit and 64-bit chunk tables retain offsets
before the movie and add the precise growth to offsets after it. Every encoded
AAC media byte remains unchanged. Unsupported extended child containers are
refused; the pinned encoder emits ordinary child atoms.

Before publication, the adapter re-reads the output and compares exact projected
picture blocks and descriptive tags. Existing codec, channel layout, rate,
decoded sample-count and signal-error checks still run. Source-format managed
inspection checks representability before application admission. Lossy-to-lossy
conversion retains its existing focused quality prompt and decline behavior.


Recorded evidence
-----------------

- **2,729 foundation contracts pass**, including 33 new M4A output contracts.
  Cases cover ordered/duplicate pictures, correct image types, consistent and
  unspecified geometry, 31 covers, unsupported fields, malformed headers,
  uninitialized inputs, count limits and aggregate/transport byte limits.
- **110 private output checks pass**, including 18 real conversions. FLAC,
  Vorbis and Opus each supply PNG, JPEG, mixed, duplicate and large alpha covers;
  MP3 supplies the three single-cover variants. Outputs retain title and exact
  cover inventories and decode to 96,000 audio frames. A separately pinned
  image decoder compares PNG pixels/alpha and JPEG source-reference pixels.
  Writer tests cover leading/trailing movie atoms with 32/64-bit offsets,
  unchanged AAC data/tags/packet extents, caller position and pre-cancellation.
  Unsupported labels/descriptions/geometry, original bytes/times and cleanup
  are also checked.
- Existing artwork regressions pass **94 MP3-output**, **114 M4A-input**,
  **81 Ogg-input** and **31 FLAC-to-Ogg** checks.
- **59 direct-worker checks pass** on the actual combined candidate. Six source
  fixtures cover all four source families, mixed covers, PNG/JPEG and large
  artwork. Published copies retain exact covers/title; collisions preserve the
  first output. Required quality prompts, declined consent without admission,
  same-format no-op without admission, original hashes/times and three metadata
  refusals before admission/publication are checked.
- Release builds have zero warnings/errors. Candidate **1.0.3** has **116**
  inventoried files, including the combined image/audio/PDF/native-shell payload.
  These checks are scoped acceptance, not a final all-feature release run.

The candidate is
`artifacts/production-staging/f1f21b2f1e63471b9f2b5a44442a90b5`.
Its inventory SHA-256 is
`4FC1E1B50B7338BDDFC0B10F6F1E97935F3464BF4E3C5FF89EB85F32DEDA3460`;
`artifacts/production-version-receipts/1.0.3.json` reserves that exact payload.
Earlier candidates and their receipts remain separate.

Evidence lives under `.codex-temp`. The main private report is
`m4a-output-regression-m4a-output-artwork-ee62ccd7941843f4b409e8b97a33e1f2/m4a-output-artwork.json`.
The direct report is
`m4a-output-direct-19091a41e53d4982b7ac00eb6a8343c9/m4a-output-artwork-direct.json`.
Logs are `m4a-output-direct-final.log`, `m4a-output-regression-<suite>.log`,
`m4a-output-foundation-final.log` and `m4a-output-production.log`.
An initial invocation using relative engine paths was correctly refused before
engine execution; its log is `m4a-output-native.err`. The corrected first run
passed 85 checks before the duplicate-cover and 64-bit-offset cases were added.
The first direct test expected Failed for deliberately unsupported metadata;
application code correctly reports Unsupported. The test assertion was corrected
and the same immutable candidate passed the rerun. The original failure remains
in `m4a-output-direct.err`; no application change was needed.


Reproduction and remaining work
-------------------------------

Build the private audio and public foundation Release test hosts from the parent
repository. Use the independently pinned candidate and decoder paths and generated
six-format fixtures documented in [audio evaluation](audio-engine-evaluation.md).
All path arguments must be absolute and evidence directories must be new.

```powershell
dotnet artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0/ContextSuite.Audio.ContractTests.dll --m4a-output-artwork '<candidate bin>' '<six-format fixtures>' '<new artwork evidence>' '<independent decoder bin>'
dotnet artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.dll --m4a-output-artwork-direct '<new direct evidence>' '<isolated worker executable>' '<artwork evidence>'
```

The later [WAV source handler](wave-id3-artwork.md) adds embedded ID3 artwork inputs.
WAV artwork output, further metadata and codec variants, listening and
player compatibility, and integrated acceptance remain open. Per-picture labels
and descriptions have no mapping in the reviewed M4A cover convention; this
checkpoint does not authorize silently dropping them. Visible UI, screen-reader
delivery, other themes/DPI and installed-shell acceptance were not exercised.
Required Office conversion, isolation, engine adoption/redistribution, signing,
installer lifecycle and live commerce remain separate unfinished gates in the
[broad support goal](broad-file-support-goal.md).
