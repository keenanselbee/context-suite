"""Exercise the pinned native PDF dark host with independently authored PDF fixtures.

Test-only Python dependencies: PyMuPDF, pypdf, reportlab and Pillow.
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
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen.canvas import Canvas
from pypdf import PdfReader, PdfWriter
from pypdf.generic import ArrayObject, DecodedStreamObject, DictionaryObject, NameObject

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
REPOSITORY = pathlib.Path(__file__).resolve().parents[2]
ROOT = pathlib.Path(args.scratch).absolute()
ENGINE = pathlib.Path(args.engine).absolute()
if ".." in ROOT.parts or not ROOT.is_relative_to(REPOSITORY / ".codex-temp") or ROOT.exists():
    raise ValueError("Use a fresh repository-local scratch directory")
pin = json.loads((REPOSITORY / "tools/pdf-dark-engine/payload-candidate.json").read_text())
if hashlib.sha256(ENGINE.read_bytes()).hexdigest().upper() != pin["host"]["sha256"]:
    raise ValueError("Use the reviewed native dark-mode host")
ROOT.mkdir(parents=True)
from io import BytesIO

light = Canvas(str(ROOT / "light-vector.pdf"), pagesize=(420, 300), pageCompression=1)
light.setFillColorRGB(1, 1, 1)
light.rect(0, 0, 420, 300, stroke=0, fill=1)
light.setFillColorRGB(0, 0, 0)
light.setFont("Helvetica", 24)
light.drawString(24, 246, "Searchable dark-mode test")
light.setStrokeColorRGB(.1, .3, .7)
light.setLineWidth(3)
light.line(24, 140, 350, 140)
light.save()

mixed = Canvas(str(ROOT / "mixed-pages.pdf"), pagesize=(420, 300), pageCompression=1)
mixed.setFillColorRGB(1, 1, 1)
mixed.rect(0, 0, 420, 300, stroke=0, fill=1)
mixed.setFillColorRGB(0, 0, 0)
mixed.drawString(24, 246, "Light page")
mixed.showPage()
mixed.setFillColorRGB(.07, .08, .09)
mixed.rect(0, 0, 420, 300, stroke=0, fill=1)
mixed.setFillColorRGB(.95, .9, .8)
mixed.drawString(24, 246, "Already dark page")
mixed.save()

image = Image.new("RGB", (180, 100), "white")
draw = ImageDraw.Draw(image)
draw.rectangle((20, 20, 160, 80), outline="black", width=3)
draw.line((25, 25, 155, 75), fill="black", width=3)
buffer = BytesIO()
image.save(buffer, format="PNG")
buffer.seek(0)
raster = Canvas(str(ROOT / "line-art.pdf"), pagesize=(420, 300), pageCompression=1)
raster.setFillColorRGB(1, 1, 1)
raster.rect(0, 0, 420, 300, stroke=0, fill=1)
raster.drawImage(ImageReader(buffer), 30, 100, width=270, height=150)
raster.setFillColorRGB(0, 0, 0)
raster.drawString(30, 260, "Diagram")
raster.save()


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=ROOT) as source:
        source.write(data)
        source.flush()
        source.seek(0)
        result = subprocess.run([str(ENGINE), mode], stdin=source, capture_output=True, timeout=30)
    if result.returncode:
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, json_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + json_size + pdf_size
    return 0, json.loads(result.stdout[16:16 + json_size]), result.stdout[16 + json_size:]


def make_pdf(draw):
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(420, 300), pageCompression=1)
    draw(canvas)
    canvas.save()
    return stream.getvalue()


def check_positive(name, data, changed, dark, recolored):
    original = hashlib.sha256(data).digest()
    code, inspection, no_pdf = invoke(data, "--inspect")
    assert code == 0, (name, inspection)
    assert not no_pdf
    code, conversion, output = invoke(data, "--convert")
    assert code == 0, (name, conversion)
    assert inspection == conversion
    assert (conversion["changedPages"], conversion["alreadyDarkPages"],
            conversion["recoloredImages"]) == (changed, dark, recolored), (name, conversion)
    assert bool(output) == bool(changed)
    if output:
        original_pdf = pymupdf.open(stream=data, filetype="pdf")
        result_pdf = pymupdf.open(stream=output, filetype="pdf")
        assert len(original_pdf) == len(result_pdf)
        for before, after in zip(original_pdf, result_pdf):
            assert before.rect == after.rect and before.get_text() == after.get_text(), name
    assert hashlib.sha256(data).digest() == original
    (ROOT / (name + "-source.pdf")).write_bytes(data)
    if output:
        (ROOT / (name + "-dark.pdf")).write_bytes(output)
        for index, page in enumerate(result_pdf):
            page.get_pixmap(matrix=pymupdf.Matrix(1.5, 1.5)).save(ROOT / (name + f"-dark-{index + 1:02}.png"))
    print("PASS", name, conversion)
    return output


for name, expected in [("light-vector", (1, 0, 0)),
                       ("mixed-pages", (2, 0, 0)),
                       ("line-art", (1, 0, 1))]:
    check_positive(name, (ROOT / (name + ".pdf")).read_bytes(), *expected)


def high_key_photo(canvas):
    image = Image.new("RGB", (180, 120))
    for y in range(120):
        for x in range(180):
            level = 218 + ((x * 7 + y * 13) % 38)
            image.putpixel((x, y), (level, level, level))
    draw = ImageDraw.Draw(image)
    draw.ellipse((70, 40, 95, 70), fill=(35, 35, 35))
    canvas.drawImage(ImageReader(image), 25, 80, width=270, height=180)
    canvas.drawString(25, 260, "High key grayscale photograph")


photo = make_pdf(high_key_photo)
check_positive("high-key-photo-preserved", photo, 1, 0, 0)


def later_white_cover(canvas):
    canvas.setFillColorRGB(.07, .08, .09)
    canvas.rect(0, 0, 420, 300, stroke=0, fill=1)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 420, 300, stroke=0, fill=1)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.drawString(20, 250, "White wins")


check_positive("later-white-cover", make_pdf(later_white_cover), 1, 0, 0)


def partial_dark(canvas):
    canvas.setFillColorRGB(.07, .08, .09)
    canvas.rect(0, 0, 300, 300, stroke=0, fill=1)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.drawString(310, 250, "Light")


check_positive("partial-dark-rectangle", make_pdf(partial_dark), 1, 0, 0)


def all_dark(canvas):
    canvas.setFillColorRGB(.07, .08, .09)
    canvas.rect(0, 0, 420, 300, stroke=0, fill=1)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.drawString(20, 250, "Already dark")


check_positive("nonpalette-dark-normalized", make_pdf(all_dark), 1, 0, 0)

code, _, converted_vector = invoke((ROOT / "light-vector.pdf").read_bytes(), "--convert")
assert code == 0 and converted_vector
code, reinspected, _ = invoke(converted_vector, "--inspect")
assert code == 0 and reinspected["changedPages"] == 0 and reinspected["alreadyDarkPages"] == 1, (code, reinspected)
code, reconverted, no_pdf = invoke(converted_vector, "--convert")
assert code == 0 and reconverted == reinspected and not no_pdf, (code, reconverted)
print("PASS idempotent converted vector")


def compound_hole(canvas):
    canvas.setFillColorRGB(.07, .08, .09)
    path = canvas.beginPath()
    path.rect(0, 0, 420, 300)
    path.rect(90, 90, 120, 120)
    canvas.drawPath(path, stroke=0, fill=1, fillMode=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.drawString(20, 250, "Compound path has a hole")


code, reason, _ = invoke(make_pdf(compound_hole), "--inspect")
assert code == 3 and "contrast-aware recoloring" in reason, (code, reason)
print("PASS compound dark fill and light text refusal")


def dark_callout(canvas):
    canvas.setFillColorRGB(.05, .07, .11)
    canvas.rect(60, 100, 300, 100, stroke=0, fill=1)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.setFont("Helvetica", 24)
    canvas.drawString(80, 145, "White label")


callout = make_pdf(dark_callout)
source_page = pymupdf.open(stream=callout, filetype="pdf")[0]
pixmap = source_page.get_pixmap(alpha=False)
image = Image.frombytes("RGB", (pixmap.width, pixmap.height), pixmap.samples)
label_area = image.crop((80, 132, 300, 162))
pixels = label_area.tobytes()
values = [sum(pixels[index:index + 3]) / 3 for index in range(0, len(pixels), 3)]
assert min(values) < 60 and max(values) > 220, "Fixture lacks visible light-on-dark contrast"
check_positive("visible-light-on-dark-callout", callout, 1, 0, 0)
code, _, output = invoke(callout, "--convert")
render = pymupdf.open(stream=output, filetype="pdf")[0].get_pixmap(alpha=False)
label = Image.frombytes("RGB", (render.width, render.height), render.samples).crop((80, 132, 300, 162))
assert min(label.convert("L").getdata()) < 60 and max(label.convert("L").getdata()) > 220
print("PASS visible light-on-dark callout contrast")


def dark_arrow(canvas):
    canvas.setFillColorRGB(0, 0, 0)
    path = canvas.beginPath()
    path.moveTo(60, 115)
    path.lineTo(120, 115)
    path.lineTo(120, 90)
    path.lineTo(180, 150)
    path.lineTo(120, 210)
    path.lineTo(120, 185)
    path.lineTo(60, 185)
    path.close()
    canvas.drawPath(path, stroke=0, fill=1)
    canvas.drawString(200, 145, "Black caption")


arrow = make_pdf(dark_arrow)
check_positive("dark-arrow-with-black-caption", arrow, 1, 0, 0)
code, _, arrow_output = invoke(arrow, "--convert")
assert code == 0 and arrow_output
arrow_page = pymupdf.open(stream=arrow_output, filetype="pdf")[0]
arrow_pixmap = arrow_page.get_pixmap(alpha=False)
arrow_image = Image.frombytes("RGB", (arrow_pixmap.width, arrow_pixmap.height), arrow_pixmap.samples)
paper_pixel = arrow_image.getpixel((20, 20))
arrow_pixel = arrow_image.getpixel((90, 150))
assert sum(arrow_pixel) - sum(paper_pixel) > 250, (paper_pixel, arrow_pixel)
print("PASS rendered arrow-paper contrast")


def text_clip(canvas):
    canvas._code.append("7 Tr")
    canvas.drawString(20, 250, "CLIP")
    canvas._code.append("0 Tr")
    canvas.setFillColorRGB(.07, .08, .09)
    canvas.rect(0, 0, 420, 300, stroke=0, fill=1)


check_positive("text-clipping-before-dark-rectangle", make_pdf(text_clip), 1, 0, 0)

writer = PdfWriter()
writer.append(PdfReader(io.BytesIO((ROOT / "light-vector.pdf").read_bytes())))
writer._root_object[NameObject("/OutputIntents")] = ArrayObject([DictionaryObject()])
output_intent_stream = io.BytesIO()
writer.write(output_intent_stream)
code, reason, _ = invoke(output_intent_stream.getvalue(), "--inspect")
assert code == 3 and reason.strip() == "PDF output intents are unsupported.", (code, reason)
print("PASS output intent refusal")

type3 = make_pdf(lambda canvas: canvas.drawString(20, 250, "Colored glyph?"))
assert b"/Subtype /Type1" in type3
type3 = type3.replace(b"/Subtype /Type1", b"/Subtype /Type3", 1)
code, reason, _ = invoke(type3, "--inspect")
assert code == 3 and "Type 3 fonts" in reason, (code, reason)
print("PASS Type 3 font refusal")


def transparent_photo(canvas):
    image = Image.new("RGBA", (120, 90))
    for y in range(90):
        for x in range(120):
            image.putpixel((x, y), ((x * 3) % 256, (y * 5) % 256, 180, 120))
    canvas.drawImage(ImageReader(image), 20, 80, width=240, height=180, mask="auto")
    canvas.drawString(20, 260, "Transparent color image")


transparent_source = make_pdf(transparent_photo)
transparent_output = check_positive("transparent-photo-original-composite", transparent_source, 1, 0, 0)


def original_composite_facts(data):
    page = PdfReader(io.BytesIO(data)).pages[0]
    images = {}
    for key, reference in page["/Resources"]["/XObject"].items():
        image = reference.get_object()
        images[str(key)] = (image.get_data(), image["/SMask"].get_data(),
                            image["/ColorSpace"], image["/Width"], image["/Height"])
    with pymupdf.open(stream=data, filetype="pdf") as document:
        page = document[0]
        text = [(entry["font"], entry["size"], entry["chars"], entry["color"],
                 entry["opacity"], entry["type"]) for entry in page.get_texttrace()]
        placement = [(entry["bbox"], entry["transform"], entry["width"], entry["height"])
                     for entry in page.get_image_info()]
    return images, text, placement


def render_on_opaque_white(data):
    # MuPDF rounds an implicit transparent page's final white composition
    # differently from an explicit white backing (up to two 8-bit levels).
    # Give both render-only copies the same opaque starting surface so exact
    # pixel equality tests the preserved PDF scene rather than that rounding.
    writer = PdfWriter()
    writer.clone_document_from_reader(PdfReader(io.BytesIO(data)))
    page = writer.pages[0]
    content = DecodedStreamObject()
    content.set_data(b"q 1 1 1 rg 0 0 420 300 re f Q\n" + page.get_contents().get_data())
    page[NameObject("/Contents")] = writer._add_object(content)
    buffer = io.BytesIO()
    writer.write(buffer)
    with pymupdf.open(stream=buffer.getvalue(), filetype="pdf") as document:
        return document[0].get_pixmap(alpha=False)


assert original_composite_facts(transparent_source) == original_composite_facts(transparent_output), \
    "Transparent composite changed samples, alpha, text colors, or geometry"
source_render = render_on_opaque_white(transparent_source)
output_render = render_on_opaque_white(transparent_output)
assert all(source_render.pixel(x, y) == output_render.pixel(x, y)
           for y in range(31, 220) for x in range(20, 260)), "Transparent composite changed appearance"
with pymupdf.open(stream=transparent_output, filetype="pdf") as document:
    pixels = document[0].get_pixmap(alpha=False)
    assert max(pixels.pixel(5, 5)) < 60 and max(pixels.pixel(350, 35)) < 60
for mode in ("--inspect", "--convert"):
    code, repeated, empty = invoke(transparent_output, mode)
    assert code == 0 and repeated["changedPages"] == 0 and not empty, (mode, code, repeated)
print("PASS transparent original-composite samples, pixels, geometry and repeat")


def unsafe_transparent_photo(canvas):
    canvas.setFillColorRGB(.8, .1, .1)
    canvas.rect(15, 255, 8, 15, fill=1, stroke=0)
    canvas.setFillColorRGB(0, 0, 0)
    transparent_photo(canvas)


for mode in ("--inspect", "--convert"):
    code, reason, empty = invoke(make_pdf(unsafe_transparent_photo), mode)
    assert code == 3 and not empty, (mode, code, reason)
print("PASS transparent image with unrelated prior paint refused")

code, reason, _ = invoke(b"not pdf", "--inspect")
assert code == 4 and "not a PDF" in reason, (code, reason)
print("PASS invalid input classification")

code, reason, _ = invoke(b"X" * (64 * 1024 * 1024 + 1), "--inspect")
assert code == 5 and "size limit" in reason, (code, reason)
print("PASS source size limit classification")
