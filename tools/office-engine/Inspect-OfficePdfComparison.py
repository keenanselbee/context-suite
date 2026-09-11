"""Inspect retained authored Office PDF comparisons without running Office."""
import base64
import collections
import difflib
import hashlib
import json
import pathlib
import subprocess
import sys
import uuid
import zipfile


ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRATCH = ROOT / ".codex-temp"


def local(value, parent):
    path = pathlib.Path(value).absolute()
    if not path.is_relative_to(parent) or path == parent:
        raise ValueError("Use retained repository scratch inputs")
    for ancestor in (path, *path.parents):
        if ancestor == ROOT:
            break
        if ancestor.is_symlink() or ancestor.is_junction():
            raise ValueError("Linked inspection path")
    if not path.resolve().is_relative_to(parent.resolve()):
        raise ValueError("Inspection path escapes scratch")
    return path


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def read_json(path):
    if path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError("Oversized inspection record")
    return json.loads(path.read_bytes().decode("utf-8-sig"))


def main():
    evaluation = local(sys.argv[1], SCRATCH / "office-engine")
    prepared = local(sys.argv[2], SCRATCH / "pdf-engine")
    pin = read_json(ROOT / "tools/pdf-engine/evaluation.json")
    archive = local(prepared / "upstream.zip", prepared)
    if archive.stat().st_size > 128 * 1024 * 1024 or sha(archive.read_bytes()) != pin["archiveSha256"]:
        raise ValueError("qpdf evaluation archive identity changed")
    with zipfile.ZipFile(archive) as retained:
        inventory = read_json(prepared / "inventory.json")
        names = {item.filename for item in retained.infolist() if not item.is_dir()}
        actual = {path.relative_to(prepared / "unpacked").as_posix() for path in (prepared / "unpacked").rglob("*") if path.is_file()}
        if len(inventory) != len(names) or {item["path"].replace("\\", "/") for item in inventory} != names or actual != names:
            raise ValueError("qpdf payload membership differs from the pinned archive")
        for item in inventory:
            name = item["path"].replace("\\", "/")
            path = local(prepared / "unpacked" / name, prepared)
            expected = retained.read(name)
            if len(expected) != item["bytes"] or sha(expected) != item["sha256"] or path.read_bytes() != expected:
                raise ValueError("qpdf runtime differs from the pinned archive")
    engine = local(prepared / "unpacked/qpdf-12.4.1-msvc64/bin/qpdf.exe", prepared)
    evaluation_record = read_json(evaluation / "office-evaluation.json")
    if evaluation_record.get("Mode") != "LegacyPdf":
        raise ValueError("Use a retained LegacyPdf run")
    results = evaluation_record["Results"]
    selected = [item for item in results if item["Source"] in {"Excel ü.xlsx", "Excel ü.xls", "PowerPoint ü.pptx", "PowerPoint ü.ppt"}]
    if len(selected) != 4 or len({item["Source"] for item in selected}) != 4 or any(not item.get("Completed") or
            item["ProfileStyle"] != ("modern" if item["Source"].endswith((".xlsx", ".pptx")) else "legacy") for item in selected):
        raise ValueError("Use a completed authored modern/legacy comparison")
    output = SCRATCH / ("office-pdf-inspection-" + uuid.uuid4().hex)
    output.mkdir()
    report, streams = [], {}
    for item in selected:
        stem = pathlib.Path(item["Source"]).stem
        label = stem + "-" + item["ProfileStyle"]
        path = local(evaluation / label / (stem + ".pdf"), evaluation)
        if path.stat().st_size > 16 * 1024 * 1024:
            raise ValueError("Oversized retained PDF")
        before = path.read_bytes()
        if sha(before) != item["PdfSha256"]:
            raise ValueError("Retained authored PDF identity changed")
        run = subprocess.run([str(engine), "--json", "--json-stream-data=inline", str(path)], capture_output=True, check=True, timeout=20)
        if len(run.stdout) > 16 * 1024 * 1024 or len(run.stderr) > 65536:
            raise ValueError("Inspection output exceeds the retained-fixture budget")
        (output / (label + ".json")).write_bytes(run.stdout)
        facts = json.loads(run.stdout)
        objects = facts["qpdf"][1]
        pages, fonts, roles = [], [], collections.Counter()
        for index, page in enumerate(facts["pages"], 1):
            value = objects["obj:" + page["object"]]["value"]
            content = b"\n".join(base64.b64decode(objects["obj:" + ref]["stream"]["data"], validate=True) for ref in page["contents"])
            (output / (label + "-" + str(index) + ".txt")).write_bytes(content)
            streams[(stem, item["ProfileStyle"], index)] = content.decode("latin1").splitlines()
            pages.append({"page": index, "mediaBox": value.get("/MediaBox"), "decodedContentSha256": sha(content)})
        for key, obj in objects.items():
            value = obj.get("value")
            if not isinstance(value, dict):
                continue
            if "/BaseFont" in value:
                fonts.append({"object": key, "dictionary": value})
            if "/FontFile2" in value:
                data = base64.b64decode(objects["obj:" + value["/FontFile2"]]["stream"]["data"], validate=True)
                fonts.append({"fontProgramSha256": sha(data), "bytes": len(data)})
            if value.get("/Type") == "/StructElem":
                roles[value.get("/S", "missing")] += 1
        if path.read_bytes() != before:
            raise ValueError("Authored PDF changed during inspection")
        report.append({"source": str(path), "sha256": sha(before), "pages": pages, "fonts": fonts, "structureRoles": dict(roles)})
    for (stem, style, page), content in streams.items():
        if style != "modern":
            continue
        difference = "\n".join(difflib.unified_diff(content, streams[(stem, "legacy", page)], fromfile="modern", tofile="legacy", n=2))
        (output / (stem + "-" + str(page) + ".diff")).write_text(difference, encoding="utf-8")
    (output / "report.json").write_text(json.dumps({"scope": "Authored retained fixture diagnostics; no fidelity acceptance or complete PDF/tag validation",
        "inspectionScriptSha256": sha(pathlib.Path(__file__).read_bytes()), "qpdfArchiveSha256": pin["archiveSha256"], "results": report}, indent=2) + "\n", encoding="utf-8")
    print("Office PDF inspection evidence: " + str(output))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise ValueError("Usage: Inspect-OfficePdfComparison.py <authored legacy evaluation> <retained qpdf directory>")
    main()
