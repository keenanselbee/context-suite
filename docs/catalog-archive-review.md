Archive, Package and Disk Image Catalog Review
=============================================

Revision **2026-09-11.7** reviews the general purposes of all 29 records in the
Archive or package and Archive or packaged document families. Sixteen records
receive clearer wording or a more relevant primary reference. The catalog still
has 243 records. IDs, names, families, extensions, exact-name hints, MIME fields,
detectors and operation permissions are unchanged.

The customer descriptions distinguish file bundles, compressed streams, software
deployment packages and virtual disks. A format's typical use is not a statement
about a particular file's contents, validity, safety or compatibility. Analyze
does not extract, mount, install, decompress or execute these files.


Reviewed purpose and sources
---------------------------

Descriptions are independently phrased from the primary references below,
consulted on 2026-09-11. No database, source code, specification text, archive
contents or executable was imported.

| Catalog IDs | Reviewed distinction | Primary evidence |
| --- | --- | --- |
| 7z | Bundles files; compression and encryption are optional features | [7-Zip format overview](https://www.7-zip.org/7z.html) |
| aab, apk | Publishing bundle versus installable Android package | [Android fundamentals](https://developer.android.com/guide/components/fundamentals), [App Bundles](https://developer.android.com/guide/app-bundle) |
| bzip2 | Compressed data, potentially wrapping an archive | [bzip2 manual](https://sourceware.org/bzip2/manual/manual.html), [GNU tar compression](https://www.gnu.org/software/tar/manual/tar.html) |
| cab | Multiple compressed files packaged for distribution/installation | [Microsoft Cabinet Files](https://learn.microsoft.com/en-us/windows/win32/msi/cabinet-files) |
| crx | Packaged browser extensions and themes | [Chrome distribution documentation](https://developer.chrome.com/docs/extensions/how-to/distribute/host-on-linux) |
| deb | Debian binary software packages | [Debian package format](https://manpages.debian.org/bookworm/dpkg-dev/deb.5.en.html) |
| dmg | Disk/folder contents stored as a mountable Mac volume | [Apple Disk Utility](https://support.apple.com/guide/disk-utility/create-a-disk-image-dskutl11888/mac) |
| gzip | Compressed data streams; TAR supplies the archive layer | [RFC 1952](https://www.rfc-editor.org/rfc/rfc1952.html), [GNU tar](https://www.gnu.org/software/tar/manual/tar.html) |
| iso | Optical-disc contents for distribution, preservation or mounting | [Library of Congress ISO description](https://wwws.loc.gov/preservation/digital/formats/fdd/fdd000348.shtml) |
| java-archive | Java libraries/applications and web/enterprise deployments | [JAR specification](https://docs.oracle.com/en/java/javase/25/docs/specs/jar/jar.html), [Oracle JAR/WAR/EAR packaging](https://docs.oracle.com/javaee/7/tutorial/packaging001.htm) |
| lz4 | Compressed data for files and streams | [LZ4 framing introduction](https://github.com/lz4/lz4/blob/dev/doc/lz4_Frame_format.md) |
| msi | MSI installation database versus MSP update patch | [Windows Installer extensions](https://learn.microsoft.com/en-us/windows/win32/msi/windows-installer-file-extensions) |
| msix | Windows app files and deployment metadata | [Microsoft MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview) |
| nuget | .NET code/assets versus debugging-symbol packages | [NuGet overview](https://learn.microsoft.com/en-us/nuget/what-is-nuget), [SNUPKG symbols](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg) |
| qcow2 | QCOW and QCOW2 virtual disks have different features/layouts | [QEMU disk image formats](https://www.qemu.org/docs/master/system/images.html), [QCOW2 structure](https://www.qemu.org/docs/master/interop/qcow2.html) |
| rar | Archives with optional encryption and multiple volumes | [RAR format description](https://www.rarlab.com/technote.htm) |
| rpm | Software files/metadata, including source-package use | [RPM format description](https://rpm.org/docs/6.0.x/manual/format_v3.html) |
| tar, tar-gzip | File/metadata bundle versus its GZIP-compressed form | [GNU tar manual](https://www.gnu.org/software/tar/manual/tar.html) |
| vhd | Virtual disks, with distinct VHD and VHDX layouts | [Microsoft VHD overview](https://learn.microsoft.com/en-us/windows/win32/vstor/about-vhd), [VHDX specification](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-vhdx/83e061f8-f6e2-4de1-91bd-5d518a43d477) |
| vmdk | Virtual disk data can have a separate descriptor | [Broadcom descriptor/data explanation](https://knowledge.broadcom.com/external/article/321422) |
| vsix | Extension packages for Visual Studio and VS Code have separate targets | [Visual Studio packaging](https://learn.microsoft.com/en-us/visualstudio/extensibility/shipping-visual-studio-extensions), [VS Code packaging](https://code.visualstudio.com/api/working-with-extensions/publishing-extension) |
| wheel | Python distributions prepared for installation | [Python wheel specification](https://packaging.python.org/en/latest/specifications/binary-distribution-format/) |
| wim | Windows files packaged as deployment images | [Microsoft Windows images](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/work-with-windows-images) |
| xpi | Firefox extension files packaged as ZIP-based XPI | [Mozilla extension packaging](https://extensionworkshop.com/documentation/publish/package-your-extension/) |
| xz | Compressed streams; archiving is a separate layer | [XZ format overview](https://tukaani.org/xz/format.html) |
| zip | General file storage/transfer container, also used by documents/packages | [PKWARE format reference](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT), [JAR](https://docs.oracle.com/en/java/javase/25/docs/specs/jar/jar.html), [Mozilla XPI](https://extensionworkshop.com/documentation/publish/package-your-extension/) |
| zstd | Compressed data that can wrap another format | [RFC 8878](https://www.rfc-editor.org/rfc/rfc8878.html) |

The modified IDs are `7z`, `aab`, `bzip2`, `crx`, `dmg`, `java-archive`, `lz4`,
`msi`, `nuget`, `qcow2`, `rpm`, `tar`, `tar-gzip`, `vmdk`, `wheel` and `xz`.
The QCOW record previously linked only QCOW2 internals despite also listing QCOW.
It now links the QEMU page that distinguishes both. The Java record now links
the packaging explanation that covers WAR and EAR, alongside JAR provenance here.

Direct retrieval failed for the prior Chromium design page, the two GNU tar
section URLs, the prior RPM format page and the prior Broadcom URL. Alternate
primary references supplied the reviewed purpose; these failures do not prove
the old URLs are broken. The ISO and VS Code purpose passages were available
through their primary sites' search-indexed text when direct retrieval failed.


Scope and verification
----------------------

This is a purpose review, not exhaustive alias, version or binary-format
certification. WIM/ESD encoding differences, ZIPX variants and full MSIX/APPX
bundle coverage still need specific variant evidence. Compressed TAR suffixes
remain filename hints until content supports them. ZIP content evidence alone
does not establish APK, JAR, wheel or extension identity. QCOW/QCOW2 and VHD/VHDX
descriptions do not imply all versions support every feature.

No package signature is validated, installation compatibility tested or encrypted
content opened. An optional encryption feature never means that a selected file
is encrypted. Vendor installation, security, performance and update claims are
not Context Suite promises. No engine or dataset redistribution decision is made.

A before/after JSON comparison confirms that only `commonUses`, `source` and
the revision changed. The delta is retained in `.codex-temp/catalog-archive-delta.json`.
Existing catalog schema/loading/lookup and capability-separation contracts provide
the relevant automated verification; no duplicate prose assertions were added.

All **2,243 foundation contracts pass**; log:
`.codex-temp/catalog-archive-foundation.log`. Public-source boundary, system-theme
policy, 94 documentation files and both repositories' whitespace checks pass.
No new production staging, native worker matrix, visible UI, screen-reader,
theme/DPI or installed-shell acceptance is claimed for this description change.
