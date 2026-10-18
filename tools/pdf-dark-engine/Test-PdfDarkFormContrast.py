"""Exercise page palette panels behind explicit-color Form text and repeatability.

All PDFs are authored in memory. Requires pypdf and ReportLab; use a fresh
repository-local scratch directory and a newly built development host.
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
                           NameObject, NumberObject)
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


def dictionary(**values):
    return DictionaryObject({name("/" + key): value for key, value in values.items()})


def fixture(*, panel, text, panel_after_form=False, arrow_width=0):
    buffer = io.BytesIO()
    canvas = Canvas(buffer, pagesize=(420, 300), pageCompression=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 420, 300, fill=1, stroke=0)
    if not panel_after_form:
        canvas.setFillColorRGB(*panel)
        canvas.rect(45, 45, 160, 100, fill=1, stroke=0)
    canvas.setFont("Helvetica", 18)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.drawString(260, 265, "Lecture")
    canvas.save()

    writer = PdfWriter()
    writer.clone_document_from_reader(PdfReader(io.BytesIO(buffer.getvalue())))
    page = writer.pages[0]
    fonts = page["/Resources"]["/Font"]
    assert "/F1" in fonts
    form = DecodedStreamObject()
    form.set_data((f"{text[0]} {text[1]} {text[2]} rg\n"
                   "BT /F1 18 Tf 8 35 Td (FORM) Tj ET\n").encode("ascii"))
    form.update(dictionary(Type=name("/XObject"), Subtype=name("/Form"),
                           FormType=NumberObject(1),
                           BBox=ArrayObject([NumberObject(x) for x in (0, 0, 100, 80)]),
                           Resources=dictionary(Font=fonts)))
    page["/Resources"][name("/XObject")] = dictionary(Fm=writer._add_object(form))
    content = page.get_contents()
    commands = b"\nq 1 0 0 1 50 50 cm /Fm Do Q\n"
    if arrow_width:
        commands += ("0.037095 0.469365 0.107405 rg\n"
                     f"45 90 m 145 90 l 145 {90 + arrow_width} l 45 {90 + arrow_width} l h\n"
                     "145 87 m 153 91.5 l 145 96 l h f\n").encode("ascii")
    if panel_after_form:
        commands += (f"{panel[0]} {panel[1]} {panel[2]} rg\n"
                     "45 45 160 100 re f\n").encode("ascii")
    content.set_data(content.get_data() + commands)
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


source = fixture(panel=(.0784, .992, .227), text=(0, 0, 0))
source_digest = hashlib.sha256(source).digest()
code, inspection, empty = invoke(source, "--inspect")
assert code == 0 and not empty, ("initial inspection", code, inspection)
code, converted, dark = invoke(source, "--convert")
assert code == 0 and dark and converted == inspection, ("initial conversion", code, converted)
assert hashlib.sha256(source).digest() == source_digest
before = PdfReader(io.BytesIO(source)).pages[0]
after = PdfReader(io.BytesIO(dark)).pages[0]
assert before.mediabox == after.mediabox
assert before["/Resources"]["/XObject"]["/Fm"]["/BBox"] == \
    after["/Resources"]["/XObject"]["/Fm"]["/BBox"]
for mode in ("--inspect", "--convert"):
    code, repeat, output = invoke(dark, mode)
    assert code == 0 and not output and repeat["changedPages"] == 0 and \
        repeat["alreadyDarkPages"] == 1, ("repeat", mode, code, repeat)
(scratch / "bright-panel-form-text-source.pdf").write_bytes(source)
(scratch / "bright-panel-form-text-dark.pdf").write_bytes(dark)
print("PASS bright panel and Form text repeated without changes")

source = fixture(panel=(.105, .11, .12), text=(1, 1, 1), arrow_width=3)
code, facts, dark = invoke(source, "--convert")
assert code == 0 and dark, ("narrow arrow overlay", code, facts)
code, facts, empty = invoke(dark, "--convert")
assert code == 0 and not empty and facts["changedPages"] == 0, ("arrow repeat", code, facts)
print("PASS narrow compound arrow over Form text and repeat")


for case, source in (
    ("ordinary-dark-fill", fixture(panel=(.04, .06, .1), text=(1, 1, 1))),
    ("palette-low-contrast-text", fixture(panel=(.105, .11, .12), text=(1, 0, 0))),
    ("palette-panel-after-form", fixture(panel=(.105, .11, .12), text=(1, 1, 1),
                                         panel_after_form=True)),
    ("broad-arrow-panel", fixture(panel=(.105, .11, .12), text=(1, 1, 1), arrow_width=12)),
):
    for mode in ("--inspect", "--convert"):
        code, reason, output = invoke(source, mode)
        assert code == 3 and "page fills and form text" in reason and not output, \
            (case, mode, code, reason)
    print("PASS", case, "refused")

(scratch / "results.json").write_text(json.dumps({"positive": 2,
                                                "negatives": 4}, indent=2), encoding="utf-8")
