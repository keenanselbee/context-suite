Common Image Header Analysis
============================

Updated: 2026-09-11. Basic Analyze now identifies JPEG, GIF, BMP and WebP from
content and reads bounded header declarations. These four catalog entries were
previously filename hints only. Catalog revision `2026-09-11.1` also corrects
JPEG's typical-use description to acknowledge lossless variants.

This read-only path uses the existing 64 KiB prefix, without a worker, paid
admission, image decoding, additional I/O or allocation from declared dimensions.
It never enables conversion or optimization merely by identifying a format.
Identity remains **likely**, even when dimensions are available. Actual pixels,
later frames, metadata and complete-file validity are not checked.


Facts and source review
-----------------------

The declarations and four catalog descriptions were reviewed against these
primary specifications. Descriptions and readers are independently authored;
no registry database, reference implementation or image asset was imported.

| Family | Parsed declarations | Primary source and remaining limits |
| --- | --- | --- |
| JPEG | First supported frame's width, height, sample precision, component count and coding mode | [ITU-T T.81, Annex B](https://www.w3.org/Graphics/JPEG/itu-t81.pdf): baseline, extended sequential, progressive and lossless SOF0-3. At most 256 marker records. Skip metadata segments by their lengths and stop before compressed scans. Other coding variants retain identity without these facts. A zero/deferred height stays unavailable; no later DNL scan. |
| GIF87a/89a | Logical-screen dimensions, version and declared global palette size | [GIF specification, sections 17-19](https://www.w3.org/Graphics/GIF/spec-gif89a.txt): no image-block, timing, transparency or animation scan. Palette declaration must fit the known file size; palette colors are not read. |
| BMP | Core or Windows header dimensions, pixel depth, raw compression and applicable row order | [Bitmap storage](https://learn.microsoft.com/en-us/windows/win32/gdi/bitmap-storage) and [BITMAPINFOHEADER](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader): supported DIB sizes 12/40/108/124, checked planes/pixel offset and signed height. Embedded JPEG/PNG may declare zero bits per pixel. Raw headerless `.dib` remains a filename hint. No pixel-array validation. |
| WebP | VP8X canvas, VP8L lossless dimensions or VP8 key-frame dimensions; available declared alpha/animation flags | [WebP container specification](https://developers.google.com/speed/webp/docs/riff_container): checked RIFF/chunk size arithmetic and padding. Only the first image header is inspected; feature declarations do not establish actual transparent pixels, animation frames or metadata validity. |

All four retain unavailable facts for actual transparency, animation, display
orientation and color profile. Dimensions are stored/header dimensions, not an
EXIF-adjusted display size. Malformed, truncated, unsupported or over-budget
headers retain the likely signature identity with an explanation and no invented
dimensions. JPEG deferred height is the explicit exception: known width and
unavailable height are shown separately. Content identity takes precedence over
contradictory extensions.

This is a factual/source review of these four records and their new detectors,
not approval of all 237 catalog records or every variant of each format. The
older [223-source reachability audit](catalog-source-review.json) remains a
historical record for catalog revision `2026-09-09.2`; broader factual and MIME
coverage remains unfinished.


Verification
------------

`Test-Foundation.ps1 -Configuration Release` passes **1,837 contracts**, including
47 new image-header checks. Authored fixtures cover all supported first-header
forms, deferred JPEG height, marker-like metadata, malformed lengths, oversized
WebP canvas, unsupported BMP layouts and the JPEG marker budget. Every fixture
prefix and 1,200 deterministic mutations complete without unexpected exceptions
or duplicate facts. Windows' independent encoders generate complete 7-by-5 JPEG,
GIF and BMP files whose dimensions agree with the new parser. The WebP cases here
are header fixtures, not independent full WebP decode comparisons.

The actual file reader also processes all six header fixtures under misleading
`.pdf` names and preserves their hashes and write timestamps. Generated fixtures
remain under `.codex-temp/foundation-tests/` in `image-headers-*` directories.
Log: `.codex-temp/image-header-foundation-encoded.log`.
The previous full image-engine, audio/PDF real-worker and hidden/visible UI suites
were not rerun for these bounded header changes.

Fresh combined production stage
`artifacts/production-staging/2c3c63dfec304305bbf51be691cbe4c1` builds the updated
managed app/worker and native shell with zero managed warnings/errors. Existing
image/audio/PDF runtime, notice and allowlist checks pass. Build log:
`.codex-temp/image-header-production-2c3c63dfec304305bbf51be691cbe4c1.log`.
