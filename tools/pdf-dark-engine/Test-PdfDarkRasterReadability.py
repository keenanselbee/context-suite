"""Guard against invented JPEG edges and lost lettering in raster PDFs.

The images and glyph masks are authored here, independent of any lecture PDF.
Test-only dependencies: NumPy, Pillow, ReportLab, PyMuPDF. Artifacts stay in
fresh repository-local scratch. --probe records expected baseline failures.
"""

import argparse
import hashlib
import io
import json
import pathlib
import struct
import subprocess
import tempfile

import numpy as np
import pymupdf
from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen.canvas import Canvas


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
parser.add_argument("--probe", action="store_true",
                    help="record current behavior without failing on readability assertions")
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)
engine = pathlib.Path(args.engine).resolve(strict=True)


def font(size):
    return ImageFont.load_default(size=size)


def dashboard():
    image = Image.new("RGB", (1200, 660), (255, 255, 255))
    draw = ImageDraw.Draw(image)
    draw.rectangle((42, 40, 1158, 622), outline=(222, 225, 229), width=3)
    draw.rectangle((43, 41, 1157, 102), fill=(246, 248, 250))
    draw.text((72, 56), "Repository / Actions", font=font(27), fill=(35, 41, 48))
    for row, title in enumerate(("Build and test", "Static checks", "Release review")):
        y = 160 + row * 135
        draw.text((76, y), title, font=font(28), fill=(35, 41, 48))
        draw.text((76, y + 43), "Run passed after the latest commit",
                  font=font(23), fill=(69, 75, 82))
        draw.line((70, y + 96, 1128, y + 96), fill=(229, 231, 234), width=3)
    draw.rectangle((86, 556, 303, 593), fill=(41, 143, 82))
    draw.text((99, 562), "View checks", font=font(21), fill="white")
    return image


def blue_badges():
    image = dashboard()
    draw = ImageDraw.Draw(image)
    glyphs = {}
    for y, label in ((153, "multiply"), (288, "release"), (423, "review")):
        draw.rounded_rectangle((852, y, 1113, y + 55), radius=12,
                               fill=(225, 243, 253))
        draw.text((874, y + 8), label, font=font(39), fill=(50, 91, 166))
        glyph = Image.new("L", image.size, 0)
        ImageDraw.Draw(glyph).text((874, y + 8), label, font=font(39), fill=255)
        glyphs[label] = glyph
    view = Image.new("L", image.size, 0)
    ImageDraw.Draw(view).text((99, 562), "View checks", font=font(21), fill=255)
    glyphs["view-checks"] = view
    return image, glyphs


def green_lettering():
    image = Image.new("RGB", (1280, 900), "white")
    draw = ImageDraw.Draw(image)
    glyph = Image.new("L", image.size, 0)
    mask = ImageDraw.Draw(glyph)
    draw.rounded_rectangle((34, 40, 1246, 852), radius=18,
                           outline=(214, 218, 221), width=3)
    draw.text((72, 67), "Complete the review", font=font(34), fill=(39, 43, 47))
    draw.text((990, 11), "Ready   Checks", font=font(23), fill=(25, 29, 32))
    draw.line((35, 123, 1245, 123), fill=(219, 222, 225), width=3)
    draw.rectangle((45, 129, 1235, 202), fill=(245, 247, 249))
    draw.rectangle((45, 446, 1235, 476), fill=(245, 247, 249))
    draw.text((73, 155), "Write   Preview     Bold     Link     Code", font=font(25),
              fill=(92, 98, 102))
    draw.rectangle((70, 211, 1210, 442), outline=(213, 218, 220), width=3)
    draw.text((98, 235), "Leave a comment", font=font(27), fill=(120, 124, 127))
    draw.text((76, 478), "Markdown is supported", font=font(24), fill=(45, 49, 52))
    for row, label in enumerate(("Comment", "Approve", "Request changes")):
        y = 540 + row * 66
        draw.ellipse((74, y + 1, 94, y + 21), outline=(105, 111, 114), width=2)
        draw.text((113, y), label, font=font(29), fill=(41, 45, 48))
    draw.line((35, 744, 1245, 744), fill=(219, 222, 225), width=3)
    draw.rounded_rectangle((830, 772, 965, 825), radius=10,
                           outline=(130, 136, 139), width=3)
    draw.text((843, 781), "Cancel", font=font(26), fill=(48, 53, 56))
    draw.rounded_rectangle((897, 753, 1214, 831), radius=10,
                           fill=(72, 139, 75))
    draw.text((919, 773), "Submit review", font=font(33), fill=(255, 255, 255))
    mask.text((919, 773), "Submit review", font=font(33), fill=255)
    draw.text((990, 839), "Ready   Checks", font=font(23), fill=(25, 29, 32))
    return image, glyph


