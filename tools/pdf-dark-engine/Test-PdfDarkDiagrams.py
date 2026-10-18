"""Independently authored JPEG diagram and photograph-preservation regressions.

Test-only dependencies: Pillow, ReportLab, PyMuPDF. Outputs stay in fresh scratch.
"""
import argparse
import base64
import hashlib
import io
import json
import pathlib
import random
import struct
import subprocess
import tempfile

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import NameObject
from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen.canvas import Canvas

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
root = pathlib.Path(args.scratch).resolve()
engine = pathlib.Path(args.engine).resolve(strict=True)
if root.exists() or not root.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
root.mkdir(parents=True)
results = []
font = ImageFont.load_default(size=17)


def jpeg(image):
    data = io.BytesIO()
    image.save(data, format="JPEG", quality=78)
    return data.getvalue()


def document(image, compressed=True, overlay=None, clip_image=False):
    data = io.BytesIO()
    canvas = Canvas(data, pagesize=(480, 340), pageCompression=1)
    canvas.drawString(20, 320, "Diagram conversion regression")
    source = io.BytesIO(jpeg(image)) if compressed else image
    canvas.saveState()
    if clip_image:
        path = canvas.beginPath()
        if clip_image == "curve":
            path.circle(120, 70, 50)
        else:
            path.rect(20, 20, 200, 100)
        canvas.clipPath(path, stroke=0, fill=0)
    canvas.drawImage(ImageReader(source), 20, 20, width=440, height=280, mask="auto")
    canvas.restoreState()
    if overlay:
        x, y, color = overlay
        canvas.setFont("Helvetica", 10)
        canvas.setFillColorRGB(*color)
        canvas.drawString(x, y, "Label")
    canvas.save()
    return data.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=root) as stream:
        stream.write(source)
        stream.seek(0)
        result = subprocess.run([str(engine), mode], stdin=stream, capture_output=True, timeout=120)
    assert result.returncode == 0, result.stderr.decode(errors="replace")
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_size + pdf_size
    return json.loads(result.stdout[16:16 + metadata_size]), result.stdout[16 + metadata_size:]


def embedded(data):
    pdf = pymupdf.open(stream=data, filetype="pdf")
    info = pdf[0].get_images(full=True)[0]
    pixmap = pymupdf.Pixmap(pdf, info[0])
    image = Image.frombytes("RGB", (pixmap.width, pixmap.height), pixmap.samples)
    mask = bytes(pymupdf.Pixmap(pdf, info[1]).samples) if info[1] else b""
    return image, mask


def run(name, image, recolor, checks=None, compressed=True, overlay=None, clip_image=False):
    source = document(image, compressed, overlay, clip_image)
    facts, output = invoke(source, "--convert")
    inspection, empty = invoke(source, "--inspect")
    assert facts == inspection and not empty and output
    before, mask = embedded(source)
    after, new_mask = embedded(output)
    assert before.size == after.size and mask == new_mask, (name, "geometry/mask changed")
    assert (before.tobytes() != after.tobytes()) == recolor, (name, "classification", facts)
    original, converted = pymupdf.open(stream=source), pymupdf.open(stream=output)
    assert original[0].get_text() == converted[0].get_text(), name
    if checks:
        checks(before, after)
    repeated, duplicate = invoke(output, "--convert")
    assert repeated["changedPages"] == 0 and not duplicate, (name, "repeat changed output")
    for label, pdf in (("source", original), ("converted", converted)):
        pdf[0].get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False).save(root / f"{name}-{label}.png")
    after.save(root / f"{name}-image.png")
    (root / f"{name}-source.pdf").write_bytes(source)
    (root / f"{name}-dark.pdf").write_bytes(output)
    results.append(dict(name=name, passed=True, recolored=recolor, facts=facts))
    print("PASS", name)


def contrast(before, after):
    assert max(after.getpixel((5, 5))) < 60, "White image paper stayed bright"
    # Originally dark labels and connectors must remain distinguishable.
    pixels = list(zip(before.getdata(), after.getdata()))
    ink = [sum(b) / 3 for a, b in pixels if max(a) < 80]
    assert ink and sum(v > 140 for v in ink) >= len(ink) * .95, "Dark diagram ink disappeared"


actor = Image.new("RGB", (100, 130), "white")
draw = ImageDraw.Draw(actor)
draw.ellipse((35, 8, 65, 38), outline=(125, 125, 125), width=2)
draw.line([(50, 38), (50, 80)], fill=(125, 125, 125), width=2)
draw.line([(16, 55), (84, 55)], fill=(125, 125, 125), width=2)
draw.line([(20, 118), (50, 80), (80, 118)], fill=(125, 125, 125), width=2)


def actor_check(before, after):
    assert max(after.getpixel((5, 5))) < 60
    foreground = [b for a, b in zip(before.getdata(), after.getdata()) if max(a) < 160]
    assert foreground and sum(sum(p) > 300 for p in foreground) > .95 * len(foreground)


run("gray-actor-jpeg", actor, True, actor_check)

