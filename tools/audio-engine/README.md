Isolated Audio Engine Evaluation
===============================

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
MP3 includes 61 cases for ID3 versions/encodings, exact unsynchronised values across
outputs, unsupported metadata, complete frame boundaries and low-rate gapless
decoding. `--mp3-preservation` accepts the same three arguments for a focused run.
The MP3 suite now also converts generated ID3v1.0/1.1 trailers, Latin-1 text,
track/genre fields and agreeing combined v1/v2 tags. It verifies original hashes,
actual output tags and decoded extents, and refuses contradictory trailers.
Codes 0-147 use reviewed names; composite or unreviewed genres remain refused.
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
