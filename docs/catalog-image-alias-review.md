Image Catalog Alias Review
==========================

Reviewed 2026-09-14 against catalog revision 2026-09-13.1. This review accounts
for all 65 existing extension associations across the 30 Image records covered
by the [purpose review](catalog-image-review.md). The associations have evidence
for their stated uses; no catalog data change is needed. This is not an exhaustive
inventory of image extensions, competing meanings or accepted format versions.

Filename associations remain hints. They do not establish actual contents,
validity, animation, color fidelity or an executable capability. Recognition
and conversion retain their separate [coverage](file-type-coverage.md).


Existing associations and evidence
----------------------------------

Each row preserves the exact current extension array. Registrations establish
some conventions; publisher documentation and implementation declarations
establish others. An implementation's filename convention does not imply that
Context Suite adopts its parser or supports its transformations.

| Catalog ID | Existing extensions | Reviewed association and boundary | Source |
| --- | --- | --- | --- |
| adobe-swatches | `.ase` | Adobe color-swatch exchange; shared with Aseprite | [Adobe Color workflow](https://helpx.adobe.com/creative-cloud/adobe-color.html) |
| aseprite | `.aseprite`, `.ase` | Two names for Aseprite working files | [Aseprite files](https://www.aseprite.org/docs/files/) |
| avif | `.avif`, `.heif`, `.heifs`, `.hif` | Registered AVIF associations; generic HEIF names remain ambiguous | [IANA AVIF registration](https://www.iana.org/assignments/media-types/image/avif) |
| bmp | `.bmp`, `.dib` | Bitmap filename conventions | [Microsoft BMP overview](https://learn.microsoft.com/en-us/windows/win32/wic/bmp-format-overview) |
| camera-raw | `.cr2`, `.cr3`, `.nef`, `.nrw`, `.arw`, `.raf`, `.rw2`, `.orf`, `.pef`, `.srw` | Manufacturer raw-photo families, not interchangeable encodings | [Adobe camera and extension table](https://helpx.adobe.com/camera-raw/desktop/dng-and-file-formats/camera-raw-plug-supported-cameras.html) |
| cur | `.cur`, `.ani` | Static and animated cursor file conventions | [Microsoft cursor-file property](https://learn.microsoft.com/en-us/previous-versions/aa472984(v=vs.85)) |
| dds | `.dds` | DirectDraw surface filename convention | [OpenImageIO DDS documentation](https://openimageio.readthedocs.io/en/latest/builtinplugins.html#dds) |
| dng | `.dng` | Digital Negative output filename convention | [Adobe DNG Converter naming](https://helpx.adobe.com/camera-raw/desktop/dng-and-file-formats/adobe-dng-converter.html) |
| eps | `.eps`, `.epsf` | Encapsulated PostScript import conventions | [Adobe archived Illustrator format table, printed page 5](https://helpx.adobe.com/archive/illustrator/illustrator-cs5-5-troubleshooting.pdf?sdid=XT3PH8LV) |
| exr | `.exr` | OpenEXR filename convention | [OpenEXR publisher](https://openexr.com/en/latest/) |
| gif | `.gif` | GIF filename convention; suffix does not prove animation | [Microsoft GIF overview](https://learn.microsoft.com/en-us/windows/win32/wic/gif-format-overview) |
| heif | `.heif`, `.heic`, `.hif`, `.heifs`, `.heics` | Image and sequence registrations distinguish generic and HEVC forms | [IANA HEIF/HEIC registration](https://www.iana.org/assignments/media-types/image/heif), [sequence registration](https://www.iana.org/assignments/media-types/image/heif-sequence) |
| icns | `.icns` | Apple deployment icon-file convention | [Apple icon documentation](https://developer.apple.com/library/archive/documentation/GraphicsAnimation/Conceptual/HighResolutionOSX/Optimizing/Optimizing.html) |
| ico | `.ico` | Windows icon-file convention | [Microsoft ICO overview](https://learn.microsoft.com/en-us/windows/win32/wic/ico-format-overview) |
| illustrator | `.ai` | Illustrator working-file convention | [Adobe Illustrator save documentation](https://helpx.adobe.com/illustrator/using/saving-artwork.html) |
| jpeg | `.jpg`, `.jpeg`, `.jpe`, `.jfif` | JPEG conventions; separate Publisher evidence names JFIF | [Microsoft JPEG overview](https://learn.microsoft.com/en-us/windows/win32/wic/jpeg-format-overview), [Publisher image-format table](https://learn.microsoft.com/en-us/office/vba/api/publisher.pictureformat.imageformat) |
| jpeg2000 | `.jp2`, `.j2k`, `.jpf`, `.jpx` | Related JPEG 2000 filename conventions, not identical profiles | [IANA JP2](https://www.iana.org/assignments/media-types/image/jp2), [IANA JPX](https://www.iana.org/assignments/media-types/image/jpx), [OpenImageIO JPEG 2000](https://openimageio.readthedocs.io/en/latest/builtinplugins.html#jpeg-2000) |
| jxl | `.jxl` | Registered JPEG XL suffix | [IANA JPEG XL](https://www.iana.org/assignments/media-types/image/jxl) |
| krita | `.kra` | Krita working-file convention | [Krita native format](https://docs.krita.org/en/general_concepts/file_formats/file_kra.html) |
| openraster | `.ora` | OpenRaster package filename convention | [OpenRaster file layout](https://www.openraster.org/baseline/file-layout-spec.html) |
| png | `.png`, `.apng` | PNG and animated PNG registrations | [IANA PNG](https://www.iana.org/assignments/media-types/image/png), [IANA APNG](https://www.iana.org/assignments/media-types/image/apng) |
| pnm | `.pbm`, `.pgm`, `.ppm`, `.pnm`, `.pam` | PNM groups PBM/PGM/PPM; PAM is a related format | [Netpbm PNM naming](https://netpbm.sourceforge.net/doc/pnm.html), [PAM naming](https://netpbm.sourceforge.net/doc/pam.html) |
| psd | `.psd`, `.psb` | Photoshop document and large-document variants | [Adobe format overview](https://helpx.adobe.com/photoshop/desktop/save-and-export/export-files-to-different-formats/photoshop-file-formats-overview.html), [Photoshop specification](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/) |
| qoi | `.qoi` | Author-recommended QOI filename extension | [QOI README](https://raw.githubusercontent.com/phoboslab/qoi/master/README.md) |
| radiance | `.hdr`, `.rgbe` | Radiance input conventions explicitly declared by OpenImageIO | [OpenImageIO HDR input declaration](https://raw.githubusercontent.com/AcademySoftwareFoundation/OpenImageIO/main/src/hdr.imageio/hdrinput.cpp) |
| svg | `.svg` | Registered SVG filename convention | [IANA SVG](https://www.iana.org/assignments/media-types/image/svg%2Bxml) |
| tga | `.tga`, `.targa` | Targa input/output conventions documented by a FreeImage consumer | [PDL image I/O format table](https://metacpan.org/pod/PDL::IO::Image) |
| tiff | `.tif`, `.tiff` | TIFF filename conventions | [Microsoft TIFF overview](https://learn.microsoft.com/en-us/windows/win32/wic/tiff-format-overview) |
| webp | `.webp` | Registered WebP suffix | [IANA WebP](https://www.iana.org/assignments/media-types/image/webp) |
| xcf | `.xcf` | GIMP working-file convention | [GIMP save/export documentation](https://docs.gimp.org/3.0/en/gimp-images-out.html) |


Qualifications and retrieval limits
-----------------------------------

The two `.ase` meanings remain alternatives. The HEIF and AVIF records retain
their shared suffixes; the [earlier registration review](catalog-mime-variant-review.md)
explains the generic, codec-specific and sequence distinctions. Camera Raw's
table explicitly contains all ten listed manufacturer suffixes. That establishes
their raw-photo association, not uniform sensor storage or support for every camera.

Microsoft's JPEG codec table names `.jpe`, `.jpeg` and `.jpg`, but does not itself
establish `.jfif`. Publisher's separate image-format table supplies that convention.
JPX registration names `.jpf` and permits `.jpx`; OpenImageIO names `.j2k` alongside
`.jp2`. Those conventions do not settle a file's codestream/container or profile.

OpenImageIO's prose documents `.hdr`; its inspected input-extension declaration
also explicitly lists `.rgbe`. Its Targa documentation/declaration does not list
`.targa`. The PDL maintainer's image-I/O table independently documents both current
Targa suffixes. This is implementation-convention evidence, not a claim that the
Truevision specification mandates `.targa`. No inspected source was copied or run.

Adobe's archived Illustrator PDF was retrieved, including its EPS extension table.
Its historical naming evidence is not a current application-compatibility claim.
The FreeImage project's documentation link resolved to a download landing page;
this review uses the retrieved PDL documentation for the Targa convention instead
of claiming to have inspected the complete FreeImage manual.

No new aliases, MIME claims, content detectors or conversion permissions are added.
Sources sometimes name further extensions; those are outside this reconciliation.
Other families, competing meanings, detailed variant acceptance and redistribution
review remain separate. The historical source-retrieval audit is unchanged.


Verification
------------

The table is reconciled against all 30 current Image records and 65 extension
associations, with no missing, extra or duplicate IDs or associations. The catalog
remains byte-identical at SHA-256
`F578EB98CA1C824FD36ACF9F614F42001E3A59FC1FDFAF0E01C93D526E14F047`.
The reconciliation receipt is `.codex-temp/catalog-image-alias-review.json`.
Repository documentation/link/whitespace validation is recorded in
`.codex-temp/catalog-image-alias-repository.log`.

Only documentation changes. No runtime rebuild or new worker, visual,
accessibility, installation or packaging acceptance is implied. Reserved
candidate 1.0.7 remains unchanged.
