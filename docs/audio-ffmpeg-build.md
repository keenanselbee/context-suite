Restricted FFmpeg Audio Build
============================

Status: a fresh native candidate builds and passes generated-media smoke checks,
2026-09-11. The hardened candidate now passes the full private adapter and isolated
worker matrix. Production payload adoption and broader release acceptance remain
separate.

Build path
----------

[Build-AudioEngine.py](../tools/audio-engine/Build-AudioEngine.py) accepts explicit
completed dependency directories, verifies their source/output inventories, and
creates a fresh `.codex-temp/audio-ffmpeg-<id>` workspace. It retains the exact
FFmpeg archive contents and copies verified headers/libraries into a separate SDK.
It checks Ogg's generated fixed-width header against the pinned template and
records the copied dependency files. No source files are edited or installed.

Use Python 3.14 and the existing Visual Studio 2026, Windows SDK 10.0.26100.0 and
Git Bash. The command shape is:

```powershell
python -B tools/audio-engine/Build-AudioEngine.py `
  --sources '<ten-archive cache>' `
  --opus '<completed Opus build>' `
  --ogg-vorbis '<completed OggVorbis build>' `
  --lame '<completed LameStable build>' `
  --make '<completed Make build>' `
  --nasm '<completed Nasm build>' `
  --zlib '<completed Zlib build>'
