"""Exercise foreground PDF rules, translucent crossings and fixed-point color.

All fixtures are authored here. The development host is supplied explicitly;
this test does not change a production payload pin or read lecture documents.
Requires pypdf, PyMuPDF and Pillow.
"""

import argparse
import io
import json
import math
from pathlib import Path
import struct
import subprocess

import pymupdf
from PIL import Image
from pypdf import PdfWriter
from pypdf.generic import (ArrayObject, DecodedStreamObject, DictionaryObject,
                           FloatObject, NameObject, NumberObject, TextStringObject)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
REPOSITORY = Path(__file__).resolve().parents[2]
SCRATCH = Path(args.scratch).absolute()
ENGINE = Path(args.engine).absolute()
if ".." in SCRATCH.parts or not SCRATCH.is_relative_to(REPOSITORY / ".codex-temp") or SCRATCH.exists():
    raise ValueError("Use a fresh repository-local scratch directory")
if not ENGINE.is_file():
    raise ValueError("PDF dark host is missing")
SCRATCH.mkdir(parents=True)

PAPER = "0 0.125 0.251 rg 0 0 420 300 re f\n"
ORANGE = (.969, .588, .275)
ALPHA = .501968


def dictionary(**values):
    return DictionaryObject({NameObject("/" + key): value for key, value in values.items()})


def pdf_stream(content):
    result = DecodedStreamObject()
    result.set_data(content.encode("ascii"))
    return result


def compound_rule(y=148, height=1.5, x=70, width=280):
    """Two closed axis-aligned subpaths with a single skinny envelope."""
    middle = x + width * .45
    right = x + width
    return (f"{x} {y} m {middle} {y} l {middle} {y + height} l "
            f"{x} {y + height} l h\n"
            f"{middle} {y} m {right} {y} l {right} {y + height} l "
            f"{middle} {y + height} l h\n")


def fixture(case, panel_color=(0, 1, 0)):
    writer = PdfWriter()
    page = writer.add_blank_page(width=420, height=300)
    font_values = dictionary(Type=NameObject("/Font"),
                             Subtype=NameObject("/Type1"),
                             BaseFont=NameObject("/Helvetica"))
    if case in ("border-around-label", "border-crosses-label"):
        # Accurate Helvetica advances keep the conservative native text box
        # inside the panel; Standard 14 no-width fallback is one em/character.
        widths = {32: 278, 65: 667, 66: 667, 69: 667,
                  76: 556, 78: 722, 80: 667}
        font_values[NameObject("/FirstChar")] = NumberObject(32)
        font_values[NameObject("/LastChar")] = NumberObject(80)
        font_values[NameObject("/Widths")] = ArrayObject([
            NumberObject(widths.get(code, 1000)) for code in range(32, 81)])
        font_values[NameObject("/FontDescriptor")] = writer._add_object(dictionary(
            Type=NameObject("/FontDescriptor"), FontName=NameObject("/Helvetica"),
            Flags=NumberObject(32),
            FontBBox=ArrayObject([NumberObject(-166), NumberObject(-225),
                                  NumberObject(1000), NumberObject(931)]),
            ItalicAngle=NumberObject(0), Ascent=NumberObject(718),
            Descent=NumberObject(-207), CapHeight=NumberObject(718),
            StemV=NumberObject(88)))
    font = writer._add_object(font_values)
    resources = dictionary(Font=dictionary(F1=font))
    if case in ("green-crossing", "pink-crossing", "opaque-conflict",
                "diagonal-nearby"):
        alpha = writer._add_object(dictionary(Type=NameObject("/ExtGState"),
                                              ca=FloatObject(ALPHA), CA=FloatObject(ALPHA)))
        resources[NameObject("/ExtGState")] = dictionary(GSf=alpha)
    page[NameObject("/Resources")] = resources
    content = PAPER
    if case in ("orange-on-white", "orange-on-gray"):
        background = "1 1 1" if case == "orange-on-white" else ".913725 .929412 .933333"
        content = f"{background} rg 0 0 420 300 re f\n"
    if case in ("orange-on-white", "orange-on-gray"):
        content += "q .921569 .337255 0 rg\n" + compound_rule() + "f* Q\n"
    elif case == "compound-white":
        content += "q 1 1 1 rg\n" + compound_rule() + "f* Q\n"
    elif case == "colored-underline":
        rgb = " ".join(map(str, ORANGE))
        content += (f"q {rgb} rg BT /F1 20 Tf 1 0 0 1 90 151 Tm "
                    "(Course help) Tj ET Q\n")
        content += f"q {rgb} rg\n" + compound_rule(y=148, x=90, width=170) + "f* Q\n"
        action = dictionary(S=NameObject("/URI"),
                            URI=TextStringObject("https://example.invalid/course-help"))
        annotation = dictionary(Type=NameObject("/Annot"),
                                Subtype=NameObject("/Link"),
                                Rect=ArrayObject([NumberObject(90), NumberObject(145),
                                                  NumberObject(260), NumberObject(175)]),
                                Border=ArrayObject([NumberObject(0), NumberObject(0),
                                                    NumberObject(0)]), A=action)
        page[NameObject("/Annots")] = ArrayObject([writer._add_object(annotation)])
    elif case == "broad-white":
        content += "q 1 1 1 rg 90 80 240 140 re f Q\n"
    elif case in ("green-crossing", "pink-crossing", "opaque-conflict",
                 "diagonal-nearby"):
        content += ("q 1 1 1 rg BT /F1 20 Tf 1 0 0 1 94 170 Tm "
                    "(PANEL LABEL) Tj ET Q\n")
        content += "q 1 1 1 rg\n" + compound_rule(y=144) + "f* Q\n"
        if case == "opaque-conflict":
            content += "q 0 0 0 rg 120 135 65 25 re f Q\n"
        if case == "diagonal-nearby":
            # A single diagonal 2.5 pt stroke is separated from the rule by
            # 10 pt horizontally and 14.5 pt vertically. Acute-join miter
            # padding would invent an overlap that the path cannot paint.
            content += "q 0.078 0.992 0.227 RG 2.5 w 360 160 m 382 182 l S Q\n"
        rgb = " ".join(map(str, panel_color))
        content += (f"q {rgb} rg /GSf gs "
                    "70 110 280 80 re f* Q\n")
        # Pure black strokes map to Ink, the same target as the white rule.
        content += "q 0 0 0 RG 1 w 70 110 280 80 re S Q\n"
    elif case in ("border-around-label", "border-crosses-label"):
        content += "q 0 0 0 rg 80 80 250 140 re f Q\n"
        if case == "border-around-label":
            content += "q 0 0 0 RG 2 w 80 80 250 140 re S Q\n"
        else:
            content += "q 0 0 0 RG 2 w 95 150 m 315 150 l S Q\n"
        content += ("q 1 1 1 rg BT /F1 24 Tf 1 0 0 1 110 150 Tm "
                    "(PANEL LABEL) Tj ET Q\n")
    else:
        raise ValueError(case)
    page[NameObject("/Contents")] = writer._add_object(pdf_stream(content))
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(source, mode):
    result = subprocess.run([str(ENGINE), mode], input=source,
                            capture_output=True, timeout=60)
    if result.returncode:
        assert not result.stdout, "Refusal published PDF bytes"
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    assert len(result.stdout) >= 16
    magic, version, json_length, pdf_length = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + json_length + pdf_length
    facts = json.loads(result.stdout[16:16 + json_length])
    return 0, facts, result.stdout[16 + json_length:]


