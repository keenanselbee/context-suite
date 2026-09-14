Archive and Package Catalog Alias Review
=======================================

Reviewed 2026-09-14 against catalog revision 2026-09-14.1. All 50 extension
associations across 30 records in the Archive or package and Archive or packaged
document families are accounted for below.
The previous catalog had 48 associations across 29 records. This review adds
Alpine Linux packages as an alternative meaning of `.apk`, and Java resource
adapters as an alternative meaning of `.rar`. The Java purpose description now
includes resource adapters. Descriptions are independently authored.

Filename associations remain hints. Neither change adds a detector, extraction,
installation or conversion. Undecoded content retains both possible meanings;
a ZIP header identifies only the container, without certifying Android or Java
package semantics. The catalog now has 244 records: 34 with content identification
and 210 available only as filename hints. Existing detector limits remain in
[coverage](file-type-coverage.md).

Reviewed associations
---------------------

Each row preserves the exact current extension array. Publisher documentation
establishes some conventions; upstream implementation declarations establish
others. The latter do not imply a format standard, parser adoption, compatible
package versions or supported transformations in Context Suite.

| Catalog ID | Extensions | Association and boundary | Primary evidence |
| --- | --- | --- | --- |
| 7z | `.7z` | 7z archive convention | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/7z/7zRegister.cpp) |
| aab | `.aab` | Android publishing bundle; distinct from an installable APK | [Android fundamentals](https://developer.android.com/guide/components/fundamentals) |
| alpine-apk | `.apk` | Alpine Linux software package; shared suffix with Android | [Alpine architecture](https://wiki.alpinelinux.org/wiki/Architecture) |
| apk | `.apk` | Android application package; shared suffix with Alpine | [Android fundamentals](https://developer.android.com/guide/components/fundamentals) |
| bzip2 | `.bz2`, `.bzip2`, `.tbz`, `.tbz2` | Stream and compressed-TAR conventions; `.bzip2` has implementation evidence | [bzip2 manual](https://www.sourceware.org/bzip2/manual/manual.html), [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/Bz2Handler.cpp) |
| cab | `.cab` | Cabinet filename convention | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/Cab/CabRegister.cpp) |
| crx | `.crx` | Chrome extension/theme package | [Chrome distribution](https://developer.chrome.com/docs/extensions/how-to/distribute/host-on-linux) |
| deb | `.deb` | Debian binary software package | [Debian format manual](https://manpages.debian.org/bookworm/dpkg-dev/deb.5.en.html) |
| dmg | `.dmg` | Apple disk-image convention | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/DmgHandler.cpp) |
| gzip | `.gz`, `.gzip` | Gzip stream conventions | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/GzHandler.cpp) |
| iso | `.iso` | Disc-image filename convention | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/Iso/IsoRegister.cpp) |
| java-archive | `.jar`, `.war`, `.ear`, `.rar` | Java archive, web/enterprise deployment and resource-adapter packages | [Oracle packaging guide](https://docs.oracle.com/javaee/7/tutorial/packaging001.htm) |
| lz4 | `.lz4` | LZ4 compressed stream convention | [LZ4 manual](https://github.com/lz4/lz4/blob/dev/programs/lz4.1) |
| msi | `.msi`, `.msp` | Installer database and patch, respectively | [Microsoft extensions](https://learn.microsoft.com/en-us/windows/win32/msi/windows-installer-file-extensions) |
| msix | `.msix`, `.appx`, `.msixbundle`, `.appxbundle` | Related Windows package and bundle conventions | [Microsoft package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements) |
| nuget | `.nupkg`, `.snupkg` | NuGet package and symbol package | [NuGet symbols](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg) |
| qcow2 | `.qcow`, `.qcow2` | Related virtual-disk variants; not identical versions | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/QcowHandler.cpp) |
| rar | `.rar` | RAR archive; shared suffix with a Java resource adapter | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/Rar/RarHandler.cpp) |
| rpm | `.rpm` | RPM software package convention | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/RpmHandler.cpp) |
| tar | `.tar` | TAR archive convention | [GNU tar manual](https://www.gnu.org/software/tar/manual/tar.html) |
| tar-gzip | `.tar.gz`, `.tgz` | Gzip-compressed TAR conventions; longest suffix takes precedence | [GNU tar manual](https://www.gnu.org/software/tar/manual/tar.html) |
| vhd | `.vhd`, `.vhdx` | Related Windows virtual-disk formats | [Microsoft virtual disks](https://learn.microsoft.com/en-us/windows-server/storage/disk-management/manage-virtual-hard-disks) |
| vmdk | `.vmdk` | Virtual-machine disk convention | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/VmdkHandler.cpp) |
| vsix | `.vsix` | Visual Studio and VS Code extensions; not interchangeable packages | [Visual Studio](https://learn.microsoft.com/en-us/visualstudio/extensibility/vsix/get-started/extension-anatomy?view=visualstudio), [VS Code](https://code.visualstudio.com/api/working-with-extensions/publishing-extension) |
| wheel | `.whl` | Python built distribution | [Python packaging specification](https://packaging.python.org/en/latest/specifications/binary-distribution-format/) |
| wim | `.wim`, `.esd` | Windows image-container conventions; variant compatibility unproven | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/Wim/WimRegister.cpp) |
| xpi | `.xpi` | Firefox extension ZIP package convention | [Mozilla packaging](https://extensionworkshop.com/documentation/publish/package-your-extension/) |
| xz | `.xz`, `.txz` | XZ stream and compressed-TAR conventions | [XZ manual](https://tukaani.org/xz/man/xz.1.html) |
| zip | `.zip`, `.zipx` | Related ZIP container conventions; codecs and variants remain distinct | [7-Zip declaration](https://raw.githubusercontent.com/ip7z/7zip/main/CPP/7zip/Archive/Zip/ZipRegister.cpp), [WinZip naming](https://kb.winzip.com/help/CS/help_split_zip_info.htm) |
| zstd | `.zst`, `.zstd`, `.tzst` | Zstandard stream and compressed-TAR conventions | [Zstandard manual](https://github.com/facebook/zstd/blob/dev/programs/zstd.1.md), [suffix changelog](https://github.com/facebook/zstd/blob/dev/CHANGELOG), [GNU tar](https://www.gnu.org/software/tar/manual/tar.html) |

Scope and verification
----------------------

This accounts for current associations, not every possible suffix or competing
meaning. The existing [purpose review](catalog-archive-review.md) covers the
previous 29 records. Alpine's publisher supplies the added record's package-manager
purpose; no assumption about its internal format generation is needed.

Sources were read as documentation or static declarations. No external source
code, implementation, binary or archive payload was imported into production.
The separate RarRegister.cpp URL returned 404; the actual declaration was retrieved
from RarHandler.cpp. The live Library of Congress ISO page was unavailable during
this review; the table instead cites a retrieved upstream filename declaration.

Foundation contracts exercise uppercase shared suffixes, undecoded ambiguity,
contradictory PDF content, and ZIP signatures that cannot certify package type.
The Release test-host build passed with zero warnings/errors, followed by
2,822 foundation contracts. Inventory rows and exact table arrays also reconcile.
The local receipt is `.codex-temp/catalog-archive-alias-review.json`; catalog SHA-256:
`50B31BB94FFAB47B4D04421057AF1806FC4E44F3381B4D692BA840928CC6850B`. Candidate 1.0.9 is unchanged; these catalog edits require
the next version before a new production payload is staged.
