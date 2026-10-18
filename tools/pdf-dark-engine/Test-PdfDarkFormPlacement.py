"""Exercise conservative Form XObject placement and cross-resource contrast.

Uses authored PDFs only. Requires pypdf, ReportLab and PyMuPDF. Run against a
newly built development host with a fresh repository-local scratch directory.
"""

import argparse
import hashlib
import io
import json
import pathlib
import struct
import subprocess
import tempfile

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, DecodedStreamObject, DictionaryObject,
                           FloatObject, NameObject, NumberObject)
from reportlab.pdfgen.canvas import Canvas


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
engine = pathlib.Path(args.engine).resolve(strict=True)
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)


def name(value):
    return NameObject(value)


def array(*values):
    return ArrayObject([NumberObject(value) if isinstance(value, int) else FloatObject(value)
                        for value in values])


def dictionary(**values):
    return DictionaryObject({name("/" + key): value for key, value in values.items()})


def form(writer, fonts, kind, *, bbox=(0, 0, 100, 80), matrix=(1, 0, 0, 1, 5, 0)):
    value = DecodedStreamObject()
    if kind == "dark":
        # The painted path exceeds BBox; the PDF Form BBox must clip it.
        value.set_data(b"0.02 0.03 0.05 rg\n-100 -100 400 300 re f\n")
    else:
        value.set_data(b"1 1 1 rg\nBT /F1 18 Tf 8 35 Td (FORM) Tj ET\n")
    value.update(dictionary(Type=name("/XObject"), Subtype=name("/Form"),
                            FormType=NumberObject(1), BBox=array(*bbox),
                            Matrix=array(*matrix),
                            Resources=dictionary(Font=fonts)))
    return writer._add_object(value)


def fixture(*, page_text=None, page_fill=False, forms=("dark",),
            positions=((40, 45),), bbox=(0, 0, 100, 80),
            matrix=(1, 0, 0, 1, 5, 0)):
    source = io.BytesIO()
    canvas = Canvas(source, pagesize=(420, 300), pageCompression=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 420, 300, fill=1, stroke=0)
    if page_fill:
        canvas.setFillColorRGB(.04, .06, .1)
        canvas.rect(45, 45, 160, 100, fill=1, stroke=0)
    canvas.setFont("Helvetica", 18)
    if page_text:
        canvas.setFillColorRGB(1, 1, 1)
        canvas.drawString(*page_text, "PAGE")
    else:
        canvas.setFillColorRGB(0, 0, 0)
        canvas.drawString(260, 265, "Lecture")
    canvas.save()
    writer = PdfWriter()
    writer.clone_document_from_reader(PdfReader(io.BytesIO(source.getvalue())))
    page = writer.pages[0]
    fonts = page["/Resources"]["/Font"]
    assert "/F1" in fonts, "The authored Form needs its own declared font."
    objects = dictionary()
    commands = []
    for index, (kind, position) in enumerate(zip(forms, positions, strict=True)):
        key = f"/Fm{index}"
        objects[name(key)] = form(writer, fonts, kind, bbox=bbox, matrix=matrix)
        x, y = position
        commands.append(f"q 1 0 0 1 {x} {y} cm {key} Do Q\n".encode("ascii"))
    page["/Resources"][name("/XObject")] = objects
    content = page.get_contents()
    content.set_data(content.get_data() + b"\n" + b"".join(commands))
    page.replace_contents(content)
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        process = subprocess.run([str(engine), mode], stdin=file,
                                 capture_output=True, timeout=45)
    if process.returncode:
        assert not process.stdout
        return process.returncode, process.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_length + pdf_length
    return 0, json.loads(process.stdout[16:16 + metadata_length]), process.stdout[16 + metadata_length:]


def text_geometry(data):
    document = pymupdf.open(stream=data, filetype="pdf")
    page = document[0]
    return [(span["font"], span["size"], span["chars"]) for span in page.get_texttrace()]


def pixel(page, x, y):
    pixmap = page.get_pixmap(alpha=False)
    offset = (y * pixmap.width + x) * pixmap.n
    return tuple(pixmap.samples[offset:offset + 3])


results = []
failures = []


def accepted(case, source):
    digest = hashlib.sha256(source).digest()
    code, inspection, empty = invoke(source, "--inspect")
    assert code == 0 and not empty, (case, code, inspection)
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output and facts == inspection and facts["changedPages"] == 1, (case, code, facts)
    assert hashlib.sha256(source).digest() == digest
    assert text_geometry(source) == text_geometry(output), (case, "text geometry")
    before = pymupdf.open(stream=source, filetype="pdf")
    after = pymupdf.open(stream=output, filetype="pdf")
    assert before[0].rect == after[0].rect
    assert before[0].get_text() == after[0].get_text()
    if case == "bbox-clipped-disjoint":
        assert max(pixel(before[0], 80, 200)) < 50, "Form interior did not render."
        assert min(pixel(before[0], 200, 200)) > 230, "Form paint escaped its BBox."
        assert min(pixel(after[0], 80, 200)) > max(pixel(after[0], 200, 200)) + 100, \
            "Recolored Form and page paper did not remain distinct."
    (scratch / f"{case}-source.pdf").write_bytes(source)
    (scratch / f"{case}-dark.pdf").write_bytes(output)
    repeated_code, repeated, repeated_output = invoke(output, "--convert")
    repeat_noop = repeated_code == 0 and not repeated_output and repeated["changedPages"] == 0
    if not repeat_noop:
        failures.append((case, "repeat conversion", repeated))
    results.append({"case": case, "converted": True, "repeatNoOp": repeat_noop})
    print("PASS" if repeat_noop else "FAIL", case)


def refused(case, source, reason=None):
    for mode in ("--inspect", "--convert"):
        code, detail, output = invoke(source, mode)
        assert code in (3, 4, 5) and not output, (case, mode, code, detail)
        if reason:
            assert reason in detail, (case, mode, detail)
    results.append({"case": case, "refused": True})
    print("PASS", case, "refused")


# The dark Form path extends across the page before BBox clipping. Its
# declared bounds and placement remain disjoint from the page's light text.
accepted("bbox-clipped-disjoint", fixture(page_text=(255, 225)))
refused("form-dark-over-page-text", fixture(page_text=(60, 80)), "form fills and page text")
refused("page-dark-over-form-text", fixture(page_fill=True, forms=("text",)),
        "page fills and form text")
refused("overlapping-dark-text-forms",
        fixture(forms=("dark", "text"), positions=((40, 45), (60, 60))),
        "Overlapping PDF forms")
refused("reversed-form-bbox", fixture(bbox=(100, 0, 0, 80)))
refused("huge-form-bbox", fixture(bbox=(0, 0, 300000000, 80)))
refused("malformed-form-matrix", fixture(matrix=(1, 0, 0, 1, 5)))
refused("huge-form-placement", fixture(matrix=(1, 0, 0, 1, 300000000, 0)))

(scratch / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
if failures:
    raise AssertionError(failures)
