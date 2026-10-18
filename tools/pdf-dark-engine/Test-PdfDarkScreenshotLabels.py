"""Protect small screenshot labels on pale UI surfaces during PDF darkening.

The glyph masks are authored with the raster fixture, not recovered by OCR.
Test-only dependencies: Pillow, ReportLab, PyMuPDF. Outputs stay in scratch.
"""

import argparse
import hashlib
import io
import json
import pathlib
import statistics
import struct
import subprocess
import tempfile

import pymupdf
from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen.canvas import Canvas


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)
engine = pathlib.Path(args.engine).resolve(strict=True)


def font(size):
    return ImageFont.load_default(size=size)


def screenshot(kind):
    image = Image.new("RGB", (1280, 720), "white")
    draw = ImageDraw.Draw(image)
    mask = Image.new("L", image.size, 0)
    glyph = ImageDraw.Draw(mask)
    dark = (45, 45, 45)
    pale_blue = (225, 243, 255)
    draw.rectangle((76, 56, 1204, 668), outline=(232, 234, 237), width=2)
    for row, title in enumerate(("Project overview", "Build checks", "Review status",
                                 "Deployment history", "Team activity", "Open tasks")):
        y = 170 + row * 77
        draw.text((105, y), title, font=font(29), fill=dark)
        draw.text((410, y + 5), "changes verified and ready for review",
                  font=font(22), fill=(58, 58, 58))
        draw.line((105, y + 42, 1170, y + 42), fill=(239, 240, 242), width=2)
    if kind == "header":
        draw.rectangle((90, 93, 1190, 124), fill=pale_blue)
        # Small colored UI accents make this a mixed screenshot rather than
        # the near-neutral line-art class; they do not touch the label mask.
        draw.rounded_rectangle((103, 615, 273, 653), radius=9,
                               fill=(51, 146, 75))
        draw.rounded_rectangle((307, 615, 477, 653), radius=9,
                               fill=(237, 113, 33))
        draw.line((680, 639, 1060, 639), fill=(35, 119, 205), width=14)
        labels = [(112, 97, "Maya opened this workspace", 22),
                  (805, 98, "Edited just now", 21)]
        background_points = [(650, 107), (1110, 108)]
    else:
        draw.rectangle((90, 93, 1190, 215), fill=(247, 248, 250))
        draw.text((114, 129), "Code    Issues    Pull requests    Actions",
                  font=font(25), fill=(63, 63, 63))
        labels = []
        background_points = []
        for row, word in enumerate(("queued", "review", "ready")):
            x, y = 1007, 276 + row * 90
            draw.rounded_rectangle((x, y, x + 145, y + 35), radius=6, fill=pale_blue)
            labels.append((x + 18, y + 6, word, 20))
            background_points.append((x + 128, y + 17))
    for x, y, word, size in labels:
        chosen = font(size)
        draw.text((x, y), word, font=chosen, fill=dark)
        glyph.text((x, y), word, font=chosen, fill=255)
    return image, mask, background_points


def panels():
    image = Image.new("RGB", (640, 400), "white")
    draw = ImageDraw.Draw(image)
    positions = ((35, 40, "Input"), (360, 40, "Parse"),
                 (35, 245, "Check"), (360, 245, "Publish"))
    for x, y, title in positions:
        draw.rectangle((x, y, x + 235, y + 95), fill=(145, 211, 240),
                       outline=(45, 45, 45), width=2)
        draw.line((x, y + 30, x + 235, y + 30), fill=(45, 45, 45), width=2)
        draw.text((x + 9, y + 7), title, font=font(17), fill=(20, 20, 20))
        draw.text((x + 9, y + 45), "+ result: Checked", font=font(17),
                  fill=(20, 20, 20))
    draw.line([(150, 136), (150, 205), (480, 205), (480, 244)],
              fill=(35, 35, 35), width=2)
    draw.line([(480, 136), (480, 165)], fill=(35, 35, 35), width=2)
    draw.text((220, 173), "Validated", font=font(17), fill=(205, 45, 45))
    return image


def make_pdf(image):
    jpeg = io.BytesIO()
    image.save(jpeg, format="JPEG", quality=86)
    output = io.BytesIO()
    canvas = Canvas(output, pagesize=(480, 345), pageCompression=1)
    canvas.drawString(18, 322, "Raster UI label regression")
    canvas.drawImage(ImageReader(io.BytesIO(jpeg.getvalue())), 20, 18,
                     width=440, height=290)
    canvas.save()
    return output.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as stream:
        stream.write(source)
        stream.seek(0)
        result = subprocess.run([str(engine), mode], stdin=stream,
                                capture_output=True, timeout=120)
    if result.returncode:
        raise AssertionError((mode, result.returncode,
                              result.stderr.decode(errors="replace")))
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_size + pdf_size
    return (json.loads(result.stdout[16:16 + metadata_size]),
            result.stdout[16 + metadata_size:])


