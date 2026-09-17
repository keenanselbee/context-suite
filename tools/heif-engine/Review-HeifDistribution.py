"""Prepare or verify local HEIF source/runtime review bundles without installing them."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import stat
import tarfile
import uuid
import zipfile


TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
SCRATCH = ROOT / ".codex-temp"
RECIPES = (".gitattributes", "Build-HeifEngine.py", "source-inputs.json", "libheif-de265-push-errors.patch",
           "libheif-hidden-hdr.patch", "libde265-color-signals.patch")


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def local(value):
    path = Path(value).absolute()
    if ".." in path.parts or not path.is_relative_to(SCRATCH) or path == SCRATCH:
        raise ValueError("Review files must remain within repository scratch")
    for part in (path, *path.parents):
        if part.exists() and part.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Linked review paths are refused")
    return path


def read(path, maximum=64 * 1024 * 1024):
    path = local(path)
    if path.stat().st_size > maximum:
        raise ValueError("Oversized review input")
    return path.read_bytes()


def safe_name(name):
    if "\\" in name or ":" in name or any(part in ("", ".", "..") for part in name.split("/")):
        raise ValueError("Unsafe review member")


def zip_contents(data):
    contents = {}
    total = 0
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for entry in archive.infolist():
            safe_name(entry.filename)
            total += entry.file_size
            if (entry.filename in contents or entry.file_size > 64 * 1024 * 1024 or total > 128 * 1024 * 1024
                    or len(contents) >= 128 or stat.S_IFMT(entry.external_attr >> 16) != stat.S_IFREG):
                raise ValueError("Invalid, duplicate or oversized review ZIP entry")
            contents[entry.filename] = archive.read(entry)
    return contents


def upstream_files(sources):
    pins = json.loads((TOOLS / "source-inputs.json").read_text())
    aom_pin = json.loads((TOOLS / "distribution-inputs.json").read_text())["aomSource"]
    files = {}
    total = 0
    for pin in pins["archives"]:
        data = sources[".codex-temp/inputs/" + pin["name"] + ".tar.gz"]
        if len(data) != pin["bytes"] or sha(data) != pin["sha256"]:
            raise ValueError("Library source archive changed")
        prefix = pin["name"] + "-" + pin["version"] + "/"
        with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as archive:
            for entry in archive:
                if entry.isdir():
                    continue
                if not entry.isfile() or not entry.name.startswith(prefix):
                    raise ValueError("Unexpected upstream source member")
                relative = entry.name[len(prefix):]
                safe_name(relative)
                name = pin["name"] + "/" + relative
                total += entry.size
                if name in files or entry.size > 32 * 1024 * 1024 or total > 128 * 1024 * 1024:
                    raise ValueError("Invalid upstream source size/membership")
                with archive.extractfile(entry) as stream:
                    files[name] = stream.read()
    data = sources[".codex-temp/inputs/aom-source.zip"]
    if len(data) != aom_pin["bytes"] or sha(data) != aom_pin["sha256"]:
        raise ValueError("AOM source archive changed")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for entry in archive.infolist():
            if entry.is_dir():
                continue
            if not entry.filename.startswith(aom_pin["prefix"]) or stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError("Unexpected AOM source member")
            relative = entry.filename[len(aom_pin["prefix"]):]
            safe_name(relative)
            name = "aom/" + relative
            total += entry.file_size
            if name in files or len(files) >= 10000 or entry.file_size > 32 * 1024 * 1024 or total > 128 * 1024 * 1024:
                raise ValueError("Invalid AOM source size/membership")
            files[name] = archive.read(entry)
    return pins, files


def notices(files):
    # Inclusive archive notices, not a claim that every archived source is linked.
    chunks = []
    count = 0
    for name, data in sorted(files.items()):
        basename = Path(name).name.upper()
        if basename.startswith(("LICENSE", "COPYING", "PATENTS")):
            blocks = [data]
        elif Path(name).suffix.lower() in (".c", ".cc", ".cpp", ".h", ".hpp", ".inc", ".asm", ".s"):
            blocks = [match.group() for match in re.finditer(rb"/\*.*?\*/", data[:65536], re.DOTALL)
                      if re.search(rb"copyright|license|permission|warranty", match.group(), re.IGNORECASE)]
        else:
            blocks = []
        if blocks:
            chunks.append(name.encode() + b"\n" + b"=" * len(name) + b"\n" + b"\n".join(blocks) + b"\n\n")
            count += 1
    return b"".join(chunks), count


def expected_runtime(sources):
    pins, files = upstream_files(sources)
    collection, count = notices(files)
    runtime = {
        "licenses/libheif.COPYING.txt": files["libheif/COPYING"],
        "licenses/libde265.COPYING.txt": files["libde265/COPYING"],
        "licenses/AOM.LICENSE.txt": files["aom/LICENSE"],
        "licenses/AOM.PATENTS.txt": files["aom/PATENTS"],
        "licenses/AOM.AUTHORS.txt": files["aom/AUTHORS"],
        "licenses/AOM.supplier.txt": sources[".codex-temp/aom/license/aom.txt"],
        "licenses/SourceCopyrightNotices.txt": collection,
        "THIRD-PARTY-NOTICES.txt": (TOOLS / "distribution/THIRD-PARTY-NOTICES.txt").read_bytes(),
        "SOURCE-CHANGES.patch": (TOOLS / "libheif-de265-push-errors.patch").read_bytes(),
        "SOURCE-HEIF-COLOR.patch": (TOOLS / "libheif-hidden-hdr.patch").read_bytes(),
        "SOURCE-DE265-COLOR.patch": (TOOLS / "libde265-color-signals.patch").read_bytes(),
    }
    return pins, runtime, count


def verify(directory):
    directory = local(directory)
    manifest = json.loads(read(directory / "bundle-inventory.json", 1024 * 1024))
    candidate = json.loads((TOOLS / "distribution-candidate.json").read_text())
    sources = zip_contents(read(directory / "heif-source-review.zip"))
    runtime = zip_contents(read(directory / "heif-runtime-review.zip"))
    pins = json.loads((TOOLS / "source-inputs.json").read_text())
    source_names = {".codex-temp/inputs/" + pin["name"] + ".tar.gz" for pin in pins["archives"]}
    source_names |= {".codex-temp/inputs/aom-source.zip", "BUILD.md"}
    source_names |= {".codex-temp/aom/" + name for name in pins["aom"]["files"]}
    source_names |= {"tools/heif-engine/" + name for name in RECIPES}
    if set(sources) != source_names:
        raise ValueError("Unexpected source kit membership")
    for name in RECIPES:
        if sources["tools/heif-engine/" + name] != (TOOLS / name).read_bytes():
            raise ValueError("Source kit recipe changed")
    if sources["BUILD.md"] != (TOOLS / "distribution/BUILD.md").read_bytes():
        raise ValueError("Source kit instructions changed")
    for name, expected in pins["aom"]["files"].items():
        if sha(sources[".codex-temp/aom/" + name]) != expected:
            raise ValueError("Selected AOM artifact changed")
    _, expected, count = expected_runtime(sources)
    if set(runtime) != set(expected) | {"bin/" + name for name in candidate["files"]}:
        raise ValueError("Unexpected runtime membership")
    for name, data in expected.items():
        if runtime[name] != data:
            raise ValueError("Runtime notice or patch changed: " + name)
    for name, pin in candidate["files"].items():
        data = runtime["bin/" + name]
        if len(data) != pin["bytes"] or sha(data) != pin["sha256"]:
            raise ValueError("Runtime binary differs from selected candidate")
    inventories = {}
    for name, content in (("heif-source-review.zip", sources), ("heif-runtime-review.zip", runtime)):
        inventories[name] = {key: {"bytes": len(value), "sha256": sha(value)} for key, value in sorted(content.items())}
    if manifest != {"schemaVersion": 1, "candidate": candidate, "noticeFiles": count, "bundles": inventories}:
        raise ValueError("Review inventory differs")
    return manifest


def prepare(candidate_path, aom_source):
    candidate_path = local(candidate_path)
    result = json.loads(read(candidate_path / "results.json"))
    if not result["Passed"] or result.get("LibrariesOnly", False):
        raise ValueError("Passing full decoder build required")
    inputs = json.loads(read(candidate_path / "inputs.json"))
    for name, expected in inputs.items():
        # Verify private/toolchain inputs in place; never put them in either ZIP.
        with Path(name).open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest().upper() != expected:
                raise ValueError("Candidate build input changed")
    pins = json.loads((TOOLS / "source-inputs.json").read_text())
    sources = {}
    for pin in pins["archives"]:
        matches = [Path(name) for name, value in inputs.items() if value == pin["sha256"]]
        if len(matches) != 1:
            raise ValueError("Ambiguous build source")
        sources[".codex-temp/inputs/" + pin["name"] + ".tar.gz"] = read(matches[0])
    for relative, expected in pins["aom"]["files"].items():
        matches = [Path(name) for name, value in inputs.items() if value == expected]
        if len(matches) != 1:
            raise ValueError("Ambiguous AOM build input")
        sources[".codex-temp/aom/" + relative] = read(matches[0])
    sources[".codex-temp/inputs/aom-source.zip"] = read(local(aom_source))
    for name in RECIPES:
        sources["tools/heif-engine/" + name] = (TOOLS / name).read_bytes()
    sources["BUILD.md"] = (TOOLS / "distribution/BUILD.md").read_bytes()
    _, runtime, count = expected_runtime(sources)
    selected = json.loads((TOOLS / "distribution-candidate.json").read_text())
    if result["Payload"] != {name: pin["sha256"] for name, pin in selected["files"].items()}:
        raise ValueError("Build is not the selected tested payload")
    for name in selected["files"]:
        runtime["bin/" + name] = read(candidate_path / "payload" / name)
    output = SCRATCH / "heif-distribution" / uuid.uuid4().hex
    output.mkdir(parents=True)
    inventories = {}
    for name, content in (("heif-source-review.zip", sources), ("heif-runtime-review.zip", runtime)):
        inventories[name] = {}
        with zipfile.ZipFile(output / name, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for key, data in sorted(content.items()):
                entry = zipfile.ZipInfo(key, (2026, 9, 16, 0, 0, 0))
                entry.create_system = 3
                entry.external_attr = (stat.S_IFREG | 0o644) << 16
                entry.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(entry, data)
                inventories[name][key] = {"bytes": len(data), "sha256": sha(data)}
    (output / "bundle-inventory.json").write_text(json.dumps({"schemaVersion": 1,
        "candidate": selected, "noticeFiles": count, "bundles": inventories}, indent=2) + "\n")
    verify(output)
    print("Verified local HEIF review bundle:", output, flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("prepare", "verify"))
    parser.add_argument("directory", type=Path)
    parser.add_argument("--aom-source", type=Path)
    args = parser.parse_args()
    if args.operation == "prepare":
        if not args.aom_source:
            raise ValueError("AOM source archive required")
        prepare(args.directory, args.aom_source)
    else:
        result = verify(args.directory)
        print("Verified HEIF source/runtime membership, identities and", result["noticeFiles"], "source notice files.")


if __name__ == "__main__":
    main()
