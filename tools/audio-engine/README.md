Isolated Audio Engine Evaluation
===============================

The opt-in [AIFF/WMA native evaluation](../../docs/audio-expanded-input-evaluation.md)
adds restricted input decoders with 27 generated decoding fixtures and fifteen
configuration guards. Application integration and production adoption remain
pending; the default build and existing six target choices are unchanged.
The later [AIFF adapter matrix](../../docs/aiff-conversion-adapter.md) adds
366 checks and 68 outputs with text/artwork and decoded-sample comparisons.
The [WMA v1/v2 adapter](../../docs/wma-conversion-adapter.md) adds 283 checks
and 36 outputs, including protection refusal and exact decoded sample timing.
Broader variants/timelines, worker integration and customer admission remain open.
The later [Windows-codec corpus](../../docs/wma-system-codec-evaluation.md)
provides independent authored sources and decoding. Its 36 observations expose
sample-extent/fixture-drain failures; the current acceptance result is false.
The Lossless ASF/VBR fixture correction now passes 13 exact source comparisons
within 40 observations; Standard/Pro/Voice extent failures still prevent acceptance.
The subsequent 57-case run adds 17 Pro boundary lengths and retains 30 failed
extents. `Inspect-WmaProFrames.py '<retained Pro probe.json>'` reports bounded
frame/trim declarations and a terminal-overlap hypothesis; it is diagnostic,
not a decoder or an output-padding policy. The full runner records those headers
and the inspector identity alongside actual sample comparisons.
The later opt-in build option `--expanded-inputs --wma-pro-tail-repair` applies a
recorded patch to fresh source only. Its `wmapro-tail2` runtime passes all 26 Pro
extents and 23 stereo accuracy checks, with 13 Lossless cases still exact.
Eleven Standard/Voice extent failures remain; the managed adapter and shipping
payload do not admit this experimental runtime. See the corpus document for
native review, fixed sample limits, retained failures and open adoption gates.
The combined `--wma-standard-tail-repair` evaluation also passes 20 Standard
extent/sample comparisons, with 62 equal extents in the 71-case corpus. Nine
Voice extent failures remain. The exact prior files pass 57 repair/preservation
assertions; all 51 non-Standard outputs are unchanged. This runtime remains an
isolated experiment, with no managed runtime pin or customer capability change.
The later `--wma-voice-cache-repair` experiment gives the cache bounded space for
packet padding and propagates EOF errors. All 95 codec extents now match, including
36 Voice lengths, but 32 Voice sample comparisons still fail. Run
`Test-WmaVoiceRefusals.py '<repaired build>' '<completed system corpus>'` for
four positive controls and eight exact decoder-error checks on generated copies.
Run `Test-WmaSystemCodecs.py '<expanded workspace>'` to reproduce. Its expected
nonzero result must not be reported as passing WMA support.

`Test-WmaVoiceMft.py '<system corpus>' '<Voice refusal corpus>'` evaluates an
explicit Windows Voice decoder against retained samples from the same system
codec. It passes 36 exact comparisons, eight malformed-input refusals and four
ownership/input guards. See [scope and next integration work](../../docs/wma-voice-windows-evaluation.md).
It neither selects a shipping provider nor fixes the FFmpeg waveform differences.
The helper's later `--inherited` mode also passes 52 private process checks using
the existing read-only snapshot/pipe/job launcher. The linked evaluation records
the typed sample header, cancellation/owner-death evidence and reproduction.
The later [private Voice adapter](../../docs/wma-voice-conversion-adapter.md)
connects that handle mode to the six existing target recipes and preserves the
tested ASF text, language and artwork. Customer/worker admission remains separate.
Its `--availability` mode checks Media Foundation/Voice startup without media
parsing, requires a parent-owned job and emits a bounded versioned reply. The
linked adapter document records the refreshed pin, 25 availability/launcher
checks and unchanged 48 native/52 process checks. Missing-component Windows
acceptance remains separate from injected exit-status tests.
`Test-WmaVoiceDuration.py --speech '<expanded runtime bin>'` authors a bounded
five-minute corpus using an installed offline Windows voice. The later
[streaming verification](../../docs/wma-voice-streaming-verification.md) records
all six targets, exact reference comparison and a retained continuous-tone
failure; the shared limits do not imply unrestricted duration acceptance.

