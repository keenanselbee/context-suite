"""Check disjoint masked-JPEG regions against independent gap and overlap paint.

The fixtures are authored here; no lecture PDF or extracted artwork is used.
Test-only dependencies: Pillow, pypdf and PyMuPDF.
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
    result = DecodedStreamObject()
    result.set_data(data)
    result.update({NameObject(key): value for key, value in entries.items()})
    return writer._add_object(result)


def artwork(rows=False, bridge=False, translucent_interior=False):
    rgb = Image.new("RGB", (200, 60), (245, 245, 243))
    draw = ImageDraw.Draw(rgb)
    mask = Image.new("L", rgb.size, 0)
    alpha = ImageDraw.Draw(mask)
    panels = ((7, 6, 193, 20), (7, 40, 193, 54)) if rows else (
        (5, 7, 55, 53), (145, 7, 195, 53))
    for index, box in enumerate(panels):
        draw.rounded_rectangle(box, radius=6, fill=(216, 232, 242) if index == 0 else (234, 222, 192),
                               outline=(38, 58, 73), width=2)
        alpha.rounded_rectangle(box, radius=6, fill=255)
    if bridge:
        # A nonzero bridge removes every fully transparent column between panels.
        alpha.rectangle((55, 26, 145, 34), fill=1)
    if translucent_interior:
        # This one-sample opacity difference exposes the paint underneath.
        alpha.rectangle((21, 26, 29, 34), fill=254)
    encoded = io.BytesIO()
    rgb.save(encoded, format="JPEG", quality=94, subsampling=0)
    return encoded.getvalue(), mask.tobytes()


def gap_image():
    image = Image.new("RGB", (60, 30))
    for y in range(30):
        for x in range(60):
            image.putpixel((x, y), (28 + x * 3, 70 + y * 4, 105 + ((x * 7 + y * 11) % 75)))
    encoded = io.BytesIO()
    image.save(encoded, format="JPEG", quality=94, subsampling=0)
    return encoded.getvalue()


def fixture(case):
    rows = case.startswith("rows-")
    writer = PdfWriter()
    page = writer.add_blank_page(width=240, height=150)
    geometry = {"/Type": NameObject("/XObject"), "/Subtype": NameObject("/Image"),
                "/Width": NumberObject(200), "/Height": NumberObject(60),
                "/BitsPerComponent": NumberObject(8)}
    jpeg, alpha = artwork(rows=rows, bridge=case == "columns-bridge",
                          translucent_interior=case in ("columns-nearopaque-basic",
                                                        "columns-prior-nearopaque-vector"))
    mask = stream(writer, alpha, {**geometry, "/ColorSpace": NameObject("/DeviceGray")})
    if case == "columns-interpolated-mask":
        mask.get_object()[NameObject("/Interpolate")] = BooleanObject(True)
    art = stream(writer, jpeg, {**geometry, "/ColorSpace": NameObject("/DeviceRGB"),
                                "/Filter": NameObject("/DCTDecode"), "/SMask": mask})
    small_geometry = {"/Type": NameObject("/XObject"), "/Subtype": NameObject("/Image"),
                      "/Width": NumberObject(60), "/Height": NumberObject(30),
                      "/BitsPerComponent": NumberObject(8),
                      "/ColorSpace": NameObject("/DeviceRGB"),
                      "/Filter": NameObject("/DCTDecode")}
    center = stream(writer, gap_image(), small_geometry)
    resources = {NameObject("/XObject"): DictionaryObject({
        NameObject("/Art"): art, NameObject("/Center"): center})}
    if case in ("columns-text-contained", "columns-crossing-text"):
        resources[NameObject("/Font")] = DictionaryObject({NameObject("/F1"):
            writer._add_object(DictionaryObject({NameObject("/Type"): NameObject("/Font"),
                NameObject("/Subtype"): NameObject("/Type1"),
                NameObject("/BaseFont"): NameObject("/Helvetica")}))})
    page[NameObject("/Resources")] = DictionaryObject(resources)

    content = "q 1 1 1 rg 0 0 240 150 re f Q\n"
    gap = "q 0.8 0.25 0.15 rg 108 66 24 8 re f Q\n" if rows else (
        "q 0.8 0.25 0.15 rg 106 68 28 10 re f Q\n")
    center_paint = ("q 40 0 0 9 100 66 cm /Center Do Q\n" if rows else
                    "q 40 0 0 20 100 63 cm /Center Do Q\n")
    if case in ("columns-prior-vector-gap", "columns-bridge", "columns-interpolated-mask"):
        content += gap
    if case in ("rows-prior-image-gap", "columns-unsafe-reuse"):
        content += center_paint
    if case == "columns-prior-opaque-wide-vector":
        content += "q 0.9 0.1 0.2 rg 43 67 15 12 re f Q\n"
    if case in ("columns-prior-opaque-vector", "columns-prior-nearopaque-vector"):
        content += "q 0.9 0.1 0.2 rg 41 66 8 8 re f Q\n"
    if case == "columns-prior-edge-vector":
        content += "q 0.9 0.1 0.2 rg 70 66 18 8 re f Q\n"
    if case == "columns-prior-outside-bounds-vector":
        content += "q 0.9 0.1 0.2 rg 17 66 23 8 re f Q\n"
    if case == "rows-clipped-prior-opaque-vector":
        content += "q 0.9 0.1 0.2 rg 41 84 8 7 re f Q\n"
    if case.startswith("rows-clipped"):
        content += "q 30 43 180 54 re W n\n"
    placement = "-200 0 0 60 220 40" if case == "columns-mirrored" else "200 0 0 60 20 40"
    content += f"q {placement} cm /Art Do Q\n"
    if case.startswith("rows-clipped"):
        content += "Q\n"
    if case == "columns-later-image-gap":
        content += center_paint
    if case == "columns-later-overlap-vector":
        content += "q 0.9 0.1 0.2 rg 41 66 8 8 re f Q\n"
    if case == "columns-text-contained":
        content += "q 0 0 0 rg BT /F1 11 Tf 34 69 Td (OK) Tj ET Q\n"
    if case == "columns-crossing-text":
        content += "q 0 0 0 rg BT /F1 11 Tf 68 69 Td (CROSS) Tj ET Q\n"
    if case == "columns-unsafe-reuse":
        content += "q 0.8 0.2 0.2 rg 20 4 200 31 re f Q\n"
        content += "q 200 0 0 60 20 0 cm /Art Do Q\n"
    page[NameObject("/Contents")] = stream(writer, content.encode("ascii"), {})
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=scratch) as source:
        source.write(data)
        source.seek(0)
        result = subprocess.run([str(engine), mode], stdin=source,
                                capture_output=True, timeout=60)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_size + pdf_size
    return (0, json.loads(result.stdout[16:16 + metadata_size]),
            result.stdout[16 + metadata_size:])


def retained_bytes(data):
    image = PdfReader(io.BytesIO(data)).pages[0]["/Resources"]["/XObject"]["/Art"].get_object()
    return image._data, image["/SMask"].get_object().get_data()


def all_image_bytes(data):
    objects = PdfReader(io.BytesIO(data)).pages[0]["/Resources"]["/XObject"]
    return {str(name): (item.get_object()._data,
                        item.get_object()["/SMask"].get_object().get_data()
                        if "/SMask" in item.get_object() else None)
            for name, item in objects.items()}


def rendered(data):
    pdf = pymupdf.open(stream=data, filetype="pdf")
    return pdf[0].get_pixmap(alpha=False)


def same_region(before, after, rectangle):
    left, top, right, bottom = rectangle
    compared = 0
    for y in range(top, bottom):
        for x in range(left, right):
            original = before.pixel(x, y)
            if min(original) > 248:
                continue
            compared += 1
            if max(abs(a - b) for a, b in zip(original, after.pixel(x, y))) > 1:
                return False
    return compared > 100


positives = ("columns-basic", "columns-nearopaque-basic",
             "columns-prior-vector-gap", "columns-later-image-gap",
             "rows-prior-image-gap", "columns-text-contained", "columns-mirrored", "rows-clipped",
             "columns-prior-opaque-vector", "columns-prior-opaque-wide-vector",
             "rows-clipped-prior-opaque-vector")
negatives = ("columns-bridge", "columns-interpolated-mask",
             "columns-prior-nearopaque-vector", "columns-prior-edge-vector",
             "columns-prior-outside-bounds-vector",
             "columns-later-overlap-vector")
fallbacks = ("columns-crossing-text", "columns-unsafe-reuse")
results = []
for case in (*positives, *fallbacks, *negatives):
    source = fixture(case)
    source_hash = hashlib.sha256(source).hexdigest()
    (scratch / f"{case}-source.pdf").write_bytes(source)
    code, facts, output = invoke(source, "--convert")
    inspect_code, inspection, empty = invoke(source, "--inspect")
    assert code == inspect_code and (code != 0 or inspection == facts and not empty), case
    details = {"exitCode": code, "diagnostic": facts if code else None}
    if case in fallbacks:
        if code:
            passed = False
        else:
            (scratch / f"{case}-dark.pdf").write_bytes(output)
            original, dark = rendered(source), rendered(output)
            original.save(scratch / f"{case}-source.png")
            dark.save(scratch / f"{case}-dark.png")
            bottom = 110 if case == "columns-crossing-text" else 150
            exact = all(original.pixel(x, y) == dark.pixel(x, y)
                        for y in range(50, bottom) for x in range(20, 220))
            content = PdfReader(io.BytesIO(output)).pages[0].get_contents().get_data()
            with pymupdf.open(stream=source, filetype="pdf") as before:
                source_text = before[0].get_texttrace()
            with pymupdf.open(stream=output, filetype="pdf") as after:
                output_text = after[0].get_texttrace()
            def visible_text(spans):
                return [(span["font"], span["size"], span["bbox"], span["chars"],
                         span["color"], span["opacity"], span["type"])
                        for span in spans]
            repeat_code, repeat_facts, duplicate = invoke(output, "--convert")
            details.update({"entireCompositePixelsExact": exact,
                            "allImageAndMaskBytesPreserved": all_image_bytes(source) ==
                            all_image_bytes(output),
                            "textAndGeometryPreserved": visible_text(source_text) ==
                            visible_text(output_text),
                            "localBackingInserted": b"/Artifact BMC" in content,
                            "outsidePaperDark": max(dark.pixel(5, 5)) < 60,
                            "repeatNoOp": repeat_code == 0 and
                            repeat_facts["changedPages"] == 0 and not duplicate})
            passed = all(details[key] for key in ("entireCompositePixelsExact",
                                                   "allImageAndMaskBytesPreserved",
                                                   "textAndGeometryPreserved", "localBackingInserted",
                                                   "outsidePaperDark", "repeatNoOp"))
    elif case in negatives:
        passed = code == 3 and not output
        if case == "columns-crossing-text":
            with pymupdf.open(stream=source, filetype="pdf") as source_document:
                assert "CROSS" in source_document[0].get_text(), "Crossing-text control is not visible"
            rendered(source).save(scratch / f"{case}-source.png")
        if output:
            (scratch / f"{case}-unexpected-dark.pdf").write_bytes(output)
            rendered(source).save(scratch / f"{case}-source.png")
            rendered(output).save(scratch / f"{case}-unexpected-dark.png")
    elif code:
        passed = False
    else:
        (scratch / f"{case}-dark.pdf").write_bytes(output)
        original, dark = rendered(source), rendered(output)
        original.save(scratch / f"{case}-source.png")
        dark.save(scratch / f"{case}-dark.png")
        if case.startswith("rows-"):
            regions = ((40, 46, 200, 57), (40, 82, 200, 94))
        else:
            regions = ((27, 50, 72, 92), (168, 50, 213, 92))
        preserved = all(same_region(original, dark, region) for region in regions)
        encoded = retained_bytes(source) == retained_bytes(output)
        dark_paper = max(dark.pixel(5, 5)) < 60
        gap_x, gap_y = (40, 75) if case.startswith("rows-") else (90, 76)
        gap_dark = max(abs(a - b) for a, b in zip(dark.pixel(gap_x, gap_y),
                                                   dark.pixel(5, 5))) <= 2
        if case in ("columns-later-image-gap", "rows-prior-image-gap"):
            gap_mark_y = 80 if case.startswith("rows-") else 70
            gap_visible = max(abs(a - b) for a, b in zip(dark.pixel(120, gap_mark_y),
                                                          dark.pixel(5, 5))) > 25
        elif case == "columns-prior-vector-gap":
            gap_visible = max(abs(a - b) for a, b in zip(dark.pixel(120, 76),
                                                          dark.pixel(5, 5))) > 25
        else:
            gap_visible = True
        text_kept = (case != "columns-text-contained" or
                     "OK" in pymupdf.open(stream=output)[0].get_text())
        repeat_code, repeat_facts, duplicate = invoke(output, "--convert")
        repeated = repeat_code == 0 and repeat_facts["changedPages"] == 0 and not duplicate
        passed = all((preserved, encoded, dark_paper, gap_dark, gap_visible, text_kept, repeated,
                      source_hash == hashlib.sha256(source).hexdigest()))
        details.update({"originalRegionPixelsPreserved": preserved,
                        "jpegAndMaskBytesPreserved": encoded, "gapPaintVisible": gap_visible,
                        "outsidePaperDark": dark_paper, "transparentGapDark": gap_dark,
                        "textRetained": text_kept,
                        "repeatNoOp": repeated})
    results.append({"case": case, "passed": passed, "sourceSha256": source_hash,
                    "details": details})
    print(("PASS" if passed else "FAIL"), case, details, flush=True)

report = {"engine": str(engine), "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
          "results": results}
(scratch / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
if not all(result["passed"] for result in results):
    raise AssertionError("Image-region cases failed; see scratch report.json")
print(f"Passed {len(results)} image-region checks.")
