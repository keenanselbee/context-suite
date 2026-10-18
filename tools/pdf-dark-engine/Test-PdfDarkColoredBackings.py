"""Preserve isolated masked JPEGs over proved flat colored PDF paper.

The colored-paper cases document a previously refused feature. Unsafe
placements must still refuse. Test-only dependencies: Pillow, pypdf, PyMuPDF.
"""

import argparse
import hashlib
import io
import json
import math
import pathlib
import struct
import subprocess
import tempfile

import pymupdf
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (BooleanObject, DecodedStreamObject, DictionaryObject,
                           NameObject, NumberObject)


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


def stream(writer, data, entries):
    object_ = DecodedStreamObject()
    object_.set_data(data)
    object_.update({NameObject(key): value for key, value in entries.items()})
    return writer._add_object(object_)


def cartoon(faint=False, cropped_support=False):
    image = Image.new("RGB", (80, 60), (240, 240, 238))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((7, 4, 73, 58), radius=17, fill=(29, 203, 124),
                           outline=(12, 69, 58), width=2)
    draw.ellipse((20, 14, 60, 49), fill=(253, 252, 240))
    draw.ellipse((29, 28, 34, 35), fill=(15, 12, 12))
    draw.ellipse((46, 28, 51, 35), fill=(15, 12, 12))
    draw.polygon(((37, 38), (43, 38), (40, 43)), fill=(245, 154, 12))
    draw.rectangle((33, 47, 47, 55), fill=(151, 65, 47))
    stencil = Image.new("L", image.size, 0)
    ImageDraw.Draw(stencil).rounded_rectangle((7, 4, 73, 58), radius=17, fill=255)
    stencil = stencil.filter(ImageFilter.GaussianBlur(.65))
    if cropped_support:
        pixels = stencil.load()
        for y in range(20):
            for x in range(80):
                pixels[x, y] = 0
    if faint:
        pixels = stencil.load()
        for y in range(60):
            for x in range(80):
                if pixels[x, y] == 0 and 2 <= x < 78 and 1 <= y < 59:
                    pixels[x, y] = 1
    encoded = io.BytesIO()
    image.save(encoded, format="JPEG", quality=90)
    return encoded.getvalue(), stencil.tobytes()


def diagram():
    image = Image.new("RGB", (640, 400), "white")
    draw = ImageDraw.Draw(image)
    text = ImageFont.load_default(size=17)
    for x, y, title in ((35, 40, "Collect"), (360, 40, "Compare"),
                        (35, 245, "Check"), (360, 245, "Publish")):
        draw.rectangle((x, y, x + 235, y + 95), fill=(145, 211, 240),
                       outline=(45, 45, 45), width=2)
        draw.line((x, y + 30, x + 235, y + 30), fill=(45, 45, 45), width=2)
        draw.text((x + 9, y + 7), title, font=text, fill=(20, 20, 20))
        draw.text((x + 9, y + 45), "+ status: Accepted", font=text,
                  fill=(20, 20, 20))
    draw.line([(150, 136), (150, 205), (480, 205), (480, 244)],
              fill=(35, 35, 35), width=2)
    draw.line([(480, 136), (480, 165)], fill=(35, 35, 35), width=2)
    draw.text((220, 173), "Reviewed", font=text, fill=(205, 45, 45))
    encoded = io.BytesIO()
    image.save(encoded, format="JPEG", quality=78)
    return encoded.getvalue()


def fixture(case):
    writer = PdfWriter()
    page = writer.add_blank_page(width=180, height=120)
    control = case == "diagram-control"
    if control:
        encoded, alpha = diagram(), None
        width, height = 640, 400
    else:
        encoded, alpha = cartoon(faint=case == "teal-faint-support",
                                 cropped_support=case == "mask-interpolated-crop")
        width, height = 80, 60
    fields = {"/Type": NameObject("/XObject"), "/Subtype": NameObject("/Image"),
              "/Width": NumberObject(width), "/Height": NumberObject(height),
              "/BitsPerComponent": NumberObject(8)}
    mask = None if alpha is None else stream(writer, alpha, {
        **fields, "/ColorSpace": NameObject("/DeviceGray")})
    if case in ("mask-interpolated-full", "mask-interpolated-crop"):
        mask.get_object()[NameObject("/Interpolate")] = BooleanObject(True)
    properties = {**fields, "/ColorSpace": NameObject("/DeviceRGB"),
                  "/Filter": NameObject("/DCTDecode")}
    if mask is not None:
        properties["/SMask"] = mask
    if case == "teal-color-interpolated":
        properties["/Interpolate"] = BooleanObject(True)
    art = stream(writer, encoded, properties)
    page[NameObject("/Resources")] = DictionaryObject({
        NameObject("/XObject"): DictionaryObject({NameObject("/Art"): art})})

    paper = "1 1 1" if control else "0.101960784 0.6 0.533333333" if case != "gray" else "0.55 0.57 0.58"
    content = f"q {paper} rg 0 0 180 120 re f Q\n"
    if not control:
        content += "q 1 1 1 rg 10 100 62 3 re f Q\n"  # heading outside image
    if case in ("prior-paint", "shared-unsafe", "mask-interpolated-crop"):
        x, y, width_, height_ = ((20, 35, 12, 12) if case == "shared-unsafe" else
                                (105, 68, 15, 6) if case == "mask-interpolated-crop" else
                                (105, 40, 12, 12))
        content += f"q 1 0 0 rg {x} {y} {width_} {height_} re f Q\n"
    if case == "unknown-clip":
        content += "q 85 20 m 150 20 l 156 76 l 88 75 l h W n\n"
    transform = "140 0 0 87 20 16" if control else "80 0 0 60 80 20"
    content += f"q {transform} cm /Art Do Q\n"
    if case == "unknown-clip":
        content += "Q\n"
    if case == "later-crossing":
        content += "q 1 0 0 rg 105 40 24 12 re f Q\n"
    if case == "shared-unsafe":
        content += "q 55 0 0 42 12 18 cm /Art Do Q\n"
    page[NameObject("/Contents")] = stream(writer, content.encode("ascii"), {})
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=scratch) as source:
        source.write(data)
        source.seek(0)
        result = subprocess.run([str(engine), mode], stdin=source,
                                capture_output=True, timeout=90)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_size + pdf_size
    return (0, json.loads(result.stdout[16:16 + metadata_size]),
            result.stdout[16 + metadata_size:])


