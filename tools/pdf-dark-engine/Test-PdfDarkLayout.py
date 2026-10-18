"""Check bounded text/panel contrast against an isolated PDF-dark native host."""

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
from pypdf.generic import NameObject


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
parser.add_argument("--lecture", help="Optional local reference PDF for layout pages")
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).absolute()
engine = pathlib.Path(args.engine).absolute()
if not scratch.is_relative_to(repository / ".codex-temp") or scratch.exists():
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        result = subprocess.run([engine, mode], stdin=file, capture_output=True, timeout=45)
    if result.returncode:
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_length + pdf_length
    metadata = json.loads(result.stdout[16:16 + metadata_length])
    return 0, metadata, result.stdout[16 + metadata_length:]


def fixture(draw):
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(420, 300), pageCompression=1)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 420, 300, stroke=0, fill=1)
    draw(canvas)
    canvas.save()
    return stream.getvalue()


def text_geometry(document):
    geometry = []
    for page in document:
        spans = []
        for block in page.get_text("dict")["blocks"]:
            for line in block.get("lines", []):
                for span in line["spans"]:
                    spans.append((span["text"], span["font"], round(span["size"], 3),
                                  tuple(round(value, 3) for value in span["bbox"])))
        geometry.append(spans)
    return geometry


def accepted(name, source, check_pixels=None):
    before_hash = hashlib.sha256(source).digest()
    code, inspected, empty = invoke(source, "--inspect")
    assert code == 0 and not empty, (name, inspected)
    code, converted, output = invoke(source, "--convert")
    assert code == 0 and converted == inspected and output, (name, converted)
    assert converted["policy"] == "pdf-dark-8", (name, converted)
    assert hashlib.sha256(source).digest() == before_hash
    original = pymupdf.open(stream=source, filetype="pdf")
    result = pymupdf.open(stream=output, filetype="pdf")
    assert text_geometry(original) == text_geometry(result), name
    if check_pixels:
        check_pixels(result[0])
    (scratch / f"{name}.pdf").write_bytes(output)
    result[0].get_pixmap(alpha=False).save(scratch / f"{name}.png")
    print(f"PASS {name}")


def refused(name, source, reason_fragment="contrast-aware recoloring"):
    code, reason, output = invoke(source, "--inspect")
    assert code == 3 and reason and not output, (name, code, reason)
    if reason_fragment:
        assert reason_fragment in reason, (name, reason)
    print(f"PASS {name} refused")


def panel(canvas):
    canvas.setFillColorRGB(.05, .07, .11)
    canvas.rect(60, 100, 300, 100, stroke=0, fill=1)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.setFont("Helvetica", 24)
    canvas.drawString(80, 145, "White label")


def panel_pixels(page):
    pixmap = page.get_pixmap(alpha=False)
    width = pixmap.width
    samples = pixmap.samples

    def pixel(x, y):
        at = 3 * (width * y + x)
        return samples[at:at + 3]

    paper = pixel(20, 150)
    dark = pixel(70, 150)
    assert max(dark) < 70 and min(paper) < 30, (paper, dark)
    label = [pixel(x, y) for y in range(132, 161) for x in range(80, 240)]
    assert max(max(value) for value in label) > 220, "Light label disappeared"


accepted("dark-panel-label", fixture(panel), panel_pixels)


