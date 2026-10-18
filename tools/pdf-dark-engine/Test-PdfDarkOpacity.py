"""Independently exercise bounded translucent PDF paint and contrast.

Fixtures are authored here. Requires pypdf, PyMuPDF and Pillow; the selected
development host is supplied explicitly and is not a production payload pin.
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
from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, DecodedStreamObject, DictionaryObject,
                           FloatObject, NameObject, NumberObject)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
REPOSITORY = pathlib.Path(__file__).resolve().parents[2]
ROOT = pathlib.Path(args.scratch).absolute()
ENGINE = pathlib.Path(args.engine).absolute()
if ".." in ROOT.parts or not ROOT.is_relative_to(REPOSITORY / ".codex-temp") or ROOT.exists():
    raise ValueError("Use a fresh repository-local scratch directory")
if not ENGINE.is_file():
    raise ValueError("PDF dark host is missing")
ROOT.mkdir(parents=True)

PAPER = "0 0.125 0.251 rg 0 0 420 300 re f\n"
PANEL_RECT = pymupdf.Rect(70, 110, 350, 190)
PANEL_GLYPHS = "PANEL LABEL"


def name(value):
    return NameObject(value)


def dictionary(**values):
    return DictionaryObject({name("/" + key): value for key, value in values.items()})


def stream(data, **entries):
    value = DecodedStreamObject()
    value.set_data(data)
    value.update(dictionary(**entries))
    return value


def path_commands(shape, x=70, y=110, width=280, height=80):
    if shape == "rectangle":
        return f"{x} {y} {width} {height} re\n"
    if shape != "rounded":
        raise ValueError(shape)
    radius = 12
    tangent = radius * .55228475
    return (f"{x + radius} {y} m\n"
            f"{x + width - radius} {y} l\n"
            f"{x + width - radius + tangent} {y} "
            f"{x + width} {y + radius - tangent} {x + width} {y + radius} c\n"
            f"{x + width} {y + height - radius} l\n"
            f"{x + width} {y + height - radius + tangent} "
            f"{x + width - radius + tangent} {y + height} "
            f"{x + width - radius} {y + height} c\n"
            f"{x + radius} {y + height} l\n"
            f"{x + radius - tangent} {y + height} "
            f"{x} {y + height - radius + tangent} "
            f"{x} {y + height - radius} c\n"
            f"{x} {y + radius} l\n"
            f"{x} {y + radius - tangent} "
            f"{x + radius - tangent} {y} {x + radius} {y} c\nh\n")


def authored_pdf(*, shape="rectangle", rgb=(0, 1, 0), text_order="before",
                 paper="navy", case="positive"):
    writer = PdfWriter()
    page = writer.add_blank_page(width=420, height=300)
    font = dictionary(Type=name("/Font"), Subtype=name("/Type1"),
                      BaseFont=name("/Helvetica"))
    alpha = dictionary(Type=name("/ExtGState"),
                       ca=FloatObject(.501968), CA=FloatObject(.501968))
    resources = dictionary(Font=dictionary(F1=writer._add_object(font)),
                           ExtGState=dictionary(GSf=writer._add_object(alpha)))
    page[name("/Resources")] = resources
    text = "q 1 1 1 rg BT /F1 22 Tf 1 0 0 1 96 145 Tm (PANEL LABEL) Tj ET Q\n"
    if paper == "navy":
        content = PAPER
    elif paper == "white":
        content = "1 1 1 rg 0 0 420 300 re f\n"
    else:
        content = ""  # An unknown backdrop cannot be normalized safely.
    if case == "opaque-underlay":
        content += "0.7 0.2 0.2 rg 75 115 250 70 re f\n"
    if text_order == "before":
        content += text
    panel_path = path_commands(shape)
    content += ("q\n" + " ".join(str(x) for x in rgb) + " rg /GSf gs\n" +
                panel_path + "f*\nQ\n")
    if case == "overlapping-panels":
        content += ("q 1 0 0 rg /GSf gs\n" +
                    path_commands("rectangle", 220, 120, 160, 60) + "f*\nQ\n")
    if case == "fractional-stroke":
        content += "q /GSf gs 1 1 1 RG 2 w 80 100 m 340 100 l S Q\n"
    if case == "fractional-text":
        content += "q /GSf gs 1 1 1 rg BT /F1 16 Tf 1 0 0 1 80 225 Tm (FAINT) Tj ET Q\n"
    if case == "fractional-image":
        image = stream(b"\xff\x00\x00", Type=name("/XObject"),
                       Subtype=name("/Image"), Width=NumberObject(1),
                       Height=NumberObject(1), ColorSpace=name("/DeviceRGB"),
                       BitsPerComponent=NumberObject(8))
        resources[name("/XObject")] = dictionary(Im=writer._add_object(image))
        content += "q /GSf gs 20 0 0 20 80 230 cm /Im Do Q\n"
    if text_order == "after":
        content += text
    if case == "overlapping-panels" and text_order == "before":
        # This second label crosses the shared area of both translucent fills.
        content += "q 1 1 1 rg BT /F1 16 Tf 1 0 0 1 235 145 Tm (CROSS) Tj ET Q\n"
    content += "q 0.8 0.8 0.8 RG 1 w\n" + panel_path + "S\nQ\n"
    page[name("/Contents")] = writer._add_object(stream(content.encode("ascii")))
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=ROOT) as source:
        source.write(data)
        source.flush()
        source.seek(0)
        result = subprocess.run([str(ENGINE), mode], stdin=source,
                                capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout, "Refusal published PDF bytes"
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    assert len(result.stdout) >= 16
    magic, version, json_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + json_size + pdf_size
    return (0, json.loads(result.stdout[16:16 + json_size]),
            result.stdout[16 + json_size:])


def luminance(rgb):
    linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4
              for v in rgb]
    return .2126 * linear[0] + .7152 * linear[1] + .0722 * linear[2]


def contrast(first, second):
    a, b = sorted((luminance(first), luminance(second)))
    return (b + .05) / (a + .05)


def rendered_glyph_contrast(data):
    doc = pymupdf.open(stream=data, filetype="pdf")
    page = doc[0]
    scale = 3
    pixmap = page.get_pixmap(matrix=pymupdf.Matrix(scale, scale), alpha=False)
    image = Image.frombytes("RGB", (pixmap.width, pixmap.height), pixmap.samples)
    # The center sample stays clear of the label and the curved/rectangular edge.
    background = tuple(v / 255 for v in image.getpixel((300 * scale, 150 * scale)))
    glyph_values = []
    for span in page.get_texttrace():
        if span["seqno"] < 0:
            continue
        for char in span["chars"]:
            codepoint, _, _, box = char
            if codepoint == 32:
                continue
            bounds = pymupdf.Rect(box)
            if not PANEL_RECT.contains(bounds):
                continue
            left = max(0, math.ceil(bounds.x0 * scale))
            top = max(0, math.ceil(bounds.y0 * scale))
            right = min(image.width, math.floor(bounds.x1 * scale))
            bottom = min(image.height, math.floor(bounds.y1 * scale))
            pixels = [tuple(v / 255 for v in image.getpixel((x, y)))
                      for y in range(top, bottom) for x in range(left, right)]
            if pixels:
                glyph_values.append(max(contrast(pixel, background) for pixel in pixels))
    assert glyph_values, "No interior glyph samples"
    return min(glyph_values)


def drawing_geometry(data):
    page = pymupdf.open(stream=data, filetype="pdf")[0]
    drawings = page.get_drawings()
    result = []
    for drawing in drawings:
        items = []
        for item in drawing["items"]:
            items.append((item[0], tuple(str(value) for value in item[1:])))
        result.append((drawing["type"], tuple(items),
                       tuple(round(float(v), 3) for v in drawing["rect"]),
                       round(float(drawing["fill_opacity"] or 1), 6),
                       round(float(drawing["stroke_opacity"] or 1), 6)))
    return result


def translucent_fill(data):
    document = pymupdf.open(stream=data, filetype="pdf")
    fills = [drawing for drawing in document[0].get_drawings()
             if drawing["fill"] is not None and drawing["fill_opacity"] < 1]
    assert len(fills) == 1, "Expected one translucent panel"
    return fills[0]["fill"], fills[0]["fill_opacity"]


def text_geometry(data):
    page = pymupdf.open(stream=data, filetype="pdf")[0]
    return [(tuple(char[0] for char in span["chars"]),
             tuple(round(float(v), 3) for v in span["bbox"]))
            for span in page.get_texttrace()]


def positive(label, **options):
    source = authored_pdf(**options)
    original_hash = hashlib.sha256(source).digest()
    code, inspection, no_pdf = invoke(source, "--inspect")
    assert code == 0 and not no_pdf, (label, code, inspection)
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output and facts == inspection, (label, code, facts)
    assert text_geometry(source) == text_geometry(output), (label, "text geometry")
    assert drawing_geometry(source) == drawing_geometry(output), (label, "path geometry or alpha")
    source_color, source_alpha = translucent_fill(source)
    output_color, output_alpha = translucent_fill(output)
    assert source_alpha == output_alpha and source_color != output_color, \
        (label, "panel alpha changed or source RGB was not adjusted")
    ratio = rendered_glyph_contrast(output)
    assert ratio >= 4.5, (label, "rendered core glyph contrast", ratio)
    again_code, again_facts, again_output = invoke(output, "--convert")
    assert again_code == 0 and not again_output and again_facts["alreadyDarkPages"] == 1, \
        (label, "second pass", again_code, again_facts)
    assert hashlib.sha256(source).digest() == original_hash, label
    (ROOT / f"{label}-source.pdf").write_bytes(source)
    (ROOT / f"{label}-dark.pdf").write_bytes(output)
    print("PASS", label, "contrast", round(ratio, 3))


def refusal(label, fragment, **options):
    source = authored_pdf(**options)
    for mode in ("--inspect", "--convert"):
        code, diagnostic, output = invoke(source, mode)
        assert code == 3 and fragment in diagnostic and not output, \
            (label, mode, code, diagnostic)
    print("PASS", label, "refused:", fragment)


failures = []
for shape in ("rectangle", "rounded"):
    for text_order in ("before", "after"):
        for color_name, rgb in (("green", (0, 1, 0)),
                                ("orange", (.969, .588, .275)),
                                ("red", (1, 0, 0)),
                                ("blue", (0, 0, 1))):
            label = f"{shape}-{text_order}-{color_name}"
            try:
                options = {"shape": shape, "text_order": text_order, "rgb": rgb}
                if text_order == "before" and color_name in ("red", "blue"):
                    # With navy paper and dark ink under a half-alpha pure red
                    # or blue fill, no source-channel adjustment meets 4.6:1.
                    refusal(label, "cannot retain readable text contrast", **options)
                else:
                    positive(label, **options)
            except AssertionError as error:
                failures.append((label, str(error)))

for label, fragment, options in (
    ("white-paper", "verified dark paper", {"paper": "white"}),
    ("unknown-backdrop", "verified dark paper", {"paper": "unknown"}),
    ("fractional-text", "Fractional PDF text", {"case": "fractional-text"}),
    ("fractional-stroke", "Fractional PDF text or strokes", {"case": "fractional-stroke"}),
    ("fractional-image", "Fractional PDF image", {"case": "fractional-image"}),
    ("opaque-underlay", "ambiguous painted backdrop", {"case": "opaque-underlay"}),
    ("crossing-panels", "text crosses overlapping translucent panels",
     {"case": "overlapping-panels"}),
):
    try:
        refusal(label, fragment, **options)
    except AssertionError as error:
        failures.append((label, str(error)))

if failures:
    raise AssertionError("\n".join(f"{label}: {message}" for label, message in failures))