def page_drawings(pdf):
    document = pymupdf.open(stream=pdf, filetype="pdf")
    return document[0].get_drawings()


def luminance(color):
    linear = [value / 12.92 if value <= .04045 else
              ((value + .055) / 1.055) ** 2.4 for value in color]
    return sum(value * weight for value, weight in
               zip(linear, (.2126, .7152, .0722)))


def contrast(a, b):
    low, high = sorted((luminance(a), luminance(b)))
    return (high + .05) / (low + .05)


def drawn_geometry(pdf):
    result = []
    for drawing in page_drawings(pdf):
        result.append((drawing["type"],
                       tuple((part[0], tuple(str(item) for item in part[1:]))
                             for part in drawing["items"]),
                       tuple(round(float(value), 3) for value in drawing["rect"]),
                       round(float(drawing["fill_opacity"] or 1), 6),
                       round(float(drawing["stroke_opacity"] or 1), 6)))
    return result


def text_geometry(pdf):
    document = pymupdf.open(stream=pdf, filetype="pdf")
    return [(tuple(char[0] for char in span["chars"]),
             tuple(round(float(v), 3) for v in span["bbox"]))
            for span in document[0].get_texttrace()]


def sample_color(pdf, x, y, scale=8):
    document = pymupdf.open(stream=pdf, filetype="pdf")
    page = document[0]
    pixmap = page.get_pixmap(matrix=pymupdf.Matrix(scale, scale), alpha=False)
    image = Image.frombytes("RGB", (pixmap.width, pixmap.height), pixmap.samples)
    return tuple(value / 255 for value in image.getpixel((round(x * scale), round(y * scale))))


