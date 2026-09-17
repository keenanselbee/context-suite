HEIF library source review kit
=============================

This local kit retains the original libheif/libde265 sources, the libheif error
propagation patch, pinned AOM source and selected upstream AOM build artifacts.
The AOM static library is an upstream artifact; this recipe does not rebuild it.
Keep the source archives, full licenses, patent text and build records together.

Extract the verified kit into a new directory under the Context Suite repository's
`.codex-temp`. From the extracted root, use Windows x64, Python 3.14, Git,
Visual Studio 18 C++ tools, a Windows SDK, and the Visual Studio CMake executable:

```powershell
python -B tools/heif-engine/Build-HeifEngine.py --libraries-only `
  --sources .codex-temp/inputs --aom .codex-temp/aom `
  --cmake '<Visual Studio CMake path>/cmake.exe'
```

The script verifies inputs, extracts fresh bounded sources, applies the patch,
and builds both LGPL DLLs into a new repository-local SDK/payload. This does not
install system components. It retains command exits, logs and hashes. Rebuilt
DLLs can have different identities because of compiler/path/build metadata.

The private Context Suite bridge/application source is absent. The original full
recipe's default mode requires that private checkout; `--libraries-only` does
not. Rebuilding these libraries alone does not make them accepted by the
application's current hash checks. Modified-library use, product/source terms,
source delivery, notices, toolchain eligibility and patent review remain release
decisions. This kit is engineering evidence, not an approved public distribution.
