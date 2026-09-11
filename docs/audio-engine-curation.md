Audio Engine Curation
=====================

Status: six source inputs retained and an isolated Opus dependency recipe added,
2026-09-11. The tested audio adapter and direct commands remain optional because
normal production staging has no adopted audio payload. This work does not enable
an unreviewed supplier bundle.

Why staging still excludes the evaluation engine
------------------------------------------------

The [evaluated shared build](audio-engine-evaluation.md) provides the required six
audio targets and tested worker/publication workflows. Its compiled dependency
set also contains video, device and network functionality outside this product's
audio boundary. Runtime format/protocol restrictions remain necessary, but do not
remove that compiled code or its source/notice obligations. The broad supplier
package therefore cannot become the curated payload merely by copying its bin
directory or changing an allowlist.

Retained inputs
---------------

[source-inputs.json](../tools/audio-engine/source-inputs.json) pins archive hashes,
sizes, immutable revisions and expected source/license entries for six inputs:

| Input | Revision | Evidence |
| --- | --- | --- |
| FFmpeg | `9b0578816c6f94514d330d4f2ae7e44a9fb42692` | Full commit resolved from the evaluated binary's abbreviated revision |
| Supplier build recipe | `847e5e1cacc2945ac46528d34d754bd36051680c` | Commit resolved from the pinned supplier release tag |
| Ogg | `06a5e0262cdc28aa4ae6797627a783b5010440f0` | Exact source revision declared by that recipe |
| Vorbis | `1b75110b5a2754ba1931d82dd83cb822b266a21d` | Exact source revision declared by that recipe |
| Opus | `3da9f7a6db1c05c3996cb363a9d1931a978bf1be` | Exact source revision declared by that recipe |
| LAME | SVN `6761` | File contents exported from the recipe's fixed upstream revision |

Sources were independently downloaded, without reference binaries or source
copying. The retained supplier recipe is read-only research and has not been
executed. It checks out the FFmpeg branch at build time, so the recipe commit alone
does not pin the FFmpeg source; the separately resolved commit matters. This is
an input-retention record, not proof of a byte-reproducible supplier build or a
complete corresponding-source distribution.

The [preparation script](../tools/audio-engine/Prepare-AudioSources.ps1) downloads
only the exact pinned archives into repository scratch. Existing files must match;
there is no fallback to latest and no overwrite of a mismatched cache. It holds a
read lease while checking size/hash and required ZIP entries, without extraction
or upstream script execution. `-VerifyOnly` performs no downloads or output writes.

LAME uses the independently authored [HTTP exporter](../tools/audio-engine/Download-LameSource.py)
because its pinned source is in SVN. It requires Python 3.14 or later and reads
only the fixed revision endpoint, with bounded directory/file sizes, four
concurrent transfers and path/link checks. It retains all 418 files and a
per-file size/hash inventory, then writes a sorted, uncompressed ZIP with fixed
metadata. The wrapper verifies the final 7,747,642-byte archive against SHA256
`C9FF77F7A92E64A21E6CC77F1DE8F1CA4652743F8BB4A5354E6760157525954C`.
This is a file-content export, not SVN properties or history. Source fixtures are
retained without playback or execution; partial failures stay in scratch.

Outstanding source and build inputs
----------------------------------

- Review whether omitted LAME SVN properties affect the build. Its supplier
  recipe declares a libiconv dependency; retaining source file contents alone
  does not resolve that dependency or reproduce the evaluated binary.
- Opus `autogen.sh` unconditionally invokes a downloader for a separate model
  archive with SHA256
  `a5177ec6fb7d15058e99e57029746100121f68e4890b1467d4094aa336b6013e`.
  That archive is not retained or reviewed. The downloader can skip verification
  if checksum tools are absent; our build must not permit that fallback. Review
  whether the chosen build path needs this input, including generated-source
  dependencies, before running bootstrap scripts. The preliminary CMake build
  below needs no model download with the neural features disabled; this does not
  establish the stock supplier bootstrap's source completeness.
- Pin the actual compiler, assembler, build tools, CRT and every linked library;
  retain source/configuration/patches/notices and output inventories. The six
  archives do not cover the supplier package's other enabled components.
- Establish the actual configuration and license/notice inventory of the curated
  binary. FFmpeg's [upstream guidance](https://ffmpeg.org/legal.html) calls for
  corresponding source and build instructions; the applicable terms depend on
  enabled components. This record is not redistribution clearance.

Proposed bounded build and acceptance
------------------------------------

