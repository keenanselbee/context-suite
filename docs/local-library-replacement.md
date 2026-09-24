Local Library Replacement
=========================

Status: HEIF and audio opt-in implemented and tested on 2026-09-23. Fresh
candidate inclusion and recipient source delivery remain pending. This advanced
local workflow leaves ordinary commands/settings unchanged. It does not approve
component adoption, product terms or commercial distribution.

Default native identities remain enforced. An explicit
`ContextSuite.LocalLibraries.json` beside an engine's DLLs permits compatible
rebuilt libraries without changing the private application or bridge. Fixed
formats, quality policies, access checks, input limits, output validation and
publication rules remain in force.


Using rebuilt libraries
-----------------------

Use a separate, writable copy of the extracted application. Close that copy of
Context Suite before changing its libraries. Keep the original release archive
and source companion so the complete original directory can be restored. No
installer, Explorer registration or system library change is needed.

1. Rebuild the libraries from the matching source kit, following its `BUILD.md`.
   Keep the ABI and exports compatible with the shipped executable/bridge. The
   [HEIF](heif-color-fallback.md#source-delivery-and-retained-failures) and
   [audio](expanded-audio-distribution.md#extracted-kit-rebuild) records identify
   the selected kits and successful standalone rebuilds.
2. In the application copy, replace `heif.dll` and/or `libde265.dll` inside `heif`,
   or the FFmpeg DLLs inside `audio-engine`. Keep `ContextSuite.Heif.Native.dll`,
   `ffprobe.exe` and `ffmpeg.exe` unchanged. Audio DLLs may contain rebuilt static
   codec dependencies from the source kit. Include any additional compatible DLL
   dependencies beside them.
3. Use [New-LocalLibraryManifest.ps1](../tools/New-LocalLibraryManifest.ps1) to
   record the selected DLL hashes:

```powershell
.\New-LocalLibraryManifest.ps1 -Engine Heif -EngineDirectory 'C:\LocalApps\ContextSuite\heif'
.\New-LocalLibraryManifest.ps1 -Engine Audio -EngineDirectory 'C:\LocalApps\ContextSuite\audio-engine'
```

The script only creates the manifest. It does not build, download, copy or execute
native code, replace files, or overwrite an existing manifest. Creating the file
explicitly opts into running the selected libraries; hashes identify their bytes,
not their publisher or correctness. At first use, Context Suite checks the
unchanged anchor against its compiled selection.

Restart the application copy and exercise representative files through normal
commands. Native libraries and the manifest stay locked while in use. Before
another change, close the app, preserve/remove the old manifest explicitly, and
generate a new one. Removing only the manifest restores default enforcement;
operation also requires restoring the original DLLs. Restoring the complete
original application directory is simpler.

This implementation is newer than reserved candidate 1.1.1 and must be included
in the next candidate. Retained source-kit `BUILD.md` files predate this route;
their statement that rebuilt identities are not accepted automatically remains
true. This explicit opt-in supplies the application path. Source delivery still
needs to collect these current instructions and the generator alongside the exact
source/runtime mapping. Retained source archives were not altered.


Manifest and lifetime rules
---------------------------

The UTF-8 JSON object has exactly four fields:

```json
{
  "formatVersion": 1,
  "anchorName": "ContextSuite.Heif.Native.dll",
  "anchorSha256": "<64 hexadecimal characters for the unchanged bridge>",
  "libraries": {
    "heif.dll": "<64 hexadecimal characters>",
    "libde265.dll": "<64 hexadecimal characters>"
  }
}
```

The audio anchor is `ffprobe.exe`; its original hash still selects the audio
profile and independently verified `ffmpeg.exe` identity. Every original DLL
name is required. At most 32 DLLs are allowed, including declared supporting
dependencies. Names are ASCII DLL basenames up to 128 characters, using letters,
digits, hyphens, underscores and single dots. Paths, streams, executable entries,
duplicate names and anchor overrides fail.

The parser rejects unknown/duplicate fields, missing fields, invalid hashes,
wrong versions, comments, trailing commas and JSON over 64 KiB or depth eight.
An absent manifest uses the original pins. An invalid, unreadable or linked
manifest fails closed; it never silently falls back. Verified file handles and
ordinary ancestor-directory handles remain open for the engine lifetime. HEIF
retains loaded-module path checks. Supporting DLLs need not be eagerly loaded.

Official staging/release verifiers still reject local manifests and changed
libraries. A recipient-modified copy cannot pass as the original package merely
by adding a matching manifest or rewriting its inventory.


Verification evidence
----------------------

[Test-LocalLibraries.py](../tools/Test-LocalLibraries.py) builds the test hosts and
uses disposable copies supplied with `--heif`, `--heif-replacements`, `--audio`,
`--audio-replacements` and `--fixture`. Inputs must be in repository scratch.
Replacement DLL bytes must differ from originals. The harness checks all source
inputs and runtime copies afterward.

Evidence beneath `.codex-temp`:

| Check | Evidence | Result |
| --- | --- | --- |
| Manifest and native replacement | `local-library-review/9d6701c5adc3445c91933dc5a780bab4` | 48 manifest contracts and 22 isolated native scenarios |
| Default HEIF loader protections | `heif-loader/8d5587ca581c4619a602b957b40c2cf7` | 25 checks: valid, preloaded, junction, hardlink, extra and missing libraries |
| Actual worker/application publication | `local-library-integration-132c9712da5b4ed18c449c0ea6702351` | 659 HEIF checks, 104 outputs; 211 expanded audio direct-command checks |
| Source-modified libde265 | `modified-de265-4507fdb14f57` | Isolated source copy appends `-contextsuite-recipient-test` to its version function; actual native return value and decoding verified |

HEIF tests combine rebuilt libheif from the unchanged source-kit replay with
source-modified libde265. Audio uses all five independently rebuilt source-kit
DLLs with the original shipped probe/encoder. Baseline and replacement native
tests produce identical HEIF fixture pixels and FLAC bytes. Both directions of
the authored WAV/FLAC round trip preserve samples. Invalid identities, undeclared
replacements, linked manifests, undeclared extra libraries and changed anchors
are refused. Declared supporting DLLs work, active files cannot be edited, and
audio disposal releases its leases.

Actual-worker HEIF tests cover HEIC/AVIF, supported color/alpha cases and existing
unsupported/HDR refusals. Audio tests cover AIFF/WMA, all six targets, consent,
access changes, retries and missing Voice-provider behavior. These scoped results
do not establish correctness of arbitrary recipient changes. Official engine
verifiers also reject manifests declaring entirely original libraries. Original
source trees, engine inputs and disposable payloads remain unchanged afterward.

Managed builds pass with zero warnings/errors. Only the small disposable
libde265 copy was compiled natively; the completed Office build is reused.
No version, installation, registration, live licensing, hosting or publishing
changed. Exact-candidate acceptance and the
[remaining distribution decisions](candidate-distribution-review.md) stay open.
