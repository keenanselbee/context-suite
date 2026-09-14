MP3 Output Artwork Verification
===============================

Status: PNG/JPEG cover transport to MP3 implemented and verified in isolated
candidate 1.0.1, 2026-09-14. Listening/player acceptance and remaining audio
metadata/output variants remain open.


Preservation boundary
---------------------

FLAC, Ogg Vorbis, Opus and AAC-LC M4A inputs can retain supported covers when
converting to MP3. The adapter inventories pictures before encoding, adds standard
ID3v2.4 APIC frames to the newly encoded candidate, and verifies the resulting
inventory before application-owned publication. It retains image bytes, MIME
type, picture type, description and order. It does not re-encode covers or invent
front/back labels. Adding pictures leaves the encoded MPEG stream, including its
gapless header, byte-identical. Existing text frames and padding are retained.

APIC descriptions must be distinct, including empty descriptions. Consequently,
M4A with several undescribed covers still needs another target; a single empty
description is supported. File icons retain the standard PNG/32-square rule.
The mapping follows the [ID3v2.4 picture definition](https://id3.org/id3v2.4.0-frames)
and [tag/frame structure](https://id3.org/id3v2.4.0-structure). The writer emits
UTF-8 descriptions in plain v2.4 frames and accepts only the pinned encoder's
plain v2.4 tag; it does not rewrite arbitrary customer MP3 tags.

ID3 has no separate FLAC width, height, bit-depth or palette-count fields. A
nonzero source declaration must agree with the embedded PNG/JPEG header before
that redundant field is represented solely by the unchanged image. Conflicting
declarations cause refusal, preserving both representations in the original.
Zero declarations remain unspecified in the normalized output inventory. PNG
checks use [IHDR and PLTE declarations](https://www.w3.org/TR/png-3/); JPEG checks
reuse the bounded header reader. These checks do not claim complete image
validation, CRC checking or broad image-codec fidelity. The independent real
cover comparisons below establish the tested decoded samples separately.

The complete output tag is bounded to 2 MiB and 4,096 frames, including existing
text/padding and new pictures. At most 31 pictures and descriptions of at most
64 UTF-16 code units are admitted. Linked/unhandled formats, reserved types,
duplicate icons/descriptions, malformed extents, conflicting geometry and
unrepresentable text are refused. Larger covers need a separately reviewed limit;
the handler never truncates them. Same-format MP3 stays a no-op. Lossy-source
transcoding still requires its existing quality decision; there is no new
acknowledgement for an ordinary preserved cover.


Verification
------------

- **2,696 foundation contracts pass**, including 33 additional MP3 output
  checks. The existing reader independently checks generated APIC frames,
  ordered PNG/JPEG payloads, title retention and audio framing. Count, tag/frame
  budgets, descriptions, icons, geometry conflicts and malformed tags are covered.
- **94 focused private checks pass** across 18 real MP3 outputs. Inputs cover
  FLAC, Vorbis, Opus and both trailing/fast-start M4A movie placement, with PNG,
  JPEG, mixed covers where representable, and a generated 512-square noisy RGBA
  cover. Independent decoding checks cover pixels and alpha; the adapter verifies
  96,000 decoded audio frames, admitted tags and existing signal-error limits.
  A direct writer check proves unchanged MPEG bytes and pre-cancellation behavior.
- **56 actual-worker/direct-command checks pass** on the combined candidate:
  required/declined quality decisions, copied output, exact published APIC/title,
  safe name collisions, original hashes/times and same-format no-ops. These are
  automated application-model checks, not visible or listening acceptance.
- Existing artwork paths pass again: **114 M4A**, **81 Ogg-source**, and **31
  FLAC-to-Ogg** checks. Counts replace obsolete MP3-target refusal assertions;
  their new MP3 success cases are in the dedicated suite above. Historical counts
  remain in the earlier checkpoint documents.

No MP3 encoding-quality preset changed. The authored JPEG reference is decoded
independently rather than compared to its pre-JPEG PNG pixels. M4A cases carry
one picture; mixed M4A covers with duplicate empty descriptions remain refused.

Evidence:

```text
.codex-temp/mp3-output-final-foundation.log
.codex-temp/mp3-output-artwork-5b27a108692f46658fab66fd2875f3d5/mp3-output-artwork.json
.codex-temp/mp3-output-artwork-writer.log
.codex-temp/mp3-output-direct-ec4340d690224636a798befec7eab7f9/mp3-output-artwork-direct.json
.codex-temp/mp3-output-direct.log
.codex-temp/mp3-output-regression-m4a-artwork-37e4a7e2317c4232b3b95945e79fa3b7
.codex-temp/mp3-output-regression-ogg-artwork-01534e2706ff40fbb0cd68ecd36ecbc0
.codex-temp/mp3-output-regression-flac-ogg-artwork-b187d4576ea24a3b8aa92ec575ec3b19
artifacts/production-staging/a932bd2a928c48d5b708d407d892693a
```

The first foundation invocation was stopped by PowerShell's treatment of an
expected negative activation diagnostic. The canonical script subsequently
passes in a child PowerShell host; the earlier log remains. The first private
run fails only when its test tries to read an intermediate candidate already
removed by successful cleanup. The corrected test exercises the writer directly
and retains that failed directory. An initial test-build span/await error is
also retained in `.codex-temp/mp3-output-contract-build.log`; the corrected
build passes without warnings/errors.

Final verification rehashes all 108 staged files and checks the exact file set
against the preserved inventory. The prior excluded edits in both repositories
also retain their hashes. Evidence: `.codex-temp/mp3-output-final-inventory.json`.
Public source boundaries, theme policy and 121 documentation files pass; no
application, worker or contract-test process remains. Native engine/payload and
notice validation passed during the combined build. No installed state changed.


Remaining scope
---------------

M4A/WAV artwork output, additional picture/tag variants, listening and player
compatibility, and final integrated/manual acceptance remain open. The prior
image/PDF/Office and installer/commerce evidence keeps its original date/scope;
this checkpoint is not a rerun of every feature or commercial release clearance.
Follow the [broad support goal](broad-file-support-goal.md).
