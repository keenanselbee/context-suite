"""Stage or verify the pinned HEIF decoder in an isolated local candidate."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import stat


TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
spec = importlib.util.spec_from_file_location("heif_distribution", TOOLS / "Review-HeifDistribution.py")
distribution = importlib.util.module_from_spec(spec)
spec.loader.exec_module(distribution)


def local(value, verification=False):
    path = Path(value).absolute()
    roots = (ROOT / ".codex-temp", ROOT / "artifacts/production-staging")
    if verification:
        roots += (ROOT / "artifacts/release-candidates",)
    if ".." in path.parts or not any(path != root and path.is_relative_to(root) for root in roots):
        raise ValueError("Use isolated repository scratch or production staging")
    for part in (path, *path.parents):
        if os.path.lexists(part):
            info = part.lstat()
            if info.st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise ValueError("Linked HEIF payload paths are refused")
            if not (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode)):
                raise ValueError("HEIF payload entries must be ordinary files or directories")
    return path


def selection():
    record = json.loads((TOOLS / "payload-candidate.json").read_text())
    candidate = json.loads((TOOLS / "distribution-candidate.json").read_text())
    native = {item["path"]: {key: item[key] for key in ("bytes", "sha256")}
              for item in record["files"] if item["path"].lower().endswith(".dll")}
    if native != candidate["files"]:
        raise ValueError("HEIF deployment differs from the reviewed decoder")
    names = [item["path"] for item in record["files"]]
    if len(names) != 14 or len(set(name.casefold() for name in names)) != len(names):
        raise ValueError("Invalid HEIF deployment membership")
    for item in record["files"]:
        distribution.safe_name(item["path"])
        distribution.safe_name(item["archivePath"])
    return record


def identity(path, pin, verification=False):
    path = local(path, verification=verification)
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size != pin["bytes"]:
        raise ValueError("HEIF payload size, type or ownership differs: " + str(path))
    with path.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest().upper() != pin["sha256"]:
            raise ValueError("HEIF payload identity differs: " + str(path))


def verify(payload):
    engine = local(local(payload, verification=True) / "heif", verification=True)
    record = selection()
    expected = {item["path"]: item for item in record["files"]}
    directories = {parent.as_posix() for name in expected for parent in Path(name).parents if parent != Path(".")}
    files, folders = set(), set()

    def failed(error):
        raise error

    for current, child_folders, names in os.walk(engine, followlinks=False, onerror=failed):
        for name in child_folders + names:
            path = local(Path(current) / name, verification=True)
            relative = path.relative_to(engine).as_posix()
            if path.is_dir():
                if relative not in directories:
                    raise ValueError("Unexpected HEIF payload directory: " + relative)
                folders.add(relative)
            else:
                if relative not in expected:
                    raise ValueError("Unexpected HEIF payload file: " + relative)
                identity(path, expected[relative], verification=True)
                files.add(relative)
    if files != set(expected) or folders != directories:
        raise ValueError("HEIF payload is missing reviewed libraries, notices or directories")
    return record


def stage(bundle, payload):
    root = local(payload)
    if not root.is_dir():
        raise ValueError("Create a fresh isolated payload directory first")
    if os.path.lexists(root / "payload-inventory.json"):
        raise ValueError("An inventoried payload cannot be changed; use a fresh stage")
    destination = local(root / "heif")
    if os.path.lexists(destination):
        raise ValueError("HEIF payload already exists; use a new stage")
    bundle = distribution.local(bundle)
    if destination.is_relative_to(bundle) or bundle.is_relative_to(destination):
        raise ValueError("HEIF source and destination must not overlap")
    distribution.verify(bundle)
    record = selection()
    archive = distribution.read(bundle / "heif-runtime-review.zip")
    if len(archive) != record["archiveBytes"] or distribution.sha(archive) != record["archiveSha256"]:
        raise ValueError("HEIF runtime archive differs from the reviewed deployment")
    contents = distribution.zip_contents(archive)
    if set(contents) != {item["archivePath"] for item in record["files"]}:
        raise ValueError("HEIF deployment does not retain the full runtime bundle")
    for item in record["files"]:
        value = contents[item["archivePath"]]
        if len(value) != item["bytes"] or distribution.sha(value) != item["sha256"]:
            raise ValueError("HEIF runtime member differs from the deployment pin")
    # Verify everything before writing. A failure retains its partial stage;
    # retries never merge with it or overwrite an existing candidate.
    destination.mkdir()
    for item in record["files"]:
        target = local(destination / item["path"])
        target.parent.mkdir(exist_ok=True)
        with target.open("xb") as stream:
            stream.write(contents[item["archivePath"]])
        identity(target, item)
    verify(root)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", required=True, type=Path)
    parser.add_argument("--distribution", type=Path)
    args = parser.parse_args()
    if args.distribution:
        stage(args.distribution, args.payload)
    else:
        verify(args.payload)
    print("Verified 14 HEIF decoder/notice files; local candidate only, not release clearance.")


if __name__ == "__main__":
    main()
