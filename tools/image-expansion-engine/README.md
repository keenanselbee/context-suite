AVIF and SVG Production Inputs
==============================

This opt-in production composition adds the pinned AVIF encoder and SVG renderer
beside the worker under `avif/` and `svg/`. The AVIF output validator also needs
the separately selected HEIF decoder. Ordinary production builds omit both new
engines. A new `StagingId` and all five absolute distribution input paths are
required:

```powershell
./tools/Build-Production.ps1 -Configuration Release -StagingId '<new-guid>' `
  -HeifDistributionDirectory '<reviewed HEIF distribution>' `
  -AvifBuildDirectory '<pinned AVIF build>' `
  -AvifSourceDirectory '<prepared AVIF sources>' `
  -SvgBuildDirectory '<pinned SVG build>' `
  -SvgCargoHome '<verified Cargo home>'
```

The version must first be finalized under the repository's release workflow.
The command above reserves that version and is **not** a scratch packaging test.
For a scratch check, create a fresh directory beneath `.codex-temp` and call
`Stage-ImageExpansionPayload.py` with `--payload`, `--avif-build`,
`--avif-sources`, `--svg-build`, and `--cargo-home`. Calling it with only
`--payload` verifies an existing isolated tree. `--inventory` also compares
its entries in an outer payload inventory when one exists; production staging
creates that inventory after this verifier runs.

The [selection](production.json) fixes all 215 members by size and SHA-256:
the two exact runtime binaries, build/source receipts, original libavif and AOM
archives, 63 Cargo crate archives, 122 crate license files, Rust 1.98.1 standard
library notices, artifact kit instructions, and the public build recipes. The
20 AVIF members total 32,302,934 bytes; the 195 SVG members total 11,059,122
bytes. The stager verifies
the original build receipts, official source pins, crate inventory and Rust
toolchain notice identity before writing either directory. It refuses linked or
hard-linked members, missing/changed/extra files, unexpected directories,
and an already inventoried stage. A partial copy is retained and cannot be
silently merged with a retry. The production verifier repeats exact membership
and hash checks; an opt-out build rejects these engine directories.

The source kits contain independently obtained upstream materials and build
instructions. They do not contain the private AVIF bridge or SVG renderer
source. The private checkout remains necessary to rebuild those components.
Neither kit contains fonts. These local hashes and notices are packaging
evidence, not a signature or commercial release clearance.
