Expanded Catalog MIME References
================================

Reviewed 2026-09-16. Catalog revision **2026-09-16.3** adds **75 descriptive
MIME associations to 64 existing records**. The catalog retains 245 descriptions;
100 records now carry 134 associations and 145 make no MIME claim. IDs, aliases,
names, typical uses, confidence, detectors and executable capabilities are
unchanged. Analyze continues to show MIME references only in technical details,
with the exact variant explicitly undetermined.

This expands the [initial MIME review](catalog-mime-descriptions.md), whose
59 associations are retained unchanged. A family reference is not a Content-Type
determination for the selected file, a decoder promise, or permission to execute
or transform it. An unknown or ambiguous file still receives no invented type.


Provenance and scope
-------------------

The [IANA registry](https://www.iana.org/assignments/media-types/) snapshot reports
an update date of **2026-09-14**. Its retained XML is
`.codex-temp/iana-media-types-20260916.xml`, SHA-256
`fd2b7773f3020173f81de4209512476a1b577590d01be11bb189a0d12a925945`.
All 74 distinct added identifiers match that registry case-insensitively.
Their registration pages were retrieved; MPEG video and Internet messages have
no individual template, so their sources point directly to the defining sections
of RFC 2046 instead. Registration spelling alone did not establish an association:
the review checked file/container purpose and variant scope as recorded below.

Only selected factual identifiers and references enter the embedded catalog.
Descriptions and qualifications are independently authored. The
[IANA/IETF registry terms](https://www.iana.org/help/licensing-terms) apply CC0 to
applicable rights in the protocol registries, not to linked RFCs or other material.
No RFC prose, source code or external database is imported into the product, and
Analyze performs no registry request. Research responses, hashes and the exact
applied mapping remain under `.codex-temp/catalog-mime-expansion-20260916`.


Reviewed associations
---------------------

Each link below is also retained as the identifier's catalog provenance. Standard
parameters and content conformance are not inferred from these parameter-free
reference names. For rows without a narrower qualification, the registration
describes the named family; individual-file validation remains separate.

| Catalog ID | Added descriptive identifiers | Qualification |
| --- | --- | --- |
| 3gp | [video/3gpp](https://www.iana.org/assignments/media-types/video/3gpp), [video/3gpp2](https://www.iana.org/assignments/media-types/video/3gpp2) | 3GPP and 3GPP2 file variants remain distinct. |
| 3mf | [model/3mf](https://www.iana.org/assignments/media-types/model/3mf) | Registered family reference; exact file variant remains undetermined. |
| aac | [audio/aac](https://www.iana.org/assignments/media-types/audio/aac) | Registration covers ADTS and LATM/LOAS; no codec or transport is inferred from a name. |
| arrow | [application/vnd.apache.arrow.file](https://www.iana.org/assignments/media-types/application/vnd.apache.arrow.file), [application/vnd.apache.arrow.stream](https://www.iana.org/assignments/media-types/application/vnd.apache.arrow.stream) | IPC file and stream layouts are separate; these do not describe legacy Feather V1. |
| asf | [application/vnd.ms-asf](https://www.iana.org/assignments/media-types/application/vnd.ms-asf) | ASF container reference; a WMV suffix does not certify a codec. |
| cab | [application/vnd.ms-cab-compressed](https://www.iana.org/assignments/media-types/application/vnd.ms-cab-compressed) | Registered family reference; exact file variant remains undetermined. |
| chm | [application/vnd.ms-htmlhelp](https://www.iana.org/assignments/media-types/application/vnd.ms-htmlhelp) | Registered family reference; exact file variant remains undetermined. |
| collada | [model/vnd.collada+xml](https://www.iana.org/assignments/media-types/model/vnd.collada+xml) | Registered family reference; exact file variant remains undetermined. |
| dart | [application/vnd.dart](https://www.iana.org/assignments/media-types/application/vnd.dart) | Registered family reference; exact file variant remains undetermined. |
| dbase | [application/vnd.dbf](https://www.iana.org/assignments/media-types/application/vnd.dbf) | The registration describes DBF tables used with GIS; no dBASE dialect is established. |
| deb | [application/vnd.debian.binary-package](https://www.iana.org/assignments/media-types/application/vnd.debian.binary-package) | Registered family reference; exact file variant remains undetermined. |
| dicom | [application/dicom](https://www.iana.org/assignments/media-types/application/dicom) | DICOM file-format reference; transfer syntax and medical semantics are not inferred. |
| djvu | [image/vnd.djvu](https://www.iana.org/assignments/media-types/image/vnd.djvu) | Registered family reference; exact file variant remains undetermined. |
| dwg | [image/vnd.dwg](https://www.iana.org/assignments/media-types/image/vnd.dwg) | Registered family reference; exact file variant remains undetermined. |
| dxf | [image/vnd.dxf](https://www.iana.org/assignments/media-types/image/vnd.dxf) | Registered family reference; exact file variant remains undetermined. |
| eml | [message/rfc822](https://datatracker.ietf.org/doc/html/rfc2046#section-5.2.1) | Encapsulated Internet message; no mail parser, attachment opening or link following is added. |
| eps | [application/postscript](https://www.iana.org/assignments/media-types/application/postscript) | PostScript-family reference; no language level or EPS conformance is inferred. |
| fits | [application/fits](https://www.iana.org/assignments/media-types/application/fits), [image/fits](https://www.iana.org/assignments/media-types/image/fits) | General FITS and image-FITS registrations are distinct. |
| font-collection | [font/collection](https://www.iana.org/assignments/media-types/font/collection) | Collection reference; suffixes alone do not determine contained font flavors. |
| geojson | [application/geo+json](https://www.iana.org/assignments/media-types/application/geo+json) | Registered family reference; exact file variant remains undetermined. |
| gltf | [model/gltf+json](https://www.iana.org/assignments/media-types/model/gltf+json), [model/gltf-binary](https://www.iana.org/assignments/media-types/model/gltf-binary) | JSON glTF and binary GLB variants. |
| hdf4 | [application/vnd.hdfgroup.hdf4](https://www.iana.org/assignments/media-types/application/vnd.hdfgroup.hdf4) | HDF4, distinct from HDF5 despite a shared .hdf hint. |
| hdf5 | [application/vnd.hdfgroup.hdf5](https://www.iana.org/assignments/media-types/application/vnd.hdfgroup.hdf5) | HDF5, distinct from HDF4 despite a shared .hdf hint. |
| ical | [text/calendar](https://www.iana.org/assignments/media-types/text/calendar) | Registered family reference; exact file variant remains undetermined. |
| ico | [image/vnd.microsoft.icon](https://www.iana.org/assignments/media-types/image/vnd.microsoft.icon) | Registered family reference; exact file variant remains undetermined. |
| iges | [model/iges](https://www.iana.org/assignments/media-types/model/iges) | Registered family reference; exact file variant remains undetermined. |
| java-archive | [application/java-archive](https://www.iana.org/assignments/media-types/application/java-archive) | JAR reference; it does not establish the exact WAR, EAR or resource-adapter variant. |
| jpeg2000 | [image/jp2](https://www.iana.org/assignments/media-types/image/jp2), [image/jpx](https://www.iana.org/assignments/media-types/image/jpx) | JP2 and JPX containers; neither establishes a raw .j2k codestream media type. |
| jxl | [image/jxl](https://www.iana.org/assignments/media-types/image/jxl) | Registered family reference; exact file variant remains undetermined. |
| keynote | [application/vnd.apple.keynote](https://www.iana.org/assignments/media-types/application/vnd.apple.keynote) | Registered family reference; exact file variant remains undetermined. |
| kml | [application/vnd.google-earth.kml+xml](https://www.iana.org/assignments/media-types/application/vnd.google-earth.kml+xml), [application/vnd.google-earth.kmz](https://www.iana.org/assignments/media-types/application/vnd.google-earth.kmz) | KML XML and packaged KMZ variants. |
| matroska | [video/matroska](https://www.iana.org/assignments/media-types/video/matroska), [audio/matroska](https://www.iana.org/assignments/media-types/audio/matroska) | Audio and video variants; subtitle-only content is not assigned either type. |
| mp4 | [video/mp4](https://www.iana.org/assignments/media-types/video/mp4) | Registered family reference; exact file variant remains undetermined. |
| mpeg | [video/mpeg](https://datatracker.ietf.org/doc/html/rfc2046#section-4.4) | Registered family reference; exact file variant remains undetermined. |
| numbers | [application/vnd.apple.numbers](https://www.iana.org/assignments/media-types/application/vnd.apple.numbers) | Registered family reference; exact file variant remains undetermined. |
| odg | [application/vnd.oasis.opendocument.graphics](https://www.iana.org/assignments/media-types/application/vnd.oasis.opendocument.graphics), [application/vnd.oasis.opendocument.graphics-template](https://www.iana.org/assignments/media-types/application/vnd.oasis.opendocument.graphics-template) | OpenDocument graphics and graphics-template variants. |
| opentype | [font/otf](https://www.iana.org/assignments/media-types/font/otf) | OpenType reference; use header evidence to distinguish fonts, not the extension. |
| pages | [application/vnd.apple.pages](https://www.iana.org/assignments/media-types/application/vnd.apple.pages) | Registered family reference; exact file variant remains undetermined. |
| parquet | [application/vnd.apache.parquet](https://www.iana.org/assignments/media-types/application/vnd.apache.parquet) | Registered family reference; exact file variant remains undetermined. |
| pcap | [application/vnd.tcpdump.pcap](https://www.iana.org/assignments/media-types/application/vnd.tcpdump.pcap) | Registered family reference; exact file variant remains undetermined. |
| pe | [application/vnd.microsoft.portable-executable](https://www.iana.org/assignments/media-types/application/vnd.microsoft.portable-executable) | Portable Executable reference; no execution, trust or signature conclusion. |
| pkcs12 | [application/pkcs12](https://www.iana.org/assignments/media-types/application/pkcs12) | Registered family reference; exact file variant remains undetermined. |
| postscript | [application/postscript](https://www.iana.org/assignments/media-types/application/postscript) | PostScript-language reference; no interpreter is started. |
| psd | [image/vnd.adobe.photoshop](https://www.iana.org/assignments/media-types/image/vnd.adobe.photoshop) | PSD registration; a grouped PSB suffix does not prove the same variant. |
| quicktime | [video/quicktime](https://www.iana.org/assignments/media-types/video/quicktime) | Registered family reference; exact file variant remains undetermined. |
| rar | [application/vnd.rar](https://www.iana.org/assignments/media-types/application/vnd.rar) | Registered family reference; exact file variant remains undetermined. |
| shapefile | [application/vnd.shp](https://www.iana.org/assignments/media-types/application/vnd.shp) | Registered family reference; exact file variant remains undetermined. |
| sql | [application/sql](https://www.iana.org/assignments/media-types/application/sql) | SQL text reference; syntax, dialect and safe execution are not determined. |
| sqlite | [application/vnd.sqlite3](https://www.iana.org/assignments/media-types/application/vnd.sqlite3) | Registered family reference; exact file variant remains undetermined. |
| step | [model/step](https://www.iana.org/assignments/media-types/model/step) | STEP product models using ISO 10303-21 exchange syntax. |
| stl | [model/stl](https://www.iana.org/assignments/media-types/model/stl) | Registered family reference; exact file variant remains undetermined. |
| toml | [application/toml](https://www.iana.org/assignments/media-types/application/toml) | Registered family reference; exact file variant remains undetermined. |
| truetype | [font/ttf](https://www.iana.org/assignments/media-types/font/ttf) | TrueType reference; use header evidence to distinguish fonts, not the extension. |
| tsv | [text/tab-separated-values](https://www.iana.org/assignments/media-types/text/tab-separated-values) | Registered family reference; exact file variant remains undetermined. |
| usd | [model/vnd.usdz+zip](https://www.iana.org/assignments/media-types/model/vnd.usdz+zip), [model/vnd.usda](https://www.iana.org/assignments/media-types/model/vnd.usda) | USDA and USDZ registrations; no MIME claim for USDC or generic USD is inferred. |
| vcard | [text/vcard](https://www.iana.org/assignments/media-types/text/vcard) | Registered family reference; exact file variant remains undetermined. |
| wasm | [application/wasm](https://www.iana.org/assignments/media-types/application/wasm) | Registered family reference; exact file variant remains undetermined. |
| wavefront | [model/obj](https://www.iana.org/assignments/media-types/model/obj), [model/mtl](https://www.iana.org/assignments/media-types/model/mtl) | OBJ geometry and MTL material-library text variants. |
| webvtt | [text/vtt](https://www.iana.org/assignments/media-types/text/vtt) | Registered family reference; exact file variant remains undetermined. |
| woff | [font/woff](https://www.iana.org/assignments/media-types/font/woff) | Registered family reference; exact file variant remains undetermined. |
| woff2 | [font/woff2](https://www.iana.org/assignments/media-types/font/woff2) | Registered family reference; exact file variant remains undetermined. |
| xlsb | [application/vnd.ms-excel.sheet.binary.macroenabled.12](https://www.iana.org/assignments/media-types/application/vnd.ms-excel.sheet.binary.macroEnabled.12) | Registered binary-workbook family; the identifier does not prove macros are present. |
| xps | [application/oxps](https://www.iana.org/assignments/media-types/application/oxps), [application/vnd.ms-xpsdocument](https://www.iana.org/assignments/media-types/application/vnd.ms-xpsdocument) | OpenXPS and Microsoft XPS variants. |
| zstd | [application/zstd](https://www.iana.org/assignments/media-types/application/zstd) | Zstandard stream reference; not evidence of the compressed inner format. |



Associations deliberately not added
----------------------------------

The registered [video/MP2T](https://www.iana.org/assignments/media-types/video/MP2T)
definition is restricted to RTP transfer. It is not added to the stored
MPEG-transport-stream record. Likewise,
[audio/basic](https://www.iana.org/assignments/media-types/audio/basic) specifies
8 kHz mono mu-law audio, not a general AU/SND container. AU retains its factual
header analysis without that misleading association.

Several familiar-looking identifiers were absent from this registry snapshot,
including the proposed Android APK, Java VM, DDS, DNG, GPX, NetCDF, ORC, PCAPNG,
QOI, WAVE and WebM spellings. None is invented or labeled registered here.
Absence does not prove that a conventional type is invalid; a separate primary
source and scope review would be required before adding it. HDF uses the actual
`application/vnd.hdfgroup.hdf4` and `application/vnd.hdfgroup.hdf5` registrations.
Other aliases, conventions and unrepresented variants remain separate review
work; this expansion does not claim universal MIME coverage.


Verification
------------

The Release foundation run passes **4,130 contracts**, with unchanged source
inputs recorded in `.codex-temp/catalog-mime-expansion-foundation-inputs.json`
and a successful `.codex-temp/catalog-mime-expansion-foundation-exit.json`.
The applied mapping preserves every non-MIME record field and all 59 existing
associations. Repository boundary, system-theme and 167-document checks pass.
This data-only change adds no recognition route or executable capability;
native media workflows and visible acceptance were not rerun for this update.
