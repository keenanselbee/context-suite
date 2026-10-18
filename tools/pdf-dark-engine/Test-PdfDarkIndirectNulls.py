"""Preserve passive PDF structure when a null value has an indirect reference.

An indirect null is still null; it carries no bookmark, name, or annotation
target. Test-only dependency: pypdf. Output stays in a fresh scratch folder.
"""

import argparse
import hashlib
import io
import json
import pathlib
import struct
import subprocess
import tempfile

from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, DecodedStreamObject, DictionaryObject,
                           NameObject, NullObject, NumberObject, TextStringObject)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)
engine = pathlib.Path(args.engine).resolve(strict=True)


def fixture(kind):
    writer = PdfWriter()
    page = writer.add_blank_page(width=100, height=100)
    content = DecodedStreamObject()
    content.set_data(b"q 1 1 1 rg 0 0 100 100 re f Q\n"
                     b"q 0 0 0 rg 20 20 60 60 re f Q\n")
    page[NameObject("/Contents")] = writer._add_object(content)
    catalog = writer._root_object

    if kind == "direct-null":
        catalog[NameObject("/Outlines")] = NullObject()
        catalog[NameObject("/Names")] = NullObject()
        page[NameObject("/Annots")] = NullObject()
    elif kind in ("indirect-null", "shared-root-null", "shared-page-null",
                  "valid-link-indirect-null", "active-link-indirect-null"):
        reference = writer._add_object(NullObject())
        catalog[NameObject("/Outlines")] = reference
        if kind in ("shared-root-null", "shared-page-null"):
            catalog[NameObject("/Names")] = reference
        if kind == "shared-page-null":
            page[NameObject("/Annots")] = reference
    elif kind == "distinct-indirect-nulls":
        catalog[NameObject("/Outlines")] = writer._add_object(NullObject())
        catalog[NameObject("/Names")] = writer._add_object(NullObject())
        page[NameObject("/Annots")] = writer._add_object(NullObject())
    else:
        assert kind == "absent", kind

    if kind in ("valid-link-indirect-null", "active-link-indirect-null"):
        uri = ("javascript:alert(1)" if kind == "active-link-indirect-null"
               else "https://example.test/requirements")
        action = DictionaryObject({NameObject("/S"): NameObject("/URI"),
                                   NameObject("/URI"): TextStringObject(uri)})
        annotation = DictionaryObject({
            NameObject("/Type"): NameObject("/Annot"),
            NameObject("/Subtype"): NameObject("/Link"),
            NameObject("/Rect"): ArrayObject([NumberObject(20), NumberObject(20),
                                               NumberObject(80), NumberObject(80)]),
            NameObject("/A"): action,
        })
        page[NameObject("/Annots")] = ArrayObject([writer._add_object(annotation)])

    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as stream:
        stream.write(source)
        stream.seek(0)
        result = subprocess.run([str(engine), mode], stdin=stream,
                                capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_size + pdf_size
    return (0, json.loads(result.stdout[16:16 + metadata_size]),
            result.stdout[16 + metadata_size:])


def passive_targets(data):
    reader = PdfReader(io.BytesIO(data))
    root = reader.trailer["/Root"]
    page = reader.pages[0]
    summary = {}
    for owner, key in ((root, "/Outlines"), (root, "/Names"), (page, "/Annots")):
        value = owner.get(key)
        if value is None:
            summary[key] = "absent"
        else:
            resolved = value.get_object() if hasattr(value, "get_object") else value
            assert isinstance(resolved, NullObject) or key == "/Annots" and isinstance(resolved, ArrayObject)
            summary[key] = "null" if isinstance(resolved, NullObject) else "link-array"
    annots = page.get("/Annots")
    if annots is not None:
        resolved = annots.get_object() if hasattr(annots, "get_object") else annots
        if isinstance(resolved, ArrayObject):
            assert len(resolved) == 1
            annotation = resolved[0].get_object()
            action = annotation["/A"]
            summary["linkUri"] = str(action["/URI"])
    return summary


results = []
positive = ("absent", "direct-null", "indirect-null", "shared-root-null",
            "shared-page-null", "distinct-indirect-nulls", "valid-link-indirect-null")
for kind in (*positive, "active-link-indirect-null"):
    source = fixture(kind)
    (scratch / f"{kind}-source.pdf").write_bytes(source)
    source_targets = passive_targets(source)
    inspection_code, inspection, inspection_pdf = invoke(source, "--inspect")
    code, facts, output = invoke(source, "--convert")
    if kind == "active-link-indirect-null":
        assert inspection_code == code == 3 and not inspection_pdf and not output
        passed = True
        details = {"activeLinkRefused": True, "diagnostic": facts}
    elif code:
        assert inspection_code == 0 and not inspection_pdf and code == 4 and not output
        passed = False
        details = {"currentConversionFailure": facts}
    else:
        assert inspection_code == 0 and inspection == facts and not inspection_pdf and output
        assert facts["changedPages"] == 1
        output_targets = passive_targets(output)
        assert source_targets.get("linkUri") == output_targets.get("linkUri")
        assert all(output_targets[key] in ("absent", "null")
                   for key in ("/Outlines", "/Names"))
        if kind != "valid-link-indirect-null":
            assert output_targets["/Annots"] in ("absent", "null")
        else:
            assert output_targets["/Annots"] == "link-array"
        (scratch / f"{kind}-dark.pdf").write_bytes(output)
        for mode in ("--inspect", "--convert"):
            repeat_code, repeat_facts, duplicate = invoke(output, mode)
            assert repeat_code == 0 and repeat_facts["changedPages"] == 0 and not duplicate
        passed = True
        details = {"passiveTargets": output_targets, "repeatNoOp": True}
    row = {"case": kind, "passed": passed, "sourceTargets": source_targets,
           "sourceSha256": hashlib.sha256(source).hexdigest(), "details": details}
    results.append(row)
    print(("PASS" if passed else "FAIL"), kind, details)

report = {"engine": str(engine),
          "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
          "results": results}
(scratch / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
if not all(row["passed"] for row in results):
    raise AssertionError("Indirect null structure mismatch; see scratch report.json")
print(f"Passed {len(results)} indirect-null structure checks.")