# Lossless ASCII85 transport must not decide whether the same JPEG is a diagram.
wrapped = document(actor)
reader = PdfReader(io.BytesIO(wrapped))
writer = PdfWriter(clone_from=reader)
for reference in writer.pages[0]["/Resources"]["/XObject"].values():
    stream = reference.get_object()
    if stream.get("/Subtype") != "/Image":
        continue
    assert list(stream["/Filter"]) == ["/ASCII85Decode", "/DCTDecode"]
    stream._data = base64.a85decode(stream._data, adobe=True)
    stream[NameObject("/Filter")] = NameObject("/DCTDecode")
direct = io.BytesIO()
writer.write(direct)
wrapped_facts, wrapped_output = invoke(wrapped, "--convert")
direct_facts, direct_output = invoke(direct.getvalue(), "--convert")
assert wrapped_facts["recoloredImages"] == direct_facts["recoloredImages"] == 1
assert embedded(wrapped_output)[0].tobytes() == embedded(direct_output)[0].tobytes()
results.append(dict(name="jpeg-wrapper-independent-classification", passed=True))
print("PASS jpeg-wrapper-independent-classification")


def panel(colored=False):
    image = Image.new("RGB", (640, 400), "white")
    draw = ImageDraw.Draw(image)
    for x, y, label in [(35, 40, "Account"), (360, 40, "Owner"), (35, 245, "Savings"), (360, 245, "Checking")]:
        draw.rectangle((x, y, x + 235, y + 95), fill=(145, 211, 240) if colored else (246, 246, 242), outline=(45, 45, 45), width=2)
        draw.line((x, y + 30, x + 235, y + 30), fill=(45, 45, 45), width=2)
        draw.text((x + 9, y + 7), label, font=font, fill=(25, 25, 25))
        draw.text((x + 9, y + 45), "+ balance: Integer", font=font, fill=(25, 25, 25))
    draw.line([(150, 136), (150, 205), (480, 205), (480, 244)], fill=(35, 35, 35), width=2)
    draw.line([(480, 136), (480, 165)], fill=(35, 35, 35), width=2)
    if colored:
        draw.text((220, 173), "Dependency", font=font, fill=(205, 45, 45))
    else:
        draw.line([(575, 160), (615, 220)], fill=(220, 0, 0), width=9)
        draw.line([(615, 160), (575, 220)], fill=(220, 0, 0), width=9)
    return image


run("neutral-jpeg-red-mark", panel(), True, contrast)


def colored_check(before, after):
    contrast(before, after)
    r, g, b = after.getpixel((250, 115))
    assert b > r + 8 and max(r, g, b) < 150, "Blue panel lost its tint or stayed bright"
    red = [b for a, b in zip(before.getdata(), after.getdata()) if a[0] > a[1] + 60 and a[0] > a[2] + 60]
    assert red and sum(p[0] > p[1] + 20 and p[0] > p[2] + 20 for p in red) > .9 * len(red)


run("blue-panels-red-label-jpeg", panel(True), True, colored_check)


def readable_marks(before, after):
    contrast(before, after)
    def luminance(pixel):
        values = [v / 255 for v in pixel]
        linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in values]
        return sum(a * b for a, b in zip(linear, (.2126, .7152, .0722)))

    background = after.getpixel((580, 320))
    stroke = after.getpixel((550, 325))
    assert (luminance(stroke) + .05) / (luminance(background) + .05) >= 4.5, "Fine panel mark is too dim"
    assert after.getpixel((550, 324)) == background, "Fine stroke expanded into adjacent paper"
    assert max(after.getpixel((400, 315))) < 80, "Open diamond was filled"
    assert min(after.getpixel((460, 315))) > 190, "Filled diamond lost its ink"
    assert max(after.getpixel((5, 5))) < 30, "Unmarked paper was brightened"
    fills = [after.getpixel((x, 330)) for x in range(375, 580)]
    assert all(max(p[c] for p in fills) - min(p[c] for p in fills) <= 3 for c in range(3)), "Panel has tile seams"