```

`--configure-only` stops before compilation, while retaining the component check.
The [dependency runner](audio-dependency-builds.md) and
[build-tool recipes](audio-build-tools.md) prepare those inputs. The launcher uses
existing Windows short paths, MSVC's dynamic CRT, explicit local library paths
and a narrow resolver for the three external codec packages. That resolver
supports only the pinned configure script's queries; it is not a general
pkg-config implementation or permission to find arbitrary system libraries.

The version base is the pinned source's `RELEASE` value `9.0.1`, with the suffix
`contextsuite-g9b0578816c6f`. Supplying the base explicitly avoids the upstream
version script reading this parent repository's Git history. Runtime version
identity is checked on both completed executables. The revision is passed to make
as well as configure because make overrides configure's version environment.

Required configuration
----------------------

[Check-AudioConfiguration.py](../tools/audio-engine/Check-AudioConfiguration.py)
checks the actual generated component header before compilation. It requires
the exact reviewed decoders, encoders, parsers, containers, protocols, filters
and bitstream filters, plus x64, assembly, shared libraries and the required
dependencies. It rejects additional protocols/encoders/devices, missing required
components, a different architecture, and enabled network/GPL/nonfree options.
This is configuration evidence, not runtime or redistribution clearance.

The six audio targets retain their tested settings. The build includes the
existing integer/float WAV representations and float64 raw output used to compare
decoded samples. WAV, FLAC, MP3, MOV/M4A and Ogg inputs and local file/pipe/held-fd
I/O remain enabled. PNG and MJPEG decoding support embedded artwork. Video
encoders, capture devices, ffplay and network protocols remain disabled.

Some shared infrastructure is selected by the unchanged upstream build rules:
the ffmpeg executable requires common video filters, and MOV/IPOD selects the
VP9 superframe bitstream filter and AC3 parser alongside its AAC handling. These
components are included explicitly in the reviewed configuration; the build is
not represented as containing no video-related code. No video conversion action
or arbitrary filter command is authorized by this dependency selection.

zlib source and codec check
--------------------------

PNG decoding needs FFmpeg's zlib-backed inflate wrapper. The independently
downloaded [zlib 1.3.2 release](https://zlib.net/) matches the publisher's SHA256:
`BB329A0A2CD0274D05519D61C667C062E06990D72E125EE2DFA8DE64F0119D16`,
1,502,830 bytes. Its GPG signature has not been verified. Source schema 4 now
requires ten archives; older caches need `zlib.tar.gz` too.

`Build-AudioDependency.ps1 -Dependency Zlib` builds the unchanged upstream static
library with dynamic CRT and no installation/contrib tools. The wrapper runs
the upstream codec example separately from upstream installation tests. Its
compression, gzip, large inflate, synchronization and dictionary checks pass,
reporting version 1.3.2. The final run is
`.codex-temp/audio-zlib-2ca53308f5134867b5ea8de43ad54066`, with zero compiler
warnings/errors and unchanged source. The generated public configuration header
is included in the dependency artifact inventory.

Current evidence and remaining work
-----------------------------------

The initial configure attempt at
`.codex-temp/audio-ffmpeg-d6bd19b967444123877c0c5ac39d5972` succeeds with the three
codec libraries, x64 assembly and local I/O. Review caught two unmet requirements:
PNG decoding was disabled without zlib, and `f64le` was the CLI format name rather
than configure's internal `pcm_f64le` muxer name. Both are corrected in the next
attempt. That first run is preliminary diagnostic evidence, not an accepted
configuration or a completed build.

Seven authored configuration-gate checks pass at
`.codex-temp/audio-config-guards-754be4fb3953460ba37264820a5209bc`. They use simulated
header copies, accepting the expected selection and rejecting missing float64,
missing PNG, additional HTTP, a video encoder, wrong architecture and network
support. The test caught an aggregate `FRAME_THREAD_ENCODER` flag being mistaken
for a codec; component enumeration now reads `config_components.h` separately.
These fixtures do not substitute for checking the real generated headers.

The next native attempt is
`.codex-temp/audio-ffmpeg-d8650ed561fd4222b42e232107afbfea`. Its actual generated
headers pass the complete component gate, with PNG/zlib and float64 output
present. Compilation first stopped on native make's inability to read MSYS
`/c/...` include paths. The launcher now supplies Windows short paths through
make's file/source/linker overrides, leaving generated configuration unchanged.
The next attempt exposed backslash loss in FFmpeg's awk dependency command. The
[make regression and script-file mode](audio-build-tools.md) address that issue;
the same configured tree subsequently compiled successfully. Its first runtime
reported this parent repository's revision; passing `REVISION=9.0.1` to make
corrects it. Original and resumed logs remain separate. This is preliminary
evidence; its original receipt predates the final checker/make changes.


Fresh native and generated-media evidence
-----------------------------------------

The complete current launcher succeeds from fresh inputs at
`.codex-temp/audio-ffmpeg-da73eac21626459098cbe1980ffe393d`. All 10,422 source files
and staged dependency files remain unchanged, and the recorded launcher/checker
hashes match. Both runtime version checks pass. `runtime-files.json` records two
executables and five versioned DLLs, totaling 4,825,088 bytes. The build also
produces byte-identical unversioned DLL aliases; those are excluded from `bin`.

SDK `dumpbin` inspection is retained under `pe-inspection`. All seven files are
x64, with high-entropy ASLR, dynamic base and NX flags. External imports are
Windows system libraries and the dynamic Microsoft C runtime, including
`VCRUNTIME140.dll`. No additional codec DLL or network-library import appears.
This is not proof of sandbox isolation or redistribution readiness. That initial
FFmpeg recipe did not request control-flow guard or CET compatibility; the
follow-up below adds explicit compiler/linker flags and binary checks.

[Test-AudioCandidate.py](../tools/audio-engine/Test-AudioCandidate.py) checks a
completed candidate's recipe/output identities and verifies every independent
evaluation-runtime binary against its pinned retained archive before execution:

```powershell
python -B tools/audio-engine/Test-AudioCandidate.py `
  '<completed FFmpeg workspace>' '<pinned evaluation engine bin>'
```

It records actual codec/container/protocol lists, requiring only file/pipe/fd
protocols, and generates a one-second 48 kHz stereo tone. All six fixed targets
encode and decode with both runtimes, returning exactly 48,000 frames. WAV and
FLAC retain exact samples. Candidate/reference decoded samples match exactly
except MP3, whose maximum difference is below `0.00000009`. Candidate RMS errors
are 0 for WAV/FLAC, 0.000203 for MP3, 0.009912 for AAC, 0.002480 for Vorbis and
0.001744 for Opus. An authored PNG in a FLAC picture block decodes as a 1x1 RGB
frame, exercising zlib-backed PNG decoding. Original hashes remain unchanged.

