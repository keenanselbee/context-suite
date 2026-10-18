"""Regression for sparse colored raster ink and genuine colored diagram panels.

Test-only dependencies: Pillow, ReportLab, PyMuPDF. All output is scratch-local.
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
font = ImageFont.load_default(size=67)
small_font = ImageFont.load_default(size=31)


def letter_image(with_symbols):
    image = Image.new("RGB", (1024, 768), "white")
    draw = ImageDraw.Draw(image)
    blue = (0, 81, 119)
    draw.rectangle((72, 103, 952, 213), outline=blue, width=7)
    draw.text((105, 117), "CONTINUOUS", font=font, fill=blue)
    draw.rectangle((72, 335, 952, 445), outline=(0, 166, 228), width=7)
    draw.text((105, 350), "INTEGRATION", font=font, fill=blue)
    draw.line((512, 218, 512, 331), fill=(45, 45, 45), width=4)
    if with_symbols:
        draw.ellipse((202, 574, 262, 634), outline=blue, width=8)
        draw.line((280, 604, 790, 604), fill=blue, width=8)
        draw.polygon([(790, 580), (826, 604), (790, 628)], fill=blue)
    return image


def panel_image():
    image = Image.new("RGB", (640, 400), "white")
    draw = ImageDraw.Draw(image)
    label_font = ImageFont.load_default(size=17)
    for x, y, fill, title in (
        (35, 40, (145, 211, 240), "Build"),
        (360, 40, (145, 211, 240), "Test"),
        (35, 245, (145, 211, 240), "Ship"),
        (360, 245, (145, 211, 240), "Review"),
    ):
        draw.rectangle((x, y, x + 235, y + 95), fill=fill,
                       outline=(45, 45, 45), width=2)
        draw.line((x, y + 30, x + 235, y + 30), fill=(45, 45, 45), width=2)
        draw.text((x + 12, y + 6), title, font=label_font, fill=(20, 20, 20))
        draw.text((x + 12, y + 48), "+ pipeline: Validated", font=label_font,
                  fill=(20, 20, 20))
    draw.line([(150, 136), (150, 205), (480, 205), (480, 244)],
              fill=(30, 30, 30), width=2)
    draw.line([(480, 136), (480, 165)], fill=(30, 30, 30), width=2)
    draw.text((220, 173), "Dependency", font=label_font, fill=(205, 45, 45))
    return image


def document(image):
    encoded = io.BytesIO()
    image.save(encoded, format="JPEG", quality=86)
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(480, 370), pageCompression=1)
    canvas.drawString(18, 346, "Raster image classification regression")
    canvas.drawImage(ImageReader(io.BytesIO(encoded.getvalue())), 20, 18,
                     width=440, height=320)
    canvas.save()
    return stream.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as stream:
        stream.write(source)
        stream.seek(0)
        process = subprocess.run([str(engine), mode], stdin=stream,
                                 capture_output=True, timeout=120)
    if process.returncode:
        raise AssertionError((mode, process.returncode,
                              process.stderr.decode(errors="replace")))
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_size + pdf_size
    return (json.loads(process.stdout[16:16 + metadata_size]),
            process.stdout[16 + metadata_size:])


def embedded(source):
    pdf = pymupdf.open(stream=source, filetype="pdf")
    xref = pdf[0].get_images(full=True)[0][0]
    pixmap = pymupdf.Pixmap(pdf, xref)
    assert pixmap.n == 3
    return Image.frombytes("RGB", (pixmap.width, pixmap.height), pixmap.samples)


def luminance(rgb):
    linear = []
    for value in rgb:
        encoded = value / 255
        linear.append(encoded / 12.92 if encoded <= .04045 else
                      ((encoded + .055) / 1.055) ** 2.4)
    return sum(a * b for a, b in zip(linear, (.2126, .7152, .0722)))


def ratio(first, second):
    first, second = sorted((first, second))
    return (second + .05) / (first + .05)


def inspect(name, image, expected_kind):
    source = document(image)
    converted_facts, converted = invoke(source, "--convert")
    inspected_facts, empty = invoke(source, "--inspect")
    assert converted_facts == inspected_facts and not empty and converted
    before, after = embedded(source), embedded(converted)
    assert before.size == after.size == image.size
    source_pdf = pymupdf.open(stream=source, filetype="pdf")
    converted_pdf = pymupdf.open(stream=converted, filetype="pdf")
    assert source_pdf[0].get_text() == converted_pdf[0].get_text()
    source_pdf[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(
        scratch / f"{name}-source.png")
    converted_pdf[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(
        scratch / f"{name}-converted.png")
    before.save(scratch / f"{name}-source-image.png")
    after.save(scratch / f"{name}-converted-image.png")
    (scratch / f"{name}-source.pdf").write_bytes(source)
    (scratch / f"{name}-converted.pdf").write_bytes(converted)
    repeated, duplicate = invoke(converted, "--convert")
    assert repeated["changedPages"] == 0 and not duplicate

    original = before.tobytes()
    output = after.tobytes()
    # Readable original-image retention is a valid conservative fallback for
    # ambiguous ink. The genuine panel control must still recolor below.
    if expected_kind == "panel":
        assert original != output, (name, "genuine panel unexpectedly retained")
    if expected_kind == "ink":
        # Measure only solid, encoded blue letter/symbol cores. JPEG ringing and
        # antialiasing cannot make the contrast target pass accidentally.
        eligible = []
        for y in range(before.height):
            for x in range(before.width):
                r, g, b = before.getpixel((x, y))
                if r <= 18 and 65 <= g <= 100 and 100 <= b <= 142:
                    eligible.append((x, y))
        assert len(eligible) >= 1000, (name, "missing colored ink cores")
        background = luminance(after.getpixel((30, 30)))
        contrasts = [ratio(background, luminance(after.getpixel(pos)))
                     for pos in eligible]
        readable = sum(value >= 3.0 for value in contrasts) / len(contrasts)
        passed = readable >= .90
        metrics = {"solidBlueInkPixels": len(eligible),
                   "readableShareAt3to1": round(readable, 4),
                   "medianContrast": round(sorted(contrasts)[len(contrasts) // 2], 3),
                   "minimumRequiredShare": .90}
    else:
        blue_panel = after.getpixel((220, 115))
        black_label = after.getpixel((35, 100))
        paper = after.getpixel((5, 5))
        contrast = ratio(luminance(blue_panel), luminance(black_label))
        label_cores = [ratio(luminance(blue_panel), luminance(after.getpixel((x, y))))
                       for y in range(80, 118) for x in range(48, 255)
                       if max(before.getpixel((x, y))) < 70]
        assert len(label_cores) >= 200, (name, "missing panel label cores")
        readable_labels = sum(value >= 3.0 for value in label_cores) / len(label_cores)
        passed = (max(paper) < 60 and max(blue_panel) < 150 and
                  contrast >= 3.0 and readable_labels >= .90)
        metrics = {"paper": paper, "bluePanel": blue_panel,
                   "labelSample": black_label,
                   "labelPanelContrast": round(contrast, 3),
                   "labelCorePixels": len(label_cores),
                   "readableLabelShareAt3to1": round(readable_labels, 4)}
    print(("PASS" if passed else "FAIL"), name, metrics)
    return {"name": name, "expected": expected_kind, "passed": passed,
            "retainedImageSamples": original == output,
            "metrics": metrics, "facts": converted_facts,
            "sourceSha256": hashlib.sha256(source).hexdigest(),
            "convertedSha256": hashlib.sha256(converted).hexdigest()}


results = [inspect("sparse-colored-letters", letter_image(False), "ink"),
           inspect("colored-letters-and-symbols", letter_image(True), "ink"),
           inspect("colored-panels-with-labels", panel_image(), "panel")]
report = {"engine": str(engine),
          "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
          "results": results}
(scratch / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
if not all(item["passed"] for item in results):
    raise AssertionError("Colored raster ink lost contrast; see scratch report.json")
print(f"Passed {len(results)} colored-image checks.")
