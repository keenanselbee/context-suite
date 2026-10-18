"""Verify every locked crate archive and unpacked source against Cargo.lock."""

import argparse
import hashlib
import json
import pathlib
import tarfile
import tomllib


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def verify_package(package, cargo_home):
    name = package["name"]
    version = package["version"]
    expected = package["checksum"]
    filename = f"{name}-{version}.crate"
    cache_root = cargo_home / "registry" / "cache"
    archives = sorted(cache_root.glob(f"index.crates.io-*/{filename}"))
    if len(archives) != 1:
        raise ValueError(f"expected one official cached archive for {name} {version}")
    archive = archives[0]
    if archive.is_symlink():
        raise ValueError(f"cached archive is a link: {name} {version}")
    actual = sha256(archive)
    if actual != expected:
        raise ValueError(f"archive checksum differs from Cargo.lock: {name} {version}")
    source = cargo_home / "registry" / "src" / archive.parent.name / f"{name}-{version}"
    if not source.is_dir() or source.is_symlink():
        raise ValueError(f"unpacked source is missing: {name} {version}")
    if any(path.is_symlink() for path in source.rglob("*")):
        raise ValueError(f"unpacked source contains a link: {name} {version}")

    extracted = set()
    prefix = f"{name}-{version}/"
    with tarfile.open(archive, "r:gz") as crate:
        for member in crate:
            if member.isdir():
                continue
            if not member.isfile() or not member.name.startswith(prefix):
                raise ValueError(f"unsupported crate archive entry: {name} {version}")
            relative = pathlib.PurePosixPath(member.name[len(prefix):])
            if (not relative.parts or any(part in ("", ".", "..") for part in relative.parts)
                    or "\\" in member.name):
                raise ValueError(f"unsafe crate archive path: {name} {version}")
            key = relative.as_posix()
            if key in extracted:
                raise ValueError(f"duplicate crate archive path: {name} {version}")
            extracted.add(key)
            disk_path = source.joinpath(*relative.parts)
            if not disk_path.is_file() or disk_path.is_symlink():
                raise ValueError(f"unpacked crate file is missing: {name} {version}")
            stream = crate.extractfile(member)
            if stream is None:
                raise ValueError(f"unreadable crate archive entry: {name} {version}")
            with stream, disk_path.open("rb") as disk:
                while True:
                    archive_block = stream.read(1024 * 1024)
                    disk_block = disk.read(1024 * 1024)
                    if archive_block != disk_block:
                        raise ValueError(f"unpacked crate differs from archive: {name} {version}")
                    if not archive_block:
                        break
    disk_files = {
        path.relative_to(source).as_posix()
        for path in source.rglob("*") if path.is_file()
    }
    if disk_files - extracted != {".cargo-ok"} and disk_files != extracted:
        raise ValueError(f"unreviewed files in unpacked crate: {name} {version}")
    return {"Name": name, "Version": version, "ArchiveSha256": actual, "ExtractedFiles": len(extracted)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--lock", type=pathlib.Path, required=True)
    parser.add_argument("--inventory", type=pathlib.Path, required=True)
    parser.add_argument("--cargo-home", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    lock = tomllib.loads(args.lock.read_text(encoding="utf-8"))
    packages = [p for p in lock["package"] if p["name"] != "context-suite-svg"]
    inventory = json.loads(args.inventory.read_text(encoding="utf-8"))
    expected = {(item["name"], item["version"]): item["crateChecksum"] for item in inventory}
    if len(packages) != 63 or len(expected) != 63:
        raise ValueError("expected 63 distinct locked dependencies")
    for package in packages:
        key = package["name"], package["version"]
        if package.get("source") != "registry+https://github.com/rust-lang/crates.io-index":
            raise ValueError(f"unsupported dependency source: {key}")
        if package["checksum"] != expected.get(key):
            raise ValueError(f"inventory checksum differs from Cargo.lock: {key}")
    verified = [verify_package(package, args.cargo_home) for package in packages]
    result = {
        "LockSha256": sha256(args.lock),
        "InventorySha256": sha256(args.inventory),
        "DependencyCount": len(verified),
        "ExtractedFileCount": sum(item["ExtractedFiles"] for item in verified),
        "Dependencies": verified,
    }
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("DependencyCount", "ExtractedFileCount")}))


if __name__ == "__main__":
    main()