def diagram():
    image = Image.new("RGB", (640, 400), "white")
    draw = ImageDraw.Draw(image)
    for x, y, title in ((36, 42, "Plan"), (354, 42, "Build"),
                        (36, 248, "Check"), (354, 248, "Ship")):
        draw.rectangle((x, y, x + 238, y + 100), fill=(146, 207, 231),
                       outline=(32, 43, 50), width=3)
        draw.line((x, y + 35, x + 238, y + 35), fill=(32, 43, 50), width=3)
        draw.text((x + 13, y + 8), title, font=font(20), fill=(24, 30, 35))
        draw.text((x + 13, y + 58), "ready + verified", font=font(19), fill=(24, 30, 35))
    draw.line(((154, 143), (154, 199), (472, 199), (472, 247)), fill=(33, 40, 45), width=3)
    draw.text((224, 171), "Pipeline", font=font(20), fill=(190, 45, 45))
    return image, None


def line_art():
    image = Image.new("RGB", (800, 520), "white")
    draw = ImageDraw.Draw(image)
    for x, y, title in ((55, 85, "Input"), (455, 85, "Output"),
                        (255, 335, "Review")):
        draw.rectangle((x, y, x + 290, y + 105), outline=(15, 20, 24), width=5)
        draw.text((x + 19, y + 23), title, font=font(42), fill=(15, 20, 24))
    draw.line(((346, 140), (453, 140)), fill=(15, 20, 24), width=6)
    draw.line(((398, 192), (398, 332)), fill=(15, 20, 24), width=6)
    return image, None


def panel_with_sparse_link():
    image, _ = diagram()
    draw = ImageDraw.Draw(image)
    glyph = Image.new("L", image.size, 0)
    text = "https://example.invalid/design/specification"
    draw.text((42, 365), text, font=font(17), fill=(32, 47, 151))
    ImageDraw.Draw(glyph).text((42, 365), text, font=font(17), fill=255)
    return image, glyph


def pdf(image):
    encoded = io.BytesIO()
    image.save(encoded, format="JPEG", quality=84, subsampling=0)
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(480, 340), pageCompression=1)
    canvas.drawString(20, 318, "Raster readability fixture")
    canvas.drawImage(ImageReader(io.BytesIO(encoded.getvalue())), 18, 15,
                     width=444, height=288)
    canvas.save()
    return stream.getvalue()


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
    return json.loads(result.stdout[16:16 + metadata_size]), result.stdout[16 + metadata_size:]


def embedded(data):
    doc = pymupdf.open(stream=data, filetype="pdf")
    images = doc[0].get_images(full=True)
    assert len(images) == 1
    xref = images[0][0]
    pixels = pymupdf.Pixmap(doc, xref)
    assert pixels.n == 3
    return (np.frombuffer(pixels.samples, dtype=np.uint8).reshape(pixels.height, pixels.width, 3).copy(),
            doc.extract_image(xref)["image"], tuple(doc[0].get_image_rects(xref)[0]))


def luminance(rgb):
    encoded = rgb.astype(np.float64) / 255
    linear = np.where(encoded <= .04045, encoded / 12.92,
                      ((encoded + .055) / 1.055) ** 2.4)
    return linear @ np.array((.2126, .7152, .0722))


def ratio(a, b):
    return (np.maximum(a, b) + .05) / (np.minimum(a, b) + .05)


