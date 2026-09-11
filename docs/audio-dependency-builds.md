Audio Dependency Builds
=======================

Status: isolated x64 Opus, Ogg/Vorbis and LAME source builds tested, 2026-09-11.
These are dependency candidates; the complete curated FFmpeg payload and its
Context Suite conversion matrix remain pending.


Commands and boundaries
-----------------------

Prepare the [pinned source cache](audio-engine-curation.md), then run:

```powershell
.\tools\audio-engine\Build-AudioDependency.ps1 -Dependency Opus -SourceDirectory '<source cache>'
.\tools\audio-engine\Build-AudioDependency.ps1 -Dependency OggVorbis -SourceDirectory '<source cache>'
.\tools\audio-engine\Build-AudioDependency.ps1 -Dependency Lame -SourceDirectory '<source cache>'
.\tools\audio-engine\Build-AudioDependency.ps1 -Dependency LameStable -SourceDirectory '<source cache>'
```

The existing `Build-OpusDependency.ps1` command forwards to the shared runner.
Each run creates fresh repository scratch, verifies archive identity again under
a read lease, and extracts into a separate source directory per input. It uses
existing Visual Studio 2026 x64 tools and Windows SDK 10.0.26100.0, rejects
unreviewed compiler diagnostics, requires the expected CTest count and rechecks
all source files. The codec builds below have no compiler warnings/errors; the
separate [build-tool record](audio-build-tools.md) documents NASM's one reviewed
warning.
Source files, generated headers, objects and evidence stay separate. No
installation, source downloads or production staging run.

`dependency-build.json` records the selected input pins, actual compiler/CMake,
recipe files, configuration, test log and output hashes. The schema now identifies
the dependency and its input array, covering multi-library builds. This scratch
evidence is not a production manifest or proof of bit-for-bit reproducibility.

| Selection | Built components | Checks |
| --- | --- | --- |
| Opus | Static Opus and linked version probe | Five upstream tests and runtime source-version identity |
| OggVorbis | Static Ogg, Vorbis, Vorbis encoder and Vorbis file libraries | Ogg bitwise/framing tests and Vorbis stream/codebook tests |
| Lame | Static core MP3 encoder, including upstream SIMD routines | Authored stereo VBR2 encoding, followed by independent decoding below |
| LameStable | Stable LAME 4.0 core encoder | Exact runtime version, authored VBR2 encoding and independent decoding |

Ogg and Vorbis use their unchanged upstream CMake projects. The wrapper supplies
the Ogg target directly to Vorbis, with static libraries, dynamic CRT and tests
enabled. Generated test audio stays in the build directory. The complete FFmpeg
link must select only the libraries it needs; building `vorbisfile.lib` does not
authorize including unused code in the shipping payload.

The Windows-hardening follow-up adds explicit `/GS`, `/guard:cf` and
`/guard:ehcont` to the Opus, Ogg and Vorbis library targets. Their earlier upstream
defaults did not provide exception-continuation metadata, causing 75 `LNK4291`
warnings when FFmpeg first enabled its corresponding linker check. Those warnings
are rejected rather than added to the diagnostic allowlist. Fresh Opus and
Ogg/Vorbis builds pass all six/four tests with zero compiler diagnostics and
unchanged source at `audio-opus-1710946271d1477ebac190cc945ae4d1` and
`audio-ogg-vorbis-42c6c8b3ffdc4187bdde3fbb6f8b0dd2` under `.codex-temp`.
Test times are 67.68 and 6.16 seconds. The FFmpeg launcher now checks current
dependency recipe-file and retained test-log identities before composition;
the previous two recipes are refused before native execution. See the
[full build follow-up](audio-ffmpeg-build.md) for emitted binary checks.

The LAME wrapper selects 27 core/vector translation units for the supplier alpha,
or 21 for stable 4.0, and copies `configMS.h` into the binary directory. It
excludes frontends, MP3 decoders, drivers and network clients. Dynamic CRT, stack
protection, control-flow guard and x64 continuation protection are selected. This
configuration builds without an external iconv library; that does not prove the
supplier's different full build has no iconv obligation. Upstream configuration
and source remain unchanged, including its compiler-warning policy.


Independent LAME smoke check
---------------------------

```powershell
python .\tools\audio-engine\Test-LameDependency.py '<completed Lame build>' '<pinned evaluation engine bin>'
```