Build only ffmpeg/ffprobe and the libraries needed by the existing adapter:
WAV/FLAC/MP3/MOV-M4A/Ogg input, PCM/FLAC/AAC/Vorbis/Opus/MP3 audio decoding, the six
selected encoders and required audio resampling/filter infrastructure. Preserve
float64 PCM decode comparison and the current float/integer WAV representations.
Keep the fixed LAME/Vorbis/Opus/AAC/FLAC settings already under test. Required
I/O includes stdin/stdout pipes, held input descriptors and reserved file output;
fast-start M4A needs seekable output behavior. The existing metadata/artwork paths
must remain testable without silently dropping information.

Start with automatic external-library detection and unrelated components disabled,
then enable an explicit reviewed list. Exclude ffplay, capture devices, network
protocols, video encoding and unrelated external libraries. Review every additional
component selected by dependencies. These are build requirements, not a tested
configure command; actual generated component lists and binary import/link
evidence must prove them. Removing a required codec to obtain a smaller build does
not satisfy the six-format goal.

After source retention and a reproducible repository-local build path are ready:

1. Produce a new isolated candidate with configuration, toolchain, source and
   output hashes. Do not overwrite the evaluated payload or installed/development
   staging. No toolchain installation is authorized by this document.
2. Run the existing adapter and real-worker matrix against the new binary,
   including sample counts, metadata/artwork, gapless behavior, FLAC seek tables,
   malformed files, cancellation and recovery. Earlier supplier-binary passes do
   not validate a changed codec build. Resolve independent decoding/listening
   and native process-isolation gaps explicitly.
3. Adopt only that verified candidate through the engine staging/runtime guards
   and production payload/notice allowlist, preserving missing-engine behavior,
   paid admission, copies, direct actions and the user-facing capability matrix.
4. Record fresh normal isolated packaging and visible acceptance separately from
   installer, signing, live commerce and commercial distribution gates.

