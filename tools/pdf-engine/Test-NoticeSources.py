"""Check the retained PDF source/notice companion without building or running it."""

import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import zipfile


ROOT = Path(__file__).resolve().parents[2]
RECORD = ROOT / "tools/pdf-engine/notice-source-review.json"
NOTICE = ROOT / "assets/third-party/pdf/Qpdf.Dependency.Notices.txt"


def pin(data):
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest().upper()}


def notice_bytes(data, transform):
    lines = data.splitlines(keepends=True)
    if transform == "copy":
        return data
    if transform == "strip-comments":
        return b"".join(re.sub(rb"^//[ \t]?", b"", line) for line in lines)
    if transform == "agg":
        stop = next(i for i, line in enumerate(lines) if b"#ifndef" in line)
        return b"".join(line for line in lines[:stop] if line.strip())
    if transform == "lcms":
        return b"".join(lines[:next(i for i, line in enumerate(lines) if not line.strip())])
    if transform == "comment":
        return b"".join(lines[:next(i for i, line in enumerate(lines) if b"*/" in line) + 1])
    if transform == "zlib":
        start = next(i for i, line in enumerate(lines) if line.startswith(b"/* zlib.h"))
        stop = next(i for i in range(start, len(lines)) if b"*/" in lines[i])
        return b"".join(lines[start:stop + 1])
    raise ValueError("Unknown supplier notice transformation.")


def verify(bundle, payload=None):
    record_bytes = RECORD.read_bytes()
    record = json.loads(record_bytes)
    selection = json.loads((ROOT / "tools/pdf-engine/payload-candidate.json").read_bytes())
    if record["formatVersion"] != 1 or record["payloadArchives"] != selection["archives"]:
        raise ValueError("PDF notice sources describe a different engine selection.")
    selected_files = {item["path"]: item for item in selection["files"]}
    with zipfile.ZipFile(bundle) as archive:
        expected = {*record["sourceArchives"], record["evidenceArchive"]["name"],
                    RECORD.name, NOTICE.name}
        if set(archive.namelist()) != expected or len(archive.namelist()) != len(expected):
            raise ValueError("Unexpected PDF review companion membership.")
        if archive.read(RECORD.name) != record_bytes:
            raise ValueError("PDF source review record differs.")
        sources = {}
        for name, expected_pin in record["sourceArchives"].items():
            data = archive.read(name)
            if pin(data) != {key: expected_pin[key] for key in ("bytes", "sha256")}:
                raise ValueError("Changed PDF dependency source archive: " + name)
            sources[name] = data
        content = bytearray(record["qpdf"]["preamble"].encode("utf-8"))
        for item in record["qpdf"]["sources"]:
            with zipfile.ZipFile(io.BytesIO(sources[item["archive"]])) as source:
                data = source.read(item["member"])
            if pin(data) != {key: item[key] for key in ("bytes", "sha256")}:
                raise ValueError("Changed qpdf dependency notice source.")
            content.extend(("\n--- " + item["archive"] + " / " + item["member"] + " ---\n\n").encode("utf-8"))
            content.extend(data)
            content.extend(b"\n")
        if (pin(content) != record["qpdf"]["notice"] or content != NOTICE.read_bytes() or
                content != archive.read(NOTICE.name)):
            raise ValueError("qpdf supplemental notice differs from retained source texts.")
        for directory in ("pdf-engine", "pdf-renderer", "pdf-validator"):
            item = selected_files[directory + "/" + NOTICE.name]
            if pin(content) != {key: item[key] for key in ("bytes", "sha256")}:
                raise ValueError("PDF payload selection omits the reviewed dependency notices.")
        evidence = archive.read(record["evidenceArchive"]["name"])
        if pin(evidence) != {key: record["evidenceArchive"][key] for key in ("bytes", "sha256")}:
            raise ValueError("Changed PDFium source/notice evidence.")
        with zipfile.ZipFile(io.BytesIO(evidence)) as source:
            for item in record["pdfium"]["notices"]:
                raw = source.read(item["sourceMember"])
                actual = source.read("shipped/" + item["name"])
                expected_text = notice_bytes(raw, item["transform"])
                if pin(raw) != item["source"] or pin(expected_text) != item["notice"] or pin(actual) != item["shipped"]:
                    raise ValueError("PDFium notice source or supplier text changed: " + item["name"])
                if item["matchPolicy"] == "exact":
                    matches = expected_text == actual
                elif item["matchPolicy"] == "line-endings-only":
                    matches = expected_text.replace(b"\r\n", b"\n") == actual.replace(b"\r\n", b"\n")
                else:
                    raise ValueError("Unknown PDFium notice comparison policy.")
                selected = selected_files["pdf-renderer/licenses/" + item["name"]]
                if not matches or pin(actual) != {key: selected[key] for key in ("bytes", "sha256")}:
                    raise ValueError("PDFium source text differs from packaged notice: " + item["name"])
            for name, expected_pin in record["pdfium"]["shippedFiles"].items():
                if pin(source.read("shipped/" + name)) != expected_pin:
                    raise ValueError("PDFium supplier configuration changed.")
    if payload is not None:
        for directory in ("pdf-engine", "pdf-renderer", "pdf-validator"):
            if (payload / directory / NOTICE.name).read_bytes() != content:
                raise ValueError("Packaged supplemental notices differ.")
            binary = (payload / directory / "qpdf30.dll").read_bytes()
            if pin(binary) != record["qpdfLibrary"] or any(
                    marker.encode("ascii") not in binary for marker in record["qpdfVersionMarkers"]):
                raise ValueError("Packaged qpdf version evidence differs.")
        for item in record["pdfium"]["notices"]:
            if pin((payload / "pdf-renderer/licenses" / item["name"]).read_bytes()) != item["shipped"]:
                raise ValueError("Packaged PDFium notice differs.")
    return {"Passed": True, "QpdfNoticeTexts": len(record["qpdf"]["sources"]),
            "PdfiumNotices": len(record["pdfium"]["notices"]),
            "SourceArchives": len(sources), "PayloadChecked": payload is not None,
            "NativeRebuilt": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    parser.add_argument("--payload", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.bundle, args.payload), indent=2))


if __name__ == "__main__":
    main()