The private `--short-audio-matrix` mode and public `--short-audio-direct` mode
cover tiny recordings, codec-frame boundaries and safe publication/recovery.
See [commands and exact results](../../docs/short-audio-verification.md): the
FLAC correction passes. The later private `--mp3-padding` mode verifies the
[MP3 duration correction](../../docs/mp3-short-padding-verification.md), including
independent explicit-format decoding and a retained automatic-probe limitation.

`Test-AudioWorker.ps1 -Packaged -IncludeOptimizationInterruptions` tests live
FLAC recompression cancellation, client timeout and worker termination, then
validated smaller-copy retries. It uses generated padded FLAC inputs and a
refusing recycler. Add `-IncludeInterruptions` to also run the five-target
conversion interruption matrix. See [scope and retained evidence](../../docs/audio-interruption-verification.md).

These tools evaluate a pinned third-party FFmpeg build independently of the
installed Context Suite application. They do not stage a production dependency,
install software, change PATH or touch Explorer registrations.

From the repository root:

```powershell
.\tools\audio-engine\Prepare-AudioEvaluation.ps1
.\tools\audio-engine\Test-AudioEvaluation.ps1 -PreparedDirectory '<printed scratch directory>'
.\tools\audio-engine\Test-AudioEvaluation.ps1 -PreparedDirectory '<printed scratch directory>' -PreservationOnly
.\tools\audio-engine\Test-AudioAdapter.ps1 -PreparedDirectory '<printed scratch directory>' -FixtureDirectory '<completed matrix directory>'
.\tools\audio-engine\Test-AudioWorker.ps1 -ProductionStage '<fresh isolated production stage>' -PreparedDirectory '<printed scratch directory>' -FixtureDirectory '<completed matrix directory>'
```

Preparation downloads the exact archive in `evaluation.json`, verifies its
published SHA-256 and retains an unpacked file/hash inventory under
`.codex-temp/audio-engine/<id>`. Evaluation verifies the archive and inventory,
then builds the public, isolated .NET probe and creates a fresh `matrix-<id>`
subdirectory. Both scripts retain evidence instead of replacing previous runs.

The probe authors stereo PCM audio, creates six input formats, crosses them with
six output formats, checks properties and decoded samples, and records four
ordinary tags. It also tests a FLAC application-block preservation canary. Each
subprocess has a 20-second deadline and bounded diagnostic output; matrix decode
outputs have a 16 MiB cap. Only fixed generated local paths and explicit engine
arguments are used. This is an experiment harness, not the production worker
isolation implementation or a way to process customer files.

`matrix.json` records per-pair arguments, hashes, timing, native peak working set
when available, sample differences and tag gaps. `version.txt`, `encoders.txt`,
generated source/output media and decoded sample files remain beside it. A zero
exit means the narrow matrix checks pass, not that precision choices, listening,
all metadata, hostile files, worker integration or release acceptance are complete.
The separate optimization canary reports preservation failure without concealing
the otherwise successful conversion matrix.

`-PreservationOnly` runs the public core FLAC metadata reconciliation against
freshly encoded disposable media. It verifies rejection of the encoder's changed
vendor/comment bytes, exact restored metadata and decoded samples, smaller output,
an unchanged original and refusal of an application-specific block. Its evidence
is `preservation.json`; it does not rerun the conversion matrix.

