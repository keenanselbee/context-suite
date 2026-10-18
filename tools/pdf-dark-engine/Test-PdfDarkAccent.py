"""Measure rendered RGB accent and caption contrast in the native PDF-dark host.

Test-only dependencies: PyMuPDF and ReportLab. Use a fresh repository-local
scratch directory; the script writes its authored fixture and render there.
"""
import argparse
import colorsys
import hashlib
import io
import json
import pathlib
import struct
import subprocess
import tempfile

import pymupdf
from reportlab.pdfgen.canvas import Canvas

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


def luminance(rgb):
    channels = []
    for channel in rgb:
        value = channel / 255
        channels.append(value / 12.92 if value <= .04045 else ((value + .055) / 1.055) ** 2.4)
    return .2126 * channels[0] + .7152 * channels[1] + .0722 * channels[2]


def contrast(left, right):
    lighter, darker = sorted((luminance(left), luminance(right)), reverse=True)
    return (lighter + .05) / (darker + .05)


source = io.BytesIO()
canvas = Canvas(source, pagesize=(420, 300), pageCompression=1)
canvas.setFillColorRGB(1, 1, 1)
canvas.rect(0, 0, 420, 300, fill=1, stroke=0)
samples = {}
for name, x, color in (("red", 45, (1, 0, 0)),
                       ("green", 170, (0, 1, 0)),
                       ("blue", 295, (0, 0, 1))):
    canvas.setFillColorRGB(*color)
    canvas.rect(x, 165, 54, 32, fill=1, stroke=0)
    canvas.setStrokeColorRGB(*color)
    canvas.setLineWidth(9)
    canvas.line(x, 125, x + 54, 125)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.setFont("Helvetica", 15)
    canvas.drawString(x, 85, name.upper())
    samples[name] = {"fill": (x + 25, 300 - 180),
                     "stroke": (x + 25, 300 - 125),
                     "caption": (x, 300 - 96, x + 70, 300 - 80)}
canvas.save()
data = source.getvalue()
(ROOT / "rgb-accents-source.pdf").write_bytes(data)
with tempfile.TemporaryFile(dir=ROOT) as stdin:
    stdin.write(data)
    stdin.flush()
    stdin.seek(0)
    process = subprocess.run([str(ENGINE), "--convert"], stdin=stdin,
                             capture_output=True, timeout=30)
assert process.returncode == 0, process.stderr.decode(errors="replace")
magic, version, json_size, pdf_size = struct.unpack_from("<IIII", process.stdout)
assert (magic, version) == (0x31445043, 1)
assert len(process.stdout) == 16 + json_size + pdf_size
facts = json.loads(process.stdout[16:16 + json_size])
assert facts["pageCount"] == 1 and facts["changedPages"] == 1 and pdf_size > 0, facts
output = process.stdout[16 + json_size:]
(ROOT / "rgb-accents-dark.pdf").write_bytes(output)
original = pymupdf.open(stream=data, filetype="pdf")
converted = pymupdf.open(stream=output, filetype="pdf")
assert len(original) == len(converted) == 1
assert original[0].rect == converted[0].rect
assert original[0].get_text() == converted[0].get_text()
pixmap = converted[0].get_pixmap(alpha=False)
pixmap.save(ROOT / "rgb-accents-dark.png")


def pixel(x, y):
    index = (y * pixmap.width + x) * pixmap.n
    return tuple(pixmap.samples[index:index + 3])


paper = pixel(20, 20)
failures = []
hues = {}
for name, locations in samples.items():
    for kind in ("fill", "stroke"):
        color = pixel(*locations[kind])
        ratio = contrast(color, paper)
        hue, saturation, _ = colorsys.rgb_to_hsv(*(channel / 255 for channel in color))
        if kind == "stroke":
            hues[name] = hue
        print(f"{name} {kind}: rgb={color}, paper={paper}, contrast={ratio:.2f}:1")
        if ratio < 3:
            failures.append(f"{name} {kind} contrast {ratio:.2f}:1 < 3:1")
        if saturation < .15:
            failures.append(f"{name} {kind} lost color saturation")
    left, top, right, bottom = locations["caption"]
    strongest = max(contrast(pixel(x, y), paper)
                    for y in range(top, bottom) for x in range(left, right))
    print(f"{name} caption strongest contrast={strongest:.2f}:1")
    if strongest < 4.5:
        failures.append(f"{name} caption contrast {strongest:.2f}:1 < 4.5:1")

for one, other in (("red", "green"), ("green", "blue"), ("blue", "red")):
    distance = abs(hues[one] - hues[other])
    if min(distance, 1 - distance) < .08:
        failures.append(f"{one} and {other} stroke hues merged")

if failures:
    for failure in failures:
        print("FAIL", failure)
    raise SystemExit(1)
print("PASS rendered RGB accents and captions")
