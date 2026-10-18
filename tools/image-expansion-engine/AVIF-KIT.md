AVIF Encoder Source Kit
=======================

`source/libavif.tar.gz` is the official libavif 1.4.2 source archive.
`source/aom.tar.gz` is the official AOM 3.15.1 source archive. Their exact
lengths and SHA-256 hashes are in `Build-AvifEngine.py` and
`Prepare-AvifSource.py`. The adjacent `notices/` directory retains the selected
upstream license and patent texts. `build-inputs.json` and
`build-results.json` identify the selected static-library build and final DLL.

The public `recipe/` scripts are copied from Context Suite's
`tools/avif-engine`. To rebuild, place this kit's archives in a fresh prepared
directory under that public repository's `.codex-temp`, then follow
`recipe/README.md` using the public checkout and its matching private checkout.
The private `ContextSuite.Avif.Native` bridge source is required and is not in
this kit. Rebuilt DLLs can differ because of build paths and toolchain metadata;
the shipped DLL is pinned by its exact SHA-256.