`Test-AudioAdapter.ps1` requires the private repository and tests its probe
component with the generated six-format inputs. It verifies runtime engine-file
hashes/leases, typed replies, cancellation, limits, protocol rejection and disabled
engine report files. It writes `probe-adapter.json` under a fresh scratch directory;
it does not add an audio payload to the production application or enable a menu.
It also writes `encoding-adapter.json` after the private candidate's 36 format
pairs, wider PCM/float/rate/layout fixtures, FLAC recompression and native child
ownership checks. It also generates a five-minute PCM24 recording to test the
file API beyond the old encoded/decoded array limits, managed allocation,
artifact leases/disposal and cancellation after native output is observed.
Generated outputs stay under the new evidence directory.
The private test host also accepts `--flac-ogg-artwork <candidate-bin>
<generated-six-format-fixtures> <fresh-absolute-evidence-directory>
<independent-decoder-bin>` for 33 focused artwork transport and framing checks.
Build that Release test host first. The public host accepts an optional fifth
argument to `--audio-conversion-direct <scratch> <worker> <fixtures>` pointing
to that generated artwork evidence directory; it adds 16 real-worker publication
checks using `two-covers.flac` and `large-cover.flac`. See the
[exact scope and retained evidence](../../docs/audio-artwork-conversion.md).
Use `--mp3-artwork <candidate-bin> <generated-six-format-fixtures>
<fresh-absolute-evidence-directory> <independent-decoder-bin>` for the separate
ID3 PNG/JPEG checks. The public test host accepts `--mp3-artwork-direct
<fresh-absolute-scratch> <staged-worker> <mp3-artwork-evidence-directory>` for
quality consent, no-op, complete cover publication and collision checks using
the generated v2/v3/v4 inputs. See the
[MP3 artwork record](../../docs/mp3-artwork-conversion.md).
The artwork fixture adds two authored PNG covers (including alpha), duplicate
comments, multiline lyrics and Unicode text. It checks raw metadata and decoded
RGBA preservation during FLAC optimization, plus refusal of linked/malformed
metadata. `authored-artwork.flac` can be passed to the worker check below.
Seek-table fixtures add lower-compression FLAC with authored seek slots, require
rebuilt offsets, compare seeked output against a linear source decode and reject
stale offsets/frame corruption. Their packet-index tests cover invalid clock,
number, structure and size declarations. Results remain in `encoding-adapter.json`.
Read [the candidate policy and limits](../../docs/audio-conversion-policy.md)
before interpreting these results as transformation or release readiness.
The adapter suite also verifies admitted WAV INFO preservation across all target
recipes, plus refusal of cue/loop, broadcast/iXML, unmapped or duplicate INFO and
ambiguous text. Authored extra-chunk files remain in the new evidence directory.
Conversion checks the complete RIFF chain, including metadata after samples;
unsupported information is never implicitly waived by a successful native probe.

`Test-AudioWorker.ps1` copies a fresh production stage into repository scratch,
adds the evaluation engine to that copy, and exercises the actual application
view-model, IPC client and worker with generated inputs. Results must include
useful audio facts, fallback after malformed input, unchanged originals and, in
the default Analyze mode, no licensing/publication calls. The test does not alter the source stage's inventory,
install an application, register Explorer or claim native shell acceptance.
Optional `-ArtworkFixture <generated authored-artwork.flac>` adds actual IPC and
Analyze checks for embedded-artwork facts; the file must be under audio scratch.
Optional `-IncludeOptimization` also exercises the FLAC worker commands, real
trial admission and application publisher using generated padded FLAC files.
It writes `flac-results/flac-workflow.json` after all checks pass. Copies and name
collisions, no-smaller-result handling, expiry between files, changed/corrupt
sources, cancellation and unsafe reservations are covered. A refusing recycler
ensures this workflow never uses the native Recycle Bin. It does not exercise a
customer menu or authorize shipping the evaluation payload.
The switch also runs `--audio-direct` through the real application queue and
records `direct-results/direct-audio.json`. It covers Auto/Lossless FLAC dispatch,
one admitted mixed PNG/FLAC batch across trial expiry, synthetic activation/retry,
captured settings, quiet completion, cancellation, malformed files, misleading
extensions and missing-engine fallback. These component checks do not operate
Explorer or inspect visible windows, keyboard focus or assistive technology.

Optional `-IncludeConversion` runs 52 checks through the audio file probe,
conversion worker, confirmed batch, local trial and transactional publisher.
It covers all 30 cross-format pairs and six no-op cases, legacy MP3 tag transport
and conflict refusal, collisions, expiry,
changed sources, unsupported metadata, cancellation, alternate folders and
refusal of nonempty/hard-linked reservations. It records
`conversion-results/audio-conversion-workflow.json`. It then runs 20 additional
checks through `MainViewModel` direct Convert dispatch and records
`conversion-direct-results/audio-conversion-direct.json`. These cover all six menu
targets, quiet routine work, one compact quality decision for mixed sources,
activation/deactivation without losing the plan, final access rejection, retries,
captured settings, cancellation, malformed input and missing engines. A refusing
recycler excludes native recycling. The tests call the actual decision model;
they do not operate Explorer or inspect visible windows. Use both switches to
verify the existing audio workflows alongside conversion.

Optional `-IncludeInterruptions` authors a long PCM24 noise fixture and verifies
cancellation, client-deadline expiry and worker-only termination while a real
native FLAC, MP3, AAC, Vorbis or Opus encoder is consuming CPU and growing its
candidate. Each of the five targets runs all three faults. It checks native
exit, original/committed-copy preservation, reservation/journal/scratch cleanup
and a validated same-target tone-file retry with a fresh worker. The public host mode is
`--audio-interruptions <new-evidence> <worker-executable>`; the wrapper supplies
these paths and verifies the packaged inventory before and after execution.
See [actual results and scope](../../docs/audio-interruption-verification.md).

