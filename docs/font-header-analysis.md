Font Header Analysis
====================

Updated: 2026-09-11. Catalog revision `2026-09-11.4` reviews the five font
descriptions and adds content-based header analysis for TrueType, CFF-based
OpenType, font collections, WOFF and WOFF2. The OpenType description now includes
optional color glyph data; its source and the TrueType source point directly to
the file-format specification. No external descriptions, code or font assets
were imported.

The existing 64 KiB prefix supplies every read. Parsing uses fixed header offsets
and 64-bit length arithmetic, with no loops or allocations based on declared
counts. Identity stays likely. TrueType's numeric marker additionally requires
a complete 12-byte header, a nonzero count and space in the declared file length
for the table directory. Distinctive four-byte tags retain likely identity when
their remaining header is truncated, with unavailable properties and a warning.


Reviewed facts and boundaries
-----------------------------

| Records | Facts and primary source |
| --- | --- |
| `truetype`, `opentype` | Declared outline flavor and table count; directory-size plausibility. [Microsoft OpenType file specification](https://learn.microsoft.com/en-us/typography/opentype/spec/otff), table directory and filename sections. |
| `font-collection` | Raw header version and declared font count for versions 1.0/2.0; directory-size plausibility including version 2's extra fields. Same Microsoft specification, collection section. Unsupported versions retain identity without interpreting their counts. |
| `woff` | Flavor, table count, packaged size and uncompressed-size reference; reserved-field and directory-size warnings. [W3C WOFF 1.0](https://www.w3.org/TR/WOFF/), sections 3-5. Unknown flavors remain raw values. |
| `woff2` | Flavor (including a collection), table count, packaged/uncompressed/compressed-size declarations. [W3C WOFF 2.0](https://www.w3.org/TR/WOFF2/), section 3.2. The uncompressed-size reference is not a verified reconstruction size. |

These facts describe headers only. Table offsets/contents, collection members,
checksums, compressed streams, names, glyphs and embedding/license rights are not
validated. No font is installed, rendered or decompressed. Fonts do not gain
Convert or Optimize permissions. Apple legacy sfnt tags and other font formats
remain outside these detectors; unknown files retain the generic result.


Verification
------------

The Release foundation run passes **2,102 contracts**, including 218 new checks.
The fixtures are independently authored headers, not complete usable fonts.
Cases cover all five families, every shortened fixture prefix, weak numeric
markers, unsupported collection versions, huge counts, contradictory sizes,
unknown flavors, WOFF2 collections and 500 deterministic mutations. The normal
file reader identifies every fixture under a misleading `.pdf` name without
changing its bytes or write timestamp. Log:
`.codex-temp/font-header-foundation-reader.log`.

Fresh combined stage
`artifacts/production-staging/cea1c7eafd284169afbc7613b90369a6` builds with zero
warnings/errors and passes the curated image, audio/PDF candidate, dependency,
notice and file allowlist checks. Log:
`.codex-temp/font-header-production-cea1c7eafd284169afbc7613b90369a6.log`.
The `-SkipShell` build does not establish fresh native shell or installed
acceptance. Real image/audio/PDF workflows were not rerun for this header change.

These checks establish bounded header reporting, not independent font rendering,
complete format validity or visible/assistive-technology acceptance. The broader
catalog factual review remains incomplete; this checkpoint reviews five records.