The [supplier's build documentation](https://github.com/BtbN/FFmpeg-Builds/tree/847e5e1cacc2945ac46528d34d754bd36051680c)
describes a Docker-based build. No Docker/MSYS2/compiler installation, production
build or customer-media processing ran. The preliminary dependency build below
uses existing Visual Studio tools. The existing evaluation media evidence remains valid
only for its pinned binaries and stated fixtures.

Opus dependency recipe
----------------------

[Build-OpusDependency.ps1](../tools/audio-engine/Build-OpusDependency.ps1) verifies
the retained source cache, extracts the pinned archive into a fresh repository
scratch directory and builds with existing Visual Studio 2026 x64 tools and
Windows SDK 10.0.26100.0. It records source/output hashes, actual compiler and
CMake identities, recipe/configuration hashes and retained logs. It neither
installs software nor updates a production payload. This is a repeatable build
recipe; bit-for-bit reproducibility across machines is not established.

The repository-owned [CMake wrapper](../tools/audio-engine/opus/CMakeLists.txt)
keeps upstream files unchanged. Static library/tests are enabled, dynamic CRT
linkage retained, and programs, shared library, neural features, fixed point,
float approximation and fast math disabled. Hardening and stack protection stay
enabled. It refuses neural source entries in the library target.

Two issues from the preliminary build below are corrected:

- Version identity is `1.6.1-contextsuite-g3da9f7a6db1c`. Upstream
  [ancestry evidence](https://github.com/xiph/opus/compare/22244de5a79bd1d6d623c32e72bf1954b56235be...3da9f7a6db1c05c3996cb363a9d1931a978bf1be)
  places the pinned revision 50 commits after the 1.6.1 tag, with no commits
  behind. The suffix identifies this source snapshot; it does not claim an
  unmodified 1.6.1 release. A separately linked executable checks the library's
  runtime version string, supplementing the five upstream codec tests.
- The pinned upstream CMake sets its clang-cl flag to `false BOOL`. CMake's
  [normal-variable syntax](https://cmake.org/cmake/help/latest/command/set.html)
  joins those values into a list rather than a typed Boolean, so that condition
  enables a GCC-only flag under MSVC. The wrapper removes `-msse4.1` from the six
  affected sources using [target-directory source properties](https://cmake.org/cmake/help/latest/command/set_source_files_properties.html).
  SSE runtime dispatch and other compiler options remain enabled. Source files
  are not patched and compiler diagnostics are not suppressed.

The wrapper requires all six tests to pass with a 120-second per-test deadline,
rejects compiler warnings/errors and rechecks every extracted source file after
execution. It rejects missing tools and nonzero native exits rather than reusing
a stale exit code. Full FFmpeg/dependency linkage, the Context Suite audio matrix,
final source/notice/toolchain inventory and production adoption remain separate.

The final recipe passes a fresh build with zero compiler warnings/errors and all
six tests in 67.59 seconds at
`.codex-temp/audio-opus-fe2313bd2762482aafe5f9a2089d4f1a`. Runtime version identity
matches, all 752 source files remain unchanged, and the recorded recipe/script
hashes match the reviewed worktree. `dependency-build.json` and configuration,
build, test and version logs remain there. The initial wrapper run also passed
six tests at `.codex-temp/audio-opus-6f5b83cbf99b43b299cef79ad29c79b2`.

Two input checks refuse an outside-scratch cache and a same-length modified Opus
archive before extraction/build; evidence is at
`.codex-temp/opus-build-refusal-5e91ed3d6a7d499d8162a380538b765b`. Three launcher
checks accept zero exit, reject nonzero exit and reject a missing executable at
`.codex-temp/opus-launcher-guards-3ba30785020e4899a142ca802cefcfa2`.
The tests caught and corrected an exit-code scoping error introduced while adding
that safeguard. The intermediate attempt at
`.codex-temp/audio-opus-8dd269a08547419f83c1a780e4ada221` stopped after configuration;
it is not a passed build. All evidence stays in repository scratch. PowerShell
syntax, new-file whitespace and repository source-boundary/theme/69-document
checks pass. Context Suite engine/worker, UI and installed-shell tests did not run.

Preliminary Opus dependency build
--------------------------------

An out-of-source x64 Release CMake/MSVC build succeeds using the retained Opus
archive and existing Visual Studio 2026 tools. It builds a static library and
upstream tests, with hardening on and programs, shared library, fixed point,
float approximation, deep PLC, DRED and OSCE disabled. No autogen/model bootstrap
or toolchain installation ran. All 752 extracted files remain byte-identical to
the archive, with no added source files. The generated library project has no
`dnn` source entries.

All five upstream CTest tests pass: decode, padding, API, encode and extensions
(74.33 seconds total, sequential, with a 120-second per-test limit). This is codec
dependency evidence, not the Context Suite conversion matrix. Two build issues
were found in this preliminary run: archive version detection falls back to `0`,
and MSVC reports D9002 for an ignored `-msse4.1` option.
The subsequent wrapper above addresses both findings; final toolchain/link
inventory and the complete audio candidate remain pending.

The build is retained at
`.codex-temp/audio-native-2bf17687968146ef9d4a32b04355c76f`, with configuration,
build and test logs in `.codex-temp/opus-native-{configure,build,tests}.log`.
`.codex-temp/audio-source-build-review.json` records source immutability and the
LAME per-file archive check. No production payload or private adapter pin changed.

Verification and retained evidence
----------------------------------

The first independently downloaded five archives (31,418,422 bytes total) are at
`.codex-temp/audio-source-78aa53f8e26a4accb9c070369ef21ed4`. Its `recipe` directory
contains the separately extracted read-only supplier source for inspection.
`Prepare-AudioSources.ps1 -VerifyOnly` passes all five size/hash/entry checks there.
The complete preparation script also downloads and verifies a fresh copy at
`.codex-temp/audio-source-84ce08fdae1847a2a859962eac104650`, with log
`.codex-temp/audio-source-download.log`.

Four negative checks pass: a cache outside repository scratch, a missing archive,
a wrong-length copy and a same-length corrupted copy are refused. These tests
operate only on disposable copies at
`.codex-temp/audio-source-contracts-8774fbbcdb3b4fe7b49be38accc01a40`; original
archives remain retained. For that first checkpoint, edited PowerShell syntax,
manifest JSON and repository source-boundary/theme/documentation/whitespace checks
passed; media-engine, worker and visible-UI tests were not rerun.

The LAME follow-up verifies all six archives (39,166,064 bytes total) in the
original cache, including every exported LAME file against its retained inventory.
The preparation wrapper downloads LAME again into
`.codex-temp/audio-source-lame-c8db9921501d46a1ab7d3bc0c68f0a89`, reusing verified
copies of the other five archives. The fresh export reproduces the exact pinned
LAME ZIP size/hash; all six wrapper checks pass. Its log is
`.codex-temp/audio-source-lame-download.log`.
Five new exporter guard checks pass: outside scratch, existing archive, wrong
filename, parent traversal and a junction path. They leave the original archive
unchanged and create no files through rejected paths. Evidence is retained at
`.codex-temp/lame-guards-19e443ad443a41a9b0ae769645ebd5e4` and
`.codex-temp/lame-link-guard-22193c02ba75400ba4b88183acc53d21`.
Python/PowerShell syntax, manifest parsing and the repository's 69-document,
source-boundary, theme and whitespace checks pass. The Opus tests above are new;
Context Suite media/worker and visible-UI suites were not rerun.
