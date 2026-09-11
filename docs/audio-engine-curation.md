Audio Engine Curation
=====================

Status: source inputs partly retained, 2026-09-11. The tested audio adapter and
direct commands remain optional because normal production staging has no adopted
audio payload. This work does not enable an unreviewed supplier bundle.

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
sizes, immutable revisions and expected source/license entries for five inputs:

| Input | Revision | Evidence |
| --- | --- | --- |
| FFmpeg | `9b0578816c6f94514d330d4f2ae7e44a9fb42692` | Full commit resolved from the evaluated binary's abbreviated revision |
| Supplier build recipe | `847e5e1cacc2945ac46528d34d754bd36051680c` | Commit resolved from the pinned supplier release tag |
| Ogg | `06a5e0262cdc28aa4ae6797627a783b5010440f0` | Exact source revision declared by that recipe |
| Vorbis | `1b75110b5a2754ba1931d82dd83cb822b266a21d` | Exact source revision declared by that recipe |
| Opus | `3da9f7a6db1c05c3996cb363a9d1931a978bf1be` | Exact source revision declared by that recipe |

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
or script execution. `-VerifyOnly` performs no downloads or output writes.

Outstanding source and build inputs
----------------------------------

- LAME uses upstream SVN revision 6761 at
  `https://svn.code.sf.net/p/lame/svn/trunk/lame`; this exact source is not retained
  yet. Its supplier recipe declares a libiconv dependency. Do not substitute a
  release tarball and claim it reproduces the evaluated binary.
- Opus `autogen.sh` unconditionally invokes a downloader for a separate model
  archive with SHA256
  `a5177ec6fb7d15058e99e57029746100121f68e4890b1467d4094aa336b6013e`.
  That archive is not retained or reviewed. The downloader can skip verification
  if checksum tools are absent; our build must not permit that fallback. Review
  whether the chosen build path needs this input, including generated-source
  dependencies, before running bootstrap scripts. No model code or bootstrap ran.
- Pin the actual compiler, assembler, build tools, CRT and every linked library;
  retain source/configuration/patches/notices and output inventories. The five
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
describes a Docker-based build. No Docker/MSYS2/compiler installation, upstream
build script, production build, customer media or new native engine ran during
this retention checkpoint. The existing evaluation media evidence remains valid
only for its pinned binaries and stated fixtures.

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
archives remain retained. Edited PowerShell syntax, manifest JSON and repository
source-boundary/theme/documentation/whitespace checks pass. No media-engine,
worker or visible-UI tests were rerun for this source-retention tool change.
