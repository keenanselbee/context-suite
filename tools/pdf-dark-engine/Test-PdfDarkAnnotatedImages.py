"""Exercise bounded preservation of an opaque image with cross-edge annotations.

Every PDF and image pixel is authored here. The positive scene puts colored
text and a stroke across a screenshot edge on dark paper. The source image and
those annotations must remain exact while a separate light panel is darkened.
Requires pypdf and PyMuPDF plus a freshly built development PDF-dark host.
"""

import argparse
import io
import json
from pathlib import Path
import struct
import subprocess

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, ContentStream, DecodedStreamObject,
                           DictionaryObject, FloatObject, NameObject,
                           NumberObject)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = Path(__file__).resolve().parents[2]
scratch = Path(args.scratch).resolve()
engine = Path(args.engine).resolve(strict=True)
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)

ORANGE = (0.969, 0.588, 0.275)


def dictionary(**values):
    return DictionaryObject({NameObject("/" + key): value for key, value in values.items()})


def stream(data):
    result = DecodedStreamObject()
    result.set_data(data)
    return result


def image(writer, *, partial_mask=False):
    width, height = 128, 80
    pixels = bytearray()
    for y in range(height):
        for x in range(width):
            if x == width - 1:
                color = (0, 0, 0)
            elif x == width - 2:
                color = (57, 207, 247)
            elif y < 5:
                color = (189, 211, 239)
            elif 10 <= x < 40 and 18 <= y < 50:
                color = (220, 224, 230)
            else:
                color = (250, 250, 250)
            pixels.extend(color)
    mask = bytearray([255]) * (width * height)
    if partial_mask:
        mask[40 * width + 80] = 128
    mask_stream = stream(bytes(mask))
    mask_stream.update(dictionary(Type=NameObject("/XObject"),
                                  Subtype=NameObject("/Image"),
                                  Width=NumberObject(width), Height=NumberObject(height),
                                  BitsPerComponent=NumberObject(8),
                                  ColorSpace=NameObject("/DeviceGray")))
    image_stream = stream(bytes(pixels))
    image_stream.update(dictionary(Type=NameObject("/XObject"),
                                   Subtype=NameObject("/Image"),
                                   Width=NumberObject(width), Height=NumberObject(height),
                                   BitsPerComponent=NumberObject(8),
                                   ColorSpace=NameObject("/DeviceRGB"),
                                   SMask=writer._add_object(mask_stream.flate_encode())))
    return writer._add_object(image_stream.flate_encode())


def fixture(variant="safe"):
    writer = PdfWriter()
    page = writer.add_blank_page(width=250, height=140)
    screenshot = image(writer, partial_mask=variant == "partial-mask")
    xobjects = dictionary(Im1=screenshot)
    if variant == "second-image":
        xobjects[NameObject("/Im2")] = image(writer)
    font = dictionary(Type=NameObject("/Font"), Subtype=NameObject("/Type1"),
                      BaseFont=NameObject("/Helvetica"))
    resources = dictionary(XObject=xobjects, Font=dictionary(F1=writer._add_object(font)))
    if variant == "fractional-overlay":
        alpha = dictionary(Type=NameObject("/ExtGState"), ca=FloatObject(.5),
                           CA=FloatObject(.5))
        resources[NameObject("/ExtGState")] = dictionary(Half=writer._add_object(alpha))
    if variant == "shading-overlay":
        fn = dictionary(FunctionType=NumberObject(2), Domain=ArrayObject(
            [NumberObject(0), NumberObject(1)]),
            C0=ArrayObject([FloatObject(.8)] * 3),
            C1=ArrayObject([FloatObject(1)] * 3), N=NumberObject(1))
        shade = dictionary(ShadingType=NumberObject(2),
                           ColorSpace=NameObject("/DeviceRGB"),
                           Coords=ArrayObject([NumberObject(0), NumberObject(0),
                                               NumberObject(250), NumberObject(0)]),
                           Function=writer._add_object(fn),
                           Extend=ArrayObject([NameObject("/true"), NameObject("/true")]))
        resources[NameObject("/Shading")] = dictionary(Sh0=writer._add_object(shade))
    page[NameObject("/Resources")] = resources
    overlay = (0.09, 0.12, 0.16) if variant == "low-cross-edge-ink" else ORANGE
    rgb = " ".join(str(v) for v in overlay)
    content = ["0 0.125 0.251 rg 0 0 250 140 re f",
               "q 160 0 0 90 25 25 cm /Im1 Do Q"]
    if variant == "second-image":
        content.append("q 30 0 0 35 166 55 cm /Im2 Do Q")
    if variant == "fractional-overlay":
        content.append("q /Half gs 1 1 1 rg 155 45 45 50 re f Q")
    if variant == "shading-overlay":
        content.append("q 155 45 45 50 re W n /Sh0 sh Q")
    content.extend([
        f"q {rgb} rg BT /F1 22 Tf 1 0 0 1 170 75 Tm (HELLO) Tj ET Q",
        f"q {rgb} RG 2 w 168 103 m 203 103 l S Q",
        "q 0.8 0.8 0.8 rg 205 18 32 28 re f Q",
    ])
    page[NameObject("/Contents")] = writer._add_object(
        stream(("\n".join(content) + "\n").encode("ascii")))
    result = io.BytesIO()
    writer.write(result)
    return result.getvalue()


