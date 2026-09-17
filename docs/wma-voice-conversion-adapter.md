Windows Voice Conversion Adapter
================================

Status: isolated private integration; customer admission and formal packaging
remain pending, 2026-09-17. The optional provider follows the
[verified Windows Voice process boundary](wma-voice-windows-evaluation.md).
It does not repair or use FFmpeg's failing Voice decoder. The later
[expanded worker checkpoint](expanded-audio-worker-verification.md) composes this
provider for actual worker admission and copy publication. Direct-command and
expanded-input interruption/recovery acceptance remain separate gates.


Provider and conversion boundary
--------------------------------

`AudioEncodingAdapter` accepts an explicit optional Windows Voice directory.
The provider requires Windows 11 x64 and retains verified directory and read-only
file leases on the owned helper. Its selected SHA-256 is
`B03029719B338363193C6111A204B144A23EF52947301D6CA5493267C3D0DBA5`.
The helper uses the operating system's Media Foundation/Voice codec. Windows DLLs
are not copied into the payload or pinned to one host's hashes. No fallback,
download, registration or automatic component installation is performed.
Missing or altered helpers cannot enable Voice input. Before a Voice plan is
returned, the pinned helper now starts Media Foundation and explicitly creates
the Windows Voice decoder under the existing parent-owned job. This availability
query does not parse media. Its exact versioned reply is bounded to 64 bytes and
15 seconds; only success is cached for the provider instance. A later decode
still checks its own initialization and exit status.

Media Foundation/Voice initialization failures return helper exit code 3.
That code and the Windows missing-DLL, missing-entry-point and DLL-initialization
exit statuses produce: "Windows media support for WMA Voice could not start.
Check Windows media components and try again. Originals were kept." Other
decoder errors stay distinct. The process launcher preserves cancellation and
output-limit failures ahead of exit-status mapping. Actual missing-component
Windows environments still require acceptance; injected statuses do not prove
that environment behavior.

The initial provider admits mono PCM16-decoded Voice at 8, 11.025, 16 and
22.05 kHz. The later [streaming revision](wma-voice-streaming-verification.md)
replaces the short evaluation bounds with the shared 512 MiB input and 1 GiB
decoded float64 budget. It verifies five-minute generated speech at all four
rates and all six targets; broader resource and media acceptance remain open.

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

The availability revision passes **25 focused availability/launcher checks** in
`wma-voice-availability-3f7553aca7094c1897ccaa624c1598f3`: real helper startup
with an empty input, cached success, cancellation/disposal, recovery after a
failed launch, injected child exit statuses, output limits and active cancellation.
No Windows components or registrations were changed. Reproduce with the same
private test host using `--wma-voice-availability '<helper directory>' '<new evidence>'`.
Native replay `wma-voice-mft-050b5eae61fd4e7392935ef62e987339` passes all
**48 path checks**; source SHA-256 is
`B0F6363D15C9137F9D575808748D122DFB136A6C83D4EA964BBE64BA6A5B6620`.
Process replay `wma-voice-process-d00d46ce94e049dcbdb24b40b6225661` passes
all **52 inherited-handle/ownership/recovery checks** with this helper.
The six-target replay `wma-voice-convert-d70c62edb20644c5bccb630a76ba90e6`
passes **339 checks with 222 outputs** after adding preflight. The initial
`ae6bcd4a13574d63967386fe5fc06bfb` invocation stopped before conversion because
it supplied relative runtime paths; the corrected run uses the required absolute
paths. The managed Release build passes with zero warnings/errors.
Existing audio replay
`voice-availability-audio-regression-4f94d0eebe5c4b149bfcadcf2c7d847d` passes
**368 probe/encoding checks**, including the shared launcher. Public foundation,
WMA v1/v2 and AIFF matrices above were not rerun for this availability-only change.

The later streaming revision records 55 focused checks and 24 five-minute speech
conversions, plus a retained continuous-tone/Opus refusal. See its linked evidence
for the refreshed helper identity, timings and unchanged fidelity thresholds.

Remaining work includes missing-component environment acceptance, broader duration/resource
and real speech/music/mixed-mode coverage, hostile/discontinuous packet timelines,
direct-command and expanded-input interruption/recovery acceptance, provider packaging,
supported Windows environments, listening and combined release acceptance.
The existing 1.1.1 stage and installed application are unchanged.
