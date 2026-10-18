"""Archive the patched, restricted-code-free vgmstream source reproducibly."""

import argparse
import pathlib
import zipfile


parser = argparse.ArgumentParser()
parser.add_argument("--source", type=pathlib.Path, required=True)
parser.add_argument("--output", type=pathlib.Path, required=True)
args = parser.parse_args()
source = args.source.resolve(strict=True)
output = args.output.resolve()
if output.exists():
    raise ValueError(f"Output already exists: {output}")
if source.name != "vgmstream-71e2361042531fe767fb98300cf8c1ee95e539a0":
    raise ValueError("Unexpected pinned source directory")
if "#ifdef VGM_WEM_ONLY" not in (source / "src/vgmstream_init.c").read_text(encoding="utf-8"):
    raise ValueError("WEM-only source patch is missing")
for path in (source / "src").rglob("*"):
    if path.is_file() and any(word in path.name.lower() for word in
                              ("relic", "compresswave", "mio_")):
        raise ValueError(f"Excluded codec source remains: {path}")
if (source / "src/meta/mio.c").exists():
    raise ValueError("Excluded MIO format source remains")

binary_suffixes = {".dll", ".exe", ".lib", ".a"}
allowed_binary = "ext_libs/dll-x64/libvorbis.dll"
files = sorted(path for path in source.rglob("*") if path.is_file()
               and path.relative_to(source).as_posix() != "version_auto.h"
               and (path.suffix.lower() not in binary_suffixes
                    or path.relative_to(source).as_posix() == allowed_binary))
retained_binaries = [path.relative_to(source).as_posix() for path in files
                     if path.suffix.lower() in binary_suffixes]
if retained_binaries != [allowed_binary]:
    raise ValueError(f"Unexpected sanitized binary inventory: {retained_binaries}")
output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED,
                     compresslevel=9) as archive:
    for path in files:
        name = f"{source.name}/{path.relative_to(source).as_posix()}"
        item = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
        item.compress_type = zipfile.ZIP_DEFLATED
        item.external_attr = 0o100644 << 16
        archive.writestr(item, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED,
                         compresslevel=9)
print(f"Packaged {len(files)} sanitized source files at {output}")
