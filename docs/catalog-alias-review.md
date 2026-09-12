Common Catalog Alias Review
===========================

Revision 2026-09-11.2 adds six independently authored descriptions, bringing the
catalog to 243 records. This review corrects missing alternative meanings; it
does not add detectors, decoders or transformation permissions.

| Extension | Meanings retained | Primary reference for the added meaning |
| --- | --- | --- |
| `.fit` | FITS astronomy; FIT activity/device data | [Garmin FIT file types](https://developer.garmin.com/fit/file-types/) and [CSV tool](https://developer.garmin.com/fit/fitcsvtool/) |
| `.pdb` | Program debugging; molecular structure | [wwPDB legacy format](https://www.wwpdb.org/documentation/file-format-content/format33/v3.3.html) and [RCSB download naming](https://www.rcsb.org/docs/programmatic-access/file-download-services) |
| `.hdf` | HDF4; HDF5 | [HDF Group HDF4 description and extensions](https://www.hdfgroup.org/solutions/hdf4/) |
| `.cdf` | NASA Common Data Format; NetCDF | [NASA CDF](https://cdf.gsfc.nasa.gov/) and [format/extension ambiguity FAQ](https://cdf.gsfc.nasa.gov/html/faq.html) |
| `.res` | Godot resource; compiled Windows resources | [Microsoft resource files](https://learn.microsoft.com/en-us/windows/win32/menurc/about-resource-files) |
| `.ase` | Aseprite sprites; Adobe Swatch Exchange | [Adobe swatch exchange documentation](https://helpx.adobe.com/indesign/desktop/apply-color/define-and-manage-color-assets/organize-and-reuse-color-swatches.html) |

The HDF Group also explicitly lists `.h4` and `.hdf4`; both are included. PDB
means the legacy text coordinate format here, not the newer PDBx/mmCIF archive
format. Readable text can narrow the `.pdb` filename hint, but does not establish
a valid molecular structure. Additional meanings of these suffixes may exist;
this catalog is not exhaustive.

The stable `static-library` record now says **Static or import library**.
[Microsoft's linker documentation](https://learn.microsoft.com/en-us/cpp/build/reference/dot-lib-files-as-linker-input?view=msvc-170)
explicitly includes both kinds of `.lib`. The existing `.a` alias remains, with
toolchain-dependent wording; see [GNU ar](https://sourceware.org/binutils/docs/binutils/ar.html).

Research on 2026-09-11 used primary documentation and search-indexed primary
excerpts. Adobe's direct page retrieval failed; its indexed documentation
supported the swatch-exchange purpose. This is not a complete binary-format or
reuse-rights audit. No source text, database or reference implementation was
imported. The older [retrieval audit](catalog-source-review.json) is unchanged.

Verification covers uppercase lookup, all six ambiguous binary fallbacks,
content precedence for misleading suffixes, qualified text-PDB hints, and
lookup reachability for all records. No production payload, real-engine,
visible UI or installed acceptance is implied by these catalog checks.

All 1,862 foundation contracts passed, including 25 added checks (six new
record-reachability checks and 19 ambiguity/content-evidence checks). Log:
`.codex-temp/catalog-alias-foundation.log`. Public boundaries, theme policy and
76 documentation files also passed repository validation.
