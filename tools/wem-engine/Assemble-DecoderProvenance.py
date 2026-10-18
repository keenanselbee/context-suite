"""Collect the pinned decoder's corresponding source, notices, and build receipt."""

import argparse
import hashlib
import json
import pathlib
import re
import shutil
import subprocess
import zipfile


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def copy(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)


def comment_with(path, marker):
    content = path.read_text(encoding="utf-8")
    position = content.find(marker)
    if position < 0:
        raise ValueError(f"Missing license marker {marker!r} in {path}")
    start = content.rfind("/*", 0, position)
    end = content.find("*/", position)
    if start < 0 or end < 0:
        raise ValueError(f"Missing surrounding license comment in {path}")
    return content[start : end + 2]


parser = argparse.ArgumentParser()
parser.add_argument("--source", type=pathlib.Path, required=True)
parser.add_argument("--archive", type=pathlib.Path, required=True)
parser.add_argument("--install", type=pathlib.Path, required=True)
parser.add_argument("--recipe", type=pathlib.Path, required=True)
parser.add_argument("--link-map", type=pathlib.Path, required=True)
parser.add_argument("--dumpbin", type=pathlib.Path, required=True)
args = parser.parse_args()
source = args.source.resolve(strict=True)
install = args.install.resolve(strict=True)
provenance = install / "provenance"
notices = provenance / "notices"
if provenance.exists():
    raise ValueError(f"Provenance directory already exists: {provenance}")
with zipfile.ZipFile(args.archive.resolve(strict=True)) as sanitized_source:
    binaries = sorted(name.split("/", 1)[1] for name in sanitized_source.namelist()
                      if pathlib.PurePosixPath(name).suffix.lower() in {".dll", ".exe", ".lib", ".a"})
if binaries != ["ext_libs/dll-x64/libvorbis.dll"]:
    raise ValueError(f"Sanitized source includes unexpected binaries: {binaries}")

copy(args.archive.resolve(strict=True), provenance / "source" / args.archive.name)
for name in ("Build-VgmstreamDecoder.ps1", "Assemble-DecoderProvenance.py",
             "Package-SanitizedSource.py",
             "Test-VgmstreamDecoder.py", "Test-VgmstreamLoop.py", "README.md"):
    copy(args.recipe / name, provenance / "recipe" / name)
copy(args.recipe / "patches" / "Apply-WemOnlyPatch.py",
     provenance / "recipe" / "patches" / "Apply-WemOnlyPatch.py")
copy(args.link_map.resolve(strict=True), provenance / "vgmstream-cli.map")

license_sources = {
    "vgmstream-COPYING.txt": source / "COPYING",
    "GNU-LGPL-2.1.txt": source / "ext_libs/licenses/ffmpeg.COPYING.LGPLv2.1",
    "Xiph-libvorbis-1.3.7.txt": source / "ext_libs/licenses/libvorbis-1.3.7.COPYING",
    "Xiph-libogg-1.3.5.txt": source / "ext_libs/licenses/libogg-1.3.5.COPYING",
}
for name, path in license_sources.items():
    copy(path, notices / name)

embedded = (
    ("src/util/miniz.c", "Copyright 2013-2014 RAD Game Tools and Valve Software"),
    ("src/util/miniz.c", "This is free and unencumbered software"),
    ("src/util/miniz.c", "Copyright 2016 Martin Raiber"),
    ("src/coding/libs/libacm_decode.c", "Copyright (c) 2004-2010, Marko Kreen"),
    ("src/coding/libs/libacm_util.c", "Copyright (c) 2004-2010, Marko Kreen"),
    ("src/coding/libs/nwa_lib.c", "Copyright 2001-2007"),
    ("src/coding/g721_decoder.c", "G.721 decoder"),
    ("src/coding/libs/utkdec.h", "Original by Andrew D'Addesio"),
    ("ext_libs/Getopt/getopt.c", "GNU Lesser General Public"),
)
sections = []
for relative, marker in embedded:
    sections.append(f"Source: {relative}\n\n{comment_with(source / relative, marker)}")
(notices / "linked-embedded-comments.txt").write_text(
    "\n\n".join(sections) + "\n", encoding="utf-8", newline="\n")