The passed run is `smoke-39f7e8c8a2ca47c9bee893b70b23c6d2` inside that workspace.
An earlier smoke run stopped on a newly authored peak-error cap of 0.2: AAC's
end-of-clip error peaks at 0.275110 in both decoders, with identical decoded bytes.
The final smoke checker records peaks and follows the product's RMS metric,
using a tighter 0.02 bound for this tone and a separate 0.00001 cross-decoder
difference bound. No product acceptance threshold changed. This single-tone
check does not certify transient quality, listening quality, other rates/layouts,
metadata preservation or the full adapter/worker matrix.

Three candidate-identity refusal checks pass at
`.codex-temp/audio-candidate-guards-ff52b6eff946482192d4dd027399c486`: missing DLL,
changed executable and changed recipe identity all stop before native execution.
All ten retained source archives verify. Edited Python/PowerShell syntax,
whitespace, and repository public-boundary/theme/72-document checks pass.


Compiler findings and next acceptance work
------------------------------------------

The full native build has zero errors and eleven visible diagnostics:

- Three `C4333` warnings at `libavutil/log.c:201,207,208`: ANSI color formatting
  shifts the Windows byte-sized color table. The smoke runner forces plain logs;
  this does not certify all console-color behavior.
- Four `C4101` warnings at `libavutil/ripemd.c:139,196,321,393`: unused local `t`
  variables already annotated `av_unused` in upstream source.
- Three `C4334` warnings at `libavcodec/vlc.c:75,340,555`: 32-bit shifts promoted
  to 64-bit pointer/arithmetic results. Table bits are bounded to 30 before
  allocation, making the signed shift representable. Code lengths are nonzero
  and checked against at most 32 before the unsigned shift by 0 through 31;
  the shifted value then widens into the 64-bit code accumulator. This review
  does not replace malformed-media tests at the public adapter boundary.
- One host-link `D9024` warning: MSVC treats `ffbuild/bin2c_host.o` as an object
  despite its Unix suffix. The host utility and final resource compilation finish.

The existing upstream MSVC recipe also supplies its usual warning exclusions;
this wrapper adds none. The launcher retains an explicitly unaccepted build
receipt, not an automatic diagnostic approval. The hardening follow-up adds a
separate exact diagnostic gate. Then rerun the existing private adapter and
real-worker matrix with explicit candidate
identity handling, preserving the independent decoder. Retain the complete
source/notice/runtime inventory before production staging. No application,
Explorer, installed lifecycle or listening acceptance ran in this checkpoint.


Explicit Windows hardening follow-up
------------------------------------

