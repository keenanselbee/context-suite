"""Download exact upstream AVIF encoder sources into a fresh isolated directory."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import tarfile
import urllib.request


PINS = (
    ("libavif", "https://github.com/AOMediaCodec/libavif/archive/refs/tags/v1.4.2.tar.gz",
     "2B645287340BA5A631D268B551DC2D72BD73AC33335962DD36DCDB6D8366921D", 14665927),
    ("aom", "https://aomedia.googlesource.com/aom/+archive/44d0a57786f432d933ff64b653347c66f4d0fa1d.tar.gz",
     "A8E348AD4F009B284BA9A4B5609E9D422A8403B074D38DF6E172F71D92994FD2", 6693644),
)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def extract(archive, destination, prefix):
    inventory = {}
    total = 0
    with tarfile.open(archive, "r:gz") as source:
        members = source.getmembers()
        if len(members) > 20000:
            raise ValueError("Too many source entries")
        for entry in members:
            name = entry.name
            if prefix and name == prefix.rstrip("/") and entry.isdir():
                continue
            if prefix:
                if not name.startswith(prefix):
                    raise ValueError("Unexpected source prefix: " + name)
                name = name[len(prefix):]
            if not name:
                continue
            if ("\\" in name or ":" in name or name.startswith("/") or
                    any(part in ("", ".", "..") for part in name.split("/"))):
                raise ValueError("Unsafe source path: " + name)
            if entry.isdir():
                continue
            if not entry.isfile() or entry.size > 32 * 1024 * 1024:
                raise ValueError("Unsupported source entry: " + name)
            total += entry.size
            if total > 256 * 1024 * 1024:
                raise ValueError("Extracted source exceeds limit")
            target = destination / name
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.extractfile(entry) as source_file, target.open("xb") as output:
                shutil.copyfileobj(source_file, output)
            inventory[target.relative_to(destination.parent).as_posix()] = digest(target)
    return inventory


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--archives", type=Path, help="Use predownloaded pinned tar.gz files instead of downloading")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    output = args.output.absolute()
    scratch = root / ".codex-temp"
    if output == scratch or not output.is_relative_to(scratch) or output.exists():
        raise ValueError("Output must be a fresh directory under repository .codex-temp")
    output.mkdir(parents=True)
    records = []
    inventory = {}
    try:
        for name, url, pin, length in PINS:
            archive = output / (name + ".tar.gz")
            if args.archives:
                shutil.copyfile(args.archives / archive.name, archive)
            else:
                with urllib.request.urlopen(url, timeout=90) as response, archive.open("xb") as stream:
                    count = 0
                    while chunk := response.read(1024 * 1024):
                        count += len(chunk)
                        if count > length:
                            raise ValueError("Source archive exceeds expected length")
                        stream.write(chunk)
            if archive.stat().st_size != length or digest(archive) != pin:
                raise ValueError("Official archive identity differs: " + name)
            destination = output / name
            destination.mkdir()
            prefix = "libavif-1.4.2/" if name == "libavif" else ""
            inventory.update(extract(archive, destination, prefix))
            records.append({"name": name, "url": url, "bytes": length, "sha256": pin})
            (output / "downloads.json").write_text(json.dumps(records, indent=2) + "\n")
        (output / "source-inventory.json").write_text(json.dumps(inventory, indent=2, sort_keys=True) + "\n")
        print(output)
    except Exception:
        print("Incomplete source preparation retained at " + str(output))
        raise


if __name__ == "__main__":
    main()