for name, fill in [("blue-fine-marks", (145, 211, 240)), ("yellow-fine-marks", (255, 255, 211))]:
    fine = panel(True)
    if fill != (145, 211, 240):
        fine.putdata([fill if pixel == (145, 211, 240) else pixel for pixel in fine.getdata()])
    drawing = ImageDraw.Draw(fine)
    # Half-covered ink models a subpixel stroke on a tinted diagram panel.
    faint = tuple((v + 15) // 2 for v in fill)
    drawing.line((500, 325, 585, 325), fill=faint, width=1)
    drawing.polygon([(400, 306), (409, 315), (400, 324), (391, 315)], outline=(15, 15, 15), width=1)
    drawing.polygon([(460, 306), (469, 315), (460, 324), (451, 315)], fill=(15, 15, 15))
    run(name, fine, True, readable_marks, compressed=False)

yellow = panel().convert("RGBA")
pixels = yellow.load()
for y in range(yellow.height):
    for x in range(yellow.width):
        r, g, b, a = pixels[x, y]
        if (r, g, b) == (246, 246, 242):
            pixels[x, y] = (255, 255, 211, 255)
        if x == 0 and y < 2:
            pixels[x, y] = (255, 255, 255, 0)
run("yellow-panels-exterior-mask", yellow, True, contrast, compressed=False)


def colored_edge_check(before, after):
    colored_check(before, after)
    for y in (110, 190):
        samples = [after.getpixel((x, y)) for x in range(245, 249)]
        assert samples[0][0] > 220, "Colored stroke core lost brightness"
        assert all(a[0] > b[0] for a, b in zip(samples, samples[1:])), "Colored edge has a bright outline"
        assert samples[-1][0] < 120, "Quarter-covered edge was promoted to opaque ink"
        assert all(p[0] > p[1] + 10 and p[0] > p[2] + 10 for p in samples), "Red edge lost its hue"
        assert after.getpixel((249, y)) == after.getpixel((252, y)), "Edge repair expanded the stroke"


edges = panel(True)
drawing = ImageDraw.Draw(edges)
for y, background in [(110, (145, 211, 240)), (190, (255, 255, 255))]:
    for x, amount in [(245, 1), (246, .75), (247, .5), (248, .25)]:
        color = tuple(round(bg * (1 - amount) + fg * amount) for bg, fg in zip(background, (205, 45, 45)))
        drawing.line((x, y - 3, x, y + 3), fill=color)
run("colored-antialiasing-on-paper-and-panel", edges, True, colored_edge_check, compressed=False)

run("dark-label-over-diagram-paper", panel(), True, contrast, overlay=(220, 288, (0, 0, 0)))
run("label-outside-rectangular-image-clip", panel(), True, contrast,
    overlay=(220, 288, (0, 0, 0)), clip_image=True)
# A white label on original dark raster ink would become light-on-light.
# Local overlap validation must continue to refuse that conversion.
conflict = panel()
ImageDraw.Draw(conflict).rectangle((270, 160, 365, 205), fill=(15, 15, 15))
gray_panel = panel()
ImageDraw.Draw(gray_panel).rectangle((270, 160, 365, 205), fill=(128, 128, 128))
negative_labels = [
    ("light-label-over-dark-raster-ink", document(conflict, overlay=(208, 151, (1, 1, 1)))),
    ("foreground-contact-cannot-cover-whole-label", document(conflict, overlay=(208, 151, (.26, .26, .26)))),
    ("dark-label-over-gray-raster-panel", document(gray_panel, overlay=(208, 151, (0, 0, 0)))),
    ("irregular-clip-cannot-prove-label-background", document(panel(), overlay=(220, 288, (0, 0, 0)), clip_image="curve")),
]
for name, source in negative_labels:
    for mode in ("--inspect", "--convert"):
        with tempfile.TemporaryFile(dir=root) as stream:
            stream.write(source)
            stream.seek(0)
            process = subprocess.run([str(engine), mode], stdin=stream, capture_output=True, timeout=120)
        assert process.returncode == 3 and not process.stdout, (name, mode, process.stderr, "unsafe label admitted")
    results.append(dict(name=name, passed=True, refused=True))
    print("PASS", name, "refused")

# Pale photograph-like controls contain broad smooth or textured regions,
# including a white surround. They must not become diagrams solely due to paper.
rng = random.Random(510)
for name, kind in [("pale-gradient", 0), ("white-backed-photo-texture", 1), ("white-backed-smooth-object", 2)]:
    image = Image.new("RGB", (640, 400), "white")
    for y in range(65, 335):
        for x in range(110, 530):
            if kind == 0:
                v = int(185 + 60 * (x - 110) / 420)
                color = (v, v, v)
            elif kind == 1:
                v = rng.randrange(170, 251)
                color = (v, min(255, v + 4), max(0, v - 5))
            else:
                d = min(1, ((x - 320) ** 2 / 210 ** 2 + (y - 200) ** 2 / 135 ** 2))
                v = int(80 + 175 * d)
                color = (v, v, v)
            image.putpixel((x, y), color)
    run(name, image, False)

green = Image.new("RGB", (400, 500), (243, 243, 243))
draw = ImageDraw.Draw(green)
for y in (30, 145, 260, 375):
    draw.rectangle((15, y, 210, y + 62), fill=(208, 232, 200), outline=(65, 125, 55), width=2)
    draw.text((25, y + 20), "Process label", font=font, fill=(20, 20, 20))
    draw.rectangle((285, y + 5, 360, y + 62), fill=(208, 232, 200), outline=(208, 232, 200), width=2)
run("colored-strokes-with-insufficient-output-contrast", green, False)

(root / "report.json").write_text(json.dumps(dict(engine=str(engine),
    engineSha256=hashlib.sha256(engine.read_bytes()).hexdigest().upper(), checks=results), indent=2), encoding="utf-8")
print(f"Passed {len(results)} diagram-image checks.")
