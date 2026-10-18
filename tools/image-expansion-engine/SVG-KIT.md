SVG Renderer Source Kit
=======================

`source/crates/` contains the 63 official locked Cargo registry archives.
`dependency-inventory.json` records each crate checksum and license mapping;
`licenses/` contains all 122 copied crate license files. `notices/` contains
the matching Rust 1.98.1 standard-library copyright notices. The adjacent
`build-provenance.json` and `source-verification.json` identify the selected
offline build, lockfile, verified crate archives, source-file count and final
executable.

The public `recipe/` scripts are copied from Context Suite's
`tools/svg-engine`. A rebuild needs the public checkout, its matching private
checkout, Rust 1.98.1 and a Cargo home populated from these pinned archives;
follow `recipe/README.md`. The private `ContextSuite.Svg.Native` Rust source
and lockfile are required and are not in this kit. The crate archives alone
cannot recreate that private renderer. Build paths can change the executable
hash; the shipped executable is pinned by its exact SHA-256.
