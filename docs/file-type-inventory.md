Common File Type Inventory
===========================

Catalog revision: 2026-09-09.2; 237 records. This is a descriptive
inventory, not a list of supported conversions or complete decoders. Multiple
extensions and related variants may share a record; aliases are not counted
separately. Known filename matches and longest compound suffixes take precedence
over generic suffix hints. Shared extensions retain multiple candidates.

All descriptions are independently authored in the embedded
[catalog](../src/ContextSuite.Core/Analysis/file-types.json).
The [source retrieval audit](catalog-source-review.json) records available,
search-indexed and unavailable references. Retrieval is not a substitute for
semantic and format-variant review; unresolved source gaps remain release work.
No registry database, source text or reference implementation was imported.

Core tests validate lookup reachability, ambiguity and confidence behavior.
Filename hints never enable an operation. See [verified analysis coverage](file-type-coverage.md)
for actual parsed facts and current resource limits.

| ID | Description | Names / extensions | Analyze identification |
| --- | --- | --- | --- |
| 3gp | 3GPP media container | `.3gp`, `.3g2` | Filename hint only |
| 3mf | 3D Manufacturing Format | `.3mf` | Filename hint only |
| 7z | 7-Zip archive | `.7z` | Filename hint only |
| aab | Android App Bundle | `.aab` | Filename hint only |
| aac | AAC audio stream | `.aac`, `.adts` | Filename hint only |
| access | Access database | `.mdb`, `.accdb` | Filename hint only |
| aiff | AIFF audio | `.aif`, `.aiff`, `.aifc` | Filename hint only |
| ape | Monkey's Audio | `.ape` | Filename hint only |
| apk | Android package | `.apk` | Filename hint only |
| arrow | Apache Arrow data | `.arrow`, `.feather` | Filename hint only |
| aseprite | Aseprite sprite | `.aseprite`, `.ase` | Filename hint only |
| asf | Advanced Systems Format | `.asf`, `.wmv` | Filename hint only |
| ass | ASS/SSA subtitles | `.ass`, `.ssa` | Filename hint; text sampling |
| au | AU audio | `.au`, `.snd` | Filename hint only |
| audacity | Audacity project | `.aup`, `.aup3` | Filename hint only |
| avi | AVI video | `.avi` | Filename hint only |
| avif | AVIF image | `.avif` | Filename hint only |
| avro | Apache Avro data | `.avro` | Filename hint only |
| batch | Windows batch script | `.bat`, `.cmd` | Filename hint; text sampling |
| bethesda-archive | Bethesda game archive | `.bsa`, `.ba2` | Filename hint only |
| bibtex | BibTeX bibliography | `.bib` | Filename hint; text sampling |
| blend | Blender project | `.blend` | Filename hint only |
| bmp | Windows bitmap | `.bmp`, `.dib` | Filename hint only |
| bzip2 | Bzip2 compressed data | `.bz2`, `.bzip2`, `.tbz`, `.tbz2` | Filename hint only |
| c | C source or header | `.c`, `.h` | Filename hint; text sampling |
| cab | Windows cabinet | `.cab` | Filename hint only |
| caf | Core Audio file | `.caf` | Filename hint only |
| camera-raw | Camera raw image | `.cr2`, `.cr3`, `.nef`, `.nrw`, `.arw`, `.raf`, `.rw2`, `.orf`, `.pef`, `.srw` | Filename hint only |
| certificate | Certificate or encoded key data | `.pem`, `.crt`, `.cer`, `.der`, `.key` | Filename hint; text sampling |
| chm | Compiled HTML Help | `.chm` | Filename hint only |
| cmake | CMake build configuration | `CMakeLists.txt`, `.cmake` | Filename hint; text sampling |
| collada | COLLADA scene | `.dae` | Filename hint; text sampling |
| cpp | C++ source or header | `.cpp`, `.cc`, `.cxx`, `.hpp`, `.hh`, `.hxx`, `.h` | Filename hint; text sampling |
| crx | Chrome extension | `.crx` | Filename hint only |
| csharp | C# source | `.cs` | Filename hint; text sampling |
| css | CSS style sheet | `.css` | Filename hint; text sampling |
| csv | Comma-separated table | `.csv` | Filename hint; text sampling |
| cue | Cue sheet | `.cue` | Filename hint; text sampling |
| cur | Windows cursor | `.cur`, `.ani` | Filename hint only |
| dart | Dart source | `.dart` | Filename hint; text sampling |
| dbase | dBASE table | `.dbf` | Filename hint only |
| dds | DDS texture | `.dds` | Existing bounded content detector |
| deb | Debian package | `.deb` | Filename hint only |
| desktop-entry | Desktop entry | `.desktop` | Filename hint; text sampling |
| dicom | DICOM medical data | `.dcm`, `.dicom` | Filename hint only |
| djvu | DjVu document | `.djvu`, `.djv` | Filename hint only |
| dmg | Apple disk image | `.dmg` | Filename hint only |
| dng | Digital Negative image | `.dng` | Filename hint only |
| doc | Legacy Word document | `.doc`, `.dot` | Filename hint only |
| dockerfile | Docker build instructions | `Dockerfile`, `Containerfile` | Filename hint; text sampling |
| docx | Word document | `.docx`, `.docm`, `.dotx`, `.dotm` | Bounded package declarations and main XML; likely family |
| dwg | AutoCAD drawing | `.dwg` | Filename hint only |
| dxf | Drawing Exchange Format | `.dxf` | Filename hint; text sampling |
| editorconfig | EditorConfig settings | `.editorconfig` | Filename hint; text sampling |
| elf | ELF program or library | `.elf`, `.so` | Filename hint only |
| elixir | Elixir source | `.ex`, `.exs` | Filename hint; text sampling |
| eml | Email message | `.eml` | Filename hint; text sampling |
| environment | Environment configuration | `.env`, `.env` | Filename hint; text sampling |
| eps | Encapsulated PostScript | `.eps`, `.epsf` | Filename hint; text sampling |
| epub | EPUB publication | `.epub` | Filename hint only |
| erlang | Erlang source | `.erl`, `.hrl` | Filename hint; text sampling |
| event-log | Windows event log | `.evtx`, `.evt` | Filename hint only |
| exr | OpenEXR image | `.exr` | Filename hint only |
| fbx | FBX scene | `.fbx` | Filename hint only |
| fits | FITS scientific data | `.fits`, `.fit`, `.fts` | Filename hint only |
| flac | FLAC audio | `.flac` | Bounded STREAMINFO and metadata block observations; samples not validated |
| flv | Flash video | `.flv`, `.f4v` | Filename hint only |
| font-collection | Font collection | `.ttc`, `.otc` | Filename hint only |
| fortran | Fortran source | `.f`, `.for`, `.f90`, `.f95`, `.f03`, `.f08` | Filename hint; text sampling |
| fsharp | F# source | `.fs`, `.fsi`, `.fsx` | Filename hint; text sampling |
| geojson | GeoJSON geographic data | `.geojson` | Filename hint; text sampling |
| gif | GIF image | `.gif` | Filename hint only |
| gitignore | Git ignore rules | `.gitignore` | Filename hint; text sampling |
| gltf | glTF scene | `.gltf`, `.glb` | Filename hint only |
| go | Go source | `.go` | Filename hint; text sampling |
| godot-resource | Godot resource or scene | `.tres`, `.res`, `.tscn`, `.scn` | Filename hint only |
| gpx | GPS exchange data | `.gpx` | Filename hint; text sampling |
| gzip | GZIP compressed data | `.gz`, `.gzip` | Filename hint only |
| har | HTTP archive | `.har` | Filename hint; text sampling |
| haskell | Haskell source | `.hs`, `.lhs` | Filename hint; text sampling |
| hdf5 | HDF5 dataset | `.h5`, `.hdf5`, `.hdf` | Filename hint only |
| heif | HEIF image container | `.heif`, `.heic`, `.hif` | Filename hint only |
| html | HTML document | `.html`, `.htm`, `.xhtml` | Filename hint; text sampling |
| ical | Calendar data | `.ics`, `.ical` | Filename hint; text sampling |
| icns | Apple icon | `.icns` | Filename hint only |
| ico | Windows icon | `.ico` | Filename hint only |
| iges | IGES CAD data | `.iges`, `.igs` | Filename hint; text sampling |
| illustrator | Illustrator artwork | `.ai` | Filename hint only |
| ini | INI-style configuration | `.ini`, `.cfg`, `.conf` | Filename hint; text sampling |
| iso | ISO disc image | `.iso` | Filename hint only |
| java | Java source | `.java` | Filename hint; text sampling |
| java-archive | Java archive | `.jar`, `.war`, `.ear` | Filename hint only |
| java-class | Java class file | `.class` | Filename hint only |
| javascript | JavaScript source | `.js`, `.mjs`, `.cjs`, `.jsx` | Filename hint; text sampling |
| jpeg | JPEG image | `.jpg`, `.jpeg`, `.jpe`, `.jfif` | Filename hint only |
| jpeg2000 | JPEG 2000 image | `.jp2`, `.j2k`, `.jpf`, `.jpx` | Filename hint only |
| json | JSON data | `.json` | Bounded whole-file structure parser; filename hint otherwise |
| json-lines | JSON Lines data | `.jsonl`, `.ndjson` | Filename hint; text sampling |
| julia | Julia source | `.jl` | Filename hint; text sampling |
| jxl | JPEG XL image | `.jxl` | Filename hint only |
| keynote | Keynote presentation | `.key` | Filename hint only |
| kindle | Kindle e-book | `.azw`, `.azw3` | Filename hint only |
| kml | KML geographic data | `.kml`, `.kmz` | Filename hint only |
| kotlin | Kotlin source | `.kt`, `.kts` | Filename hint; text sampling |
| krita | Krita document | `.kra` | Filename hint only |
| latex | TeX/LaTeX source | `.tex`, `.sty`, `.cls` | Filename hint; text sampling |
| less | Less style source | `.less` | Filename hint; text sampling |
| lua | Lua script | `.lua` | Filename hint; text sampling |
| lz4 | LZ4 compressed data | `.lz4` | Filename hint only |
| m3u | M3U playlist | `.m3u`, `.m3u8` | Filename hint; text sampling |
| m4a | MPEG-4 audio container | `.m4a`, `.m4b` | Filename hint only |
| makefile | Make build instructions | `Makefile`, `GNUmakefile`, `.mk`, `.mak` | Filename hint; text sampling |
| markdown | Markdown text | `.md`, `.markdown` | Filename hint; text sampling |
| matlab-data | MATLAB data | `.mat` | Filename hint only |
| matlab-script | MATLAB script or function | `.m` | Filename hint; text sampling |
| matroska | Matroska media container | `.mkv`, `.mka`, `.mks` | Filename hint only |
| mhtml | Archived web page | `.mht`, `.mhtml` | Filename hint; text sampling |
| midi | MIDI sequence | `.mid`, `.midi`, `.kar` | Filename hint only |
| minidump | Crash dump | `.dmp`, `.mdmp` | Filename hint only |
| mobi | Mobipocket e-book | `.mobi`, `.prc` | Filename hint only |
| mp3 | MP3 audio | `.mp3` | Filename hint only |
| mp4 | MPEG-4 media container | `.mp4`, `.m4v` | Filename hint only |
| mpeg | MPEG program stream | `.mpg`, `.mpeg`, `.mpe`, `.vob` | Filename hint only |
| mpeg-ts | MPEG transport stream | `.ts`, `.m2ts`, `.mts`, `.m2t` | Filename hint only |
| msg | Outlook message | `.msg` | Filename hint only |
| msi | Windows Installer database | `.msi`, `.msp` | Filename hint only |
| msix | Windows app package | `.msix`, `.appx`, `.msixbundle`, `.appxbundle` | Filename hint only |
| mz | DOS executable header | Signature only | Existing bounded content detector |
| netcdf | NetCDF dataset | `.nc`, `.nc4`, `.cdf` | Filename hint only |
| notebook | Jupyter notebook | `.ipynb` | Filename hint; text sampling |
| npy | NumPy array | `.npy`, `.npz` | Filename hint only |
| nuget | NuGet package | `.nupkg`, `.snupkg` | Filename hint only |
| numbers | Numbers spreadsheet | `.numbers` | Filename hint only |
| object-code | Compiled object file | `.o`, `.obj` | Filename hint only |
| objective-c | Objective-C source | `.m`, `.mm` | Filename hint; text sampling |
| odg | OpenDocument drawing | `.odg`, `.otg` | Filename hint only |
| odp | OpenDocument presentation | `.odp`, `.otp` | Bounded MIME/manifest and unencrypted content family |
| ods | OpenDocument spreadsheet | `.ods`, `.ots` | Bounded MIME/manifest and unencrypted content family |
| odt | OpenDocument text | `.odt`, `.ott` | Bounded MIME/manifest and unencrypted content family |
| ogg | Ogg container | `.ogg`, `.oga`, `.ogv`, `.opus`, `.ogx` | Existing bounded content detector |
| ole | Compound file container | Signature only | Existing bounded content detector |
| openraster | OpenRaster image | `.ora` | Filename hint only |
| opentype | OpenType font | `.otf` | Filename hint only |
| orc | Apache ORC data | `.orc` | Filename hint only |
| pages | Pages document | `.pages` | Filename hint only |
| parquet | Apache Parquet data | `.parquet` | Filename hint only |
| pcap | Packet capture | `.pcap`, `.cap` | Filename hint only |
| pcapng | PCAP Next Generation capture | `.pcapng` | Filename hint only |
| pdb | Program debug database | `.pdb` | Filename hint only |
| pdf | PDF document | `.pdf` | Existing bounded content detector |
| pe | Windows executable image | `.exe`, `.dll`, `.sys`, `.scr`, `.cpl`, `.ocx` | Existing bounded content detector |
| perl | Perl source | `.pl`, `.pm` | Filename hint; text sampling |
| php | PHP source | `.php`, `.phtml` | Filename hint; text sampling |
| pickle | Python pickle data | `.pkl`, `.pickle` | Filename hint only |
| pkcs12 | PKCS #12 key container | `.p12`, `.pfx` | Filename hint only |
| plist | Apple property list | `.plist` | Filename hint; text sampling |
| pls | PLS playlist | `.pls` | Filename hint; text sampling |
| ply | PLY geometry | `.ply` | Filename hint only |
| png | PNG image | `.png`, `.apng` | Existing bounded content detector |
| pnm | Portable anymap image | `.pbm`, `.pgm`, `.ppm`, `.pnm`, `.pam` | Filename hint only |
| postscript | PostScript document | `.ps` | Filename hint; text sampling |
| powershell | PowerShell script | `.ps1`, `.psm1`, `.psd1` | Filename hint; text sampling |
| ppt | Legacy PowerPoint presentation | `.ppt`, `.pot`, `.pps` | Filename hint only |
| pptx | PowerPoint presentation | `.pptx`, `.pptm`, `.potx`, `.potm`, `.ppsx`, `.ppsm` | Bounded package declarations and main XML; likely family |
| properties | Java properties | `.properties` | Filename hint; text sampling |
| psd | Photoshop document | `.psd`, `.psb` | Filename hint only |
| python | Python source | `.py`, `.pyw`, `.pyi` | Filename hint; text sampling |
| python-bytecode | Python bytecode | `.pyc`, `.pyo` | Filename hint only |
| qcow2 | QEMU virtual disk | `.qcow`, `.qcow2` | Filename hint only |
| qoi | QOI image | `.qoi` | Filename hint only |
| quicktime | QuickTime movie | `.mov`, `.qt` | Filename hint only |
| r | R source | `.r`, `.rscript` | Filename hint; text sampling |
| radiance | Radiance HDR image | `.hdr`, `.rgbe` | Filename hint only |
| rar | RAR archive | `.rar` | Filename hint only |
| reg | Windows Registry data | `.reg` | Filename hint; text sampling |
| rpm | RPM package | `.rpm` | Filename hint only |
| rtf | Rich Text Format | `.rtf` | Filename hint; text sampling |
| ruby | Ruby source | `.rb`, `.rake`, `.gemspec` | Filename hint; text sampling |
| rust | Rust source | `.rs` | Filename hint; text sampling |
| sass | Sass style source | `.scss`, `.sass` | Filename hint; text sampling |
| scala | Scala source | `.scala`, `.sc` | Filename hint; text sampling |
| shapefile | ESRI shapefile geometry | `.shp` | Filename hint only |
| shell-script | Shell script | `.sh`, `.bash`, `.zsh` | Filename hint; text sampling |
| soundfont | SoundFont instrument bank | `.sf2`, `.sf3` | Filename hint only |
| sql | SQL script | `.sql` | Filename hint; text sampling |
| sqlite | SQLite database | `.sqlite`, `.sqlite3`, `.db`, `.db3` | Filename hint only |
| srt | SubRip subtitles | `.srt` | Filename hint; text sampling |
| static-library | Static code library | `.a`, `.lib` | Filename hint only |
| step | STEP product data | `.step`, `.stp` | Filename hint; text sampling |
| stl | STL mesh | `.stl` | Filename hint only |
| svg | SVG vector image | `.svg` | Filename hint; text sampling |
| swift | Swift source | `.swift` | Filename hint; text sampling |
| tar | TAR archive | `.tar` | Filename hint only |
| tar-gzip | GZIP-compressed TAR archive | `.tar.gz`, `.tgz` | Filename hint only |
| text | Text | `.txt`, `.text`, `.log` | Existing bounded content detector |
| tga | Targa image | `.tga`, `.targa` | Filename hint only |
| tiff | TIFF image | `.tif`, `.tiff` | Filename hint only |
| toml | TOML configuration | `.toml` | Filename hint; text sampling |
| torrent | BitTorrent metadata | `.torrent` | Filename hint only |
| tracker | Tracker music module | `.mod`, `.xm`, `.it`, `.s3m`, `.mptm` | Filename hint only |
| truetype | TrueType font | `.ttf` | Filename hint only |
| tsv | Tab-separated table | `.tsv`, `.tab` | Filename hint; text sampling |
| typescript | TypeScript source | `.ts`, `.tsx`, `.d.ts` | Filename hint; text sampling |
| unitypackage | Unity asset package | `.unitypackage` | Filename hint only |
| unreal-asset | Unreal Engine asset | `.uasset`, `.umap` | Filename hint only |
| usd | Universal Scene Description | `.usd`, `.usda`, `.usdc`, `.usdz` | Filename hint only |
| valve-package | Valve package | `.vpk` | Filename hint only |
| vbscript | VBScript source | `.vbs` | Filename hint; text sampling |
| vcard | Contact card | `.vcf`, `.vcard` | Filename hint; text sampling |
| vhd | Virtual Hard Disk | `.vhd`, `.vhdx` | Filename hint only |
| vmdk | VMware virtual disk | `.vmdk` | Filename hint only |
| vsix | Visual Studio extension | `.vsix` | Filename hint only |
| wasm | WebAssembly module | `.wasm` | Filename hint only |
| wave | WAVE audio | `.wav`, `.wave` | Bounded RIFF/chunk/format declarations; samples not validated |
| wavefront | Wavefront model | `.obj`, `.mtl` | Filename hint; text sampling |
| wavpack | WavPack audio | `.wv` | Filename hint only |
| webm | WebM media container | `.webm` | Filename hint only |
| webp | WebP image | `.webp` | Filename hint only |
| webvtt | WebVTT captions | `.vtt` | Filename hint; text sampling |
| wheel | Python wheel | `.whl` | Filename hint only |
| wim | Windows imaging archive | `.wim`, `.esd` | Filename hint only |
| windows-shortcut | Windows shortcut | `.lnk` | Filename hint only |
| windows-url | Internet shortcut | `.url` | Filename hint; text sampling |
| wma | Windows Media audio | `.wma` | Filename hint only |
| woff | WOFF web font | `.woff` | Filename hint only |
| woff2 | WOFF2 web font | `.woff2` | Filename hint only |
| xcf | GIMP image | `.xcf` | Filename hint only |
| xls | Legacy Excel workbook | `.xls`, `.xlt` | Filename hint only |
| xlsb | Excel binary workbook | `.xlsb` | Filename hint only |
| xlsx | Excel workbook | `.xlsx`, `.xlsm`, `.xltx`, `.xltm` | Bounded package declarations and main XML; likely family |
| xml | XML document | `.xml` | Bounded whole-file structure parser; filename hint otherwise |
| xpi | Firefox extension | `.xpi` | Filename hint only |
| xps | XML Paper Specification document | `.xps`, `.oxps` | Filename hint only |
| xz | XZ compressed data | `.xz`, `.txz` | Filename hint only |
| yaml | YAML data | `.yaml`, `.yml` | Filename hint; text sampling |
| zip | ZIP container | `.zip`, `.zipx` | Existing bounded content detector |
| zstd | Zstandard compressed data | `.zst`, `.zstd`, `.tzst` | Filename hint only |

The next coverage pass should add MIME identifiers where meaningful, independently
tested content rules for additional families, and representative real files for
unverified variants. Do not turn an extension hint into confirmed identity merely
to increase the detector count.