def image_streams(data):
    object_ = PdfReader(io.BytesIO(data)).pages[0]["/Resources"]["/XObject"]["/Art"].get_object()
    mask = object_.get("/SMask")
    return object_._data, b"" if mask is None else mask.get_object().get_data()


def rendered(data):
    pdf = pymupdf.open(stream=data, filetype="pdf")
    page = pdf[0]
    images = page.get_images(full=True)
    assert len(images) == 1
    placements = page.get_image_rects(images[0][0])
    assert placements
    return page.get_pixmap(alpha=False), placements[0]


results = []
positives = ("teal", "gray", "teal-faint-support", "teal-color-interpolated",
             "mask-interpolated-full", "prior-paint")
negatives = ("later-crossing", "shared-unsafe", "unknown-clip",
             "mask-interpolated-crop")
for case in (*positives, *negatives, "diagram-control"):
    source = fixture(case)
    (scratch / f"{case}-source.pdf").write_bytes(source)
    source_render, source_bounds = rendered(source)
    source_render.save(scratch / f"{case}-source.png")
    code, facts, output = invoke(source, "--convert")
    inspected_code, inspected, empty = invoke(source, "--inspect")
    assert code == inspected_code and (code != 0 or facts == inspected and not empty)
    if case in negatives:
        assert code == 3 and not output, (case, code, facts)
        passed = True
        details = {"refused": True}
    elif code:
        assert code == 3 and not output, (case, code, facts)
        passed = False
        details = {"currentRefusal": facts}
    else:
        assert output, case
        (scratch / f"{case}-dark.pdf").write_bytes(output)
        dark_render, dark_bounds = rendered(output)
        dark_render.save(scratch / f"{case}-dark.png")
        if case == "diagram-control":
            changed = image_streams(source)[0] != image_streams(output)[0]
            dark_paper = max(dark_render.pixel(5, 5)) < 60
            passed = changed and dark_paper and facts["recoloredImages"] == 1
            details = {"imageRecolored": changed, "darkPaper": dark_paper}
        else:
            encoded_match = image_streams(source) == image_streams(output)
            assert all(abs(a - b) <= .001 for a, b in zip(source_bounds, dark_bounds)), case
            matching = 0
            considered = 0
            left, top, right, bottom = source_bounds
            # PyMuPDF image rectangles use top-left page coordinates, as do
            # pixmap samples. Never infer raster rows from PDF's bottom origin.
            for y in range(max(0, math.floor(top)), min(source_render.height, math.ceil(bottom))):
                for x in range(max(0, math.floor(left)), min(source_render.width, math.ceil(right))):
                    considered += 1
                    matching += max(abs(a - b) for a, b in zip(
                        source_render.pixel(x, y), dark_render.pixel(x, y))) <= 1
            dark_paper = max(dark_render.pixel(5, 5)) < 60
            passed = encoded_match and matching == considered and dark_paper
            details = {"encodedJpegAndDecodedMaskMatch": encoded_match,
                       "exactLocalPixels": matching, "localPixels": considered,
                       "outsidePaperDark": dark_paper}
            if case == "prior-paint":
                # The earlier red patch is behind the opaque cartoon center.
                # Its original compositing, including antialiased mask edges,
                # must remain intact while the surrounding teal paper darkens.
                patch_exact = all(source_render.pixel(x, y) == dark_render.pixel(x, y)
                                  for y in range(68, 80) for x in range(105, 117))
                content = PdfReader(io.BytesIO(output)).pages[0].get_contents().get_data()
                local_backing = b"/Artifact BMC" in content
                passed = passed and patch_exact and local_backing
                details.update({"priorPatchPixelsExact": patch_exact,
                                "localBackingInserted": local_backing})
        again_code, again_facts, duplicate = invoke(output, "--convert")
        assert again_code == 0 and again_facts["changedPages"] == 0 and not duplicate
        details["repeatNoOp"] = True
    results.append({"case": case, "passed": passed, "exitCode": code,
                    "details": details,
                    "sourceSha256": hashlib.sha256(source).hexdigest()})
    print(("PASS" if passed else "FAIL"), case, code, details)

report = {"engine": str(engine),
          "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
          "results": results}
(scratch / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
if not all(result["passed"] for result in results):
    raise AssertionError("Colored image backing cases still refuse; see scratch report.json")
print(f"Passed {len(results)} colored-backing checks.")
