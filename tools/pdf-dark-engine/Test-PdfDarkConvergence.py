"""Check palette normalization and no-op repetition against an isolated native host."""

import argparse
import io
import json
import pathlib
import struct
import subprocess
import tempfile

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (BooleanObject, DictionaryObject, FloatObject,
                           NameObject, NumberObject)
from reportlab.pdfgen.canvas import Canvas

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
if not scratch.is_relative_to(repository / ".codex-temp") or scratch.exists():
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)
engine = pathlib.Path(args.engine).resolve(strict=True)


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        result = subprocess.run([str(engine), mode], stdin=file, capture_output=True, timeout=45)
    assert result.returncode == 0, result.stderr.decode(errors="replace")
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_length + pdf_length
    return json.loads(result.stdout[16:16 + metadata_length]), result.stdout[16 + metadata_length:]


def fixture(paper, panel=None, *, panel_crosses_text=False, accent=(1, 0, 0)):
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(420, 300), pageCompression=1)
    if paper is not None:
        canvas.setFillColorRGB(*paper)
        canvas.rect(0, 0, 420, 300, fill=1, stroke=0)
    if panel is not None:
        canvas.setFillColorRGB(*panel)
        canvas.rect(140 if panel_crosses_text else 20, 80,
                    60 if panel_crosses_text else 380, 110, fill=1, stroke=0)
    canvas.setFillColorRGB(*( (1, 1, 1) if paper and max(paper) < .3 else (0, 0, 0)))
    canvas.setFont("Helvetica", 14)
    canvas.drawString(45, 130, "Readable lecture content")
    canvas.setStrokeColorRGB(*accent)
    canvas.setLineWidth(2)
    canvas.line(30, 50, 390, 50)
    canvas.save()
    return stream.getvalue()


def with_graphics_state(source, change=None):
    writer = PdfWriter()
    writer.clone_document_from_reader(PdfReader(io.BytesIO(source)))
    page = writer.pages[0]
    state = DictionaryObject({
        NameObject("/Type"): NameObject("/ExtGState"),
        NameObject("/BM"): NameObject("/Normal"),
        NameObject("/SMask"): NameObject("/None"),
        NameObject("/AIS"): BooleanObject(False),
        NameObject("/OP"): BooleanObject(False),
        NameObject("/op"): BooleanObject(False),
        NameObject("/OPM"): NumberObject(1),
        NameObject("/SA"): BooleanObject(True),
        NameObject("/ca"): FloatObject(1),
        NameObject("/CA"): FloatObject(1),
    })
    if change:
        state[NameObject(change[0])] = change[1]
    page["/Resources"][NameObject("/ExtGState")] = DictionaryObject({NameObject("/GS1"): state})
    page["/Resources"][NameObject("/ColorSpace")] = DictionaryObject({NameObject("/Unused"): NameObject("/DeviceCMYK")})
    contents = page.get_contents()
    contents.set_data(b"/GS1 gs\n" + contents.get_data())
    page.replace_contents(contents)
    stream = io.BytesIO()
    writer.write(stream)
    return stream.getvalue()


results = []
for name, paper, panel in (
    ("implicit-paper", None, None),
    ("white-paper", (1, 1, 1), None),
    ("navy-paper", (0, .125, .251), None),
    ("gray-panel", (1, 1, 1), (.8, .8, .8)),
    ("blue-panel", (1, 1, 1), (.4, .7, .9)),
    ("pastel-panel", (1, 1, 1), (.855, .992, .655)),
    ("palette-panel-edge", (0, .125, .251), (.575538, .349243, .163336)),
    ("neutral-graphics-metadata", (0, .125, .251), None),
    ("muted-accent-endpoint", (1, 1, 1), None),
    ("bright-accent-endpoint", (1, 1, 1), None),
):
    accent = (.42, .227, .204) if name == "muted-accent-endpoint" else (
        (1, .98, .80) if name == "bright-accent-endpoint" else (1, 0, 0))
    source = fixture(paper, panel, panel_crosses_text=name == "palette-panel-edge", accent=accent)
    if name == "neutral-graphics-metadata":
        source = with_graphics_state(source)
    inspected, empty = invoke(source, "--inspect")
    facts, output = invoke(source, "--convert")
    assert inspected == facts and not empty and output and facts["changedPages"] == 1, (name, facts)
    repeated, second = invoke(output, "--convert")
    assert not second and repeated["changedPages"] == 0 and repeated["alreadyDarkPages"] == 1, (name, repeated)
    original = pymupdf.open(stream=source, filetype="pdf")
    converted = pymupdf.open(stream=output, filetype="pdf")
    assert original[0].get_text() == converted[0].get_text(), name
    assert [(s["font"], s["size"], s["chars"]) for s in original[0].get_texttrace()] == [
        (s["font"], s["size"], s["chars"]) for s in converted[0].get_texttrace()], name
    pixmap = converted[0].get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False)
    assert all(abs(actual - expected) <= 1 for actual, expected in
               zip(pixmap.samples[:3], (18, 19, 20))), (name, pixmap.samples[:3])
    pixmap.save(scratch / f"{name}.png")
    (scratch / f"{name}.pdf").write_bytes(output)
    results.append(dict(case=name, normalized=True, repeatNoOp=True, textAndGeometry=True))
for key, value in (("/BM", NameObject("/Multiply")), ("/OP", BooleanObject(True)),
                   ("/AIS", BooleanObject(True)), ("/SMask", DictionaryObject())):
    source = with_graphics_state(fixture((1, 1, 1)), (key, value))
    for mode in ("--inspect", "--convert"):
        with tempfile.TemporaryFile(dir=scratch) as file:
            file.write(source)
            file.seek(0)
            result = subprocess.run([str(engine), mode], stdin=file, capture_output=True, timeout=45)
        assert result.returncode == 3 and not result.stdout, (key, mode, result.stderr)
    results.append(dict(case=f"unsupported-{key[1:]}", refused=True))
(scratch / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"Palette convergence: {len(results)} normalization and refusal cases passed.")
