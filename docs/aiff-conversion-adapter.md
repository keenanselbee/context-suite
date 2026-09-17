AIFF Conversion Adapter
=======================

Status: isolated managed/native adapter verified, 2026-09-16. AIFF is not yet an
enabled customer command. WMA metadata/planning, actual-worker admission and
publication, final runtime adoption and human acceptance remain separate work.
The selected production payload and reserved combined 1.1.1 stage are unchanged.

This builds on the [restricted input engine](audio-expanded-input-evaluation.md).
The private loader recognizes its exact seven-file identity as an evaluation
candidate and permits AIFF input only for that identity. The existing curated
and supplier adapter identities retain their earlier input allowlist. The later
[WMA checkpoint](wma-conversion-adapter.md) adds separate ASF preservation and
v1/v2 conversion tests; its AIFF regression retains all fidelity checks.


Planning and preservation boundary
----------------------------------

The output enum still contains exactly WAV, FLAC, MP3, M4A/AAC, Ogg Vorbis and
Opus. AIFF is an input-only container/codec policy; it never becomes an output
choice or a same-format no-op. Signed PCM converts with its retained precision.
Big-endian float32/64 becomes the corresponding little-endian floating WAV,
while FLAC retains its existing explicit 24-bit precision acknowledgement.
A-law, mu-law and IMA4 are lossy sources and retain the lossy-generation prompt.
The existing rate, channel-layout and target restrictions continue to apply.

`AiffMetadata` walks the complete bounded FORM, including metadata after sound
data, without allocating the audio extent. It checks chunk bounds/counts,
duplicate required chunks, exact integer 80-bit sample rates, sample storage,
sound offsets and AIFF-C version/compression framing. It restores caller stream
position on success, refusal and cancellation. Limits are 4,096 chunks, 256 KiB
legacy text, the existing bounded ID3 transport and the shared input-file budget.

ASCII NAME/AUTH/copyright/annotation fields retain their literal values as title,
author, copyright and comment. Unicode ID3 text and embedded PNG/JPEG artwork use
the existing reviewed handlers. Duplicate/aliased values cannot silently replace
each other. Ambiguous legacy encodings, unsupported ID3 frames, markers, loops,
application chunks, CHAN and other unknown chunks require a preservation handler.
Block-aligned SSND data is not admitted. These refusals preserve the source.

The managed inventory owns output tags, including tags the native probe might
omit or truncate. Encoding disables inherited metadata and supplies the admitted
values explicitly. Existing output-specific picture and text restrictions still
apply; a format choice does not authorize discarding a picture designation or
description. Decoded PCM/float/companded lengths must agree with the inventoried
extent. IMA4 requires complete 34-byte-per-channel packets and consistent packet
or decoded-frame declarations; no partial tail is trimmed or synthesized.

Verified native variants in this checkpoint are the preceding corpus's AIFF and
AIFF-C NONE PCM8/16/24/32, sowt PCM16, fl32/fl64, A-law, mu-law and IMA4. Positive
sample comparisons cover mono/stereo at 48 kHz. Other rates/layouts, declaration
aliases, the alternative IMA4 count convention, broader text fields (including
AUTH output compatibility), malformed native media and large-file/resource
acceptance remain open. Core parsing/planning coverage is not native fidelity
evidence for those untested variants.


Evidence and reproduction
--------------------------

The focused private test runs actual native inspection and file conversion,
checks output tags, then decodes samples/artwork through the independently pinned
supplier build. That supplier shares the FFmpeg revision; it is not a separate
codec implementation. The preceding native corpus adds exact authored PCM/float
sample oracles. No installed application or Explorer operation is involved.

```powershell
dotnet run --project proprietary/tests/ContextSuite.Audio.ContractTests/ContextSuite.Audio.ContractTests.csproj -c Release -- `
  --aiff-inputs '<expanded engine bin>' '<native expanded-input fixture directory>' `
  '<new evidence directory>' '<pinned supplier bin>'
```

Run `.\tools\Test-Foundation.ps1 -Configuration Release` for public framing,
policy, consent and existing foundation contracts. The main private audio test
mode retains the existing six-format regression suite.

The focused run at `.codex-temp/aiff-adapter-b48f9ffd3e9f496692b44b222613a3bd`
passes **366 checks with 68 outputs**. Twenty-five native AIFF/AIFF-C fixtures
convert to WAV and FLAC. A one-second authored AIFF converts to all six targets
as plain audio, with legacy text, and with Unicode ID3 plus an unlabelled PNG.
Lossless comparisons are exact; precision-reduced FLAC retains its existing
sample-error limit and the tone's lossy outputs meet the declared RMS bound.
All sample counts, tested text values, artwork RGBA pixels, original hashes and
timestamps pass. Unknown chunks, missing compressed-source consent and premature
ASF admission are refused; owned conversion scratch is cleaned.

The initial focused run at
`.codex-temp/aiff-adapter-911932213ebd4c39b48d4241028e00d9` is retained as failed
test development: its labelled cover correctly could not fit M4A, and its
negative-fixture handler did not accept a native malformed-chunk refusal.
The final positive cover is representable in all six targets; production picture
preservation restrictions were not relaxed. The test now retains progress
receipts before later negative checks.

The full public suite passes **4,233 foundation contracts** in
`.codex-temp/aiff-foundation-b9172336a7b84b43b33058a6770904fa.log`, including 103
new AIFF contracts. The first redirected PowerShell run terminated on expected
native stderr and is not a pass. A subsequent run exposed a transient sharing
violation reading an existing Office test's atomically renamed readiness file.
The reader now retries only Windows sharing/lock violations within its existing
45-second deadline while checking child liveness; malformed JSON and other I/O
errors still fail. The final complete suite passes with that correction.

The existing selected-engine audio suite also passes **368 combined probe and
encoding checks**, with supplier comparisons, at
`.codex-temp/aiff-existing-audio-7d5c19a19135445186f689e5decf1bb1`.
The managed Release build has zero warnings/errors. Public source-boundary,
system-theme, whitespace and 175-document checks pass.

Current native engine identities and source provenance remain in
[the evaluation record](audio-expanded-input-evaluation.md). These results do
not verify context-menu dispatch, worker publication/recovery, installed behavior,
listening quality or commercial release readiness.