def unrelated(canvas):
    canvas.setFillColorRGB(0, 0, 0)
    path = canvas.beginPath()
    path.moveTo(40, 80)
    path.lineTo(140, 80)
    path.lineTo(100, 160)
    path.close()
    canvas.drawPath(path, fill=1, stroke=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.drawString(220, 240, "Separate label")


accepted("disjoint-shape-and-light-text", fixture(unrelated))


def arrow(canvas):
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
    canvas.drawPath(path, fill=1, stroke=0)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.drawString(200, 145, "Dark caption")


def arrow_pixels(page):
    pixmap = page.get_pixmap(alpha=False)
    x, y = 90, 150
    at = 3 * (pixmap.width * y + x)
    arrow_color = pixmap.samples[at:at + 3]
    paper_color = pixmap.samples[3 * (pixmap.width * 20 + 20):3 * (pixmap.width * 20 + 20) + 3]
    assert sum(arrow_color) - sum(paper_color) > 250, (arrow_color, paper_color)


accepted("light-arrow-dark-caption", fixture(arrow), arrow_pixels)


def compound(canvas):
    canvas.setFillColorRGB(.05, .07, .11)
    path = canvas.beginPath()
    path.rect(60, 100, 300, 100)
    path.rect(100, 130, 60, 40)
    canvas.drawPath(path, fill=1, stroke=0, fillMode=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.drawString(80, 145, "Ambiguous label")


refused("compound-fill", fixture(compound))


def partial(canvas):
    canvas.setFillColorRGB(.05, .07, .11)
    canvas.rect(60, 100, 180, 100, fill=1, stroke=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.drawString(215, 145, "Crosses edge")


refused("partial-panel-overlap", fixture(partial))


def rotated(canvas):
    canvas.setFillColorRGB(.05, .07, .11)
    canvas.rect(60, 100, 180, 100, fill=1, stroke=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.saveState()
    canvas.translate(222, 145)
    canvas.rotate(40)
    canvas.drawString(0, 0, "Tilted label")
    canvas.restoreState()


refused("rotated-text-crossing-panel", fixture(rotated))


def clipped(canvas):
    canvas.saveState()
    clip = canvas.beginPath()
    clip.rect(70, 105, 260, 90)
    canvas.clipPath(clip, stroke=0, fill=0)
    canvas.setFillColorRGB(.05, .07, .11)
    canvas.rect(60, 100, 300, 100, fill=1, stroke=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.drawString(90, 145, "Clipped panel")
    canvas.restoreState()


refused("clipped-panel", fixture(clipped))


def covered(canvas):
    canvas.setFillColorRGB(.05, .07, .11)
    canvas.rect(60, 100, 300, 100, fill=1, stroke=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(60, 100, 300, 100, fill=1, stroke=0)
    canvas.drawString(90, 145, "Covered label")


refused("covered-panel", fixture(covered))


def gray_label(canvas):
    canvas.setFillColorRGB(.05, .05, .05)
    canvas.rect(60, 100, 300, 100, fill=1, stroke=0)
    canvas.setFillColorRGB(.36, .36, .36)
    canvas.setFont("Helvetica", 24)
    canvas.drawString(80, 145, "GRAY LABEL")


def gray_label_pixels(page):
    pixmap = page.get_pixmap(alpha=False)
    width = pixmap.width
    samples = pixmap.samples
    panel = samples[3 * (150 * width + 70)]
    label = [samples[3 * (y * width + x)]
             for y in range(136, 161) for x in range(80, 240)]
    assert panel < 100 and max(label) - min(label) >= 130, (panel, min(label), max(label))


accepted("gray-label-on-dark-panel", fixture(gray_label), gray_label_pixels)


def green_paper_marker(canvas):
    canvas.setFillColorRGB(.05, .45, .18)
    canvas.rect(0, 0, 420, 300, fill=1, stroke=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(40, 230, 60, 3, fill=1, stroke=0)
    canvas.setFont("Helvetica", 18)
    canvas.drawString(40, 250, "White caption")


def green_marker_pixels(page):
    pixmap = page.get_pixmap(alpha=False)
    width = pixmap.width
    samples = pixmap.samples

    def pixel(x, y):
        at = 3 * (y * width + x)
        return samples[at:at + 3]

    marker, paper = pixel(50, 68), pixel(20, 68)
    assert sum(marker) - sum(paper) > 400, (marker, paper)
    caption = [samples[3 * (y * width + x)]
               for y in range(34, 53) for x in range(40, 200)]
    assert max(caption) > 220, "White caption disappeared"


accepted("thin-white-marker-on-green-paper", fixture(green_paper_marker), green_marker_pixels)


def joined_white_markers(canvas):
    green_paper_marker(canvas)
    canvas.rect(99, 230, 60, 3, fill=1, stroke=0)


def joined_marker_pixels(page):
    green_marker_pixels(page)
    pixmap = page.get_pixmap(alpha=False)
    for x in (99, 120, 150):
        at = 3 * (68 * pixmap.width + x)
        assert min(pixmap.samples[at:at + 3]) > 200, "Joined marker segment disappeared"


accepted("joined-white-markers-on-green-paper", fixture(joined_white_markers), joined_marker_pixels)


line_art = Image.new("RGB", (240, 80), "white")
ImageDraw.Draw(line_art).rectangle((10, 35, 130, 44), fill="black")


def white_label(canvas):
    canvas.setFillColorRGB(1, 1, 1)
    canvas.setFont("Helvetica", 8)
    canvas.drawString(102, 167, "WHITE LABEL")


def image_label(canvas, background_panel):
    if background_panel:
        canvas.setFillColorRGB(.04, .05, .06)
        canvas.rect(50, 90, 320, 130, fill=1, stroke=0)
    canvas.drawImage(ImageReader(line_art), 90, 130, width=240, height=80)
    white_label(canvas)


def source_label_contrast(source):
    page = pymupdf.open(stream=source, filetype="pdf")[0]
    pixmap = page.get_pixmap(alpha=False)
    values = [pixmap.samples[3 * (y * pixmap.width + x)]
              for y in range(125, 134) for x in range(102, 170)]
    assert max(values) - min(values) > 150, (min(values), max(values))


image_panel = fixture(lambda canvas: image_label(canvas, True))
source_label_contrast(image_panel)
refused("image-between-panel-and-label", image_panel, reason_fragment=None)

image_only = fixture(lambda canvas: image_label(canvas, False))
source_label_contrast(image_only)


def retained_image_label(source):
    name = "recolored-image-under-label"
    inspected_code, inspected, empty = invoke(source, "--inspect")
    code, facts, output = invoke(source, "--convert")
    assert code == inspected_code == 0 and facts == inspected and not empty and output, name
    before = pymupdf.open(stream=source, filetype="pdf")
    after = pymupdf.open(stream=output, filetype="pdf")
    assert text_geometry(before) == text_geometry(after), name
    original = before[0].get_pixmap(alpha=False)
    dark = after[0].get_pixmap(alpha=False)
    assert all(original.pixel(x, y) == dark.pixel(x, y)
               for y in range(90, 170) for x in range(90, 330)), name
    assert max(dark.pixel(20, 20)) < 60, name
    source_images = PdfReader(io.BytesIO(source)).pages[0]["/Resources"]["/XObject"]
    output_images = PdfReader(io.BytesIO(output)).pages[0]["/Resources"]["/XObject"]
    # QPDF may normalize ReportLab's ASCII85+Flate encoding to Flate while
    # preserving the same underlying image samples.
    assert sorted(value.get_object().get_data() for value in source_images.values()) == \
           sorted(value.get_object().get_data() for value in output_images.values()), name
    content = PdfReader(io.BytesIO(output)).pages[0].get_contents().get_data()
    assert b"/Artifact BMC" in content, name
    again_code, again_facts, duplicate = invoke(output, "--convert")
    assert again_code == 0 and again_facts["changedPages"] == 0 and not duplicate, name
    (scratch / f"{name}.pdf").write_bytes(output)
    dark.save(scratch / f"{name}.png")
    print(f"PASS {name} retained original composite")


retained_image_label(image_only)


def stroke_panel(canvas):
    canvas.setFillColorRGB(.04, .05, .06)
    canvas.rect(50, 90, 320, 130, fill=1, stroke=0)
    canvas.setStrokeColorRGB(0, 0, 0)
    canvas.setLineWidth(12)
    canvas.line(100, 167, 220, 167)
    white_label(canvas)


stroke_source = fixture(stroke_panel)
source_label_contrast(stroke_source)
refused("stroke-between-panel-and-label", stroke_source, reason_fragment=None)


unknown = PdfReader(io.BytesIO(fixture(panel)))
font = next(iter(unknown.pages[0]["/Resources"]["/Font"].values())).get_object()
font[NameObject("/BaseFont")] = NameObject("/UnknownCustomFont")
writer = PdfWriter()
writer.add_page(unknown.pages[0])
with io.BytesIO() as stream:
    writer.write(stream)
    code, reason, _ = invoke(stream.getvalue(), "--inspect")
assert code == 3 and "font widths" in reason, (code, reason)
print("PASS unknown font metrics refused")

if args.lecture:
    source = pathlib.Path(args.lecture).read_bytes()
    lecture = pymupdf.open(stream=source, filetype="pdf")
    for page_number in (6, 20, 25, 31):
        isolated = pymupdf.open()
        isolated.insert_pdf(lecture, from_page=page_number - 1, to_page=page_number - 1)
        accepted(f"lecture-layout-{page_number}", isolated.tobytes())

print("PDF dark layout checks passed")
