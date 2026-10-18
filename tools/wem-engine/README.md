Experimental WEM decoder build
==============================

`Build-VgmstreamDecoder.ps1` builds vgmstream CLI r2117 from the pinned upstream
source archive at commit `71e2361042531fe767fb98300cf8c1ee95e539a0`. It checks the archive
SHA-256 (`82D81CC5EE7A085427F75D9886703976B15F51309ED24532A72BE5A27467006B`),
extracts it only into a fresh `.codex-temp` directory, applies the adjacent
scratch-source patch, removes unrelated RELIC, CompressWave, and MIO files,
creates a sanitized corresponding-source ZIP, and builds x64 Release with
Visual Studio and CMake. It does not install or package the decoder outside
scratch. The patch points CMake's Vorbis install path at the pinned x64
`libvorbis.dll`; the x86 DLL and unrelated prebuilt binaries are excluded from
the sanitized source archive. The upstream archive can be downloaded from the immutable
`https://codeload.github.com/vgmstream/vgmstream/zip/71e2361042531fe767fb98300cf8c1ee95e539a0`
URL. Its size is 9,258,749 bytes. For example:

```powershell
$archive = '.codex-temp/wem-engine-research/vgmstream-71e2361042531fe767fb98300cf8c1ee95e539a0.zip'
Invoke-WebRequest -Uri 'https://codeload.github.com/vgmstream/vgmstream/zip/71e2361042531fe767fb98300cf8c1ee95e539a0' -OutFile $archive
(Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash
```

Compare the printed hash with the pinned SHA-256 above. No vendor source or
binary is checked into `tools/wem-engine`.

Run from the repository root:

```powershell
./tools/wem-engine/Build-VgmstreamDecoder.ps1
python ./tools/wem-engine/Test-VgmstreamDecoder.py --decoder <scratch>/install/bin/vgmstream-cli.exe --fixtures <Wwise-fixture-directory> --output <new-scratch-directory>
```

The build registers only the Wwise file initializer and strips RELIC and
CompressWave sources from compilation. A linker-map check rejects those
objects plus MIO objects; other generic decoder objects remain linked through
vgmstream's core decode switches. The validation corpus covers Wwise PCM16 and
Vorbis from synthetic 44.1/48 kHz mono/stereo inputs. This is a narrow tested
subset, not a claim of support for every WEM codec or Wwise version.
The scratch `install/provenance` directory contains the pinned corresponding
sanitized source archive, this build recipe and patch, full upstream/GNU/Xiph notices,
linked embedded-source notice excerpts, the linker map, and a JSON receipt of
file hashes, compiler options, linked objects, and runtime imports. A release
candidate should copy and review that exact provenance folder alongside its
separate decoder executable.
The sanitized ZIP retains only `ext_libs/dll-x64/libvorbis.dll` among upstream
`.dll`, `.exe`, `.lib`, and `.a` files; CMake generates its x64 import library
from `ext_libs/libvorbis.def` during the build.
The original upstream ZIP remains in local scratch and is **not** copied into
the provenance folder because it contains excluded source files. To rebuild
using only the sanitized ZIP delivered with a candidate package, use the hash
from `build-receipt.json`:

```powershell
./tools/wem-engine/Build-VgmstreamDecoder.ps1 `
  -SanitizedSourceArchive '<install>/provenance/source/<archive>.zip' `
  -SanitizedSourceHash '<receipt SHA-256>'
```

Dependency provenance and release gate
--------------------------------------

