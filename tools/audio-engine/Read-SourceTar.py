"""Verify or extract pinned release tarballs into fresh repository scratch."""
import hashlib
import json
import pathlib
import re
import stat
import sys
import tarfile


def scratch_path(value):
    scratch = pathlib.Path(__file__).resolve().parents[2] / ".codex-temp"
    path = pathlib.Path(value).absolute()
    if ".." in path.parts or not path.is_relative_to(scratch) or path == scratch:
        raise ValueError("Source paths must remain inside repository scratch")
    for ancestor in (path, *path.parents):
        if ancestor.exists() and ancestor.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Linked source paths are not allowed")
    return path


def inspect(archive, pin):
    members = []
    seen = {}
    total = 0
    for index, entry in enumerate(archive):
        if index >= 10000 or entry.issparse() or not (entry.isfile() or entry.isdir()):
            raise ValueError("Unsupported tar entry or entry count")
        if entry.isdir() and entry.name == pin["prefix"].rstrip("/"):
            continue
        if not entry.name.startswith(pin["prefix"]):
            raise ValueError("Unexpected source archive prefix")
        relative = entry.name[len(pin["prefix"]):].rstrip("/")
        if not relative and entry.isdir():
            continue
        parts = relative.split("/")
        if not relative or len(parts) > 32 or any(
            part in ("", ".", "..") or len(part) > 200 or part.endswith((".", " "))
            or re.search(r'[\x00-\x1f<>:"\\|?*]', part)
            or re.fullmatch(r"(?i:con|prn|aux|nul|com[0-9¹²³]|lpt[0-9¹²³])(?:\..*)?", part)
            for part in parts
        ):
            raise ValueError("Unsafe source archive path")
        key = relative.casefold()
        if key in seen:
            raise ValueError("Duplicate or case-colliding source path")
        seen[key] = entry.isdir()
        if entry.size < 0 or entry.size > 32 * 1024 * 1024 or (entry.isdir() and entry.size):
            raise ValueError("Oversized source entry")
        total += entry.size
        if total > 256 * 1024 * 1024:
            raise ValueError("Oversized source tree")
        members.append((entry, relative))
    for key in seen:
        parent = pathlib.PurePosixPath(key).parent
        while parent != pathlib.PurePosixPath("."):
            if seen.get(parent.as_posix()) is False:
                raise ValueError("Source file conflicts with a directory")
            parent = parent.parent
    for required in pin["requiredEntries"]:
        if seen.get(required.casefold()) is not False:
            raise ValueError("Missing required source file: " + required)
    return members


def main():
    if len(sys.argv) not in (3, 4) or sys.version_info < (3, 14):
        raise ValueError("Use Python 3.14+: Read-SourceTar.py <cache> <source-id> [new destination]")
    manifest = json.loads(pathlib.Path(__file__).with_name("source-inputs.json").read_text())
    pin = next(item for item in manifest["archives"] if item["id"] == sys.argv[2])
    if pin["downloadKind"] != "release-tar" or not re.fullmatch(r"[a-z-]+\.tar\.gz", pin["file"]):
        raise ValueError("Use a pinned release tar input")
    path = scratch_path(scratch_path(sys.argv[1]) / pin["file"])
    destination = scratch_path(sys.argv[3]) if len(sys.argv) == 4 else None
    if destination is not None and destination.exists():
        raise ValueError("Extraction requires a fresh destination")
    records = []
    with path.open("rb") as stream:
        if path.stat().st_size != pin["bytes"] or hashlib.file_digest(stream, "sha256").hexdigest().upper() != pin["sha256"]:
            raise ValueError("Source archive identity changed")
        stream.seek(0)
        with tarfile.open(fileobj=stream, mode="r:gz") as archive:
            members = inspect(archive, pin)
            if destination is not None:
                destination.mkdir(parents=True, exist_ok=False)
                for entry, relative in members:
                    target = destination / relative
                    if entry.isdir():
                        target.mkdir(parents=True, exist_ok=True)
                        continue
                    target.parent.mkdir(parents=True, exist_ok=True)
                    hasher = hashlib.sha256()
                    size = 0
                    with archive.extractfile(entry) as source, target.open("xb") as output:
                        while block := source.read(65536):
                            size += len(block)
                            if size > entry.size:
                                raise ValueError("Source entry exceeded its declared size")
                            hasher.update(block)
                            output.write(block)
                    if size != entry.size:
                        raise ValueError("Truncated source entry")
                    records.append({"path": pin["id"] + "/" + relative, "bytes": size, "sha256": hasher.hexdigest().upper()})
        stream.seek(0)
        if hashlib.file_digest(stream, "sha256").hexdigest().upper() != pin["sha256"]:
            raise ValueError("Source archive changed during reading")
    print(json.dumps({"id": pin["id"], "entries": len(members), "files": records}))


if __name__ == "__main__":
    main()
