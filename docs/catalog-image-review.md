Image Catalog Purpose Review
============================

Catalog revision **2026-09-11.11** reviews the 26 Image-family records outside
the earlier [BMP/GIF/JPEG/WebP review](image-header-analysis.md), completing a
purpose review across the current 30 Image records. Twelve descriptions or
references change; the catalog remains at 243 records. This is not an expansion
of the supported image conversion matrix or a review of every format variant.

The changes clarify static/animated cursors, APNG, PSD/PSB and the related Netpbm
formats. JPEG 2000 and JPEG XL now have more useful typical-use descriptions.
AVIF and Photoshop link to publisher specifications; broad TGA and icon links
are replaced with relevant Microsoft documentation. Existing accurate descriptions
remain unchanged. All prose is independently phrased; no registry data, source
implementation, reference binary or sample image was imported.

Purpose evidence
----------------

References were consulted on 2026-09-11. These support purposes and distinctions,
not Context Suite's ability to decode, render or convert the described features.

| Catalog ID | Purpose or distinction reviewed | Evidence |
| --- | --- | --- |
| adobe-swatches | Color libraries exchanged between Adobe design applications | [Adobe ASE workflow](https://helpx.adobe.com/indesign/desktop/apply-color/define-and-manage-color-assets/organize-and-reuse-color-swatches.html) |
| aseprite | Editable sprites with frames, layers and pixel data | [Aseprite file specification](https://github.com/aseprite/aseprite/blob/main/docs/ase-file-specs.md) |
| avif | AV1 images with HDR and auxiliary transparency; the specification also defines sequences | [AOM AVIF specification](https://aomediacodec.github.io/av1-avif/v1.2.0.html) |
| camera-raw | Camera image data for photo processing; manufacturer extensions denote different formats | [Adobe raw-file introduction](https://helpx.adobe.com/camera-raw/desktop/get-started/overview-and-setup/introduction-camera-raw.html), [camera/extension inventory](https://helpx.adobe.com/camera-raw/desktop/dng-and-file-formats/camera-raw-plug-supported-cameras.html) |
| cur | Static and animated pointer artwork, with cursor-specific hot spots | [Microsoft cursor documentation](https://learn.microsoft.com/en-us/windows/win32/menurc/about-cursors) |
| dds | Texture storage including mip levels and texture arrays | [Microsoft DDS guide](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide) |
| dng | Public raw-photo representation for processing and archival use | [Adobe DNG overview](https://helpx.adobe.com/camera-raw/desktop/dng-and-file-formats/digital-negative.html) |
| eps | PostScript artwork placed into publishing documents; may include vectors and raster images | [Adobe EPS export documentation](https://helpx.adobe.com/photoshop/using/saving-files-graphics-formats.html) |
| exr | HDR and arbitrary image channels in rendering/image-processing workflows | [OpenEXR technical introduction](https://openexr.com/en/latest/TechnicalIntroduction.html) |
| heif | Image and image-sequence storage/sharing in a container | [HEIF project overview](https://nokiatech.github.io/heif/) |
| icns | Packaged application/document icons at several resolutions | [Apple high-resolution icon documentation](https://developer.apple.com/library/archive/documentation/GraphicsAnimation/Conceptual/HighResolutionOSX/Optimizing/Optimizing.html) |
| ico | Icons combining image sizes and color depths | [Microsoft icon guidance](https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-icons) |
| illustrator | Editable artwork whose retained information depends on format/save choices | [Adobe Illustrator save documentation](https://helpx.adobe.com/illustrator/using/saving-artwork.html) |
| jpeg2000 | Medical, geographic and publishing imagery; supports lossless and lossy coding | [JPEG committee applications](https://jpeg.org/jpeg2000/applications.html), [coding overview](https://jpeg.org/jpeg2000/) |
| jxl | Web image delivery and photography with lossless/lossy coding | [JPEG committee overview](https://jpeg.org/jpegxl/) |
| krita | Native editable painting/animation work, distinct from an exported flat image | [Krita native-format documentation](https://docs.krita.org/en/general_concepts/file_formats/file_kra.html) |
| openraster | Layered raster interchange across participating applications | [OpenRaster baseline intent](https://www.openraster.org/baseline/baseline.html) |
| png | Lossless static images, alpha and animated PNG | [W3C PNG third edition](https://www.w3.org/TR/png-3/) |
| pnm | PBM/PGM/PPM are grouped as PNM; PAM is a related extensible representation | [Netpbm PAM specification and relationship explanation](https://netpbm.sourceforge.net/doc/pam.html) |
| psd | Photoshop editing information, with PSB as the large-document relative | [Adobe Photoshop file specifications](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/) |
| qoi | Simple lossless raster compression/interchange | [QOI publisher](https://qoiformat.org/) |
| radiance | Radiance-valued image data for lighting/rendering workflows | [Radiance author's file-format chapter](https://radsite.lbl.gov/radiance/refer/filefmts.pdf) |
| svg | XML-described scalable vector/mixed graphics; content is not necessarily passive | [W3C SVG specification](https://www.w3.org/TR/SVG2/) |
| tga | Raster artwork in graphics/texture conversion workflows | [Microsoft texture-converter documentation](https://github.com/microsoft/DirectXTex/wiki/Texconv) |
| tiff | Photography/publishing imagery, including multi-image files | [Adobe Bridge TIFF usage](https://helpx.adobe.com/bridge/desktop/share-and-export/supported-export-file-formats.html), [Microsoft multi-frame TIFF support](https://github.com/microsoft/DirectXTex/wiki/Texconv) |
| xcf | GIMP working images retaining editing data, distinct from exported formats | [GIMP save/export documentation](https://docs.gimp.org/3.0/en/gimp-images-out.html) |

Changed IDs: `avif`, `cur`, `eps`, `ico`, `jpeg2000`, `jxl`, `openraster`,
`png`, `pnm`, `psd`, `tga` and `tiff`.

Retrieval and acceptance limits
-------------------------------

The Adobe ASE page was available through primary search-index text but its direct
open failed. Its existing source URL is retained. The older Adobe EPS specification
and archived Microsoft icon article could not be retrieved; replacement references
support the catalog purposes without claiming that those original specifications
were fully reviewed. Direct retrieval of the Truevision TGA PDF mirrors also failed.
The TGA reference now establishes a concrete texture workflow from Microsoft's
own tool documentation, not a complete Truevision specification review.

Camera-raw families remain grouped. This review does not adopt blanket statements
that every raw file is uncompressed or has identical sensor storage. HEIF codec,
AVIF sequence/profile, HDR/color, TIFF page/compression, JPEG 2000 codestream versus
container, PSD/PSB size and editing-feature differences need exact-variant evidence
before any new executable capability. The shared `.ase` hint still distinguishes
Aseprite and Adobe swatches as alternatives. Animation in a format description
does not promise that Context Suite transforms every frame.

An independent JSON comparison in `.codex-temp/catalog-image-delta.json` permits
only revision, `commonUses` and `source` changes. All IDs, names, families, aliases,
MIME fields and identification rules are unchanged. Existing catalog/schema,
conflict and capability-separation contracts validate the embedded data; no tests
were added merely to repeat the new wording. No new production staging, worker,
image decoder, installed-shell or visible acceptance is implied by this review.

All 2,358 foundation contracts pass; the final log is
`.codex-temp/catalog-image-foundation.log`. The production payload remains the
preceding admission checkpoint and does not yet contain this catalog revision.