Optional `-IncludePublicationCrashes` verifies WAV-to-FLAC conversion and FLAC
optimization across five abrupt application-layer publication checkpoints. It
checks original/candidate fingerprints, worker exit, restart recovery notices,
retained journals and fresh collision-safe work. It uses the real executors and
publisher in a child test process, with native recycling disabled. Retained
recovery files are deliberate evidence, not temporary files to delete on restart.
See [the ten-case matrix](../../docs/audio-publication-crash-verification.md).

The latest adapter run passes 292 combined checks. FLAC conversion cases include
original-block inventory, literal Unicode/multiline tag transport, canonical
comment aliases, refusal of unsupported metadata, stale seek tables and corrupt
frames. WAV allows ordinary ASCII line breaks and tabs; Unicode WAV text remains
outside the admitted conversion policy. This is candidate validation, not a
shipping audio conversion command or listening acceptance.
Ogg cases add page CRC/continuation/stream validation, original comment transport,
short Vorbis sample counts and six-channel Vorbis/Opus round trips. The pinned
default Ogg packing failed several short decoded extents; packet pages correct
that behavior without relaxing sample counts or signal-error limits. The
private test host also accepts `--ogg-preservation <pinned-bin> <generated-fixtures>
<new-evidence>` for focused diagnostics; the normal adapter script includes all
51 of these checks.
  MP3 includes 100 cases for ID3 versions/encodings, exact unsynchronised values across
outputs, unsupported metadata, complete frame boundaries and low-rate gapless
decoding. `--mp3-preservation` accepts the same three arguments for a focused run.
The MP3 suite now also converts generated ID3v1.0/1.1 trailers, Latin-1 text,
track/genre fields and agreeing combined v1/v2 tags. It verifies original hashes,
actual output tags and decoded extents, and refuses contradictory trailers.
  Codes 0-147 use reviewed names; composite or unreviewed genres remain refused.
  The v2.2 cases cover shorter frame headers, shared descriptive fields and
  Unicode/unsynchronisation, with five target conversions and one byte-identical
  MP3 no-op. Compressed tags and unmapped picture/object/date/time frames refuse.
These cases do not finish APE, additional genre conventions, language-specific comments,
artwork or the remaining M4A metadata variants. The native Analyze probe retains its separately
documented unsynchronised-tag reporting limitation.

M4A adds 31 cases for local AAC-LC inventory, ordinary and Unicode/multiline tags,
external-reference and unsupported-metadata refusal, priming at three rates,
source digests and cleanup. `--m4a-preservation` accepts the same focus arguments.
The public parser also checks sample/chunk extents, timing, roll groups and bounded
reads. Timestamps, artwork/freeform tags, extended/fragmented structures and other
variants remain outside admission; see the exact policy and evidence below.

See [recorded results](../../docs/audio-engine-evaluation.md) and the
[engine comparison](../../docs/media-engine-evaluation.md). Supplier archive
retention is limited; this evaluation pin must never fall back silently to latest.
Actual production adoption needs retained reproducible source/dependency inputs,
license/notice review and an intentionally bounded capability build.

Use `Prepare-AudioSources.ps1` to retain the currently pinned FFmpeg, supplier
recipe, Ogg, Vorbis, Opus, supplier LAME, stable LAME 4.0, GNU make, NASM and zlib source
archives in a new repository scratch directory.
Use `-SourceDirectory '<existing repository scratch directory>' -VerifyOnly` to
verify them without downloads or writes. Hash/size mismatches are refused; no
upstream scripts execute. The five GitHub archives are retained without extraction;
LAME's fixed SVN revision is exported over HTTP using Python 3.14 or later, with
bounded reads, a per-file inventory and a deterministic pinned ZIP. SVN properties
and history are not exported. Stable LAME and GNU make retain their original
release tarballs; NASM retains its original source ZIP. The checked tar reader
rejects unsafe paths, links and oversized entries before extraction. Python 3.14
is required for tar verification too. Source schema 4 defaults to all ten archives,
including when verifying an older cache. `-BuildInputsOnly` selects the eight
inputs for the current build, excluding the supplier recipe and alpha LAME.
The
[curation record](../../docs/audio-engine-curation.md) lists unresolved SVN-property,
Opus bootstrap, toolchain and transitive inputs, plus the preliminary Opus findings
and subsequent verified dependency recipe. These archives do not constitute
a complete source-distribution bundle or authorize production adoption.