def rendered_label_contrast(pdf, background):
    document = pymupdf.open(stream=pdf, filetype="pdf")
    page = document[0]
    scale = 3
    pixmap = page.get_pixmap(matrix=pymupdf.Matrix(scale, scale), alpha=False)
    image = Image.frombytes("RGB", (pixmap.width, pixmap.height), pixmap.samples)
    measured = []
    for span in page.get_texttrace():
        for codepoint, _, _, box in span["chars"]:
            if codepoint == 32:
                continue
            bounds = pymupdf.Rect(box)
            if not pymupdf.Rect(80, 80, 330, 220).contains(bounds):
                continue
            left = max(0, math.ceil(bounds.x0 * scale))
            top = max(0, math.ceil(bounds.y0 * scale))
            right = min(image.width, math.floor(bounds.x1 * scale))
            bottom = min(image.height, math.floor(bounds.y1 * scale))
            pixels = [tuple(value / 255 for value in image.getpixel((x, y)))
                      for y in range(top, bottom) for x in range(left, right)]
            if pixels:
                measured.append(max(contrast(pixel, background) for pixel in pixels))
    assert measured, "No interior label pixels"
    return min(measured)


def find_fill(pdf, *, opacity=None, shape=None):
    fills = [drawing for drawing in page_drawings(pdf)
             if drawing["fill"] is not None and
             (opacity is None or abs(drawing["fill_opacity"] - opacity) < .00001) and
             (shape is None or shape(drawing["rect"]))]
    assert len(fills) == 1, ("expected exactly one selected fill", len(fills))
    return fills[0]


def positive(label, case, *, panel_color=(0, 1, 0)):
    source = fixture(case, panel_color)
    (SCRATCH / f"{label}-source.pdf").write_bytes(source)
    code, inspected, no_pdf = invoke(source, "--inspect")
    assert code == 0 and not no_pdf, (label, code, inspected)
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output and inspected == facts, (label, code, facts)
    assert drawn_geometry(source) == drawn_geometry(output), (label, "path geometry/alpha changed")
    assert text_geometry(source) == text_geometry(output), (label, "text geometry changed")
    if case in ("compound-white", "colored-underline", "orange-on-white", "orange-on-gray"):
        rule = find_fill(output, shape=lambda rect: rect.height <= 2 and rect.width >= 100)
        assert luminance(rule["fill"]) >= .225 - .00001, (label, "rule lost foreground role", rule["fill"])
        ratio = contrast(sample_color(output, 120, 151.25), sample_color(output, 120, 141))
        assert ratio >= 4.5, (label, "rendered rule contrast", ratio)
        if case == "colored-underline":
            assert all(abs(a - b) < .001 for a, b in zip(rule["fill"], ORANGE)), \
                (label, "underline hue or luminance changed", rule["fill"])
    elif case == "broad-white":
        panel = find_fill(output, shape=lambda rect: rect.width > 200 and 100 < rect.height < 200)
        assert luminance(panel["fill"]) < .04, (label, "broad panel became foreground", panel["fill"])
    elif case == "border-around-label":
        background = sample_color(output, 290, 150)
        ratio = rendered_label_contrast(output, background)
        assert ratio >= 4.5, (label, "rendered label/panel contrast", ratio)
    else:
        panel = find_fill(output, opacity=ALPHA)
        rule = find_fill(output, shape=lambda rect: rect.height <= 2 and rect.width >= 100)
        assert abs(panel["fill_opacity"] - ALPHA) < .00001
        assert luminance(rule["fill"]) >= .225 - .00001, (label, "rule lost foreground role")
        ratio = contrast(sample_color(output, 250, 155.25), sample_color(output, 250, 144))
        assert ratio >= 4.5, (label, "rendered rule/panel contrast", ratio)
    second_code, second_facts, second_pdf = invoke(output, "--convert")
    assert second_code == 0 and not second_pdf and second_facts["alreadyDarkPages"] == 1, \
        (label, "second pass was not a no-op", second_code, second_facts)
    (SCRATCH / f"{label}-dark.pdf").write_bytes(output)
    print("PASS", label)


def refusal(label, case, fragment):
    source = fixture(case)
    for mode in ("--inspect", "--convert"):
        code, diagnostic, no_pdf = invoke(source, mode)
        assert code == 3 and fragment in diagnostic and not no_pdf, \
            (label, mode, code, diagnostic)
    print("PASS", label, "refused:", fragment)


positive("compound-white-separator", "compound-white")
positive("orange-on-white-first-pass-fixed-point", "orange-on-white")
positive("orange-on-gray-first-pass-fixed-point", "orange-on-gray")
positive("colored-link-underline", "colored-underline")
positive("broad-white-panel", "broad-white")
positive("border-surrounding-label", "border-around-label")
positive("green-panel-over-white-rule", "green-crossing", panel_color=(0, 1, 0))
positive("pink-panel-over-white-rule", "pink-crossing", panel_color=(1, .8, 1))
positive("diagonal-stroke-near-rule", "diagonal-nearby", panel_color=(0, 1, 0))
refusal("opaque-paint-under-panel", "opaque-conflict", "ambiguous painted backdrop")
refusal("border-crosses-label", "border-crosses-label", "Covered dark panel")
