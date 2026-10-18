SVG Renderer Third-Party Evidence
=================================

`dependency-inventory.json` records the 63 registry dependencies selected by
the private `ContextSuite.Svg.Native` Cargo lockfile. Each entry contains its
locked crate checksum, declared license expression, and SHA-256 of all license
files copied from the corresponding official crate source. The `licenses/`
directory retains those 122 original license files. The application redistributes
neither fonts nor crate source; this evidence accompanies review of the compiled
renderer. The build recipe verifies every recorded license file before building.

The direct renderer dependency is `resvg` 0.48.1, with default features disabled
and only `text` and `raster-images` enabled. The source is prepared through Cargo's
registry cache; the implementation never reads the repository's ignored
`reference/` material.
