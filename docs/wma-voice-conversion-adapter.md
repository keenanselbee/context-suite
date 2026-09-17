Windows Voice Conversion Adapter
================================

Status: isolated private integration; customer/worker admission and packaging
remain pending, 2026-09-17. The optional provider follows the
[verified Windows Voice process boundary](wma-voice-windows-evaluation.md).
It does not repair or use FFmpeg's failing Voice decoder.


Provider and conversion boundary
--------------------------------

`AudioEncodingAdapter` accepts an explicit optional Windows Voice directory.
The provider requires Windows 11 x64 and retains verified directory and read-only
file leases on the owned helper. Its selected SHA-256 is
`2A7B3FE9AB55A8A6AB260246FA53DD19756195A28C1FBF875114BC35B4875904`.
The helper uses the operating system's Media Foundation/Voice codec. Windows DLLs
are not copied into the payload or pinned to one host's hashes. No fallback,
download, registration or automatic component installation is performed.
Missing or altered helpers cannot enable Voice input. Missing Windows media
component behavior and specific availability guidance still require verification.

The initial provider admits mono PCM16-decoded Voice at 8, 11.025, 16 and
22.05 kHz. It retains the native evaluation's 16 MiB input and 262,144-frame
decoded limit; these are temporary integration bounds, not the final launch
duration budget. The broader file-support goal is unchanged.

Before decoding, the complete bounded ASF object inventory must pass protection,
stream, metadata and object-extent checks. The provider inherits the owned
snapshot handle through the existing suspended-start/explicit-handle-list/job
launcher. It validates the returned typed header, complete sample extent and
exact finite PCM16 values before creating an owned float64 WAV transport.
Float32-to-float64 promotion preserves these values exactly.

The existing encoding path consumes that transport while retaining the original
ASF facts, tags and artwork as the preservation contract. The six output recipes,
lossy-transcoding/precision/resampling prompts, output validation, source hashes,
caller-position restoration and cleanup remain in force. WAV/FLAC preserve the
tested decoded Voice samples. The fixed 24-bit FLAC recipe exactly represents
the decoder's PCM16 values and therefore needs no precision-reduction prompt.
Converting lossy Voice to a lossless container
does not restore the original recording. Opus retains its explicit 48 kHz
resampling choice and finite-source sample-count checks.


ASF and M4A metadata
--------------------

The ASF inventory now parses single-stream bitrate properties, the fixed
extended-stream form, and a bounded associated language list. It checks stream
references, reserved flags, field extents and language indexes. Stream names,
payload extension systems, embedded alternate streams and unsupported objects
remain refused rather than skipped. Language becomes a descriptive tag. Typed
`IsVBR` and `MediaFoundationVersion` values are validated as source-encoder
provenance, consistent with the existing encoder-tag policy; a new encoding owns
its own bitrate mode and encoder version.

These fields follow the retained Microsoft
[ASF specification](https://download.microsoft.com/download/7/9/0/790fecaa-f64a-4a5e-a430-0bccdab3f1b4/ASF_Specification.doc)
and [header-object definitions](https://learn.microsoft.com/en-us/windows/win32/medfound/media-foundation-attributes-for-asf-header-objects).
Bitrate properties describe encoding, while the associated stream language
describes content. Packet/timeline fidelity beyond the declared corpus remains
separate acceptance work.

The encoder's ordinary M4A metadata mapping drops regional language identifiers
such as `en-us`. An owned reconciliation now stores the complete identifier in a
strictly scoped `com.apple.iTunes` freeform `language` atom and adjusts chunk
offsets without changing encoded media bytes. The managed inventory and native
probe must recover it. A complete identifier may refine the media header's
`und` (undetermined) value; conflicting known languages and duplicate freeform
values remain refused. Unknown freeform tags are not generally admitted.


Verification and remaining work
--------------------------------

Reproduce with the pinned expanded encoder, inherited-handle Voice helper and
authored 95-case system corpus described in the linked evaluation:

```powershell
dotnet run --project proprietary/tests/ContextSuite.Audio.ContractTests/ContextSuite.Audio.ContractTests.csproj -c Release -- `
  --wma-voice-convert '<expanded engine bin>' '<Voice helper directory>' `
  '<system corpus>' '<new evidence directory>' '<pinned supplier bin>'
```

The supplier decoder is used for artwork pixels; the Windows sample reference
uses the same Windows Voice codec as the provider. Neither comparison replaces
independent codec or listening acceptance.

Initial evidence `wma-voice-convert-2003490fc53e4a8894351a481098aa93` passes
324 checks and 216 outputs: 36 Voice fixtures across all six targets, including
one-sample clips and 480/960-frame boundaries. WAV and FLAC samples exactly match
the retained Windows output. Final evidence
`wma-voice-convert-f737c5abb29c43a99420b2b00e07ecad` passes **339 checks with 222
outputs**, adding all six targets with Unicode text, regional language and exact
artwork pixels, absent/altered helper refusal, read-lease mutation protection,
required quality consent, pre-cancellation and cleanup. These are candidate files,
not application publication receipts.

Review then removed an unnecessary FLAC precision acknowledgement: the selected
decoder's PCM16 values fit the fixed 24-bit FLAC output exactly. The focused
ASF/policy suite passes **114 checks** after this correction; the earlier full
foundation result below precedes that final policy-only change.
Final replay `wma-voice-convert-585b1f5cb9cb4f1e907d31a64a33bf5f` again passes
**339 checks with 222 retained outputs**, including exact FLAC conversion with no
precision acknowledgement. The prior `f737c5abb29c43a99420b2b00e07ecad` result
is retained as the pre-review policy evidence.

The full public suite passes **4,363 foundation contracts**, including the new
ASF stream/language/provenance and M4A language malformed/conflict cases. The
managed Release build has zero warnings/errors. Existing WMA v1/v2 and AIFF
regressions pass **283/36 outputs** and **365/68 outputs** respectively in
`voice-adapter-regression-1a9fb67f53764441852715624574357c`. Existing audio passes
**368 checks** in `voice-existing-audio-e37e2064d06e48cb917f313217f54d69`.

Retained failures: `1d638cfa70a44ec68438cee814e428ca` identifies the previously
unsupported Windows ASF header fields; `cb5b4968a6494ec7abbfcaa2d2cf668c` requests
an unavailable float32 diagnostic output from the restricted engine; that check
now uses its existing float64 output with exact reference promotion.
`fd00ae2a3d2c411398f678355d39841a` correctly refuses missing M4A language before
the preservation repair. `194242b1b9044bb589d5d83e434c8f9a` repeats all 216 plain
outputs but requests unavailable artwork extraction from the restricted engine;
the expanded test now selects the already-pinned supplier used by existing tests.

Remaining work includes native availability diagnostics, broader duration/resource
and real speech/music/mixed-mode coverage, hostile/discontinuous packet timelines,
actual worker/direct-command publication and recovery, provider packaging,
supported Windows environments, listening and combined release acceptance.
The existing 1.1.1 stage and installed application are unchanged.
