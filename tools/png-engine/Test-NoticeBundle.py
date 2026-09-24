"""Verify the retained PNG source/notice archive without building or executing it."""
import hashlib
import io
import json
from pathlib import Path
import sys
import tarfile
import zipfile


def digest(data):
    return hashlib.sha256(data).hexdigest().upper()


def verify(path):
    repository = Path(__file__).resolve().parents[2]
    review = json.loads((Path(__file__).parent / "notice-review.json").read_text())
    data = path.read_bytes()
    if len(data) != review["sourceBundle"]["bytes"] or digest(data) != review["sourceBundle"]["sha256"]:
        raise ValueError("PNG source/notice archive identity differs")
    with zipfile.ZipFile(io.BytesIO(data)) as bundle:
        expected = {"notice-review.json", "oxipng-source.zip", *review["files"]}
        expected.update("crates/" + item["name"] + "-" + item["version"] + ".crate"
                        for item in review["components"] if item["name"] != "oxipng")
        if len(bundle.namelist()) != len(expected) or set(bundle.namelist()) != expected:
            raise ValueError("PNG source/notice archive membership differs")
        if json.loads(bundle.read("notice-review.json")) != {k: v for k, v in review.items() if k != "sourceBundle"}:
            raise ValueError("PNG archive review record differs")
        for name, expected_hash in review["files"].items():
            if digest(bundle.read(name)) != expected_hash or digest((repository / "assets/third-party/oxipng" / name).read_bytes()) != expected_hash:
                raise ValueError("PNG notice asset differs: " + name)
        source = bundle.read("oxipng-source.zip")
        if len(source) != review["oxipng"]["Bytes"] or digest(source) != review["oxipng"]["ArchiveSha256"].upper():
            raise ValueError("Oxipng source archive differs")
        notice_count = 0
        for component in review["components"]:
            prefix = component["name"] + "-" + component["version"]
            if component["name"] == "oxipng":
                archive = zipfile.ZipFile(io.BytesIO(source))
                read = archive.read
                prefix = "oxipng-" + review["oxipng"]["Revision"]
                if digest(read(prefix + "/Cargo.lock")) != review["cargoLockSha256"]:
                    raise ValueError("Oxipng dependency lock differs")
            else:
                crate = bundle.read("crates/" + prefix + ".crate")
                if len(crate) != component["sourceArchiveBytes"] or digest(crate) != component["sourceArchiveSha256"]:
                    raise ValueError("Crate differs: " + prefix)
                archive = tarfile.open(fileobj=io.BytesIO(crate))
                read = lambda name: archive.extractfile(name).read()
            with archive:
                for notice in component["notices"]:
                    content = read(prefix + "/" + notice["path"])
                    if len(content) != notice["bytes"] or digest(content) != notice["sha256"]:
                        raise ValueError("Crate notice differs: " + prefix + "/" + notice["path"])
                    notice_count += 1
    return {"Passed": True, "Components": len(review["components"]), "Notices": notice_count,
            "ArchiveSha256": digest(data), "Scope": "Pinned archive and notice integrity; no rebuild or commercial clearance"}


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python -B tools/png-engine/Test-NoticeBundle.py <oxipng-source-notices.zip>")
    print(json.dumps(verify(Path(sys.argv[1])), indent=2))
