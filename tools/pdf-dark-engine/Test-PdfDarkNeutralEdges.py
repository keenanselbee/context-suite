"""Regression for neutral JPEG text edges beside colored diagram paint.

The fixture is independently drawn. Pillow, ReportLab and PyMuPDF are test-only
dependencies; all PDFs and renders stay in a fresh repository-local scratch.
"""

import argparse
import hashlib
import io
import json
import pathlib
import random
import struct
import subprocess
import tempfile

import pymupdf
from PIL import Image, ImageDraw, ImageFont
from pypdf import PdfReader, PdfWriter
from pypdf.generic import DecodedStreamObject, NameObject, NumberObject
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
font = ImageFont.load_default(size=37)


def neutral_diagram(masked=False):
    image = Image.new("RGB", (900, 560), "white")
    draw = ImageDraw.Draw(image)
    positions = [(35, 35), (345, 195), (625, 355)]
    for index, (x, y) in enumerate(positions):
        draw.rounded_rectangle((x, y, x + 240, y + 115), radius=42,
                               outline=(0, 175, 222), width=7)
        ink = ((17, 24, 40), (38, 23, 18), (18, 18, 18))[index]
        draw.text((x + 37, y + 33), "VALIDATE", font=font, fill=ink)
    draw.line((275, 95, 345, 95, 345, 195), fill=(55, 55, 55), width=3)
    draw.line((585, 255, 625, 255, 625, 355), fill=(55, 55, 55), width=3)
    # This explicitly monotonic neutral ramp tests the same grayscale mapping
    # as antialiased text without depending on one font renderer's edge pixels.
    for value in range(256):
        draw.line((30 + value * 3, 528, 32 + value * 3, 528),
                  fill=(value, value, value), width=12)
        # Smooth, mildly chromatic antialiasing contamination. Neighboring
        # source samples differ by at most a few levels while crossing the
        # implementation's former neutral/color routing boundary.
        chroma = value * 40 // 255
        draw.line((30 + value * 3, 498, 32 + value * 3, 498),
                  fill=(125 - chroma // 2, 125, 125 + chroma // 2), width=12)
    # Independently generated mild color contamination models the channel
    # variation that JPEG introduces around nominally black raster letters.
    # It is restricted to drawn text; cyan borders remain genuine color.
    rng = random.Random(4107)
    pixels = image.load()
    for left, top, right, bottom in ((70, 70, 245, 125),
                                     (380, 230, 555, 285),
                                     (660, 390, 835, 445)):
        for y in range(top, bottom):
            for x in range(left, right):
                r, g, b = pixels[x, y]
                if max(r, g, b) > 225:
                    continue
                offset = rng.choice((-11, -7, 7, 11))
                pixels[x, y] = (max(0, min(255, r + offset)),
                                max(0, min(255, g - offset // 2)),
                                max(0, min(255, b - offset)))
    if not masked:
        return image
    rgba = image.convert("RGBA")
    alpha = Image.new("L", image.size, 255)
    ImageDraw.Draw(alpha).rectangle((0, 0, 0, 3), fill=0)
    rgba.putalpha(alpha)
    return rgba


def colored_panel():
    image = Image.new("RGB", (640, 400), "white")
    draw = ImageDraw.Draw(image)
    for x, y in ((35, 40), (360, 40), (35, 245), (360, 245)):
        draw.rectangle((x, y, x + 235, y + 95), fill=(145, 211, 240),
                       outline=(45, 45, 45), width=2)
        draw.text((x + 10, y + 14), "DATA", font=ImageFont.load_default(size=23),
                  fill=(25, 25, 25))
        draw.line((x, y + 30, x + 235, y + 30), fill=(45, 45, 45), width=2)
        draw.text((x + 10, y + 48), "+ balance: Integer",
                  font=ImageFont.load_default(size=17), fill=(25, 25, 25))
    draw.line((150, 136, 150, 205, 480, 205, 480, 245),
              fill=(35, 35, 35), width=3)
    draw.text((260, 185), "ACTION", font=ImageFont.load_default(size=26),
              fill=(205, 45, 45))
    return image


def colored_text():
    image = Image.new("RGB", (900, 560), "white")
    draw = ImageDraw.Draw(image)
    for y, label in ((50, "REQUEST"), (210, "REVIEW"), (370, "RELEASE")):
        draw.rectangle((35, y, 850, y + 110), outline=(0, 166, 228), width=7)
        draw.text((80, y + 23), label, font=font, fill=(0, 81, 119))
    draw.line((450, 160, 450, 210), fill=(50, 50, 50), width=3)
    draw.line((450, 320, 450, 370), fill=(50, 50, 50), width=3)
    return image


def shaded_control():
    image = Image.new("RGB", (640, 400), "white")
    pixels = image.load()
    for y in range(65, 335):
        for x in range(110, 530):
            level = int(185 + 60 * (x - 110) / 420)
            pixels[x, y] = (level, level, level)
    return image


def document(image):
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(500, 360), pageCompression=1)
    canvas.drawString(20, 341, "Raster text edge regression")
    encoded = io.BytesIO()
    image.convert("RGB").save(encoded, format="JPEG", quality=78)
    source = io.BytesIO(encoded.getvalue())
    # Pillow and the native qpdf/libjpeg path agree on this JPEG's channel
    # samples. MuPDF can choose a slightly different JPEG color conversion,
    # so use the encoded fixture's Pillow decode for pixel-edge assertions.
    source_pixels = Image.open(io.BytesIO(encoded.getvalue())).convert("RGB")
    canvas.drawImage(ImageReader(source), 20, 20, width=460, height=300,
                     mask="auto")
    canvas.save()
    if image.mode == "RGBA":
        # Attach a true binary PDF soft mask to the same JPEG RGB stream. The
        # mask sample bytes must survive recoloring exactly.
        reader = PdfReader(io.BytesIO(stream.getvalue()))
        writer = PdfWriter(clone_from=reader)
        xobjects = writer.pages[0]["/Resources"]["/XObject"]
        art = next(iter(xobjects.values())).get_object()
        mask = DecodedStreamObject()
        mask.set_data(image.getchannel("A").tobytes())
        mask.update({NameObject("/Type"): NameObject("/XObject"),
                     NameObject("/Subtype"): NameObject("/Image"),
                     NameObject("/ColorSpace"): NameObject("/DeviceGray"),
                     NameObject("/Width"): NumberObject(image.width),
                     NameObject("/Height"): NumberObject(image.height),
                     NameObject("/BitsPerComponent"): NumberObject(8)})
        art[NameObject("/SMask")] = writer._add_object(mask)
        result = io.BytesIO()
        writer.write(result)
        return result.getvalue(), source_pixels
    return stream.getvalue(), source_pixels


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as stream:
        stream.write(source)
        stream.seek(0)
        process = subprocess.run([str(engine), mode], stdin=stream,
                                 capture_output=True, timeout=120)
    assert process.returncode == 0, process.stderr.decode(errors="replace")
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_size + pdf_size
    return (json.loads(process.stdout[16:16 + metadata_size]),
            process.stdout[16 + metadata_size:])


def embedded(source):
    pdf = pymupdf.open(stream=source, filetype="pdf")
    xref, mask_xref = pdf[0].get_images(full=True)[0][:2]
    pixmap = pymupdf.Pixmap(pdf, xref)
    assert pixmap.n == 3
    image = Image.frombytes("RGB", (pixmap.width, pixmap.height), pixmap.samples)
    mask = bytes(pymupdf.Pixmap(pdf, mask_xref).samples) if mask_xref else b""
    return image, mask


def luma(pixel):
    return sum(pixel) / 3


def check_neutral(before, after):
    # A retained resource is a valid conservative outcome for an ambiguous
    # JPEG. Once recolored, its neutral source ramp and text edges must stay
    # warm-neutral and smoothly ordered despite neighboring cyan pixels.
    if before.tobytes() == after.tobytes():
        return {"retained": True}
    assert max(after.getpixel((8, 8))) < 60, "White diagram paper stayed bright"
    samples = []
    for x in range(31, 31 + 256 * 3, 3):
        source = before.getpixel((x, 528))
        target = after.getpixel((x, 528))
        if max(source) - min(source) <= 5:
            samples.append((luma(source), luma(target), target))
    assert len(samples) >= 175, "JPEG ramp lost neutral samples"
    samples.sort()
    violations = sum(samples[i + 1][1] > samples[i][1] + 16
                     for i in range(len(samples) - 1))
    assert violations <= 2, ("Neutral grayscale ramp is discontinuous", violations)
    assert all(max(target) - min(target) <= 32 for _, _, target in samples), \
        "Neutral grayscale ramp acquired a colored edge"
    soft_edges = []
    for x in range(31, 31 + 256 * 3 - 1):
        first, second = before.getpixel((x, 498)), before.getpixel((x + 1, 498))
        if max(abs(a - b) for a, b in zip(first, second)) > 5:
            continue
        if max(first) - min(first) > 45 or max(second) - min(second) > 45:
            continue
        mapped, neighbor = after.getpixel((x, 498)), after.getpixel((x + 1, 498))
        soft_edges.append(max(abs(a - b) for a, b in zip(mapped, neighbor)))
    assert len(soft_edges) >= 300, "No smooth near-neutral edge samples found"
    abrupt = sum(delta > 60 for delta in soft_edges)
    assert abrupt == 0, ("Near-neutral JPEG edge has a routing jump", abrupt)
    # Compare only source pixels within the authored labels, excluding cyan
    # borders. Adjacent grayscale edge samples should map to a neutral, nearly
    # continuous output. The source font's edge location is measured, not OCR.
    areas = [(70, 70, 245, 125), (380, 230, 555, 285), (660, 390, 835, 445)]
    pairs = []
    for left, top, right, bottom in areas:
        for y in range(top, bottom):
            for x in range(left, right - 1):
                a, b = before.getpixel((x, y)), before.getpixel((x + 1, y))
                if max(a) - min(a) > 35 or max(b) - min(b) > 35:
                    continue
                delta = abs(luma(a) - luma(b))
                if 10 <= delta <= 90 and min(luma(a), luma(b)) < 220:
                    u, v = after.getpixel((x, y)), after.getpixel((x + 1, y))
                    pairs.append((a, b, u, v))
    assert len(pairs) >= 100, "No independently drawn neutral text edges found"
    neutral = sum(max(u) - min(u) <= 35 and max(v) - min(v) <= 35
                  for _, _, u, v in pairs) / len(pairs)
    reversed_edges = sum((luma(a) - luma(b)) * (luma(u) - luma(v)) > 80
                         for a, b, u, v in pairs) / len(pairs)
    assert neutral >= .95, ("Neutral text edges changed hue", neutral)
    assert reversed_edges <= .02, ("Neutral text edges reversed", reversed_edges)
    contaminated = []
    for left, top, right, bottom in areas:
        for y in range(top, bottom):
            for x in range(left, right):
                source, target = before.getpixel((x, y)), after.getpixel((x, y))
                gray = luma(source)
                if 20 <= gray <= 235 and 8 <= max(source) - min(source) <= 35:
                    expected = tuple(paper + (ink - paper) * (1 - gray / 255)
                                     for paper, ink in ((18, 244), (19, 235), (20, 216)))
                    contaminated.append(max(abs(actual - wanted)
                                            for actual, wanted in zip(target, expected)))
    assert len(contaminated) >= 1000, ("No near-neutral JPEG text noise found", len(contaminated))
    deviated = sum(error > 35 for error in contaminated) / len(contaminated)
    assert deviated <= .005, ("Near-neutral text took a colored route", deviated)
    return {"retained": False, "rampSamples": len(samples),
            "rampViolations": violations, "softEdgePairs": len(soft_edges),
            "abruptSoftEdges": abrupt, "textEdgePairs": len(pairs),
            "neutralEdgeShare": round(neutral, 4),
            "reversedEdgeShare": round(reversed_edges, 4),
            "nearNeutralTextPixels": len(contaminated),
            "deviatedTextShare": round(deviated, 4)}


def inspect(name, image, kind):
    source, source_pixels = document(image)
    facts, converted = invoke(source, "--convert")
    inspected, empty = invoke(source, "--inspect")
    assert facts == inspected and not empty and converted
    extracted, original_mask = embedded(source)
    after, resulting_mask = embedded(converted)
    assert extracted.size == after.size == source_pixels.size == image.size
    assert original_mask == resulting_mask, (name, "soft mask changed")
    before = source_pixels
    a = pymupdf.open(stream=source, filetype="pdf")
    b = pymupdf.open(stream=converted, filetype="pdf")
    assert a[0].get_text() == b[0].get_text(), (name, "PDF text changed")
    repeated, duplicate = invoke(converted, "--convert")
    assert repeated["changedPages"] == 0 and not duplicate, (name, "repeat changed output")
    before.save(scratch / f"{name}-source-image.png")
    after.save(scratch / f"{name}-converted-image.png")
    b[0].get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False).save(
        scratch / f"{name}-converted-page.png")
    (scratch / f"{name}-source.pdf").write_bytes(source)
    (scratch / f"{name}-dark.pdf").write_bytes(converted)
    changed = before.tobytes() != after.tobytes()
    if kind == "neutral":
        metrics = check_neutral(before, after)
    elif kind == "panel":
        assert changed, (name, "known colored panel was retained")
        paper = after.getpixel((5, 5))
        fill = after.getpixel((250, 115))
        assert max(paper) < 60 and fill[2] > fill[0] + 8 and max(fill) < 150
        metrics = {"bluePanel": fill}
    elif kind == "color":
        if changed:
            colored = [result for original, result in zip(before.get_flattened_data(),
                                                        after.get_flattened_data())
                       if original[2] > original[0] + 65 and
                       original[1] > original[0] + 40 and max(original) < 165]
            assert len(colored) >= 200, (name, "missing colored ink cores")
            retained_hue = sum(pixel[2] > pixel[0] + 25 for pixel in colored) / len(colored)
            assert retained_hue >= .9, (name, "colored ink lost its hue", retained_hue)
            metrics = {"coloredCorePixels": len(colored),
                       "retainedHueShare": round(retained_hue, 4)}
        else:
            metrics = {"retained": True}
    else:
        assert not changed, (name, "smooth shaded control was recolored")
        metrics = {"retained": True}
    print("PASS", name, metrics)
    return {"name": name, "kind": kind, "passed": True, "changedImage": changed,
            "metrics": metrics, "facts": facts,
            "sourceSha256": hashlib.sha256(source).hexdigest(),
            "convertedSha256": hashlib.sha256(converted).hexdigest()}


results = []
for name, image, kind in (
    ("black-text-cyan-borders-jpeg", neutral_diagram(), "neutral"),
    ("black-text-cyan-borders-binary-mask", neutral_diagram(True), "neutral"),
    ("black-text-blue-panel-jpeg", colored_panel(), "panel"),
    ("blue-text-white-paper-jpeg", colored_text(), "color"),
    ("smooth-neutral-shading-jpeg", shaded_control(), "preserve"),
):
    try:
        results.append(inspect(name, image, kind))
    except AssertionError as error:
        print("FAIL", name, str(error))
        results.append({"name": name, "kind": kind, "passed": False,
                        "error": str(error)})
(scratch / "report.json").write_text(json.dumps({"engine": str(engine),
    "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
    "results": results}, indent=2), encoding="utf-8")
if any(result.get("passed") is False for result in results):
    raise AssertionError("Neutral diagram text regressed; see scratch report.json")
print(f"Passed {len(results)} neutral-edge checks.")
