Audio Engine Evaluation Results
===============================

Date: 2026-09-09. Status: isolated generated-fixture experiment; no production
audio adapter, audio menu commands or shipping dependency added.

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

The latest foundation suite passes 944 contracts, including prefix/lease/fallback
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
probing and integration raise the latest total to 944. The 76 hidden view checks
were rerun for integration and retain their nonvisual scope. No engine result proves document
actions, installed-shell behavior, accessibility or commercial release readiness.
