"""Check the DDS/palette source companion and retained notice identities offline."""
import hashlib
import io
import json
from pathlib import Path
import sys
import zipfile


def digest(data):
    return hashlib.sha256(data).hexdigest().upper()


def check(data, record):
    if len(data) != record["bytes"] or digest(data) != record["sha256"]:
        raise ValueError("Source companion input differs from the reviewed bytes")


def verify(path):
    root = Path(__file__).resolve().parents[2]
    dds = json.loads((root / "tools/dds-engine/source-review.json").read_text())
    palette = json.loads((root / "tools/palette-engine/source-review.json").read_text())
    data = path.read_bytes()
    check(data, dds["sourceBundle"])
    check(data, palette["sourceBundle"])
    if dds["directXTex"]["sourceFiles"]["LICENSE"] != dds["noticeSha256"] or palette["sourceFiles"]["LICENSE"] != palette["runtimeFiles"]["ExoQuant.License.txt"]:
        raise ValueError("Root source licenses differ from the bundled notices")
    expected = {"sources/directxtex.zip", "sources/directxmath-upstream.zip", "sources/directxmath-sdk319.zip",
                "sources/exoquant.zip", "notices/DirectXTex.License.txt", "notices/ExoQuant.License.txt",
                "notices/Rust.Library.Notices.html", "records/dds.json", "records/palette.json"}
    with zipfile.ZipFile(io.BytesIO(data)) as bundle:
        if len(bundle.namelist()) != len(expected) or set(bundle.namelist()) != expected:
            raise ValueError("Unexpected companion membership")
        for component, record in [("dds", dds), ("palette", palette)]:
            if json.loads(bundle.read("records/" + component + ".json")) != {k: v for k, v in record.items() if k != "sourceBundle"}:
                raise ValueError("Archived component record differs")
        for name, archive_record, prefix, source_files in [
            ("directxtex", dds["directXTex"]["archive"], "DirectXTex-" + dds["directXTex"]["revision"] + "/", dds["directXTex"]["sourceFiles"]),
            ("exoquant", palette["sourceArchive"], "exoquant-rs-" + palette["revision"] + "/", palette["sourceFiles"]),
        ]:
            source = bundle.read("sources/" + name + ".zip")
            check(source, archive_record)
            with zipfile.ZipFile(io.BytesIO(source)) as archive:
                files = [entry for entry in archive.infolist() if not entry.is_dir()]
                if len(files) != len(source_files) or {entry.filename for entry in files} != {prefix + relative for relative in source_files}:
                    raise ValueError("Upstream source membership differs")
                for relative, expected_hash in source_files.items():
                    if digest(archive.read(prefix + relative)) != expected_hash:
                        raise ValueError("Upstream source file differs: " + relative)
        math = dds["directXMath"]
        upstream = bundle.read("sources/directxmath-upstream.zip")
        check(upstream, {"bytes": math["archiveBytes"], "sha256": math["archiveSha256"]})
        sdk = bundle.read("sources/directxmath-sdk319.zip")
        check(sdk, math["sdkArchive"])
        with zipfile.ZipFile(io.BytesIO(sdk)) as sdk_zip, zipfile.ZipFile(io.BytesIO(upstream)) as upstream_zip:
            names = {"LICENSE", *("Inc/" + header["name"] for header in math["headers"])}
            if len(sdk_zip.namelist()) != len(names) or set(sdk_zip.namelist()) != names:
                raise ValueError("SDK header companion membership differs")
            for header in math["headers"]:
                if digest(sdk_zip.read("Inc/" + header["name"])) != header["sdkSha256"]:
                    raise ValueError("SDK header differs")
                if digest(upstream_zip.read("DirectXMath-" + math["revision"] + "/Inc/" + header["name"])) != header["upstreamSha256"]:
                    raise ValueError("Public DirectXMath header differs")
            license_text = bundle.read("notices/DirectXTex.License.txt")
            if sdk_zip.read("LICENSE") != license_text or upstream_zip.read("DirectXMath-" + math["revision"] + "/LICENSE") != license_text:
                raise ValueError("DirectXMath attribution differs from the retained Microsoft MIT text")
        if digest(license_text) != dds["noticeSha256"]:
            raise ValueError("DDS notice differs")
        for name in ["ExoQuant.License.txt", "Rust.Library.Notices.html"]:
            if digest(bundle.read("notices/" + name)) != palette["runtimeFiles"][name]:
                raise ValueError("Palette notice differs")
    return {"Passed": True, "ArchiveSha256": digest(data), "DirectXTexSourceFiles": len(dds["directXTex"]["sourceFiles"]),
            "ExoQuantSourceFiles": len(palette["sourceFiles"]), "SdkHeaders": len(math["headers"]),
            "Scope": "Source/notice integrity; no native rebuild, private source distribution or commercial clearance"}


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python -B tools/release/Test-BaseComponentSources.py <dds-palette-source-notices.zip>")
    print(json.dumps(verify(Path(sys.argv[1])), indent=2))
