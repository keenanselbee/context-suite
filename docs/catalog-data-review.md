Data Catalog Purpose Review
===========================

Revision **2026-09-11.9** reviews all 37 Data-family records and updates fifteen
descriptions or references. The catalog retains 243 records with unchanged IDs,
names, families, extensions, exact-name hints, MIME fields, detectors and operation
permissions. Existing accurate descriptions are retained.

The main corrections distinguish Feather versions, make object serialization
clearer, and explain application databases, numerical arrays, web logs and digital
identity containers in terms of their usual purpose. These descriptions do not
establish a particular file's contents, trust, scientific meaning or compatibility.


Reviewed purposes and sources
-----------------------------

The following primary project/specification references were consulted on
2026-09-11. Descriptions are independently phrased. No reference implementation,
registry database, specimen dataset or executable was imported or loaded.

| Catalog IDs | Reviewed purpose or distinction | Evidence |
| --- | --- | --- |
| access | Application records and Access objects; MDB and ACCDB are separate generations | [Microsoft database basics](https://support.microsoft.com/en-US/Access/database-basics) |
| arrow | Columnar analytical tables; Feather V2 uses Arrow IPC, while V1 has a different layout | [Arrow columnar specification](https://arrow.apache.org/docs/format/Columnar.html), [Feather versions](https://arrow.apache.org/docs/python/feather.html) |
| avro | Stored objects with a schema describing their structure | [Avro object containers](https://avro.apache.org/docs/1.12.0/specification/#object-container-files) |
| cdf | Multidimensional scientific data through NASA CDF | [NASA CDF overview](https://cdf.gsfc.nasa.gov/) |
| certificate | Encoded certificates and key objects; suffix alone does not identify the object | [RFC 7468](https://www.rfc-editor.org/rfc/rfc7468.html) |
| csv | Tabular interchange, including spreadsheet use | [RFC 4180](https://www.rfc-editor.org/rfc/rfc4180.html) |
| dbase | Table records; some geographic datasets keep attributes in a separate DBF | [dBASE structure](https://www.dbase.com/Knowledgebase/INT/db7_file_fmt.htm), [Esri shapefile specification](https://www.esri.com/content/dam/esrisites/sitecore-archive/Files/Pdfs/library/whitepapers/pdfs/shapefile.pdf) |
| dicom | Medical imaging information and related data for exchange/management | [DICOM scope](https://dicom.nema.org/medical/dicom/current/output/chtml/part01/chapter_1.html) |
| fit-activity | Activity, course and workout/device records | [Garmin FIT file types](https://developer.garmin.com/fit/file-types/) |
| fits | Astronomical arrays, images, tables and descriptive metadata | [NASA FITS overview](https://fits.gsfc.nasa.gov/), [FITS standard](https://fits.gsfc.nasa.gov/fits_standard.html) |
| geojson | Geographic features, geometry and properties represented as JSON | [RFC 7946](https://www.rfc-editor.org/rfc/rfc7946.html) |
| gpx | GPS waypoints, routes and tracks | [Topografix GPX](https://www.topografix.com/gpx.asp) |
| har | Web request/response and timing records for diagnosing page loading | [HAR draft](https://w3c.github.io/web-performance/specs/HAR/Overview.html) |
| hdf4 | Scientific data objects, including Earth-observation use | [HDF Group HDF4 overview](https://www.hdfgroup.org/solutions/hdf4/) |
| hdf5 | Datasets and metadata organized through groups and objects; distinct from HDF4 | [HDF5 format specification](https://support.hdfgroup.org/documentation/hdf5/latest/_f_m_t3.html), [HDF4 distinction](https://www.hdfgroup.org/solutions/hdf4/) |
| json | Language-independent structured data exchange | [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259.html) |
| json-lines | One JSON value per line for streaming records, logs and data exchange | [JSON Lines definition](https://jsonlines.org/) |
| kml | Geographic visualization documents; KMZ packages KML and optional resources in ZIP | [Google KML/KMZ documentation](https://developers.google.com/kml/documentation/kmzarchives) |
| matlab-data | Saved MATLAB variables; storage changes between MAT-file versions | [MathWorks MAT versions](https://www.mathworks.com/help/matlab/import_export/mat-file-versions.html) |
| netcdf | Array-oriented scientific data exchange; format variants are distinct | [Unidata overview](https://www.unidata.ucar.edu/software/netcdf/), [format specifications](https://docs.unidata.ucar.edu/netcdf-c/current/file_format_specifications.html) |
| npy | NPY persists one array; NPZ packages arrays as NPY members in ZIP | [NumPy file formats](https://numpy.org/doc/stable/reference/generated/numpy.lib.format.html) |
| orc | Columnar storage for analytical processing | [Apache ORC](https://orc.apache.org/), [ORC versions](https://orc.apache.org/specification/) |
| parquet | Column-oriented data for storage/retrieval and analytical tools | [Apache Parquet overview](https://parquet.apache.org/docs/overview/) |
| pcap | Captured packet records with timestamps, potentially truncated | [IETF PCAP format draft](https://www.ietf.org/archive/id/draft-ietf-opsawg-pcap-06.html) |
| pcapng | Extensible packet, interface and capture metadata | [IETF pcapng draft](https://www.ietf.org/archive/id/draft-ietf-opsawg-pcapng-05.html) |
| pickle | Saved Python objects; unpickling itself can execute code | [Python pickle documentation](https://docs.python.org/3/library/pickle.html) |
| pkcs12 | Transferring digital identity material, including certificates and keys | [RFC 7292](https://www.rfc-editor.org/rfc/rfc7292.html) |
| plist | Structured application settings and named/list values | [Apple property lists](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/PropertyLists/Introduction/Introduction.html) |
| protein-data-bank | Legacy molecular coordinates and annotations | [wwPDB format guide](https://www.wwpdb.org/documentation/file-format-content/format33/v3.3.html) |
| reg | Saved Registry changes or exports for system/application settings | [Microsoft Registry documentation](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/windows-registry-advanced-users) |
| shapefile | Feature geometry with separate index and attribute companion files | [Esri specification](https://www.esri.com/content/dam/esrisites/sitecore-archive/Files/Pdfs/library/whitepapers/pdfs/shapefile.pdf) |
| sqlite | Application tables/indexes and other database objects; journals can matter to state | [SQLite file format](https://www.sqlite.org/fileformat.html) |
| toml | Named application/project settings and tables | [TOML specification](https://toml.io/en/v1.0.0) |
| torrent | File-distribution metadata, rather than the distributed file contents | [BitTorrent BEP 3](https://www.bittorrent.org/beps/bep_0003.html) |
| tsv | Table records with tab-separated fields | [IANA TSV registration](https://www.iana.org/assignments/media-types/text/tab-separated-values) |
| xml | Structured markup used across document and application formats | [W3C XML](https://www.w3.org/TR/xml/) |
| yaml | Data serialization, often used for readable configuration | [YAML specification](https://yaml.org/spec/1.2.2/) |

Changed IDs: `access`, `arrow`, `avro`, `dbase`, `dicom`, `fits`, `har`,
`json-lines`, `netcdf`, `npy`, `parquet`, `pcap`, `pickle`, `pkcs12` and `sqlite`.

The prior Arrow description implied that all Feather files used Arrow file
storage. The new wording explicitly distinguishes V1 from V2. The prior pickle
description connected execution to reading data "as code"; it now accurately
attributes that behavior to unpickling. Analyze does not unpickle objects.

The prior Access and DICOM URLs could not be retrieved. Alternate primary
overview pages supplied their purpose evidence. The libpcap manual was blocked
by the retrieval service; the retained IETF draft supplied packet-file evidence.
Access and FITS purpose passages were available through primary-site search text.
The OGC landing page returned navigation without the relevant prose, so the KML
row uses Google's format documentation. These limitations do not establish that
the older URLs are broken. PCAP/pcapng and HAR drafts are identified as drafts;
this review does not claim that they are current finalized standards.


Scope and verification
----------------------

This is a purpose review, not exhaustive alias, variant or decoder acceptance.
CSV/TSV dialects, all NDJSON conventions, DICOM object types, database generations,
certificate encodings, HDF/netCDF variants and legacy property-list formats still
need variant-specific evidence. The .fit, .cdf, .hdf, .pdb, .cap and .db ambiguities
remain intact. ZIP alone does not establish NPZ or KMZ, HDF5 does not establish
MATLAB data, and JSON/XML syntax alone does not establish an application's format.
An Arrow-family description does not prove support for reading Feather V1.

No database query, Registry import, certificate/key installation, packet replay,
network access from a selected file, GIS rendering, clinical interpretation or
object deserialization is introduced. Existing bounded analysis remains separate
from these descriptions. The review does not promise complete scientific data,
all companion files, absence of private information or safe execution by another
application. No new engine, library or third-party database is adopted.

A before/after comparison confirms that only `commonUses`, `source` and revision
changed, retaining all 243 records and recognition fields. Its delta is retained
in `.codex-temp/catalog-data-delta.json`. Existing catalog schema/loading/lookup
and capability-separation contracts provide verification without duplicating the
new prose in assertions. All 2,266 foundation contracts passed; the log is retained
in `.codex-temp/catalog-data-foundation.log`. No new production staging, worker,
native engine or interactive acceptance run is claimed for this prose-only change.
