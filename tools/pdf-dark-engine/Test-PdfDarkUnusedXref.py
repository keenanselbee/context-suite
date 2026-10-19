"""Bound zero-offset xref recovery to objects unreachable from the PDF trailer.

Every fixture is authored here, including its classic cross-reference table.
Requires PyMuPDF and an isolated PDF-dark native host. No source PDF is copied.
"""

import argparse
import hashlib
import json
import pathlib
import struct
import subprocess
import tempfile

import pymupdf


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
engine = pathlib.Path(args.engine).resolve(strict=True)
scratch = pathlib.Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)


def stream(data, extra=b"", wrong_length=False):
    length = 1 if wrong_length else len(data)
    return (b"<< /Length " + str(length).encode("ascii") + b" " + extra +
            b" >>\nstream\n" + data + b"\nendstream")


def fixture(case):
    content = (b"1 1 1 rg 0 0 420 300 re f\n"
               b"0 0 0 rg BT /F1 14 Tf 50 200 Td (Authored xref check) Tj ET\n")
    root_extra = b""
    page_extra = b""
    trailer_extra = b""
    objects = {
        1: b"<< /Type /Catalog /Pages 2 0 R >>",
        2: b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        3: b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 420 300] "
           b"/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
        4: stream(content, wrong_length=case == "wrong-stream-length"),
        5: b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    }
    zero_entries = {20}
    corrupt_offsets = {}
    if case == "clean":
        zero_entries.clear()
    elif case == "unreachable-zero-chain":
        objects[6] = b"<< /Next 7 0 R >>"
        objects[7] = b"[20 0 R]"
    elif case == "many-unreachable-zero":
        zero_entries = set(range(20, 40))
    elif case in ("reachable-root", "reachable-page", "reachable-resources", "reachable-content"):
        zero_entries.add({"reachable-root": 1, "reachable-page": 3,
                          "reachable-resources": 5, "reachable-content": 4}[case])
    elif case == "reachable-metadata":
        root_extra = b" /Metadata 20 0 R"
    elif case == "reachable-trailer-info":
        trailer_extra = b" /Info 20 0 R"
    elif case == "reachable-indirect-chain":
        root_extra = b" /AuthoredChain [6 0 R]"
        objects[6] = b"<< /Next 7 0 R >>"
        objects[7] = b"[20 0 R]"
    elif case == "reachable-stream-dictionary":
        root_extra = b" /Metadata 6 0 R"
        objects[6] = stream(b"<x:xmpmeta xmlns:x='adobe:ns:meta/'/>",
                            b"/Type /Metadata /Subtype /XML /AuthoredReference 20 0 R")
    elif case == "reachable-page-extension":
        page_extra = b" /AuthoredReference 20 0 R"
    elif case == "nonzero-corrupt-unused":
        zero_entries.clear()
        corrupt_offsets[20] = 9  # Inside the header, not an object boundary.
    elif case not in ("unreachable-zero", "wrong-startxref", "wrong-stream-length"):
        raise ValueError(case)

    objects[1] = objects[1][:-2] + root_extra + b" >>"
    objects[3] = objects[3][:-2] + page_extra + b" >>"
    output = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets = {}
    for number, body in sorted(objects.items()):
        offsets[number] = len(output)
        output += str(number).encode("ascii") + b" 0 obj\n" + body + b"\nendobj\n"
    size = max((*objects, *zero_entries, *corrupt_offsets)) + 1
    xref = len(output)
    output += f"xref\n0 {size}\n".encode("ascii")
    output += b"0000000000 65535 f \n"
    for number in range(1, size):
        if number in zero_entries:
            output += b"0000000000 00000 n \n"
        elif number in corrupt_offsets:
            output += f"{corrupt_offsets[number]:010d} 00000 n \n".encode("ascii")
        elif number in offsets:
            output += f"{offsets[number]:010d} 00000 n \n".encode("ascii")
        else:
            output += b"0000000000 00000 f \n"
    startxref = xref + (3 if case == "wrong-startxref" else 0)
    output += (f"trailer\n<< /Size {size} /Root 1 0 R".encode("ascii") + trailer_extra +
               f" >>\nstartxref\n{startxref}\n%%EOF\n".encode("ascii"))
    return bytes(output)


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        process = subprocess.run([str(engine), mode], stdin=file, capture_output=True, timeout=45)
        file.seek(0)
        assert file.read() == source, "Native host changed its source input"
    if process.returncode:
        assert not process.stdout
        return process.returncode, process.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_size + pdf_size
    return (0, json.loads(process.stdout[16:16 + metadata_size]),
            process.stdout[16 + metadata_size:])


def geometry(data):
    with pymupdf.open(stream=data, filetype="pdf") as document:
        return [(tuple(page.mediabox), page.rotation,
                 [(word[4], tuple(round(float(value), 4) for value in word[:4]))
                  for word in page.get_text("words")]) for page in document]


results = []
positive = ("clean", "unreachable-zero", "unreachable-zero-chain", "many-unreachable-zero")
negative = ("reachable-root", "reachable-page", "reachable-resources", "reachable-content",
            "reachable-metadata", "reachable-trailer-info", "reachable-indirect-chain",
            "reachable-stream-dictionary", "reachable-page-extension", "nonzero-corrupt-unused",
            "wrong-startxref", "wrong-stream-length")
for case in (*positive, *negative):
    source = fixture(case)
    digest = hashlib.sha256(source).hexdigest()
    (scratch / f"{case}-source.pdf").write_bytes(source)
    inspection_code, inspected, inspection_output = invoke(source, "--inspect")
    code, converted, output = invoke(source, "--convert")
    assert hashlib.sha256(source).hexdigest() == digest
    if case in positive:
        assert inspection_code == code == 0 and inspected == converted and not inspection_output and output, \
            (case, inspection_code, inspected, code, converted)
        assert converted["changedPages"] == 1
        assert geometry(source) == geometry(output), (case, "Text or geometry changed")
        (scratch / f"{case}-dark.pdf").write_bytes(output)
        for mode in ("--inspect", "--convert"):
            repeat_code, facts, repeated = invoke(output, mode)
            assert repeat_code == 0 and facts["changedPages"] == 0 and \
                facts["alreadyDarkPages"] == 1 and not repeated, (case, mode, repeat_code, facts)
        details = {"converted": True, "repeatNoOp": True}
    else:
        assert inspection_code in (3, 4, 5) and code in (3, 4, 5) and not inspection_output and not output, \
            (case, inspection_code, inspected, code, converted)
        details = {"refused": True, "inspectionReason": inspected, "conversionReason": converted}
    results.append({"case": case, "sourceSha256": digest, **details})
    print("PASS", case, "converted" if case in positive else "refused")

(scratch / "results.json").write_text(json.dumps({
    "engine": str(engine),
    "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
    "suiteSha256": hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(),
    "reproduction": ["python", str(pathlib.Path(__file__).resolve()), "--engine", str(engine),
                     "--scratch", "<fresh repository-local scratch directory>"],
    "cases": results,
}, indent=2), encoding="utf-8")
print(f"Passed {len(results)} unused-xref checks.")
