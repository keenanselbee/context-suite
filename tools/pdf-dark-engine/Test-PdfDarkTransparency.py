"""Check preserved masked panels and recolored sparse annotations independently.

Fixtures are authored here; no customer/reference images are redistributed.
Test-only dependencies: PyMuPDF, Pillow, pypdf and ReportLab.
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


def fixture(image, underlay=False):
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(420, 300), pageCompression=1)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 420, 300, fill=1, stroke=0)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.setFont("Helvetica", 18)
    canvas.drawString(20, 270, "Transparent diagram review")
    if underlay:
        canvas.setStrokeColorRGB(0, 0, 0)
        canvas.setLineWidth(2)
        canvas.line(190, 50, 190, 210)
    canvas.drawImage(ImageReader(image), 30, 60, width=360, height=180, mask="auto")
    canvas.save()
    return stream.getvalue()


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=root) as stream:
        stream.write(data)
        stream.flush()
        stream.seek(0)
        process = subprocess.run([str(engine), mode], stdin=stream,
                                 capture_output=True, timeout=30)
    if process.returncode:
        assert not process.stdout
        return process.returncode, process.stderr.decode(errors="replace"), b""
    magic, version, length, output_length = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + length + output_length
    return 0, json.loads(process.stdout[16:16 + length]), process.stdout[16 + length:]


def image_data(data):
    page = PdfReader(io.BytesIO(data)).pages[0]
    image = next(obj.get_object() for obj in page["/Resources"]["/XObject"].values()
                 if obj.get_object().get("/Subtype") == "/Image")
    return image.get_data(), image["/SMask"].get_object().get_data()


def positive(name, image, preserve, underlay=False):
    source = fixture(image, underlay)
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output, (name, code, facts)
    inspect_code, inspection, empty = invoke(source, "--inspect")
    assert inspect_code == 0 and inspection == facts and not empty
    old_pixels, old_mask = image_data(source)
    new_pixels, new_mask = image_data(output)
    assert old_mask == new_mask, (name, "mask changed")
    assert (old_pixels == new_pixels) == preserve, (name, "pixel policy")
    repeat_code, repeated, duplicate = invoke(output, "--convert")
    assert repeat_code == 0 and repeated["changedPages"] == 0 and not duplicate, (name, repeated)
    original, converted = pymupdf.open(stream=source), pymupdf.open(stream=output)
    assert original[0].get_text() == converted[0].get_text()
    for scale in (1, 4):
        pixmap = converted[0].get_pixmap(matrix=pymupdf.Matrix(scale, scale), alpha=False)
        pixmap.save(root / f"{name}-{scale}x.png")
        if underlay:
            pixels = Image.frombytes("RGB", (pixmap.width, pixmap.height), pixmap.samples)
            # The central hole in the fixture must still expose its underlying line.
            center = pixels.getpixel((190 * scale, 150 * scale))
            beside = pixels.getpixel((195 * scale, 150 * scale))
            assert sum(center) > sum(beside) + 300, (name, "underlying mark disappeared")
    (root / f"{name}-source.pdf").write_bytes(source)
    (root / f"{name}-dark.pdf").write_bytes(output)
    results.append(dict(name=name, passed=True, facts=facts))
    print("PASS", name)


def refusal(name, image):
    source = fixture(image)
    for mode in ("--inspect", "--convert"):
        code, reason, output = invoke(source, mode)
        assert code == 3 and not output, (name, mode, code, reason)
    results.append(dict(name=name, passed=True, refused=True))
    print("PASS", name, "refused")


panel = Image.new("RGBA", (360, 180), (255, 255, 225, 254))
draw = ImageDraw.Draw(panel)
draw.rectangle((0, 0, 359, 4), fill=(0, 0, 0, 0))
draw.rectangle((30, 30, 150, 130), fill=(190, 225, 250, 254), outline=(20, 20, 20, 254), width=2)
draw.text((45, 65), "Panel label", fill=(0, 0, 0, 254))
draw.text((210, 75), "Keep colours", fill=(0, 0, 0, 254))
positive("self-backed-colour-panel", panel, preserve=True)

binary = panel.copy()
binary.putalpha(binary.getchannel("A").point(lambda value: 255 if value else 0))
positive("binary-masked-colour-panel", binary, preserve=True)

annotation = Image.new("RGBA", (360, 180), (0, 0, 0, 0))
draw = ImageDraw.Draw(annotation)
draw.line((15, 35, 120, 35), fill=(15, 50, 110, 255), width=2)
draw.line((15, 100, 120, 100), fill=(230, 165, 40, 220), width=2)
draw.text((220, 40), "Name", fill=(220, 155, 35, 255))
draw.text((220, 105), "Value", fill=(0, 0, 0, 255))
positive("sparse-colour-annotation", annotation, preserve=False, underlay=True)

gradient = Image.new("RGBA", (180, 120))
for y in range(120):
    for x in range(180):
        gradient.putpixel((x, y), (x * 255 // 179, y * 255 // 119, 160, 120))
refusal("transparent-photographic-gradient", gradient)

dark_patch = Image.new("RGBA", (180, 120), (15, 40, 90, 220))
ImageDraw.Draw(dark_patch).text((20, 40), "Ambiguous", fill=(10, 10, 10, 220))
refusal("dark-translucent-patch", dark_patch)

wide_annotation = Image.new("RGBA", (360, 180), (0, 0, 0, 0))
ImageDraw.Draw(wide_annotation).rectangle((20, 30, 65, 75), fill=(180, 80, 70, 160))
refusal("sparse-wide-translucent-object", wide_annotation)

hole = Image.new("RGBA", (100, 100), (255, 255, 255, 255))
draw = ImageDraw.Draw(hole)
draw.rectangle((10, 10, 34, 34), fill=(20, 90, 45, 255))
draw.rectangle((45, 45, 54, 54), fill=(255, 255, 255, 0))
refusal("new-internal-dark-hole", hole)

# Move the previously safe underlying line onto a visible annotation stroke.
overlap = annotation.copy()
ImageDraw.Draw(overlap).line((155, 90, 175, 90), fill=(20, 50, 100, 255), width=2)
overlap_source = fixture(overlap, underlay=True)
for mode in ("--inspect", "--convert"):
    code, reason, output = invoke(overlap_source, mode)
    assert code == 3 and not output, ("overlapping-annotation", mode, code, reason)
results.append(dict(name="overlapping-annotation", passed=True, refused=True))
print("PASS overlapping-annotation refused")

(root / "report.json").write_text(json.dumps(dict(
    engineSha256=hashlib.sha256(engine.read_bytes()).hexdigest().upper(),
    checks=results, passed=True), indent=2), encoding="utf-8")
