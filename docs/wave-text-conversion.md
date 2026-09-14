WAV Unicode Tag Conversion
==========================

Status: implemented with isolated real-engine and candidate 1.0.7 staged-worker
evidence, 2026-09-14.
Broader audio, Office and integrated acceptance remain open.


Preservation policy
-------------------

Conversion from FLAC, MP3, AAC-LC M4A, Vorbis and Opus to WAV can now retain
supported Unicode descriptive values, including alongside artwork. Representable
ASCII title, artist, album, comment, date, genre, language, track and copyright
values keep their RIFF INFO convention. Other admitted descriptive values use
explicit UTF-8 ID3v2.4 frames in one embedded `id3 ` chunk. The encoder receives
only the selected INFO values; inherited global and stream metadata are disabled
for WAV output. A field is never deliberately duplicated across INFO and ID3.

The independently authored text writer follows the
[ID3v2.4 structure](https://github.com/id3/ID3v2.4/blob/master/id3v2.40-structure.txt)
and [native frames](https://github.com/id3/ID3v2.4/blob/master/id3v2.4.0-frames.txt).
Common descriptive fields use typed frames when their syntax fits; unnamed comments
use `COMM` with undefined language. Other keys use described `TXXX` fields, keeping
literal dates and genres instead of interpreting them as timestamps or numeric
genre codes. Track/disc typed frames require a numeric part/total form; language
requires a lowercase three-letter code form, and copyright requires a four-digit
year followed by a space. Nonmatching values retain their keys and literal text
in custom fields. The [existing picture writer](wave-output-artwork.md) combines those
frames with APIC pictures while retaining all encoded RIFF sample/chunk bytes.

Limits remain 128 fields, 128 characters per name, 4,096 UTF-16 code units per
value and 256 KiB of text. NUL, unsupported controls, invalid Unicode and
unrepresentable custom names are refused before writing. Custom names must use
the existing reader's printable ASCII range through `}`. Tab, carriage return and
newline remain literal values. Aggregate picture/tag and output-file budgets also
apply. Known text refusals are checked during conversion inspection, before paid
admission, and rechecked during execution.

The complete WAV inventory is now validated even when no artwork exists. This
exposed an omitted `IPRT` mapping for track numbers emitted by the encoder.
The reader now recognizes both `IPRT` and `ITRK`, consistent with
[FFmpeg's RIFF mapping](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavformat/riff.c).
Both occurring together remain an unsupported duplicate; neither overrides the
other. Unknown INFO fields and ambiguous non-ASCII INFO input remain refused.
This adds explicit ID3 output encoding, not code-page guessing for input files.

Output must retain every admitted descriptive value, the planned picture
inventory, rate, channel layout and decoded sample count. WAV conversion still
requires exact decoded samples against the production decoder. Copies remain
default; same-format WAV retention remains a no-op without paid admission.
Subsequent floating-WAV-to-FLAC conversion keeps its separate precision-reduction
consent and validation policy.


Verification
------------

- **155 focused checks** cover thirteen authored WAV conversion cases: five input
  formats with/without a PNG cover, richer descriptive/custom fields, and a
  maximum-length field containing supplementary Unicode, and structured-field
  values that must remain custom text. They also include
  invalid-text/aggregate-budget refusals and the existing FLAC conversion suite.
- Each of the thirteen cases checks inspection, complete single-store tags,
  decoded frames, preservation on a return conversion to FLAC, original bytes
  and modification time, and cleanup. A separately pinned test runtime probes
  the saved Unicode values and decodes the samples. Five cover cases also compare
  independent RGBA pixels and alpha.
- Existing regressions pass: **103 WAV output-artwork**, **339 WAV source-artwork**,
  **100 MP3**, **51 Ogg**, **31 M4A**, and **14 WAV preservation** checks. Earlier
  Unicode-to-WAV refusal expectations now require successful preserved output.
- **2,812 foundation contracts pass**, including three cases for both track
  identifiers and conflicting track fields.
- **66 staged-worker direct checks pass** across the same thirteen cases: exact
  published tags/pictures, validated copies, collision safety, unchanged originals
  and same-format no-ops without additional admission. Invalid control text is
  refused before paid admission and produces no output.

Independent lossless-source decoding requires zero sample error. Separately
built lossy decoders may round floating samples differently; that comparison
uses a maximum absolute bound of `2^-20` with an exact frame/channel count.
The largest measured error was `8.940696716308594E-08`; lossless cases had zero
error. Per-case errors are retained alongside the report. This test tolerance
does not relax production's exact own-decoder sample requirement and does not
establish listening or player compatibility.

The focused report is
`.codex-temp/wave-text-syntax-9c5c905df0ab4c5292956635e838d5b6/wave-text.json`.
Regression logs use `.codex-temp/wave-text-regression-<suite>.log`.
Earlier diagnostic runs are retained: the first found the missing track mapping;
new test assertions also needed to account for floating-WAV-to-FLAC precision,
independent decoder rounding, and M4A's explicit undefined-language value.
No failed diagnostic run is counted as passing evidence.

Candidate **1.0.7** is staged at
`artifacts/production-staging/19e12a8a4db142e3bb73df3518c67243`, with 116 files,
verified assembly/package versions, curated image engines and pinned audio/PDF
payloads. Its inventory SHA-256 is
`47021FA4F76752899B10EABFEBB7EB950906C63B8B5EA7F93F46BAA594D6FDA1`.
The reservation is `artifacts/production-version-receipts/1.0.7.json`.
The direct report is
`.codex-temp/wave-text-final-direct-e6ceb442889d47749b60ad1899567555/wave-text-direct.json`.
Final production and direct logs use `.codex-temp/wave-text-final-<suite>.log`.
Foundation evidence remains `.codex-temp/wave-text-foundation.log`, with expected
listener diagnostics separate in its `.err` file. The final focused log is
`.codex-temp/wave-text-syntax.log`.
Repository and payload-hash verification are recorded in
`.codex-temp/wave-text-repository.log` and `.codex-temp/wave-text-final-verification.json`.

The preliminary candidate 1.0.6 at
`artifacts/production-staging/bb84b91390924088abe0b6e2edb6740c` passed the earlier
146-check focused and 61-check direct matrices. Final specification review found
that arbitrary track/disc/language/copyright values cannot always use typed ID3
frames. The correction and extra literal-field case required a new version.
Candidate 1.0.6 and all earlier payloads/receipts remain intact; its narrower
evidence does not establish the corrected syntax policy.


Remaining acceptance
--------------------

This verifies generated local fixtures, not every ID3/INFO variant, metadata
combination or audio rate/layout. Named/language-specific comments, repeated
values, ambiguous INFO code pages and unsupported structural metadata retain
their existing refusal policies. Player/listening compatibility, visible UI,
screen-reader delivery, other themes/DPI, installed-shell acceptance and final
integrated release testing remain separate. No live licensing, installation,
Explorer registration or native recycling is exercised here.