Build the pinned Opus dependency using existing Visual Studio 2026 x64 tools:

```powershell
.\tools\audio-engine\Build-OpusDependency.ps1 -SourceDirectory '<verified source cache>'
```

The command creates a fresh `.codex-temp/audio-opus-<id>` directory, verifies and
extracts the source, builds a static library and runs five upstream tests plus a
linked-version check. It retains logs, source/output hashes and compiler/recipe
identity in `dependency-build.json`. No downloads, installation or production
staging run. The repository wrapper supplies a truthful source version and fixes
the pinned CMake/MSVC flag mismatch without editing upstream files. This Opus
dependency does not yet provide the complete curated FFmpeg audio payload.

`Build-AudioDependency.ps1` accepts `-Dependency Opus`, `OggVorbis`, `Lame` or
`LameStable`, plus
`-SourceDirectory '<verified source cache>'`. It shares the source, build, test
and inventory checks across all selections; the older Opus command forwards
to it.
Ogg/Vorbis runs four upstream tests. LAME builds only the core encoder and an
authored VBR2 probe. Run `python tools/audio-engine/Test-LameDependency.py
'<completed Lame build>' '<evaluation engine bin>'` for independent stream,
gapless sample-count and generated-tone checks after verifying the complete
pinned decoder runtime. See [exact evidence and remaining work](../../docs/audio-dependency-builds.md),
including the passing stable LAME candidate and remaining FFmpeg composition.
`LameStable` checks its runtime version as well as encoding. Use
`python -B tools/audio-engine/Test-SourceTar.py` for authored tar-reader checks.

The same shared runner now accepts `-Dependency Make` and `-Dependency Nasm` to
build the pinned GNU make 4.4.1 and NASM 3.02 tools in fresh scratch. Make runs
version and Git Bash/recursive/incremental/failure checks. NASM runs version and
Win64 COFF assembly/link/execution checks. Neither tool is installed or staged
with the customer app. See [audio build tools](../../docs/audio-build-tools.md)
for compiler compatibility fixes, the retained NASM warning and exact evidence.

`-Dependency Zlib` builds the pinned static zlib 1.3.2 library and runs its upstream
codec example for the PNG artwork dependency. The isolated
`Build-AudioEngine.py` composes explicit completed dependencies into a fresh
FFmpeg workspace; `Check-AudioConfiguration.py` gates the generated component
selection. See [commands, scope and current results](../../docs/audio-ffmpeg-build.md)
before using this unaccepted candidate path. `Test-AudioCandidate.py` verifies
candidate and independent runtime identities, tests six generated audio targets
with both decoders and exercises embedded PNG decoding. Its smoke evidence does
not replace the full adapter/worker, metadata, compiler or listening review.

Run `python -B tools/audio-engine/Check-AudioBuild.py '<completed FFmpeg workspace>'`
to verify current recipe identity, generated hardening flags, exact reviewed
diagnostics and emitted PE security/import metadata. The separate native review
retains its report beside the candidate and does not authorize production staging.

For the complete curated adapter suite:

```powershell
.\tools\audio-engine\Test-CuratedAudioAdapter.ps1 `
  -CandidateDirectory '<completed FFmpeg workspace>' `
  -FixtureDirectory '<completed evaluation matrix>' `
  -IndependentDecoderDirectory '<pinned evaluation bin>'
