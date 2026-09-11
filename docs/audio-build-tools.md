Audio Build Tools
=================

Status: repository-local GNU make 4.4.1 and NASM 3.02 built and checked,
2026-09-11. These are build tools, not customer payload components. The complete
restricted FFmpeg build and Context Suite audio matrix remain pending.

Commands and source boundaries
-----------------------------

Prepare the nine [pinned source archives](audio-engine-curation.md), then use the
existing x64 Visual Studio 2026 tools and Windows SDK 10.0.26100.0:

```powershell
.\tools\audio-engine\Build-AudioDependency.ps1 -Dependency Make -SourceDirectory '<source cache>'
.\tools\audio-engine\Build-AudioDependency.ps1 -Dependency Nasm -SourceDirectory '<source cache>'
```

The shared runner verifies archives before extraction, builds in fresh scratch,
runs the required tests and rechecks every upstream file. It records compiler,
CMake, recipe, source-reader, configuration, source/output and test-log identities
in `dependency-build.json`. Neither command installs software, changes Explorer,
modifies upstream source or refreshes a production payload. No upstream cleanup
or installation target runs. The authored wrappers and checks are in
[make](../tools/audio-engine/make/CMakeLists.txt) and
[nasm](../tools/audio-engine/nasm/CMakeLists.txt).

GNU make
--------

The wrapper follows the release's `build_w32.bat` MSVC source selection and uses
its unchanged Windows configuration. Config/glob/fnmatch headers are copied into
the binary directory. Static CRT, upstream `/W4` policy and Windows hardening are
selected; Guile discovery is absent. The source's own warning configuration stays
unchanged. This build has zero compiler warnings/errors.

Two CTest cases pass: release-version identity and an authored workflow using the
existing Git Bash. The workflow checks included variables, shell quoting,
parallel prerequisites, ordered combined output, recursive make, an incremental
no-op and propagation of a recipe's nonzero exit. A subsequent FFmpeg integration
check adds a nested backslash/awk regression. Every generated file stays in
the workflow directory. Child environment changes do not persist in Windows.

An initial test exposed recursive `$(MAKE)` expansion breaking at the space in
the executable path. The workflow now obtains the existing Windows short path
for make and Bash; it refuses missing or still-spaced paths. The upcoming FFmpeg
build must use that spelling for make as well as its source/build paths. This is
not a claim that arbitrary whitespace in upstream makefiles is supported.

The FFmpeg follow-up also exposes nested quoting that the original smoke did not
cover. `HAVE_CYGWIN_SHELL` does not fix it with this Git Bash version. The final
recipe selects upstream's documented `BATCH_MODE_ONLY_SHELL`: make writes complete
recipes into temporary scripts, preserving the awk expression. The new regression
and all earlier checks pass at `audio-make-6c3e7d5d0bb44a18b065ab047957b9d9`, with
zero compiler diagnostics and unchanged source. `TMP`, `TEMP` and `TMPDIR` point
inside each workflow/build workspace. This supersedes the table's initial make
recipe for the full FFmpeg build.

NASM
----

The wrapper selects the assembler's common, preprocessing, instruction, output
and bundled zlib source files from `Mkfiles/msvc.mak`. It uses the release's
distributed generated sources without rerunning Perl. The disassembler and
installer are not built. Static CRT, upstream `/W2` policy, C11 and Windows
hardening are selected. The upstream long-path manifest is embedded and checked
in the actual executable.

Two MSVC compatibility fixes stay in repository-owned configuration:

- `nasmlib/file.c` gets `windows.h` before its direct `stringapiset.h` include,
  establishing the SDK's architecture definitions.
- [MsvcCompatibility.h](../tools/audio-engine/nasm/MsvcCompatibility.h) gives
  header inline copies internal linkage. Upstream maps `inline` to `__inline`
  while selecting C99 inline semantics in C11 mode; this toolchain otherwise
  emits duplicate external `ilog2` definitions. The upstream `ILOG2_C` branch
  still emits the separate external implementation.

One compiler warning remains visible and reviewed: `C4319` at
`output/outmacho.c:1295`, where Mach-O relocation-offset alignment widens a
32-bit complement mask. The tool's tested purpose here is Windows COFF assembly;
this does not accept Mach-O behavior or offsets above 4 GiB. The runner accepts
at most that one exact source location/code for the pinned NASM build and records
the diagnostic and review in its manifest. Other warnings and all errors still
stop acceptance. No warning is hidden with a compiler suppression flag.

Two CTest cases pass: NASM version identity and assembly/link/execution of an
authored Win64 COFF probe. The resulting object links with MSVC, and its scalar
addition and SSE2 four-lane sum return exact expected values. These are build-tool
smoke checks, not the full upstream NASM suite or FFmpeg's SIMD acceptance.

Evidence and next work
----------------------

Final runs under `.codex-temp`, with matching current recipe/runner hashes:

| Tool | Directory | Unchanged source files | Results |
| --- | --- | --- | --- |
| GNU make | `audio-make-0dd6a9b027614517aaa84cf9a9d2f3f7` | 395 | Two tests passed; zero compiler warnings/errors |
| NASM | `audio-nasm-88792fddc1d44f5aadd2a471dfe2ca27` | 1,375 | Two tests passed; one reviewed warning, zero errors |

Make's detailed results are in `build/workflow/result.json`; NASM's emitted
object and probe are inventoried, and `embedded-manifest.xml` records the SDK
manifest extraction check. Eight checks of the actual diagnostic gate pass at
`build-diagnostic-guards-58b561e89c3b4454bc95bbe7cfc0757e`: clean and reviewed
diagnostics accepted; other dependency/source, duplicate warning, unexpected
warning, error and misleading prefix refused.

Earlier failed attempts remain as diagnostic evidence: make
`audio-make-853f89d859344e7f976ece8c4b0c6f04` exposed the recursive path issue;
NASM `audio-nasm-e6b340364a604deeb0426476e2cec147` exposed the SDK include issue,
and `audio-nasm-b0ec6442c4574ad2a15f308a8057c17f` exposed duplicate definitions.
Intermediate successful runs also remain; the table identifies the final recipes.

Next, compose the restricted FFmpeg configuration using these tools and the
[tested codec dependencies](audio-dependency-builds.md), then rerun the full audio
adapter/worker, metadata, sample-count and recovery matrix. Retain the complete
source, notices, build instructions and linked-runtime inventory before adopting
the payload. No product/UI, installed-shell or listening acceptance is implied
by this build-tool checkpoint.