The probe authors one second of 44.1 kHz stereo PCM16: 440 Hz at amplitude 12,000
on the left and 880 Hz at amplitude 9,000 on the right. It uses VBR quality 2,
flushes the encoder and writes the LAME/Xing tag. The separate checker verifies
the evaluation archive plus encoder, probe and every bundled DLL against that
archive before invoking FFmpeg/ffprobe. It also verifies the generated MP3 against
the build record, bounds native calls to 20 seconds and decoding to two seconds,
and checks stream identity, exact decoded sample count and generated-tone error.

The observed MP3 is 11,684 bytes. Decoding returns exactly 44,100 stereo frames at
44.1 kHz, with RMS error `0.0001796571` and peak error `0.0054550022` on normalized
samples. The smoke limits are RMS `0.01` and peak `0.10`; they detect gross encoding,
channel or timing errors for this authored signal. They are not listening criteria,
metadata/artwork acceptance, malicious-input testing or the full product matrix.
Original MP3 bytes remain unchanged.

The supplier source identifies itself as **LAME 4.1 alpha 0**. The separate stable
candidate uses **4.0** from the [official release page](https://lame.sourceforge.io/download.php),
retaining its original release tarball. `LameStable` selects its smaller upstream
vector source list and requires runtime `get_lame_version()` to equal `4.0`.
It passes the same authored encoding and independent decode checks: 11,684 bytes,
44,100 frames, and the same measured errors above. Stable 4.0 is the candidate for
the next curated FFmpeg build; shipping adoption still requires the full audio
matrix. The alpha result remains supplier-comparison evidence.


Verified evidence
-----------------

The recorded builds report zero compiler warnings/errors and unchanged source:

| Dependency | Scratch directory suffix | Source files | Native tests |
| --- | --- | --- | --- |
| Ogg/Vorbis | `audio-ogg-vorbis-60bfc013079d4de3b5a18641f667615e` | 546 | 4 passed, 5.90 seconds |
| Opus regression | `audio-opus-d83484e46cff4b89bd481cf99ce8281b` | 752 | 6 passed, 68.52 seconds |
| LAME | `audio-lame-9b7197bf07d845bc8fcd5288592e5d21` | 418 | Authored encoding passed |
| Stable LAME 4.0 | `audio-lame-5b1849b0f6af4718acce8907caadddae` | 316 | Runtime version and authored encoding passed |

Directories are under `.codex-temp`. LAME's independent result is in
`independent-check-c7116742de3542c09920bff77e098914` beneath that build. The checker
also refuses changed fixture bytes and an unpinned engine archive before media
processing, leaving the original fixture unchanged. Those two checks are retained
at `.codex-temp/lame-check-refusals-2926dca30cad49d0b17b9984c123c732`.
Earlier shared-runner Ogg/Vorbis and Opus runs are retained separately; the table
identifies the final runner revision. Source/output inventories and logs remain
beside each build, with Python/PowerShell syntax and repository checks recorded.
No private adapter, production engine pin, customer media, installed state or UI
acceptance changed.

The stable build's independent result is
`independent-check-d7f574a245164070a743e8cd70edaeb7`. Its manifest also hashes the
release-tar reader. The earlier stable build at
`audio-lame-05080d80a7ca49f098b4b0497ea386a0` passed before adding the explicit
runtime-version assertion; the table identifies the final recipe. Previous ZIP
build evidence remains historical and was not rerun for the new tar branch.


Next build work
---------------

Compose the restricted FFmpeg build with stable LAME 4.0 and run
the existing complete audio adapter/worker matrix against it. Retain the compiler,
CRT, linked-component, source and notice inventory before production adoption.

[FFmpeg's Windows instructions](https://ffmpeg.org/platform.html#Microsoft-Visual-C_002b_002b-or-Intel-C_002b_002b-Compiler-for-Windows)
support MSVC with a Unix-like build environment and assembler. GNU make and NASM
now have pinned original release source archives and tested repository-local
executables: GNU make 4.4.1 and NASM 3.02. See the [build-tool commands and checks](audio-build-tools.md),
including recursive make's short-path requirement and NASM's reviewed diagnostic.
The existing Git Bash installation alone has no make executable. Do not install a toolchain
or remove required codec behavior just to get a build to pass.

The pinned FFmpeg configure script rejects whitespace in an out-of-tree source
path. A read-only check confirms this checkout has a whitespace-free Windows
short path, and Git Bash preserves that spelling in `pwd`. That offers a local
path to evaluate without creating an external junction or drive mapping; it is
not yet proof that the complete configure/build succeeds.
