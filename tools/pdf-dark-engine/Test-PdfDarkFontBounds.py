"""Check wider retained font metrics and conservative contrast in an isolated host."""

import argparse
import io
import json
import pathlib
import struct
import subprocess
import tempfile

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import ArrayObject, DictionaryObject, NameObject, NumberObject
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


def fixture(box, panel=False):
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(420, 300), pageCompression=1)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 420, 300, fill=1, stroke=0)
    if panel:
        canvas.setFillColorRGB(0, 0, 0)
        canvas.rect(30, 100, 125, 75, fill=1, stroke=0)
        canvas.setFillColorRGB(1, 1, 1)
    else:
        canvas.setFillColorRGB(0, 0, 0)
    canvas.setFont("Helvetica", 12)
    canvas.drawString(40, 130, "Font metric test")
    canvas.save()
    writer = PdfWriter()
    writer.clone_document_from_reader(PdfReader(io.BytesIO(stream.getvalue())))
    font = writer.pages[0]["/Resources"]["/Font"]["/F1"]
    font[NameObject("/FontDescriptor")] = DictionaryObject({
        NameObject("/Type"): NameObject("/FontDescriptor"),
        NameObject("/FontName"): NameObject("/Helvetica"),
        NameObject("/Flags"): NumberObject(32),
        NameObject("/FontBBox"): ArrayObject([NumberObject(x) for x in box]),
        NameObject("/Ascent"): NumberObject(800),
        NameObject("/Descent"): NumberObject(-200),
        NameObject("/CapHeight"): NumberObject(700),
        NameObject("/ItalicAngle"): NumberObject(0),
        NameObject("/StemV"): NumberObject(80),
    })
    result = io.BytesIO()
    writer.write(result)
    return result.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        result = subprocess.run([str(engine), mode], stdin=file, capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_length + pdf_length
    return 0, json.loads(result.stdout[16:16 + metadata_length]), result.stdout[16 + metadata_length:]


results = []
for right in (2046, 2868, 4000, 8000):
    box = [-700, -300, right, 1100]
    source = fixture(box)
    code, facts, empty = invoke(source, "--inspect")
    assert code == 0 and not empty, (right, facts)
    code, converted_facts, output = invoke(source, "--convert")
    assert code == 0 and output and converted_facts == facts, (right, converted_facts)
    original = pymupdf.open(stream=source, filetype="pdf")
    converted = pymupdf.open(stream=output, filetype="pdf")
    assert original[0].get_text() == converted[0].get_text()
    before_trace, after_trace = original[0].get_texttrace(), converted[0].get_texttrace()
    assert [(s["font"], s["size"], s["chars"]) for s in before_trace] == [
        (s["font"], s["size"], s["chars"]) for s in after_trace]
    after = PdfReader(io.BytesIO(output))
    assert list(after.pages[0]["/Resources"]["/Font"]["/F1"]["/FontDescriptor"]["/FontBBox"]) == box
    converted[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(scratch / f"bounds-{right}.png")
    results.append(dict(case=f"bounds-{right}", textAndGeometry=True, retainedMetrics=True))

# Expanded boxes must still participate in overlap refusal, not shrink to an
# ordinary font box merely to make panel text appear admissible.
source = fixture([-700, -300, 8000, 1100], panel=True)
code, reason, output = invoke(source, "--convert")
assert code == 3 and "contrast" in reason and not output, (code, reason)
results.append(dict(case="wide-ink-overlap", refused=True))
for name, box, expected in (
    ("metric-budget", [-700, -300, 64001, 1100], 5),
    ("reversed-bounds", [2000, -300, 1000, 1100], 4),
):
    code, reason, output = invoke(fixture(box), "--convert")
    assert code == expected and not output, (name, code, reason)
    results.append(dict(case=name, refused=True))
(scratch / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"Font bounds: {len(results)} cases passed; metrics preserved and overlap/resource guards retained.")