def tile_counts(before, after):
    source_l, target_l = luminance(before), luminance(after)
    height, width = source_l.shape
    invented = np.zeros(((height + 63) // 64, (width + 63) // 64), dtype=np.int32)
    lost = np.zeros_like(invented)
    pale_invented = np.zeros_like(invented)
    white_color_lost = np.zeros_like(invented)
    for dy, dx in ((0, 1), (1, 0)):
        first = before[:height - dy, :width - dx].astype(np.int16)
        second = before[dy:, dx:].astype(np.int16)
        source = ratio(source_l[:height - dy, :width - dx], source_l[dy:, dx:])
        target = ratio(target_l[:height - dy, :width - dx], target_l[dy:, dx:])
        flat = np.max(np.abs(first - second), axis=2) <= 8
        first_span = first.max(axis=2) - first.min(axis=2)
        second_span = second.max(axis=2) - second.min(axis=2)
        chromatic = (first_span >= 25) | (second_span >= 25)
        pale = ((first.min(axis=2) >= 180) & (second.min(axis=2) >= 180) &
                ((first_span >= 12) | (second_span >= 12)))
        white_chromatic = (((first.min(axis=2) >= 230) & (second_span >= 30)) |
                           ((second.min(axis=2) >= 230) & (first_span >= 30)))
        for accumulator, mask in ((invented, flat & (target >= 3)),
                                  (lost, chromatic & (source >= 3.5) & (target < 2)),
                                  (pale_invented, flat & pale & (target >= 3)),
                                  (white_color_lost, white_chromatic &
                                   (source >= 3) & (target < 3))):
            y, x = np.where(mask)
            np.add.at(accumulator, (y // 64, x // 64), 1)
    return {"inventedTotal": int(invented.sum()),
            "inventedTilesAt24": int((invented >= 24).sum()),
            "inventedMaxTile": int(invented.max()),
            "lostTotal": int(lost.sum()),
            "lostTilesAt24": int((lost >= 24).sum()),
            "lostMaxTile": int(lost.max()),
            "paleInventedTotal": int(pale_invented.sum()),
            "paleInventedTilesAt24": int((pale_invented >= 24).sum()),
            "paleInventedMaxTile": int(pale_invented.max()),
            "whiteColorLostTotal": int(white_color_lost.sum()),
            "whiteColorLostTilesAt24": int((white_color_lost >= 24).sum()),
            "whiteColorLostMaxTile": int(white_color_lost.max())}


def inspect_case(name, image, glyph):
    source = pdf(image)
    inspected, no_pdf = invoke(source, "--inspect")
    facts, converted = invoke(source, "--convert")
    assert inspected == facts and not no_pdf and converted
    before, encoded_before, rect_before = embedded(source)
    after, encoded_after, rect_after = embedded(converted)
    assert before.shape == after.shape and rect_before == rect_after
    doc_before = pymupdf.open(stream=source, filetype="pdf")
    doc_after = pymupdf.open(stream=converted, filetype="pdf")
    assert doc_before[0].get_text() == doc_after[0].get_text()
    before_l, after_l = luminance(before), luminance(after)
    metrics = tile_counts(before, after)
    metrics["encodedImageUnchanged"] = encoded_before == encoded_after
    metrics["decodedPixelsUnchanged"] = np.array_equal(before, after)
    metrics["imageRect"] = rect_before
    if name == "white-on-green-ui":
        assert glyph is not None
        known_cores = (np.asarray(glyph) >= 250) & (before.min(axis=2) >= 230)
        assert int(known_cores.sum()) >= 500
        source_panel = np.array((72, 139, 75), dtype=np.int16)
        panel = ((np.abs(before.astype(np.int16) - source_panel).max(axis=2) <= 18) &
                 (np.indices(known_cores.shape)[1] >= 897))
        assert int(panel.sum()) >= 8000
        source_ink, source_fill = float(np.median(before_l[known_cores])), float(np.median(before_l[panel]))
        target_ink, target_fill = float(np.median(after_l[known_cores])), float(np.median(after_l[panel]))
        metrics.update({"glyphCores": int(known_cores.sum()),
                        "sourcePanelContrast": round(float(ratio(source_ink, source_fill)), 3),
                        "outputPanelContrast": round(float(ratio(target_ink, target_fill)), 3)})
        assert 3 <= metrics["sourcePanelContrast"] <= 4.5
    if name == "colored-panels-sparse-link-control":
        assert glyph is not None
        known_cores = ((np.asarray(glyph) >= 250) &
                       (before[:, :, 2].astype(np.int16) - before[:, :, 0] >= 45) &
                       (before[:, :, 0] < 110))
        assert int(known_cores.sum()) >= 300
        source_bg, target_bg = before_l[380, 10], after_l[380, 10]
        source_ratios = ratio(before_l[known_cores], source_bg)
        target_ratios = ratio(after_l[known_cores], target_bg)
        assert float(np.mean(source_ratios >= 3)) >= .95
        metrics.update({"blueLinkCorePixels": int(known_cores.sum()),
                        "sourceLinkMedianContrast": round(float(np.median(source_ratios)), 3),
                        "outputLinkMedianContrast": round(float(np.median(target_ratios)), 3),
                        "outputLinkReadableShareAt3": round(float(np.mean(target_ratios >= 3)), 4)})
    if name == "pale-blue-jpeg-badges":
        assert isinstance(glyph, dict)
        metrics["badgeReadability"] = {}
        for y, label in ((153, "multiply"), (288, "release"), (423, "review")):
            authored = np.asarray(glyph[label]) >= 250
            known_cores = (authored &
                           (before[:, :, 2].astype(np.int16) - before[:, :, 0] >= 45) &
                           (before[:, :, 0] < 110))
            assert int(known_cores.sum()) >= 300
            source_fill = float(np.median(before_l[y + 17:y + 39, 859:867]))
            target_fill = float(np.median(after_l[y + 17:y + 39, 859:867]))
            source_ratios = ratio(before_l[known_cores], source_fill)
            target_ratios = ratio(after_l[known_cores], target_fill)
            assert float(np.mean(source_ratios >= 3)) >= .95
            after_cores = after[known_cores].astype(np.int16)
            metrics["badgeReadability"][label] = {
                "corePixels": int(known_cores.sum()),
                "medianContrast": round(float(np.median(target_ratios)), 3),
                "readableShareAt3": round(float(np.mean(target_ratios >= 3)), 4),
                "blueHueShare": round(float(np.mean(after_cores[:, 2] - after_cores[:, 0] >= 15)), 4)}
        view_mask = ((np.asarray(glyph["view-checks"]) >= 250) &
                     (before.min(axis=2) >= 230))
        assert int(view_mask.sum()) >= 100
        source_button = np.array((41, 143, 82), dtype=np.int16)
        button = ((np.abs(before.astype(np.int16) - source_button).max(axis=2) <= 18) &
                  (np.indices(before_l.shape)[0] >= 556) &
                  (np.indices(before_l.shape)[0] <= 593))
        assert int(button.sum()) >= 3000
        source_fill = float(np.median(before_l[button]))
        target_fill = float(np.median(after_l[button]))
        source_ratios = ratio(before_l[view_mask], source_fill)
        target_ratios = ratio(after_l[view_mask], target_fill)
        assert float(np.mean(source_ratios >= 3)) >= .95
        metrics["viewChecksReadability"] = {
            "corePixels": int(view_mask.sum()),
            "medianContrast": round(float(np.median(target_ratios)), 3),
            "readableShareAt3": round(float(np.mean(target_ratios >= 3)), 4)}
    repeat, duplicate = invoke(converted, "--convert")
    assert repeat["changedPages"] == 0 and not duplicate
    (scratch / f"{name}-source.pdf").write_bytes(source)
    (scratch / f"{name}-converted.pdf").write_bytes(converted)
    Image.fromarray(before).save(scratch / f"{name}-source-image.png")
    Image.fromarray(after).save(scratch / f"{name}-converted-image.png")
    doc_before[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(scratch / f"{name}-source-page.png")
    doc_after[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(scratch / f"{name}-converted-page.png")
    if glyph is not None:
        if isinstance(glyph, dict):
            for label, mask in glyph.items():
                mask.save(scratch / f"{name}-{label}-authored-glyph-mask.png")
        else:
            glyph.save(scratch / f"{name}-authored-glyph-mask.png")
    if name == "pale-blue-jpeg-badges":
        passed = (metrics["paleInventedTilesAt24"] == 0 and
                  all(item["readableShareAt3"] >= .95 and item["blueHueShare"] >= .95
                      for item in metrics["badgeReadability"].values()) and
                  metrics["viewChecksReadability"]["readableShareAt3"] >= .95)
    elif name == "white-on-green-ui":
        passed = (metrics["encodedImageUnchanged"] and
                  metrics["outputPanelContrast"] >= 3)
    else:
        passed = (not metrics["encodedImageUnchanged"] and
                  not metrics["decodedPixelsUnchanged"] and
                  int(np.median(after[8:30, 8:30])) < 60)
        if name == "colored-panels-sparse-link-control":
            passed = (passed and metrics["outputLinkMedianContrast"] >= 3 and
                      metrics["outputLinkReadableShareAt3"] >= .85)
    result = {"case": name, "passed": passed, "metrics": metrics,
              "facts": facts, "sourceSha256": hashlib.sha256(source).hexdigest(),
              "convertedSha256": hashlib.sha256(converted).hexdigest()}
    print(("PASS" if passed else "FAIL"), name, metrics)
    return result


results = [inspect_case("pale-blue-jpeg-badges", *blue_badges()),
           inspect_case("white-on-green-ui", *green_lettering()),
           inspect_case("colored-panels-control", *diagram()),
           inspect_case("line-art-control", *line_art()),
           inspect_case("colored-panels-sparse-link-control", *panel_with_sparse_link())]
report = {"engine": str(engine),
          "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
          "probe": args.probe, "results": results}
(scratch / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
if not args.probe and not all(result["passed"] for result in results):
    raise SystemExit(1)
