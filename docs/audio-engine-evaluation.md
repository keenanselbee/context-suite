Audio Engine Evaluation Results
===============================

Date: 2026-09-09. Status: generated-fixture evaluation and private integration
candidates; no audio transformation menu commands or shipping dependency added.

Provenance and reproduction
---------------------------

The [evaluation tools](../tools/audio-engine/README.md) acquired the BtbN
`autobuild-2026-09-09-14-51` Windows x64 shared LGPL build,
`n9.0.1-27-g9b0578816c`. FFmpeg links this supplier on its
[download page](https://ffmpeg.org/download.html); the supplier documents its
[build variants and retention policy](https://github.com/BtbN/FFmpeg-Builds).
The archive digest is pinned in [evaluation.json](../tools/audio-engine/evaluation.json)
and was verified before execution. No reference binary was used.

| Item | Observed value |
| --- | --- |
| Archive SHA-256 | `575642722B7C7079C6855C166ED1EA9D5B7451CDF70919871229E4E1EBE24A34` |
| ffmpeg executable SHA-256 | `E94AFE21C9ECBA51A43B0EF7874386253E5EFD47E8902E783373B5CC74920A19` |
| ffprobe executable SHA-256 | `4A9D4A879B5F345005141E73543D98525C8FB0A2A14F00877AFEA3CBC9AB4BAB` |
| Archive size | 76,400,218 bytes |
| Full unpacked supplier payload | 224 files; 188,077,387 bytes |
| Supplier bin directory | 173,445,632 bytes, including ffplay and non-audio components |
| Build licensing evidence | Included LGPL version 3 text; build enables version3 and shared libraries, disables libfdk-aac/libx264/libx265 |

These sizes are the uncurated evaluation payload, not proposed installer size.
The supplied configuration includes video libraries and network-capable components
outside the desired audio boundary. Source/dependency archival, redistribution
review, capability reduction and production isolation remain required.

Retained evidence is under:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  upstream.zip
  evaluation.json
  inventory.json
  matrix-1aed7d0a6e644d999646e12e232c1533/  # initial metadata-loss discovery
  matrix-bf57b1dafd444a63b8704a885a416cc4/  # corrected mapping and optimization canary
```

A prior `matrix-1635311cd3f842b697102cb0967d525f` attempt stopped before the matrix
because the managed process API rejected post-exit memory measurement. The harness
now uses the retained native process handle and records null if memory measurement
is unavailable. This failed harness attempt is not media acceptance evidence.

Six-by-six conversion matrix
-----------------------------

All inputs derive from an independently authored two-second, 48 kHz stereo PCM16
tone/chirp fixture. They contain title, artist, album and comment tags. There is
no music, speech, third-party asset, artwork or customer file in this corpus.

Each cell below passes the tested codec, rate, channels, decoded frame count,
gross signal-difference, four-tag preservation and original-hash checks. Every
output decoded to 96,000 stereo frames in the final run.

| Input / output | WAVE float32 | FLAC | MP3 | M4A/AAC | Ogg Vorbis | Opus |
| --- | --- | --- | --- | --- | --- | --- |
| PCM16 WAVE | Exact | Exact | Lossy | Lossy | Lossy | Lossy |
| FLAC | Exact | Exact | Lossy | Lossy | Lossy | Lossy |
| MP3 | Exact decoded samples | Precision decision | Lossy again | Lossy again | Lossy again | Lossy again |
| M4A/AAC | Exact decoded samples | Precision decision | Lossy again | Lossy again | Lossy again | Lossy again |
| Ogg Vorbis | Exact decoded samples | Precision decision | Lossy again | Lossy again | Lossy again | Lossy again |
| Opus | Exact decoded samples | Precision decision | Lossy again | Lossy again | Lossy again | Lossy again |

"Exact decoded samples" describes the engine's decoded float representation;
it does not restore quality lost in the input encoding. FLAC from these lossy
inputs used PCM24 and introduced measured quantization: maximum sample differences
were about 1.19e-7. This is a required precision-policy decision, not permission
to claim a sample-exact conversion. The 16 lossy-to-lossy cells need the existing
focused consent policy before customer use.

Candidate settings were LAME VBR quality 2, AAC-LC 192 kb/s, libvorbis quality 5,
libopus music 160 kb/s VBR, and FLAC compression 8. These are trial settings,
not listening-reviewed commercial presets. WAVE output used float32 to avoid
silently truncating a lossy decoder's float samples.

Per-encode process wall time was 48-166 ms; observed maximum process peak working
set was 29,745,152 bytes on this machine. This excludes separate probe/decode work
and is not a large-file or cold-start performance guarantee. Full arguments,
input/output hashes and measurements are in the final `matrix.json`.

Metadata discovery and correction
---------------------------------

The first run passed decoding but lost all four tested tags in eight pairs:
Ogg Vorbis or Opus inputs exported to WAVE, FLAC, MP3 or M4A. Those input tags
live at stream scope, so a global-only metadata map was insufficient. The harness
now selects the audio-stream map for those inputs. The final 36 pairs preserve
all four tested tags; metadata losses now fail the matrix checks.

This does not establish arbitrary tag, duplicate-value, artwork, chapters,
cuesheet, replay-gain, loop, application-block or multistream preservation.
The production adapter needs an explicit metadata model and validation policy;
it must not merely copy this four-tag experiment's command arguments.

FLAC optimization finding
--------------------------

An authored FLAC application block was inserted into a compression-level-0
fixture. Re-encoding at level 8 reduced size from 147,156 to 70,712 bytes and
preserved the decoded samples exactly, but dropped that block. Therefore this
raw engine operation fails the required optimization-preservation gate.

The adapter must inventory required blocks and either preserve them correctly
or decline the optimization. Seek offsets, cuesheets, unknown application data,
embedded artwork and integrity fields need separate treatment. A smaller file,
successful exit and exact PCM comparison together are still insufficient.

Implemented metadata reconciliation
-----------------------------------

The public core now contains a bounded native-FLAC metadata inventory and header
reconciler in [FlacMetadata.cs](../src/ContextSuite.Core/Audio/FlacMetadata.cs).
It retains original non-padding metadata bytes and uses the encoder's STREAMINFO
for new frame-size/checksum declarations. Source rate/channels/precision and known
sample count/checksum must agree. Comments, pictures and cuesheets retain their
original order and bytes; padding may be omitted. A separate comparison catches
metadata changes. This parses block framing and minimum lengths, not all embedded
picture/comment/cuesheet semantics, and does not replace full audio validation.

Parsing is bounded to 256 blocks and a 32 MiB metadata prefix, with no file I/O,
decompression or allocation based on an unchecked declared length. Application
blocks, reserved/unknown blocks and seek tables currently prevent header
reconciliation: unknown application semantics and changed frame offsets require
additional handlers. These are open implementation gaps, not dropped launch work.
The rules follow the independent implementation of
[FLAC metadata framing and seeking](https://www.rfc-editor.org/rfc/rfc9639.html#section-8).

`Test-AudioEvaluation.ps1 -PreservationOnly` passed against the same pinned
engine. The generated PCM16 stereo fixture shrank from 147,039 to 62,411 bytes,
with exact decoded samples, exact original comment/vendor bytes and an unchanged
source hash. The encoder's raw metadata rewrite was rejected before reconciliation;
an application-block canary was refused. Evidence is retained at:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  matrix-22f90351d4034c96acf7aa93d1cdc5bf/preservation.json
```

The foundation suite now passes 919 contracts, including 25 preservation checks
for declarations, metadata ordering, unsafe block types, duplicate/truncated
headers, record budgets and stale metadata detection. The generated real-engine
test validates comment bytes; its result does not claim real artwork/cuesheet
acceptance, publication recovery or a production audio command.

Remaining acceptance
---------------------

The private probe component and public typed parser now have an isolated adapter
test, separate from the earlier CLI matrix. Thirteen checks passed on the six
generated files, including M4A through stdin, pre-cancellation, input bounds,
an engine-file write lease, disposal, playlist rejection with no observed loopback
connection, and suppression of inherited `FFREPORT` file logging. Evidence:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  adapter-4fd757df3bef45338e16bd99b45fb73e/probe-adapter.json
```

This candidate hashes and holds read leases on ffprobe and seven supporting
DLLs, starts with no media path, assigns the child to the
existing kill-on-close Windows job, and only then sends input bytes. The job's
existing process-memory limit is 1 GiB. Input is capped at 64 MiB, stdout JSON at
1 MiB, diagnostics at 64 KiB, and runtime at 15 seconds. The command allows only
pipe protocol and WAVE/FLAC/MP3/MOV/Ogg demuxers. Process argument construction
is fixed; no customer-supplied engine arguments are accepted.

The public parser limits JSON depth to 16, streams to 32, tags per scope to 128,
tag values to 4,096 characters, and numeric/string fields explicitly. It retains
format tags separately from stream tags and unknown values as null. Stream
identification does not grant transformation permission. The foundation suite now
passes 935 contracts, including 16 new parser checks.

Pipe probing provided duration for the generated FLAC and M4A, but left duration
unknown for WAVE, MP3, Vorbis and Opus. WAVE header facts can complement this;
accurate general duration and seeking need further work. Success probing M4A
over a pipe does not prove that all M4A layouts can be converted without seeking.
The tested loopback rejection is not proof of an OS network sandbox or universal
external-resource safety. Native dependency loading, hostile supported containers,
mid-operation cancellation and crash ownership still need dedicated acceptance.
The component is now registered by the worker and optionally used by Analyze;
the evaluation payload has not been added to normal production packaging.

Integrated Analyze evidence
----------------------------

The `audio-probe` message accepts only a byte snapshot capped at 1 MiB; all other
command payload combinations are rejected. Public reply validation bounds typed
facts and aggregate tag text. The worker retains verified engine-file leases and
uses the existing sequential request loop. It receives no media path for probing.

When `audio-engine/ffprobe.exe` is present beside the selected worker, Analyze
attempts deeper probing for recognized audio hints/signatures. The application
retains the same source read lease, reads at most a 1 MiB prefix, checks file
stability after probing, and adds container, codec, channel/rate/precision,
reported duration and tag-count facts to the existing collapsed details. Missing
properties stay unavailable; malformed media, process failures or unavailable
engines retain the basic header report. Cancellation still cancels the operation.
No trial/paid access call or output publication is introduced.

Eleven isolated application-to-worker checks passed for all six generated formats,
malformed audio followed by an unknown file, source hashes, read-only results and
a throwing access service. Evidence is retained under:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  worker-ca36f10464e54342bc58ce51df3ccd15/results/analysis-results.txt
```

The base Release stage is
`artifacts/production-staging/fc4ff3b5a3444e689cec962ba454f999` and built with zero
warnings/errors using `-SkipShell`. Its normal payload allowlist and dependency
checks passed. The worker experiment copied this stage into scratch and added
the pinned evaluation engine there; it did not modify the base inventory or an
installed application. This is managed staging evidence, not fresh native shell
or release packaging acceptance. A test-harness array-type compilation error in
the first attempt was corrected before the passing run.

That integration step passed 944 foundation contracts, including prefix/lease/fallback
and protocol checks. All 76 hidden view contracts also pass. These checks do not
prove visible layout, keyboard or screen-reader acceptance of the new audio facts.

Remaining work
---------------

- Evaluate PCM24/32 and floating point, mono/multichannel layouts, 44.1/96 kHz,
  clipping, non-finite samples, gapless boundaries and decoder-delay variants.
- Add real representative music/speech with explicit fixture rights and human
  listening; the current error threshold detects gross corruption only.
- Add full metadata/artwork, malformed inputs, multiple streams, large files,
  cancellation, process/resource/network isolation and deterministic failures.
- Select and package an intentionally bounded engine build with complete source,
  dependency and notice provenance, then implement worker/probe/validation contracts.
- Add application-owned publication, access rules, safe overwrite handling and
  simple context-menu actions; perform isolated workflow and visible acceptance.

The earlier 36-pair run used the then-current 894 foundation baseline; the new
preservation code initially had 919 passing foundation contracts; typed audio
probing and integration raised that total to 944. The 76 hidden view checks
were rerun for integration and retain their nonvisual scope. No engine result proves document
actions, installed-shell behavior, accessibility or commercial release readiness.

Typed conversion and private encoding milestone
------------------------------------------------

The [fixed policy](audio-conversion-policy.md) and private `AudioEncodingAdapter`
now cover generated conversions and FLAC recompression. These are candidate
components; they do not register worker transformation commands or authorize
application publication. Missing metadata and independent resampling fidelity
remain explicit limitations rather than successful publication receipts.

The final run passed **70 combined checks**: the original 13 probe checks, 36
format pairs (30 encodes and six same-format unchanged results), nine wider
precision/rate/layout cases, four FLAC optimization/cleanup checks, four encoder
consent/lifetime checks, and four native-launcher checks. Evidence:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  adapter-97c3b2ed549845569d54893b68c0ef36/encoding-adapter.json
```

All converted two-second matrix inputs retained 96,000 decoded stereo frames,
their tested tags and original file hashes. The wider authored fixtures cover
PCM8/16/24/32, float32/64, 96 kHz 24-bit 5.1, and 44.1/8 kHz mono to Opus.
Signed integer fixtures include full-scale minimum/maximum values. Floating-to-
FLAC is an acknowledged 24-bit quantization, not an exact result. Resampling has
explicit consent and frame/rate checks; time-aligned signal/listening comparison
was still missing at this milestone (see the streaming follow-up below). These fixtures contain generated signals, not licensed music
or speech, and do not establish listening or broad player compatibility.

The owned child fixture proves seekable input, native write denial on that input,
job membership before its work, bounded output rejection, and cancellation after
the child signals that it is running. The launcher terminates and awaits it.
This tests the launcher with an authored child, not every native FFmpeg failure
phase or an OS sandbox. Engine file leases and conversion/optimization scratch
cleanup are also checked. Both success and refusal keep customer paths out of
native input arguments.

FLAC optimization reconciled original metadata bytes and preserved exact decoded
samples while reducing the padded fixture from **332,860 to 62,516 bytes**.
A second pass never enlarged the result. An application-block canary was refused.
Seek-table rebuilding, application metadata handlers, real artwork/cuesheets and
publication/recovery remain required work.

Failed iterations retained actionable evidence:

- `adapter-5c5e15f75f0646999bb2a06f6e121d37`: pipe-based MP3 decoding failed the
  exact frame-count check; the candidate now uses seekable inherited handles.
- `adapter-28715a094b9846169776d4eeaa50bf13`: M4A stream language was absent from
  WAVE; explicit non-conflicting descriptive tags are now also mapped globally.
- `adapter-c17d6b27c51246d1a04352616b9a6b09`: PCM8 expected an 8-bit FLAC stream;
  this encoder stores it sample-exactly as PCM16, now explicit in the plan.
- `adapter-d73758e4248a4167a73cbf7a2541a445`: PCM32 was silently encoded as PCM24;
  the 32-bit-only encoder option and exact-sample/precision checks address it.
- `adapter-edea3b4c41d842378ea7ecb17774ef6d`: low-rate Opus output decoded at 48 kHz;
  every non-48-kHz source now requires explicit resampling acknowledgement.

The first redirected foundation rerun stopped because PowerShell treated an
expected router diagnostic as an error. The process was confirmed absent before
rerunning through the documented script without merging stderr. It is not counted
as a passed run. Current foundation/build totals are recorded in the active goal.

Streaming file candidate follow-up
----------------------------------

The file API now avoids complete encoded/decoded media arrays. It owns a bounded
snapshot, captures canonical decoded samples to scratch with fixed-size buffers,
then compares the output incrementally. A successful result holds a read lease
and source/output digests until disposed. The small-fixture byte API remains for
compatibility. See the [current bounds](audio-conversion-policy.md).

The follow-up passed **77 combined audio checks**, including seven new file
contracts. Evidence:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  adapter-339c4af469b64e49abfd1763c3d342ff/encoding-adapter.json
```

An independently generated five-minute PCM24 stereo WAVE (86,400,044 bytes)
converted to FLAC (36,984,654 bytes) and back to WAVE (86,400,102 bytes), retaining
all 14,400,000 decoded frames exactly. Each decoded reference was 230,400,000
bytes, exceeding the former 128 MiB array limit. Both operations together took
6,399 ms and allocated 1,980,256 managed bytes in this run. This is measured
managed allocation, not native peak memory or a general performance guarantee.
Returned artifacts excluded writers; disposal removed their owned scratch.

Cancellation was requested after a filesystem notification exposed the decoded
reference. Windows exposed its full 230,400,000 bytes in this run, so this proves
cancellation during a running multi-phase operation, not interruption mid-decode.
Cleanup and the original source digest/stream position passed. The earlier
`adapter-eb4d545b58ce4b5a9965d423911408e5` attempt exposed a startup race: cancellation
could kill a suspended child before job assignment, yielding access denied.
The launcher now establishes job ownership before registering cancellation and
reports cancellation if it races native resume. Pipe consumers are awaited even
when resume fails, before their streams and reference file are disposed.

Resampled Opus output now has time-aligned maximum/RMS error measurements against
a reference at the planned rate. The shared engine is not an independent fidelity
oracle. Seventeen new public sample contracts cover fragmented reads, late sample
differences, finite samples, framing, bounds, unequal lengths and cancellation.
The first public rerun revealed that its rejection helper did not catch
`InvalidDataException`; the helper was corrected before 1,081 contracts passed.
Audio metadata admission, worker/publication integration, hostile-file and crash
coverage, representative listening and payload adoption remain open.

Embedded FLAC artwork and descriptive metadata
----------------------------------------------

The next run passed **85 combined audio checks**, adding eight real-engine
artwork/preservation checks. Evidence:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  adapter-b662a7860d534fbba2b24146ff436f50/encoding-adapter.json
```

An authored FLAC with front/back PNG covers, alpha, Unicode descriptions, duplicate
artist fields, multiline lyrics, ReplayGain text and padding shrank from
**324,903 to 62,755 bytes**. All 96,000 audio frames remained exact; original
vendor/comment/picture block bytes and order were retained. Each embedded cover
decoded to its authored RGBA bytes both before and after optimization. These are
two tiny generated images, not broad artwork codec/color-profile acceptance.
The original digest and scratch cleanup also passed on successful and refused work.

Cross-format artwork conversion still refuses pending a preservation path.
Linked artwork is rejected before native probing; malformed descriptive UTF-8
is rejected. The earlier `adapter-1e1b9684f99141a68f2255656b833a62` attempt found
multiline lyrics entering command-line tag mapping. FLAC recompression now restores
original raw blocks without that unnecessary mapping. This does not resolve
multiline/duplicate metadata transport across other audio containers.

Twenty-seven public description, disposition, plan and report checks bring the
foundation total to **1,108**. A fresh isolated Release stage at
`artifacts/production-staging/d3991827a072434389d91b1de6556aa5` built with zero
warnings/errors and passed its normal inventory/dependency/notices checks using
`-SkipShell`. Normal staging still excludes the evaluation audio payload.

Adding that payload to a separate scratch copy passed **14 actual worker/Analyze
checks**, including attached-picture IPC and explicit embedded-artwork report facts:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  worker-8b7dd6867cc245b99d3a03415f1ea6ef/results/analysis-results.txt
```

The read-only flow retained original hashes, avoided licensing/publication and
continued after malformed audio. No new visible layout, screen-reader, theme/DPI,
installed-shell or customer audio-operation acceptance is implied.

FLAC seek-table rebuilding
-------------------------

The seek-table slice passed **104 combined private audio checks**: the prior 85,
11 bounded packet-index parsing checks and eight real seek/preservation checks.
Evidence:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  adapter-fdccc93acbbd460ab6e2276ec20a8f42/encoding-adapter.json
```

The generated level-0/1,024-sample-block source carries 34 seek slots and padding.
Optimization reduced **411,760 to 79,986 bytes**, retaining exact decoded samples
and descriptive metadata. Both streams had 94 frames; their byte offsets changed.
A fresh output packet index and frame CRC scan agreed with every rebuilt point.
Seeked 50 ms output segments at samples 0, 6,000, 24,000 and 86,400 matched the
same segments of a complete linear source decode. Stale source offsets and a
corrupted frame checksum were refused; original hash and scratch cleanup passed.

The first run (`adapter-73f20a80e33e47778b1406da3ee78ac8`) used an already identically
encoded fixture. Its assertion correctly failed because the table did not change.
The fixture was strengthened with different source compression; production encoding
was not changed to satisfy the test. Native changed-frame-boundary and long-file
seeking are still unverified. Synthetic contracts exercise changed boundaries,
coalesced points/placeholders, ordering, frame gaps/overlaps, extents and bounds.
Twenty-two new public checks bring the foundation total to **1,130**. An initial
test compilation error from target-typed construction in a params call was fixed
before the passing run.

These checks use the pinned FFmpeg decoder and frame parser. Managed CRC/extent
validation adds checks but is not an independent decoder, sandbox or complete
FLAC conformance proof. Publication, recovery, remaining metadata, customer UI
and production payload adoption remain open.

Fresh isolated Release stage
`artifacts/production-staging/92679db2410b4025882f7324faefb3cc` built with zero
warnings/errors and passed the normal curated identities, allowlist, dependency
and notice checks with `-SkipShell`. No new UI or installed-shell acceptance was
run for this internal seek-table slice; normal packaging still excludes the
evaluation audio engine.

FLAC worker, batch admission and publication (2026-09-10)
--------------------------------------------------------

The actual worker and application executor passed **14 isolated FLAC workflow
checks** with a scratch copy of Release stage
`artifacts/production-staging/f4b4f20e6cb84fcab29d06168d7478a7` plus the pinned
evaluation engine. The preceding **11 worker/Analyze checks** also passed.
Evidence is retained at:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  worker-c4e98ce8a3f0424fb82559092d42f199/flac-results/flac-workflow.json
  worker-c4e98ce8a3f0424fb82559092d42f199/results/analysis-results.txt
```

Generated padded sources shrank from **332,860 to 62,516 bytes** with preserved
original hashes and descriptive metadata. One shared worker handled both files
under one actual local-trial admission, including expiry after the first output.
New expired work produced no reservation. A pre-existing output canary remained
unchanged and the publisher chose the expected numbered name. Re-optimizing the
compressed output returned no smaller result without creating another file.

Changed source identity was rejected. A corrupted frame failed while the next
valid file completed in the same worker. Cancellation after the first completed
file retained that output and skipped the second. Nonempty and hard-linked
reservations refused writes. Publication records and temporary reservations were
closed after the tested successes, failures, no-change and cancellation. The
test recycler always refuses: no native recycling or overwrite acceptance is
claimed. This does not cover crash injection, cancellation at every publication
phase, long-file worker limits or customer UI.

All FLAC optimizations now require complete source/output frame indexes and CRCs,
including files without seek tables, to prevent unnoticed trailing/unindexed
audio bytes from being omitted. **104 private audio checks** passed again at:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  adapter-af648a64fbde4ded85cdb353ca63c80c/encoding-adapter.json
```

Shared output-handle validation retained **942 passing image-engine contracts**
at `.codex-temp/image-tests/engine-dc2415ab5f47443ca808799b057aaabb`.
The final **1,154 foundation contracts** include 24 new FLAC plan/IPC/trial/paid
checks. An initial integration-harness build lacked the executor source link;
the test project was corrected before the passing workflow. Source-facts
comparison uses typed values across IPC rather than dictionary serialization order.

Final isolated Release stage
`artifacts/production-staging/182fd3478db94d7d82852321125e02c6` built with zero
warnings/errors and passed curated identities, allowlist, dependencies and notices
using `-SkipShell`. It includes the final defensive malformed-plan checks, verified
by the foundation run. Evaluation audio binaries remain excluded from normal
packaging. No customer audio command, visible acceptance, install/registration,
live Polar, listening acceptance or release clearance is implied.

Direct FLAC and mixed-family dispatch (2026-09-10)
-------------------------------------------------

**16 isolated direct-audio checks** now pass through `MainViewModel.Admit`, the
actual queue, worker client and transactional publisher. Existing Auto/Lossless
actions dispatch FLAC; Balanced/Smallest explain their PNG-only scope. The run
used the prior verified scratch worker payload (worker code was unchanged) and
current application sources. Evidence:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  direct-eeaa430a6cc34163a65866aa9a3f2cad/direct-audio.json
```

The mixed PNG/FLAC batch used one real local-trial admission and completed both
formats after expiry was advanced on PNG completion. Result formats, one quick
completion, aggregate savings and quiet-state policy passed. Separate synthetic
access tests covered activation/retry, deactivation, reactivation, duplicate retry
clicks and changed saved output folders. Other checks covered corrupt FLAC among
valid files, cancellation between families, missing engines and original hashes.
This is component workflow evidence, not operation of the visible License window.

The first attempt at `direct-3664f845fcc549c7bc30ec8f48783927` intentionally swapped
extensions and reached the publisher's same-extension guard. Dispatch now gives
content-based rename guidance before admission; tests verify those files remain
unchanged. Normal mixed-format fixtures use matching extensions. A test helper's
missing namespace was also corrected before the passing run. These findings did
not weaken publication validation or authorize automatic renaming.

**1,157 foundation contracts** pass, including rejection of another batch's
admission by both executors and header-only routing without optional native probes.
Fresh isolated Release stage
`artifacts/production-staging/ea440b35199f45cf879be090fcde88c9` passed normal payload
identities, allowlist, dependencies and notices. Native shell binaries were rebuilt
under `.codex-temp/native-staging-ea440b35199f45cf879be090fcde88c9`; shell COM/host
contracts passed complete three-file selections for all three independent tools.
**13 existing image direct-command checks** passed against that stage at
`.codex-temp/license-workflow/direct-66bba78d13024d8ea077d70b1bbf61d6`.

The earlier 942 image-engine and 104 private-audio checks were not repeated because
private engines were unchanged. The 76 hidden view contracts, visible UI, keyboard,
screen-reader, theme/DPI and installed-shell acceptance were not run for this slice.
Normal packaging excludes the evaluation audio engine. Its production curation,
broader metadata/crash/fidelity coverage, listening and remaining release gates
stay open. No installation, Explorer registration or live commerce was performed.

WAV inventory and conversion admission (2026-09-10)
--------------------------------------------------

**33 new public WAV inventory contracts** bring the foundation total to **1,190**.
They cover INFO after samples, native-tag disagreement, cue/loop/broadcast/iXML/
ID3/unknown chunks, associated labels, duplicate values, ambiguous encoding and
controls, padding, IEEE float/fact counts, extensible PCM precision/speaker masks,
RIFF/chunk extents, repeated format/data, partial frames, malformed INFO, resource
limits and pre-cancellation. A four-MiB generated sample region is skipped while
the inventory reads fewer than 256 bytes, including INFO after the audio.

The private suite passed **118 combined audio checks**. Fourteen new cases verify
the metadata-coverage flag, admitted tags and decoded frames across all six target
recipes, refusal of cue/loop/broadcast/iXML/unmapped/duplicate/ambiguous metadata,
source hashes, trailing-byte rejection and scratch cleanup. Evidence:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  adapter-2fc5676707894f39902a80455899ff65/encoding-adapter.json
```

`SourceMetadataVerified` is independent of exact-sample or signal-error validation.
Admitted WAV conversions now require both inventoried source values and output
values to agree. Byte-identical same-format retention and the separately validated
FLAC optimizer set that flag through their own preservation paths. Other
cross-format sources remain candidates with unverified metadata coverage.
This does not enable an audio conversion menu or complete artwork transport,
Unicode/code-page handling, broader metadata or independent listening acceptance.

Fresh isolated Release stage
`artifacts/production-staging/112da8268e674d5fb70efd33cfb6dc4f` built with zero
warnings/errors and passed curated identities, allowlist, dependencies and notices
using `-SkipShell`. Its separate evaluation-engine copy passed **11 worker/Analyze,
14 FLAC workflow and 16 direct-audio checks** at:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  worker-c7c93fe1a45649a799d96adb993aa52c/
```

Normal packaging still excludes evaluation audio binaries. Image-engine, native
shell, hidden/visible UI and installed lifecycle checks were not repeated for this
metadata-only implementation slice. No native recycling or live commerce was used.

FLAC conversion inventory and literal text transport (2026-09-10)
----------------------------------------------------------------

**1,216 foundation contracts** pass, including 25 new FLAC conversion metadata
contracts and one additional WAV multiline case. Cross-format FLAC admission now
examines original metadata blocks and ordered UTF-8 comments rather than relying
on a flattened probe alone. Descriptive values must match both source and output
probes. Unmapped blocks, duplicate/aliased values and chapter/loop/playback-gain
semantics stop conversion. Complete frame indexes/CRCs and present seek tables
must validate before encoding. Raw-preserving optimization is unchanged.

**149 combined private audio checks** pass. The 25 new FLAC cases cover six
baseline targets, Unicode/multiline values in five non-WAV targets (including
same-format retention), ASCII multiline WAV output, comment aliases, unsupported
metadata, corrupt/trailing frames, stale seek offsets, source hashes and scratch
cleanup. Six launcher checks prove literal argument boundaries for empty values,
spaces, quotes, trailing backslashes, Unicode and multiline text, plus refusal of
NUL and oversized native command lines. WAV Unicode text remains an explicit
policy gap; this does not certify arbitrary comments or other input containers.

Native evidence:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  adapter-26f020b6f5ea4c849ef61203bd56b7c1/encoding-adapter.json
  worker-b9fbf0167fa14ed4a6d787d71df8fcf2/
    results/analysis-results.txt
    flac-results/flac-workflow.json
    direct-results/direct-audio.json
```

Fresh isolated Release stage
`artifacts/production-staging/e4cc9bc6df3e48db8afc9231ef8fb046` passed with zero
warnings/errors, curated identities, file allowlist, dependencies and notices.
The evaluation-only worker copy passed 11 Analyze, 14 FLAC workflow and 16 direct
audio checks. The normal stage excludes evaluation audio/PDF engines. This build
used `-SkipShell`; visible UI, native shell, image engine, installer, independent
listening and live commerce were not rerun. Audio conversion worker/menu
integration, remaining metadata handlers, engine adoption and release gates stay open.

Ogg inventory and short Vorbis framing (2026-09-10)
---------------------------------------------------

**1,266 foundation contracts** now pass, including 47 Ogg inventory cases.
The shared UTF-8 comment reader retains ordered values and applies the format's
ASCII name range through 0x7D. FLAC keeps its aggregate comment/picture text
budget and raw optimization behavior. Conversion comments and native probes now
share technical provenance exclusions; copied M4A container labels otherwise
caused false missing-comment failures in the new output validation.

Ogg source and encoded-output admission validates complete page framing/CRCs,
continuation and stream identity, bounded packets, header order and descriptive
values. Native decoding remains responsible for codec setup, audio and pre-skip.
Chained/multiplexed streams, damaged/trailing data, nonzero Opus header gain,
preserved binary comment extensions and unsupported semantic comments stop
conversion. The inventory is bounded, not a general Ogg conformance decoder.

The **200 combined private checks** include **51 new Ogg cases**: six-format
stereo conversions, Unicode/multiline values, ASCII WAV output, malformed and
unsupported metadata, source preservation/cleanup, short Vorbis extents and
six-channel Vorbis/Opus round trips. The final public name-boundary correction
was verified by foundation contracts after this combined native run.

The short six-channel fixture exposed a real framing defect in the pinned
default encode/decode path. Independent local diagnostic packing variants
produced these decoded sample counts:

| Input frames | Default packing | Packet pages |
| --- | --- | --- |
| 1 | 128 | 1 |
| 64 | 128 | 64 |
| 128 | 0 | 128 |
| 129 | 704 | 129 |
| 256 | 128 | 256 |
| 511 | 512 | 511 |
| 1,024 | 896 | 1,024 |
| 9,600 | 9,472 | 9,600 |

The fixed Vorbis recipe now uses `-page_duration 1`; this changes page packing,
not sample validation. A full-scale single-sample impulse (RMS error about 0.424)
and the 9,600-frame fixture with a 535 Hz LFE signal (about 0.199) still exceed
the 0.15 signal-error limit and are refused. A normal bass LFE fixture passes
the separate layout round trip. These are generated numerical checks, not
listening or independent-decoder acceptance. Default packing diagnostics and
measurements remain under `.codex-temp/ogg-trim-boundaries-20260910`; their small
authored harnesses are `.codex-temp/OggTrimProbe.cs` and `OggTrimMeasure.cs`.

Combined native evidence is
`.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/adapter-4e951a3cd59f4713a86d5936c587bb68/encoding-adapter.json`.
The isolated Release build at
`artifacts/production-staging/27fb9e8c75d54f6ab82aa29aca79d4c4` passes with zero
warnings/errors and normal curated identities, file inventory and notices.
It uses `-SkipShell` and excludes evaluation audio/PDF engines. No installation,
Explorer change, native recycling, live commerce or visible acceptance was performed.
The evaluation-only worker copy passed 11 Analyze, 14 FLAC workflow and 16
direct-audio checks. Results are under
`.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/worker-cfbc466b3a42436b8f31170cb2074256/`
in `results/analysis-results.txt`, `flac-results/flac-workflow.json` and
`direct-results/direct-audio.json`. These protect existing app workflows;
they do not implement an audio conversion menu.
MP3/ID3 and M4A source metadata, audio conversion worker/menu admission,
independent fidelity/listening, engine adoption and document actions remain open.

MP3 ID3 inventory and native truncation workaround (2026-09-10)
--------------------------------------------------------------

**1,318 foundation contracts** pass, including 52 new MP3 cases. The bounded
inventory checks ID3v2.3/v2.4 framing, version-specific text encodings, padding,
unsynchronisation, footer/data-length declarations and duplicate/unsupported
information. Complete MPEG Layer III frame boundaries, stable rate/channels,
truncation, trailing tags/data, resource limits and cancellation are covered.
A generated file with over 1 MiB of compressed-frame space requires only 12,003
bytes of reads for 3,000 frame headers; sample payloads are seeked over.
This is not MPEG CRC, bit-reservoir or full ancillary-data validation.

**238 combined private audio checks** pass, including **38 new MP3 checks**.
These cover six baseline targets; ID3v2.3 Latin-1/UTF-16 and v2.4 UTF-16BE/UTF-8;
Unicode/multiline values; unsynchronisation to FLAC/M4A/Vorbis/Opus; explicit
unsupported-metadata refusal; unchanged originals/cleanup; and 8/16/24 kHz mono
MP3-to-WAV gapless decoding. Ordinary 48 kHz stereo conversions retain 96,000
decoded frames; the low-rate cases retain exactly one fifth of their rate.

The authored v2.3 unsynchronised title `AÿàB` exposed native truncation to `Aÿà`.
The managed parser reverses the tag-level escape before traversing frames and
retains the full value. Conversion now disables inherited MP3 metadata, supplies
every admitted inventoried field explicitly, and requires actual output equality.
The native source probe still provides audio facts; it is no longer authoritative
for these MP3 descriptive tags. The tests assert the complete title in all four
converted output types. The standalone Analyze probe still has this reporting
limitation until it integrates the inventory.

Combined evidence:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  adapter-49a326e6f2db44beb7c4ee52f6a57493/
    encoding-adapter.json
    mp3-unsync-v3.mp3
    mp3-unsync-v3.mp3.json
    mp3-unsync-v4.mp3
    mp3-unsync-v4.mp3.json
```

Fresh isolated Release stage
`artifacts/production-staging/4f82e588ada84ed68865e2263ad6834f` passes with zero
warnings/errors, normal curated identities, payload inventory and notices.
It uses `-SkipShell` and does not ship evaluation audio/PDF engines. ID3v1/APE,
older/extended ID3 variants, genre codes, language-specific/named comments,
artwork/lyrics/chapters and other frame handlers remain open, as do M4A inventory,
audio conversion app/menu integration, independent fidelity/listening, engine
adoption and document transformations. No installation, Explorer change, native
recycling, visible acceptance or live commerce was performed.
The evaluation-only worker copy passed 11 Analyze, 14 FLAC workflow and 16
direct-audio checks. Its evidence is under
`.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/worker-07af9f2621e24c4195a0d4277530abb7/`
in `results/analysis-results.txt`, `flac-results/flac-workflow.json` and
`direct-results/direct-audio.json`. Audio conversion remains a private candidate;
these existing worker checks do not establish a shipping conversion command.

M4A local container and metadata inventory (2026-09-10)
------------------------------------------------------

**1,371 foundation contracts** pass, including **53 new M4A checks**. The managed
inventory checks one local AAC-LC track, atom/descriptors, sample sizes/timing,
32/64-bit chunk tables, complete nonoverlapping media coverage, ordinary text
tags, distinct comment/description values, track/disc totals and language.
Authored fixtures exercise physical chunks out of time order, variable sample
sizes, paired roll groups, simple priming edits, unsupported structures/text,
external references, cancellation and restored stream positions. A counting
stream verifies that over 1 MiB of encoded samples is skipped during inventory.

**269 combined private audio checks** pass, including **31 M4A checks**: all six
targets, Unicode/multiline values, unsupported metadata and references, malformed
tables, unchanged original hashes and owned-scratch cleanup. Source metadata
coverage is now verified for this admitted M4A profile. The ordinary two-second
fixture retains 96,000 decoded frames. Fresh faststart mono AAC conversions at
16,000, 44,100 and 48,000 Hz each decode to the expected one-fifth second length.
This confirms those generated priming cases with the pinned native engine;
independent decoding, listening and broader recording-device files remain open.

The first synthetic run exposed an optional-memory null-conversion issue; explicit
absence handling corrected it. The first native run found alternate-group 1 on
the only audio track; that number has no alternate-track selection effect and is
now accepted without relaxing the exactly-one-track requirement. The final runs
above include both corrections.

Evidence beneath the existing prepared engine directory
`.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/`:

```text
adapter-df73259913744e87976c152344838c38/encoding-adapter.json
```

Focused evidence is also at
`.codex-temp/m4a-preservation-8d44fe0fdec54c53a4c9136902a23ef4/m4a-preservation.json`.
The fresh evaluation-only worker copy passed 11 Analyze, 14 FLAC workflow and
16 direct-audio checks. Evidence is beneath the prepared directory at
`worker-e8837ca44acb41269d87deb646bc1403/`, in `results/analysis-results.txt`,
`flac-results/flac-workflow.json` and `direct-results/direct-audio.json`.
Fresh isolated Release stage
`artifacts/production-staging/8f66c6e26f61480ea13d31452c0bfc09` passes with zero
warnings/errors and curated identities, payload allowlist, dependencies and
notices. It uses `-SkipShell`; evaluation audio/PDF engines remain excluded.

See the [exact M4A policy](audio-conversion-policy.md). Artwork/freeform fields,
timestamps, version-one headers, ALAC/HE-AAC, fragments, complex edits and other
variants remain refused pending handlers. This does not finish audio conversion
worker/menu admission, production engine adoption, document transformations or
the active broad-file goal. No image-engine, native-shell or visible UI acceptance
was rerun for this metadata slice; installation, live commerce and signing were
not performed.

Audio conversion worker and publication (2026-09-10)
----------------------------------------------------

**1,408 foundation contracts** pass: 35 new audio batch/IPC/access checks and
two paid-audio-admission checks supplement the preceding 1,371. They cover fixed
targets, combined quality consent, forged-plan rejection, same-format no-op
admission, copy defaults, overwrite gates, contradictory worker payloads and
batch identity. Synthetic paid access bypasses trial bookkeeping and cannot
fall back to a new trial after expiry. No live license service is involved.

**50 isolated audio conversion workflow checks** pass through the real worker
and application publisher. All 30 cross-format pairs create validated named copies;
the six same-format plans remain unchanged without starting the trial. Additional
checks cover one admission across expiry, output collisions, expired-work denial,
changed sources, unsupported metadata with continued worker use, external M4A
references, mixed converted/no-op batches, cancellation, alternate folders and
nonempty/hard-linked reservations. Output formats are reprobed, original hashes
are retained and publication records/reservations are empty at completion. A
refusing recycler ensures these are copy workflows, not native overwrite tests.

The existing **11 Analyze, 14 FLAC workflow and 16 direct-audio checks** also pass
against the final stage, for **91 audio workflow checks** total. The preceding
269-check private encoding suite was not rerun: encoding recipes and sample
validation are unchanged; the new file-adapter/publication path is exercised by
the real-worker matrix. New unsupported-feature handling returns a stable failure
category instead of allowing `NotSupportedException` to terminate the worker.

Final evidence is under
`.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/worker-3de59d034c60422db7e46217c6dd9662/`:

```text
conversion-results/audio-conversion-workflow.json
results/analysis-results.txt
flac-results/flac-workflow.json
direct-results/direct-audio.json
```

Fresh isolated Release stage
`artifacts/production-staging/7758790beffd4ae583b5da4ab377f8ff` passes with zero
warnings/errors, curated identities, payload allowlist, dependencies and notices.
It uses `-SkipShell`; audio/PDF evaluation engines are still excluded from the
normal payload. The test script adds them only to a separate evaluation copy.
Repository/public-boundary, system-theme policy and 64 documentation files pass.

This delivers the file-based audio conversion and publisher integration, not the
customer menu. Direct audio Convert routing, compact quality consent, dispatch
retry behavior, expanded metadata/artwork coverage, independent fidelity/listening,
audio payload adoption, broader crash/recovery and required document actions
remain open. No hidden/visible UI, image-engine, native-shell, installer, signing
or live-commerce acceptance was rerun for this slice. The broad-file goal remains
active.

Direct audio Convert commands and quality prompt (2026-09-10)
------------------------------------------------------------

The six fixed audio targets now route from native Convert through the managed
quick-work queue. Routine conversion needs no planner. Required lossy-to-lossy,
resampling or precision changes use one compact decision, retaining target/files
and captured settings across licensing changes. The shell keeps one submenu
level, with the existing image targets first and Settings last.

**1,422 foundation contracts** pass, including 14 new action/decision checks.
**20 isolated direct audio conversion checks** pass through the actual application
queue, worker and publisher. They cover all six actions, same-format no-op, quiet
routine success, one decision/admission for a mixed-source batch, expired access,
activation/deactivation while keeping the plan, final access rejection, retries
with current settings, declined/cancelled decisions, malformed input and a missing
engine. Original hashes and reservation cleanup pass; a refusing recycler prevents
native recycling. Access is simulated and no live license requests occur.

The existing **50 conversion, 11 Analyze, 14 FLAC workflow and 16 direct FLAC
checks** also pass, bringing the real-worker audio total to **111**. Evidence:

```text
.codex-temp/audio-engine/81751fade35f4af787aa653bd8a5c1a4/
  worker-43caac2844f549c7976a876370004bc6/
    conversion-direct-results/audio-conversion-direct.json
    conversion-results/audio-conversion-workflow.json
    results/analysis-results.txt
    flac-results/flac-workflow.json
    direct-results/direct-audio.json
```

These workflows used isolated stage `a88b9d3ee55c45ef82dbfe138b6f6054`.
Its native shell contracts also passed all menu actions, complete multi-file
activation, IDs, separator/Settings order and enumerator cloning/reset without
registering or operating Explorer. **13 image direct-command checks** passed at
`.codex-temp/license-workflow/direct-b350f7b2c9c1441ead6f41b4f6ab0cea`.

**86 hidden view contracts** pass against the final layout. They instantiate the
audio decision, check bindings and accessible peer text, keep files collapsed,
retain explicit confirmation/Escape semantics, refit height on disclosure and
verify that status and actions stay outside scrolling content at minimum size.
The initial binding/sizing checks needed an explicit measure/arrange/layout pass
because these windows are never shown. The final production change keeps the
status/buttons visible when a long file list scrolls.

The final isolated Release build is
`artifacts/production-staging/449797dd9d6447e5891f05b2a96da634`, including native
shell and the adjusted audio window. Compilation, curated identities, payload
allowlist, dependency and notice checks pass. The worker workflows were not
repeated after the window-only change. Normal staging still excludes the
evaluation audio/PDF engines; they are added only to an isolated test copy.

The preceding 269 private encoder and 942 image-engine checks were not rerun;
their implementations did not change. Hidden contracts do not establish visible
usability, actual keyboard focus, screen-reader delivery, other themes/DPI or the
expanded installed menu's layout. Metadata/artwork coverage, independent decoding
and listening, production audio adoption, hostile-file/crash/recovery coverage and
required PDF/Office actions remain open. No installer, signing, live commerce or
release acceptance was performed. The broad-file goal remains active.
