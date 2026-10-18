"""Stage or verify the pinned WEM decoder in an isolated production candidate."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import stat


ROOT = Path(__file__).resolve().parents[2]
RECEIPT_SHA256 = "91DA18D737E7BEF676BD697759AE6F7F23634B8E517E076B61502C68E0AAC0D3"
CLI_SHA256 = "CA9D68C1ACFBB865B6118A4043599EFDC97E62CCC929C6E712008419AA00D818"
VORBIS_SHA256 = "5F330A20B19B3C70DC43EEDCC95891E65940FCA04A77155C573EFCDD4C2CA665"
SOURCE_SHA256 = "17C310068B491D10B35838CE7E1A69404AAD69D34A5465EA4CE109394FB00C7E"
SOURCE_NAME = "vgmstream-71e2361042531fe767fb98300cf8c1ee95e539a0-wem-only-source.zip"
RECEIPT_FILES = {
    "bin/vgmstream-cli.exe", "bin/libvorbis.dll", "provenance/vgmstream-cli.map",
    "provenance/notices/GNU-LGPL-2.1.txt", "provenance/notices/linked-embedded-comments.txt",
    "provenance/notices/vgmstream-COPYING.txt", "provenance/notices/Xiph-libogg-1.3.5.txt",
    "provenance/notices/Xiph-libvorbis-1.3.7.txt",
    "provenance/recipe/Assemble-DecoderProvenance.py",
    "provenance/recipe/Build-VgmstreamDecoder.ps1",
    "provenance/recipe/Package-SanitizedSource.py",
    "provenance/recipe/patches/Apply-WemOnlyPatch.py",
    "provenance/recipe/README.md", "provenance/recipe/Test-VgmstreamDecoder.py",
    "provenance/recipe/Test-VgmstreamLoop.py", "provenance/source/" + SOURCE_NAME,
}


def safe_path(value, payload=False, verification=False):
    path = Path(value)
    if not path.is_absolute() or ".." in path.parts:
        raise ValueError("Use an absolute path without traversal")
    path = path.absolute()
    if payload:
        allowed = [ROOT / ".codex-temp", ROOT / "artifacts/production-staging"]
        if verification:
            allowed.append(ROOT / "artifacts/release-candidates")
        if not any(path != base and path.is_relative_to(base) for base in allowed):
            raise ValueError("Use an isolated repository scratch or production stage")
    for ancestor in (path, *path.parents):
        if os.path.lexists(ancestor):
            info = ancestor.lstat()
            if info.st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise ValueError("Linked WEM payload paths are refused")
            if not (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode)):
                raise ValueError("WEM payload paths must be ordinary files or directories")
    return path


def sha256(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest().upper()


def checked_file(path, size, digest):
    path = safe_path(path)
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size != size or sha256(path) != digest:
        raise ValueError("WEM candidate file identity differs: " + str(path))


def members(root, expected):
    actual_files = set()
    expected_dirs = {parent.as_posix() for name in expected for parent in Path(name).parents if parent != Path(".")}
    actual_dirs = set()

    def failed(error):
        raise error

    for current, directories, files in os.walk(root, followlinks=False, onerror=failed):
        for name in directories + files:
            path = safe_path(Path(current) / name)
            relative = path.relative_to(root).as_posix()
            if path.is_dir():
                if relative not in expected_dirs:
                    raise ValueError("Unexpected WEM candidate directory: " + relative)
                actual_dirs.add(relative)
            else:
                if relative not in expected:
                    raise ValueError("Unexpected WEM candidate file: " + relative)
                actual_files.add(relative)
    if actual_files != set(expected) or actual_dirs != expected_dirs:
        raise ValueError("WEM candidate is missing pinned binaries, notices, recipe or source")


def receipt(root, staged=False):
    receipt_path = safe_path(root / "provenance/build-receipt.json")
    checked_file(receipt_path, 8969, RECEIPT_SHA256)
    data = json.loads(receipt_path.read_text(encoding="utf-8"))
    if data.get("upstream") != "vgmstream r2117 commit 71e2361042531fe767fb98300cf8c1ee95e539a0" or \
            data.get("sanitizedSourceArchive") != "provenance/source/" + SOURCE_NAME or \
            set(data.get("files", {})) != RECEIPT_FILES or len(data["files"]) != 16 or \
            data.get("buildConfiguration", {}).get("VGM_WEM_ONLY") != "ON" or \
            data.get("buildConfiguration", {}).get("USE_VORBIS") != "ON":
        raise ValueError("WEM build receipt differs from the reviewed candidate")
    pins = data["files"]
    if pins["bin/vgmstream-cli.exe"]["sha256"] != CLI_SHA256 or \
            pins["bin/libvorbis.dll"]["sha256"] != VORBIS_SHA256 or \
            pins["provenance/source/" + SOURCE_NAME]["sha256"] != SOURCE_SHA256:
        raise ValueError("WEM executable, library or corresponding source identity differs")
    mapped = {(name[4:] if name.startswith("bin/") else name): pin
              for name, pin in pins.items()} if staged else pins
    mapped["provenance/build-receipt.json"] = {"bytes": 8969, "sha256": RECEIPT_SHA256}
    members(root, mapped)
    for name, pin in mapped.items():
        checked_file(root / name, pin["bytes"], pin["sha256"])
    return mapped


def verify(payload):
    engine = safe_path(safe_path(payload, payload=True, verification=True) / "wem-engine", payload=True, verification=True)
    if not engine.is_dir():
        raise ValueError("WEM decoder payload is missing")
    return receipt(engine, staged=True)


def stage(distribution, payload):
    source = safe_path(distribution)
    root = safe_path(payload, payload=True)
    if not source.is_dir() or not root.is_dir():
        raise ValueError("Use an existing WEM distribution and a fresh isolated payload directory")
    target = safe_path(root / "wem-engine", payload=True)
    if target.is_relative_to(source) or source.is_relative_to(target) or os.path.lexists(target) or \
            os.path.lexists(root / "payload-inventory.json"):
        raise ValueError("WEM staging needs a fresh, nonoverlapping, uninventoried payload")
    pins = receipt(source)
    target.mkdir()
    for name in sorted(pins):
        relative = name[4:] if name.startswith("bin/") else name
        destination = safe_path(target / relative, payload=True)
        destination.parent.mkdir(parents=True, exist_ok=True)
        with (source / name).open("rb") as input_file, destination.open("xb") as output_file:
            for block in iter(lambda: input_file.read(1024 * 1024), b""):
                output_file.write(block)
    verify(root)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", required=True)
    parser.add_argument("--distribution")
    parser.add_argument("--inventory", action="store_true")
    arguments = parser.parse_args()
    if arguments.distribution:
        stage(arguments.distribution, arguments.payload)
    pins = verify(arguments.payload)
    if arguments.inventory:
        # Build-Production creates its global inventory after the candidate
        # verifier. The pinned receipt and exact subtree are our inventory at
        # that point; cross-check the global inventory if it already exists.
        inventory = safe_path(arguments.payload, payload=True, verification=True) / "payload-inventory.json"
        if os.path.lexists(inventory):
            if not inventory.is_file() or inventory.stat().st_size > 16 * 1024 * 1024:
                raise ValueError("Invalid production inventory")
            records = json.loads(inventory.read_text(encoding="utf-8"))
            if not isinstance(records, list):
                raise ValueError("Invalid production inventory entries")
            expected = {"wem-engine/" + name: pin for name, pin in pins.items()}
            listed = {}
            for item in records:
                name = item["path"].replace("\\", "/")
                if name.startswith("wem-engine/"):
                    if name in listed or name not in expected:
                        raise ValueError("Unexpected or duplicate WEM inventory entry")
                    listed[name] = {"bytes": item["bytes"], "sha256": item["sha256"]}
            if listed != expected:
                raise ValueError("Production inventory differs from the pinned WEM decoder payload")
    print("Verified 17 pinned WEM decoder, notice, recipe and source files; isolated candidate only, not release clearance.")


if __name__ == "__main__":
    main()