def invoke(source, mode):
    process = subprocess.run([str(engine), mode], input=source,
                             capture_output=True, timeout=60)
    if process.returncode:
        assert not process.stdout, "Refusal published PDF bytes"
        return process.returncode, process.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_length + pdf_length
    return (0, json.loads(process.stdout[16:16 + metadata_length]),
            process.stdout[16 + metadata_length:])


def page_facts(pdf):
    reader = PdfReader(io.BytesIO(pdf))
    page = reader.pages[0]
    image_object = page["/Resources"]["/XObject"]["/Im1"]
    mask = image_object["/SMask"]
    operations = ContentStream(page["/Contents"], reader).operations
    annotations = [(bytes(op), tuple(str(value) for value in operands))
                   for operands, op in operations if op in (b"Do", b"Tj", b"Tm", b"Tf",
                                                            b"m", b"l", b"S")]
    colors = [tuple(float(value) for value in operands)
              for operands, op in operations if op == b"rg"]
    drawing_geometry = [(entry["type"],
                         tuple(round(float(value), 4) for value in entry["rect"]),
                         round(float(entry["fill_opacity"]), 6) if entry["fill_opacity"] is not None else None,
                         round(float(entry["stroke_opacity"]), 6) if entry["stroke_opacity"] is not None else None)
                        for entry in pymupdf.open(stream=pdf, filetype="pdf")[0].get_drawings()]
    return image_object.get_data(), mask.get_data(), annotations, colors, drawing_geometry


source = fixture()
(scratch / "annotated-source.pdf").write_bytes(source)
code, inspected, empty = invoke(source, "--inspect")
assert code == 0 and not empty, (code, inspected)
code, facts, output = invoke(source, "--convert")
assert code == 0 and output and facts == inspected, (code, facts)
before = page_facts(source)
after = page_facts(output)
assert before[:3] == after[:3], "Image/mask bytes or overlay paint operators changed"
assert before[4] == after[4], "Drawn geometry or alpha changed"
assert ORANGE in after[3] and ORANGE in before[3], "Cross-edge orange text color changed"
original_page = pymupdf.open(stream=source, filetype="pdf")[0]
converted_page = pymupdf.open(stream=output, filetype="pdf")[0]
original_draws, converted_draws = original_page.get_drawings(), converted_page.get_drawings()
assert original_draws[-1]["fill"] != converted_draws[-1]["fill"], "Separate light panel did not darken"
assert [d["color"] for d in original_draws if d["color"]] == \
    [d["color"] for d in converted_draws if d["color"]], "Annotation stroke color changed"
clip = pymupdf.Rect(26, 26, 184, 114)
assert original_page.get_pixmap(matrix=pymupdf.Matrix(2, 2), clip=clip).samples == \
    converted_page.get_pixmap(matrix=pymupdf.Matrix(2, 2), clip=clip).samples, "Screenshot appearance changed"
code, repeated, empty = invoke(output, "--convert")
assert code == 0 and not empty and repeated["alreadyDarkPages"] == 1, (code, repeated)
(scratch / "annotated-dark.pdf").write_bytes(output)
print("PASS opaque-annotated-image-preserved")

for variant in ("low-cross-edge-ink", "partial-mask", "fractional-overlay",
                "second-image", "shading-overlay"):
    candidate = fixture(variant)
    for mode in ("--inspect", "--convert"):
        code, diagnostic, empty = invoke(candidate, mode)
        assert code != 0 and not empty, (variant, mode, code, diagnostic)
    print("PASS refused", variant)