```

The focused `--ogg-artwork` private-host mode generates PNG/JPEG Vorbis/Opus
fixtures and verifies conversion to FLAC and the other Ogg codec. The public
`--ogg-artwork-direct` mode uses those fixtures with a freshly staged worker.
See [commands, exact checks and limitations](../../docs/ogg-artwork-conversion.md).
The corresponding `--m4a-artwork` and `--m4a-artwork-direct` modes cover AAC-LC
M4A PNG/JPEG images, both movie-atom placements and multiple covers. See
[M4A commands and evidence](../../docs/m4a-artwork-conversion.md).
The private host's `--mp3-output-artwork` mode accepts the same binary, fixture,
new evidence and independent-decoder arguments, and tests covers from FLAC, Ogg
and M4A in MP3 output. The public host's `--mp3-output-artwork-direct` mode takes
a new evidence directory, staged worker and that generated-fixture directory.
See [MP3 output evidence and limits](../../docs/mp3-output-artwork.md).

The independent decoder supplies artwork pixel comparisons;
the candidate performs every production media operation. The latest run passes
298 checks. `Test-AudioWorker.ps1` accepts `-CandidateDirectory` instead of
`-PreparedDirectory` for the same curated candidate. Use fresh isolated production
staging, the generated fixtures, `-ArtworkFixture`, `-IncludeOptimization` and
`-IncludeConversion` for all 116 workflow checks. The harness copies only the
exact files in `curated-candidate.json` to its disposable payload. This is not
production engine staging or redistribution approval.

`Prepare-AudioDistribution.py --candidate '<completed workspace>' --sources
'<retained source cache>'` creates local runtime/source review ZIPs, with explicit
file membership and original source/license identities. Run
`Verify-AudioDistribution.py '<review directory>'` for read-only integrity checks
and `Test-AudioDistribution.py '<review directory>'` for disposable tampering
cases. The extracted source kit provides `Rebuild-AudioSource.ps1`; its completed
build passes dependency tests and native review. See [archive hashes, rebuild
evidence and remaining distribution decisions](../../docs/audio-distribution.md).

For an actual isolated production payload, use `tools/Build-Production.ps1`
with a fresh `-StagingId` and `-AudioDistributionDirectory '<verified review
directory>'`. The build stages only the exact reviewed archive and all notices;
it cannot refresh the development payload with this option. Use
`Test-AudioWorker.ps1 -Packaged -ProductionStage '<fresh stage>'` with the same
generated fixture/artwork and optimization/conversion flags to exercise that
worker directly, without injecting a runtime. Run `Test-AudioPayload.py '<stage>'`
for deployment/inventory and build/release refusal checks. See
[commands and evidence](../../docs/audio-distribution.md). Default release
packaging still rejects the audio candidate pending redistribution review.

`Test-ExpandedAudioWorker.py '<repaired build>' '<Voice helper directory>'
'<95-case system corpus>' '<27-case expanded fixtures>'` builds a disposable
managed worker payload and exercises AIFF/WMA conversion through access admission
and transactional copy publication. It verifies exact runtime/helper identities,
adds representative AIFF tones, retains boundary refusals and records independent
WAV comparisons. See [worker evidence and open gates](../../docs/expanded-audio-worker-verification.md).
This does not update formal staging, the installed app or Explorer registration.

`Test-ExpandedAudioRecovery.py '<successful expanded worker evidence>'
'<generated-speech duration corpus>'` authors long inputs and verifies actual
decode/encode interruption and following publication. See the
[recovery record](../../docs/expanded-audio-recovery.md) for exact scope and results.
`Build-AudioEngine.py --raw-audio-timestamp-repair` is an explicit expanded-input
evaluation switch. `Test-RawAudioTimestamps.py '<previous build>' '<new build>'
'<95-case system corpus>' '<long Lossless fixture directory>'` verifies unchanged
decoded samples and preserved errors after the timestamp-free transport fix.

`Test-ExpandedAudioDirect.py '<successful expanded worker evidence>'` builds
fresh managed components and exercises the existing Convert command workflow
with seven AIFF/WMA inputs and all six targets. It includes quality decisions,
access refresh, mixed selections, retry and missing-provider cases. See the
[direct-command evidence](../../docs/expanded-audio-direct-verification.md).

`Test-ExpandedAudioMetadata.py '<worker evidence>' '<AIFF adapter evidence>'
'<WMA adapter evidence>' '<Voice adapter evidence>' '<supplier bin>'` uses the
same fresh command payload for seven text/artwork cases and two preservation
refusals. It independently checks tags and decoded cover pixels in all applicable
published targets. The supplier must match the pinned evaluation archive.

`Build-AudioEngine.py --wma-pro-matrix-repair` selects the isolated transmitted
channel-matrix repair with expanded inputs. Prepare the frozen surround corpus
using `Test-WmaProSurround.py --prepare '<95-case system corpus>' '<explicit-PCM
Windows writer directory>'`, then run `--verify '<completed matrix build>'
'<prepared surround evidence>'`. `Test-AudioSourceReview.py '<completed matrix
build>'` exercises sequential patch identity and source-tampering refusals on
disposable copies. See [surround fidelity evidence](../../docs/wma-pro-surround-repair.md)
before adopting the candidate; it does not clear broader human or release gates.
