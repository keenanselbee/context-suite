"""Verify or copy the complete pinned Office runtime into an isolated candidate.

No engine is executed, no Windows profile is created and no installed payload is
changed. Build-time verification does not replace the worker's runtime leases.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat


TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]


def ordinary(path):
    info = path.lstat()
    if info.st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
        raise ValueError("Linked Office payload paths are not allowed: " + str(path))
    if not (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode)):
        raise ValueError("Office payload entries must be ordinary files or directories.")
    return info


def local_path(value, scratch_only=False):
    path = Path(value).absolute()
    roots = [ROOT / ".codex-temp"]
    if not scratch_only:
        roots.append(ROOT / "artifacts/production-staging")
    if ".." in path.parts or not any(path != root and path.is_relative_to(root) for root in roots):
        raise ValueError("Use isolated repository staging and scratch inputs.")
    for ancestor in (path, *path.parents):
        if os.path.lexists(ancestor):
            ordinary(ancestor)
    return path


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def identity(path, pin):
    info = ordinary(path)
    if not stat.S_ISREG(info.st_mode) or info.st_size != pin["bytes"] or digest(path) != pin["sha256"]:
        raise ValueError("Office payload identity differs: " + str(path))


def selection():
    record = json.loads((TOOLS / "payload-candidate.json").read_text(encoding="utf-8"))
    upstream = json.loads((TOOLS / "evaluation.json").read_text(encoding="utf-8"))
    if record["archiveSha256"] != upstream["archiveSha256"]:
        raise ValueError("Office archive selection differs from the reviewed candidate.")
    return record


def membership(engine, record):
    inventory = engine / record["inventory"]["path"]
    identity(inventory, record["inventory"])
    data = inventory.read_bytes()
    # Parse only the exact pinned bytes, including when a file changes after hashing.
    if hashlib.sha256(data).hexdigest().upper() != record["inventory"]["sha256"]:
        raise ValueError("Office inventory changed while reading.")
    files = {record["host"]["path"]: record["host"], record["inventory"]["path"]: record["inventory"]}
    directories = {"runtime", *("runtime/" + name for name in record["emptyDirectories"])}
    names, total = [], 0
    for line in data.decode("utf-8").splitlines():
        name, length, sha256 = line.split("\t")
        if ("\\" in name or ":" in name or any(part in ("", ".", "..") for part in name.split("/"))):
            raise ValueError("Invalid Office inventory path.")
        names.append(name)
        total += int(length)
        relative = "runtime/" + name
        files[relative] = {"bytes": int(length), "sha256": sha256}
        directories.update(parent.as_posix() for parent in Path(relative).parents if parent != Path("."))
    if (names != sorted(names) or len(names) != len(set(name.casefold() for name in names)) or
            len(names) != record["inventory"]["files"] or total != record["inventory"]["runtimeBytes"]):
        raise ValueError("Office inventory membership differs from the pinned candidate.")
    return files, directories


def verify(engine):
    engine = local_path(engine)
    record = selection()
    identity(engine / record["host"]["path"], record["host"])
    files, directories = membership(engine, record)
    actual_files, actual_directories = set(), set()

    def failed(error):
        raise error

    # Check links before descending, including empty or otherwise unlisted folders.
    for current, folders, names in os.walk(engine, followlinks=False, onerror=failed):
        for name in folders + names:
            path = Path(current) / name
            info = ordinary(path)
            relative = path.relative_to(engine).as_posix()
            if stat.S_ISDIR(info.st_mode):
                if relative not in directories:
                    raise ValueError("Unreviewed Office payload directory: " + relative)
                actual_directories.add(relative)
            else:
                if relative not in files:
                    raise ValueError("Unreviewed Office payload file: " + relative)
                actual_files.add(relative)
    if actual_files != set(files) or actual_directories != directories:
        raise ValueError("Office payload is missing reviewed runtime files, notices or directories.")
    for name, pin in files.items():
        identity(engine / name, pin)
    return files, directories


def stage(payload, engine_directory):
    root = local_path(payload)
    if not root.is_dir():
        raise ValueError("Create a new isolated payload directory before staging Office.")
    destination = local_path(root / "office-engine")
    if os.path.lexists(destination):
        raise ValueError("Office payload already exists; use a new stage.")
    source = local_path(engine_directory, scratch_only=True)
    if destination.is_relative_to(source) or source.is_relative_to(destination):
        raise ValueError("Office source and destination must not overlap.")
    files, directories = verify(source)
    for item in selection()["host"]["sources"]:
        path = ROOT / item["path"]
        ordinary(path)
        if digest(path) != item["sha256"]:
            raise ValueError("Office host source changed; rebuild and review the candidate.")
    # No writes before full source verification. Leave any partial failed stage
    # for inspection; never merge, overwrite or automatically delete a payload.
    destination.mkdir()
    for name in sorted(directories, key=lambda value: (value.count("/"), value)):
        (destination / name).mkdir()
    for number, (name, pin) in enumerate(files.items(), 1):
        source_file = local_path(source / name, scratch_only=True)
        target = local_path(destination / name)
        with source_file.open("rb") as original, target.open("xb") as output:
            shutil.copyfileobj(original, output)
        identity(target, pin)
        if number % 4000 == 0:
            print(f"Copied and verified {number:,} Office payload files.", flush=True)
    verify(destination)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", required=True)
    parser.add_argument("--engine-directory", help="Complete pinned Office engine in repository scratch.")
    args = parser.parse_args()
    if args.engine_directory:
        stage(args.payload, args.engine_directory)
    else:
        verify(local_path(args.payload) / "office-engine")
    print("Verified 19,332 Office runtime files, host, inventory and exact directories; release approval remains separate.")


if __name__ == "__main__":
    main()