dumpbin = args.dumpbin.resolve(strict=True)
binary_names = ("vgmstream-cli.exe", "libvorbis.dll")
imports = {}
for name in binary_names:
    result = subprocess.run([str(dumpbin), "/dependents", str(install / "bin" / name)],
                            capture_output=True, text=True, timeout=20, check=True)
    imports[name] = sorted(set(re.findall(r"(?im)^\s+([\w.-]+\.dll)\s*$", result.stdout)))
if "libvorbis.dll" not in imports["vgmstream-cli.exe"]:
    raise ValueError("Expected dynamic libvorbis dependency is missing")
allowed_extra = {"libvorbis.dll", "KERNEL32.dll", "VCRUNTIME140.dll", "msvcrt.dll"}
for name, modules in imports.items():
    unexpected = [module for module in modules if module not in allowed_extra
                  and not module.startswith("api-ms-win-crt-")]
    if unexpected:
        raise ValueError(f"Unexpected external dependency in {name}: {unexpected}")

map_text = args.link_map.read_text(encoding="utf-8", errors="replace")
linked_objects = sorted(set(re.findall(r"\b([\w.-]+\.obj)\b", map_text)))
for excluded in ("relic_mixfft.obj", "relic_lib.obj", "relic_decoder.obj",
                 "compresswave_lib.obj", "compresswave_decoder.obj", "mio_decoder.obj"):
    if excluded in linked_objects:
        raise ValueError(f"Excluded object linked: {excluded}")

cache = (args.link_map.parents[2] / "CMakeCache.txt").read_text(
    encoding="utf-8", errors="replace")
build_configuration = {}
for key in ("CMAKE_GENERATOR", "CMAKE_GENERATOR_INSTANCE", "CMAKE_GENERATOR_PLATFORM",
            "CMAKE_EXE_LINKER_FLAGS_RELEASE",
            "BUILD_CLI", "BUILD_FB2K", "BUILD_WINAMP", "BUILD_XMPLAY", "VGM_WEM_ONLY",
            "USE_VORBIS", "USE_MPEG", "USE_FFMPEG", "USE_G7221", "USE_G719",
            "USE_ATRAC9", "USE_CELT", "USE_SPEEX"):
    match = re.search(rf"(?m)^{key}:[^=]*=(.*)$", cache)
    if not match:
        raise ValueError(f"Build option missing from CMakeCache: {key}")
    build_configuration[key] = match.group(1)
compiler_files = list(args.link_map.parents[2].glob("CMakeFiles/*/CMakeCCompiler.cmake"))
if len(compiler_files) != 1:
    raise ValueError("Expected one CMake C compiler description")
compiler_description = compiler_files[0].read_text(encoding="utf-8", errors="replace")
for key in ("CMAKE_C_COMPILER", "CMAKE_C_COMPILER_VERSION"):
    match = re.search(rf'(?m)^set\({key} "([^"]+)"\)', compiler_description)
    if not match:
        raise ValueError(f"Compiler field missing: {key}")
    build_configuration[key] = match.group(1)

files = {}
for path in sorted(provenance.rglob("*")):
    if path.is_file():
        files[path.relative_to(install).as_posix()] = {
            "bytes": path.stat().st_size, "sha256": sha256(path)}
for name in binary_names:
    path = install / "bin" / name
    files[f"bin/{name}"] = {"bytes": path.stat().st_size, "sha256": sha256(path)}
receipt = {
    "upstream": "vgmstream r2117 commit 71e2361042531fe767fb98300cf8c1ee95e539a0",
    "upstreamSourceUrl": "https://codeload.github.com/vgmstream/vgmstream/zip/71e2361042531fe767fb98300cf8c1ee95e539a0",
    "upstreamSourceSha256": "82D81CC5EE7A085427F75D9886703976B15F51309ED24532A72BE5A27467006B",
    "sanitizedSourceArchive": f"provenance/source/{args.archive.name}",
    "buildConfiguration": build_configuration,
    "imports": imports,
    "linkedObjects": linked_objects,
    "files": files,
}
(provenance / "build-receipt.json").write_text(
    json.dumps(receipt, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Collected decoder provenance and notices at {provenance}")