- vgmstream source and its `COPYING`: [pinned upstream commit](https://github.com/vgmstream/vgmstream/tree/71e2361042531fe767fb98300cf8c1ee95e539a0).
- GNU Getopt is copied within upstream `ext_libs/Getopt` and statically linked
  by the CLI. Its headers state LGPL 2.1 or later. The pinned archive includes
  the LGPL 2.1 text at `ext_libs/licenses/ffmpeg.COPYING.LGPLv2.1`.
- The pinned archive's x64 `libvorbis.dll` and its embedded Ogg dependency
  require the Xiph notices at `ext_libs/licenses/libvorbis-1.3.7.COPYING` and
  `ext_libs/licenses/libogg-1.3.5.COPYING`.
- Linked vgmstream objects also include `src/util/miniz.c` (MIT notice in that
  file), `src/coding/libs/libacm_*.c` (ISC notice in those files),
  `src/coding/libs/nwa_lib.c` (permissive notice in that file),
  `src/coding/g721_decoder.c` (Sun unrestricted-use notice), and
  `src/coding/libs/utkdec.h` (public-domain origin notice). These notices need
  to be preserved in release documentation alongside upstream `COPYING`.

The scratch bundle contains the corresponding source, patch, recipe, LGPL
text, Xiph notices, and linked-code excerpts for a review candidate. A
production package must carry those materials with the separate decoder
executable, confirm the final linked-object inventory and notice coverage,
and satisfy GNU Getopt's LGPL source/relink requirements for its distribution.


Worker and payload verification
-------------------------------

`Stage-WemPayload.py` admits the exact reviewed scratch distribution, including
the decoder, Vorbis DLL, notices, build recipe, linker map and corresponding
source. It verifies all 17 files against the pinned receipt and stages only into
a fresh engine directory in repository scratch or a new production stage.
`--inventory` verifies an existing engine tree and, when present, its entries in
the outer payload inventory. Unknown files, changed notices and linked paths
are rejected.

With the private checkout and existing curated image prerequisites available,
run the actual worker/application publication checks from the repository root:

```powershell
./tools/wem-engine/Test-WemWorkflow.ps1 `
  -DecoderDistribution '<scratch>/install' `
  -Fixtures '<Test-WwiseConversion.py output with --loops>' `
  -WwiseConsole '<installed Authoring>/x64/Release/bin/WwiseConsole.exe' `
  -AudioDistributionDirectory '<verified curated audio distribution>'
```

The test builds into new `.codex-temp` output, stages the exact decoder and source
bundle, and exercises the existing batch, worker and transactional publisher.
It never installs or registers the application. Current decode admission is
little-endian PCM16/Vorbis, mono/stereo at 44.1/48 kHz, including supported forward
loops. Omitting `-WwiseConsole` runs the decode checks only; supplying it also
runs actual encoding/setup-resume/publication checks without FFmpeg. Adding
`-AudioDistributionDirectory` then stages the verified audio payload and exercises
MP3 and mixed-input quality-consent workflows. Wwise is never bundled.
The tested version is 2023.1.10.8659. Encoding accepts PCM16/float32 WAV and MP3,
mono/stereo at 44.1/48 kHz, up to 64 MiB, with a supported WAV forward loop and no
unknown metadata. Float32 and MP3 preparation uses PCM16 after explicit precision
consent; MP3-to-Vorbis also requires lossy-transcoding consent. Both are combined
into one quality prompt for the batch. Match original uses only codec/rate/channels, with rate and
channels required to agree already. Advanced uses PCM As Input or Vorbis Quality
High. The adapter validates exact PCM samples or decoded Vorbis samples, frame
count and loops before publication. Each channel's Vorbis RMS error must be at
most 0.15 full scale and at most half of source RMS for non-silent sources.

Vorbis WEM decoding preserves precision as float32 WAV. That WAV can be selected
for WEM conversion directly: bounded PCM16 preparation and cleanup are internal.
Nonfinite/out-of-range float samples are refused instead of clipped. MP3 tags
and artwork that WEM cannot preserve are refused; codec recognition alone does
not authorize their omission. WAV encoding does not require the audio runtime.
Codec recognition and a detected installation do not prove target-game support.

Future production/release builds can explicitly pass `-WemDistributionDirectory`
to their existing scripts. That still requires a new version/staging identity
and their full acceptance checks. The bundled notice index links the WEM notices,
build instructions and source archive. Isolated stage checks do not establish
in-game compatibility or release acceptance.
