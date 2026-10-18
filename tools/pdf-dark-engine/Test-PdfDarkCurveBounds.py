"""Exercise bounded smooth cubic stroke footprints near image-overlaid text.

The PDFs, raster diagram and cubic paths are authored in memory. Requires
Pillow, ReportLab and PyMuPDF; use a fresh repository-local scratch directory.
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
from PIL import Image, ImageDraw
from pypdf import PdfReader
from pypdf.generic import ContentStream
from reportlab.lib.utils import ImageReader
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


def fixture(*, ellipse_x=130, sharp_join=False):
    diagram = Image.new("RGB", (180, 180), "white")
    ink = ImageDraw.Draw(diagram)
    ink.rectangle((10, 10, 35, 35), fill=(20, 20, 20))
    ink.line((12, 12, 42, 12), fill=(20, 20, 20), width=2)
    ink.line((12, 12, 12, 42), fill=(20, 20, 20), width=2)
    output = io.BytesIO()
    canvas = Canvas(output, pagesize=(320, 300), pageCompression=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 320, 300, fill=1, stroke=0)
    canvas.drawImage(ImageReader(diagram), 20, 20, width=260, height=260)

    # Four tangent-continuous cubics. The entire path is explicitly closed
    # before stroking; changing the final control makes one sharp join.
    x, y, radius = ellipse_x, 145, 20
    k = radius * .5522847498
    path = canvas.beginPath()
    path.moveTo(x + radius, y)
    path.curveTo(x + radius, y + k, x + k, y + radius, x, y + radius)
    path.curveTo(x - k, y + radius, x - radius, y + k, x - radius, y)
    path.curveTo(x - radius, y - k, x - k, y - radius, x, y - radius)
    path.curveTo(x + k, y - radius,
                 x + radius - (5 if sharp_join else 0), y - k,
                 x + radius, y)
    canvas.setStrokeColorRGB(.2, .2, .2)
    canvas.setLineWidth(1)
    canvas.drawPath(path, stroke=1, fill=0)
    canvas.setFont("Helvetica", 18)
    canvas.setFillColorRGB(0, 0, 0)
    # Helvetica's admitted font-wide fallback extends .25 em left of the
    # origin: at 18 pt this starts at x152.5, still clear of x150.500001.
    canvas.drawString(157, 140, "TEXT")
    canvas.save()
    return output.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        process = subprocess.run([str(engine), mode], stdin=file,
                                 capture_output=True, timeout=60)
    if process.returncode:
        assert not process.stdout
        return process.returncode, process.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_length + pdf_length
    return 0, json.loads(process.stdout[16:16 + metadata_length]), process.stdout[16 + metadata_length:]


def cubic_geometry(data):
    reader = PdfReader(io.BytesIO(data))
    result = []
    for operands, operator in ContentStream(reader.pages[0].get_contents(), reader).operations:
        if operator in (b"m", b"c", b"S", b"s"):
            result.append((operator, tuple(float(value) for value in operands)))
    return result


def text_geometry(page):
    return [(span["font"], span["size"], span["chars"])
            for span in page.get_texttrace()]


source = fixture()
digest = hashlib.sha256(source).digest()
code, inspection, empty = invoke(source, "--inspect")
assert code == 0 and not empty, ("smooth near miss inspect", code, inspection)
code, converted, dark = invoke(source, "--convert")
assert code == 0 and dark and converted == inspection, ("smooth near miss convert", code, converted)
assert hashlib.sha256(source).digest() == digest
before = pymupdf.open(stream=source, filetype="pdf")
after = pymupdf.open(stream=dark, filetype="pdf")
assert before[0].rect == after[0].rect
assert before[0].get_text() == after[0].get_text() == "TEXT\n"
assert text_geometry(before[0]) == text_geometry(after[0])
assert cubic_geometry(source) == cubic_geometry(dark)
assert len(before[0].get_images()) == len(after[0].get_images()) == 1
assert before[0].get_image_rects(before[0].get_images()[0][0]) == \
    after[0].get_image_rects(after[0].get_images()[0][0])
assert len(before[0].get_drawings()) == len(after[0].get_drawings())
for mode in ("--inspect", "--convert"):
    code, repeat, output = invoke(dark, mode)
    assert code == 0 and not output and repeat["changedPages"] == 0 and \
        repeat["alreadyDarkPages"] == 1, ("smooth near miss repeat", mode, code, repeat)
(scratch / "smooth-near-miss-source.pdf").write_bytes(source)
(scratch / "smooth-near-miss-dark.pdf").write_bytes(dark)
print("PASS smooth closed cubic near text and repeat")


for case, source in (
    ("true-stroke-overlap", fixture(ellipse_x=137)),
    ("sharp-join-fallback", fixture(sharp_join=True)),
):
    code, inspection, empty = invoke(source, "--inspect")
    assert code == 0 and not empty, (case, inspection)
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output and facts == inspection, (case, facts)
    with pymupdf.open(stream=source, filetype="pdf") as before, \
            pymupdf.open(stream=output, filetype="pdf") as after:
        assert text_geometry(before[0]) == text_geometry(after[0])
        assert cubic_geometry(source) == cubic_geometry(output)
        assert before[0].get_image_rects(before[0].get_images()[0][0]) == \
            after[0].get_image_rects(after[0].get_images()[0][0])
        if case == "true-stroke-overlap":
            # The local original-color composite now preserves this ambiguous
            # overlap. Admission must not silently recolor the underlying image.
            assert facts["preservedImages"] == 1 and facts["recoloredImages"] == 0
            original_image = before[0].get_images()[0][0]
            output_image = after[0].get_images()[0][0]
            assert before.xref_stream(original_image) == after.xref_stream(output_image)
            a = before[0].get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False)
            b = after[0].get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False)
            assert all(a.pixel(x, y) == b.pixel(x, y)
                       for x in range(42, 558) for y in range(42, 558)), case
            assert [s["color"] for s in before[0].get_texttrace()] == \
                [s["color"] for s in after[0].get_texttrace()]
        else:
            # This join turns less than 90 degrees. The rounded-panel tangent
            # proof bounds its miter while retaining the nearby text clearance.
            assert facts["recoloredImages"] == 1
            image = after[0].get_pixmap(alpha=False)
            assert max(image.pixel(30, 260)) < 35
    for mode in ("--inspect", "--convert"):
        code, repeat, empty = invoke(output, mode)
        assert code == 0 and not empty and repeat["changedPages"] == 0, (case, repeat)
    (scratch / (case + "-source.pdf")).write_bytes(source)
    (scratch / (case + "-dark.pdf")).write_bytes(output)
    print("PASS", case, "geometry, appearance and repeat")

(scratch / "results.json").write_text(json.dumps({"safeRecoloring": 2,
                                                "preservedComposite": 1,
                                                "repeatNoOp": 3}, indent=2), encoding="utf-8")