def raster(source):
    pdf = pymupdf.open(stream=source, filetype="pdf")
    xref = pdf[0].get_images(full=True)[0][0]
    pix = pymupdf.Pixmap(pdf, xref)
    assert pix.n == 3
    return Image.frombytes("RGB", (pix.width, pix.height), pix.samples)


def luminance(color):
    values = [v / 255 for v in color]
    linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4
              for v in values]
    return sum(x * y for x, y in zip(linear, (.2126, .7152, .0722)))


def contrast(first, second):
    low, high = sorted((luminance(first), luminance(second)))
    return (high + .05) / (low + .05)


def run(name, image, mask=None, backgrounds=None):
    source = make_pdf(image)
    facts, converted = invoke(source, "--convert")
    inspected, empty = invoke(source, "--inspect")
    assert facts == inspected and not empty and converted
    before, after = raster(source), raster(converted)
    assert before.size == after.size == image.size
    original_pdf = pymupdf.open(stream=source, filetype="pdf")
    converted_pdf = pymupdf.open(stream=converted, filetype="pdf")
    assert original_pdf[0].get_text() == converted_pdf[0].get_text()
    original_pdf[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(
        scratch / f"{name}-source.png")
    converted_pdf[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(
        scratch / f"{name}-converted.png")
    before.save(scratch / f"{name}-source-image.png")
    after.save(scratch / f"{name}-converted-image.png")
    (scratch / f"{name}-source.pdf").write_bytes(source)
    (scratch / f"{name}-converted.pdf").write_bytes(converted)
    repeated, duplicate = invoke(converted, "--convert")
    assert repeated["changedPages"] == 0 and not duplicate

    if mask is not None:
        mask.save(scratch / f"{name}-glyph-mask.png")
        points = [(x, y) for y in range(mask.height) for x in range(mask.width)
                  if mask.getpixel((x, y)) >= 250 and
                  max(before.getpixel((x, y))) <= 115]
        assert len(points) >= 150, (name, "missing known glyph cores")
        assert backgrounds and all(mask.getpixel(point) == 0 for point in backgrounds)
        # Each glyph is compared with its own pale surface. A retained source
        # JPEG is acceptable; a darkened backdrop with light glyphs also passes.
        source_backdrops = [before.getpixel(point) for point in backgrounds]
        output_backdrops = [after.getpixel(point) for point in backgrounds]
        src_bg = source_backdrops[0]
        out_bg = output_backdrops[0]
        source_ratios = [contrast(before.getpixel(point), src_bg) for point in points]
        output_ratios = [contrast(after.getpixel(point), out_bg) for point in points]
        readable = sum(value >= 4.5 for value in output_ratios) / len(points)
        passed = (min(source_ratios) >= 4.5 and readable >= .90)
        metrics = {"glyphCorePixels": len(points),
                   "sourceBackdrop": src_bg, "convertedBackdrop": out_bg,
                   "sourceMedianContrast": round(statistics.median(source_ratios), 3),
                   "convertedMedianContrast": round(statistics.median(output_ratios), 3),
                   "readableShareAt4_5to1": round(readable, 4)}
    else:
        assert before.tobytes() != after.tobytes(), (name, "colored panel retained")
        paper = after.getpixel((5, 5))
        panel = after.getpixel((220, 115))
        border = after.getpixel((35, 100))
        label_points = [(x, y) for y in range(80, 118) for x in range(48, 255)
                        if max(before.getpixel((x, y))) < 70]
        assert len(label_points) >= 200
        label_ratios = [contrast(after.getpixel(point), panel) for point in label_points]
        readable = sum(value >= 3 for value in label_ratios) / len(label_ratios)
        passed = max(paper) < 60 and max(panel) < 150 and readable >= .90
        metrics = {"paper": paper, "panel": panel, "border": border,
                   "labelCorePixels": len(label_points),
                   "readableLabelShareAt3to1": round(readable, 4)}
    print(("PASS" if passed else "FAIL"), name, metrics)
    return {"name": name, "passed": passed, "metrics": metrics, "facts": facts,
            "sourceSha256": hashlib.sha256(source).hexdigest(),
            "convertedSha256": hashlib.sha256(converted).hexdigest()}


header, header_mask, header_bg = screenshot("header")
badges, badge_mask, badge_bg = screenshot("badges")
results = [run("pale-header-small-labels", header, header_mask, header_bg),
           run("pale-blue-compact-badges", badges, badge_mask, badge_bg),
           run("colored-panels-control", panels())]
report = {"engine": str(engine),
          "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
          "results": results}
(scratch / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
if not all(result["passed"] for result in results):
    raise AssertionError("Raster UI label contrast failed; see scratch report.json")
print(f"Passed {len(results)} screenshot-label checks.")