The next fresh recipe requests `/GS`, `/guard:cf` and `/guard:ehcont` for C
compilation, and `/guard:cf`, `/guard:ehcont`, `/CETCOMPAT`, `/DYNAMICBASE`,
`/HIGHENTROPYVA` and `/NXCOMPAT` at link time. Both compiler and linker settings
matter for [control-flow guard](https://learn.microsoft.com/en-us/windows/win32/secbp/control-flow-guard)
and [exception-continuation metadata](https://learn.microsoft.com/en-us/cpp/build/reference/guard-enable-eh-continuation-metadata).
These flags supplement the existing codec dependency flags without changing
upstream sources or removing assembly. They do not create a process sandbox.

[Check-AudioBuild.py](../tools/audio-engine/Check-AudioBuild.py) checks a completed
current-recipe build. It requires the requested flags to survive in generated
`config.mak`, then checks each runtime file with SDK `dumpbin`: x64, ASLR, NX,
CFG instrumentation and function table, CET compatibility, a nonzero security
cookie and the reviewed local/system/CRT import set. It verifies each inspected
binary against its build inventory and records tool/checker/binary hashes.

The diagnostic gate accepts at most one of each reviewed exact source path,
line, warning code and message, plus the exact host-object warning. Unexpected,
duplicate and misleading-prefix diagnostics fail. It preserves accepted warnings
and their explanations; it does not add warning suppression. Fourteen guard
checks pass at `.codex-temp/audio-build-guards-75250275a6384271b729da9740db03c5`.
These include the actual earlier native log and an existing hardened dependency
executable, plus altered diagnostics/PE reports. They do not substitute for
checking the new FFmpeg binaries.

Run `python -B tools/audio-engine/Check-AudioBuild.py '<completed workspace>'`
before full runtime acceptance. The first hardened run at
`.codex-temp/audio-ffmpeg-a7f2bd726e5f4a99917e0b892dfabf9a` links but is rejected by
that gate: 59 Opus, 15 Vorbis and one Ogg object produce `LNK4291`, reporting
missing exception-continuation metadata. It is not an accepted hardened build.
The [dependency follow-up](audio-dependency-builds.md) adds the required compiler
flags and passes fresh tests. The complete subsequent build is
`.codex-temp/audio-ffmpeg-8982dc9e3a0646f0bc201d7c61784128`. All 10,422 source files
remain unchanged. Native review `build-review-da841646588b43979188b0d6f2f1a20a`
passes all seven PE files and the eleven reviewed diagnostics, with zero linker
continuation warnings and zero errors. The runtime totals 4,844,544 bytes.
Both executable version/loading checks pass. Independent-decoder smoke also
passes at `smoke-91ad240d9c6745daa363fdf7033c7e08`.

Configure emitted one transient `Device or resource busy` message while replacing
its probe source. The retained `configuration-comparison.json` shows no changed
platform/component macros versus the prior hardened configuration; only scratch
paths in its descriptive command string differ. The exact component gate and
all subsequent runtime checks pass. No tool or security configuration was changed
to bypass that transient condition.


Curated adapter and real-worker acceptance
-----------------------------------------

[curated-candidate.json](../tools/audio-engine/curated-candidate.json) identifies
this exact seven-file runtime. The private adapters select either this candidate
or the retained evaluation runtime by the leased probe hash, then require its
complete DLL membership/hashes and matching encoder hash. Arbitrary manifests
cannot add an engine identity. All verified files remain held against writes.

[Test-CuratedAudioAdapter.ps1](../tools/audio-engine/Test-CuratedAudioAdapter.ps1)
runs native review before the complete private suite. Its mandatory independent
decoder is hash-verified and held by the existing adapter. That decoder supplies
test-only artwork pixel comparisons, which require raw-video output unavailable
in the restricted audio build. The candidate performs every production probe,
audio encode/decode and optimization; no preservation case is skipped.

The run at `adapter-9488c92ca80c4436910f1aef653fec39` passes 298 checks: the existing
292 plus six runtime-identity checks. New cases refuse changed probe/library
bytes, missing/extra libraries and a mixed encoder; failed constructors release
their leases, and verified library leases exclude writers. Existing media cases
cover conversion pairs, precision, metadata, artwork, malformed inputs, bounded
processes, cancellation, frame counts and cleanup.

Fresh full application staging is
`artifacts/production-staging/5165575d4c9844bfbef0927dc20f88e0`, with zero managed
warnings/errors and passed curated-image/native-shell/payload checks. It still
excludes the optional audio payload. `Test-AudioWorker.ps1 -CandidateDirectory`
copies the seven pinned files into its own disposable worker payload and retains
results at `worker-0530f7fc740d4d7293cd668529eb1b9d` inside the FFmpeg workspace.
All 116 workflow checks pass: 14 Analyze, 14 FLAC, 16 mixed direct-audio, 52 audio
conversion/publication and 20 direct conversion checks. This includes all 30
cross-format pairs, access changes, preserved originals, output collisions,
cancellation, recovery cleanup and missing-engine behavior.

These results advance local audio acceptance. Complete source/notice/runtime
distribution, player/listening compatibility, remaining metadata variants,
visible/assistive UI and installed lifecycle acceptance remain open. Existing
image contracts were not rerun; the mixed PNG/audio workflows above did run.
No installation, Explorer registration, native recycling, live licensing,
signing or publishing took place.
