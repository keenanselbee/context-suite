"""Check glyph-sized contrast contact over an independently authored JPEG diagram.

The spaced capital I glyphs leave a wide gap inside their shared text-run box.
Test-only dependencies: Pillow, ReportLab, PyMuPDF and pypdf.
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
import reportlab
from PIL import Image, ImageDraw, ImageFont
from pypdf import PdfReader, PdfWriter
from pypdf.generic import DecodedStreamObject, NameObject, NumberObject
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase.pdfmetrics import registerFont
from reportlab.pdfbase.ttfonts import TTFont
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
registerFont(TTFont("GlyphContrastFixture",
                  str(pathlib.Path(reportlab.__file__).parent / "fonts" / "Vera.ttf")))
image_font = ImageFont.load_default(size=17)


def diagram(mark):
    image = Image.new("RGB", (640, 400), "white")
    draw = ImageDraw.Draw(image)
    for x in (35, 400):
        for y in (40, 245):
            draw.rectangle((x, y, x + 195, y + 95), fill=(145, 211, 240),
                           outline=(35, 45, 55), width=2)
            draw.line((x + 12, y + 30, x + 175, y + 30), fill=(35, 45, 55), width=2)
            draw.text((x + 9, y + 7), "Account", font=image_font, fill=(20, 20, 20))
            draw.text((x + 9, y + 45), "+ balance: Integer", font=image_font,
                      fill=(20, 20, 20))
    draw.line((150, 136, 150, 205, 480, 205, 480, 244), fill=(35, 45, 55), width=2)
    draw.line((575, 160, 615, 220), fill=(220, 0, 0), width=9)
    draw.line((615, 160, 575, 220), fill=(220, 0, 0), width=9)
    if mark == "gap":
        # PDF x 239..244 lies between both I glyphs, yet inside the run box.
        draw.rectangle((318, 24, 326, 59), fill=(25, 28, 34))
    elif mark == "glyph":
        # PDF x 220..226 touches the first glyph's actual ink.
        draw.rectangle((290, 24, 301, 59), fill=(25, 28, 34))
    encoded = io.BytesIO()
    image.save(encoded, format="JPEG", quality=95, subsampling=0)
    return encoded.getvalue()


def fixture(mark, font="GlyphContrastFixture", label=True):
    output = io.BytesIO()
    canvas = Canvas(output, pagesize=(480, 340), pageCompression=0)
    # Dark source paper excludes the separate white-paper image-composite
    # fallback, so these cases exercise local glyph/image contrast directly.
    canvas.setFillColorRGB(.07, .08, .09)
    canvas.rect(0, 0, 480, 340, fill=1, stroke=0)
    canvas.drawImage(ImageReader(io.BytesIO(diagram(mark))), 20, 20, width=440, height=280)
    if label:
        canvas.setFont(font, 20)
        # Colored ink disables the policy's narrow neutral-foreground contact exception.
        canvas.setFillColorRGB(.24, .04, .12)
        canvas.drawString(220, 270, "I     I")
    canvas.save()
    if font == "Helvetica" or not label:
        return output.getvalue()
    # ReportLab normally subsets Vera with a private encoding. Use the complete
    # test font and WinAnsi ASCII mapping so the engine can verify actual glyph
    # outlines, including the empty space glyph.
    writer = PdfWriter(clone_from=PdfReader(io.BytesIO(output.getvalue())))
    fonts = writer.pages[0]["/Resources"]["/Font"]
    embedded = next(ref.get_object() for ref in fonts.values()
                    if ref.get_object().get("/Subtype") == "/TrueType")
    font_bytes = (pathlib.Path(reportlab.__file__).parent / "fonts" / "Vera.ttf").read_bytes()
    program = DecodedStreamObject()
    program.set_data(font_bytes)
    program[NameObject("/Length1")] = NumberObject(len(font_bytes))
    embedded["/FontDescriptor"][NameObject("/FontFile2")] = writer._add_object(program)
    embedded[NameObject("/Encoding")] = NameObject("/WinAnsiEncoding")
    embedded["/FontDescriptor"][NameObject("/Flags")] = NumberObject(32)
    rewritten = io.BytesIO()
    writer.write(rewritten)
    return rewritten.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as stream:
        stream.write(source)
        stream.seek(0)
        result = subprocess.run([str(engine), mode], stdin=stream,
                                capture_output=True, timeout=90)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, facts_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + facts_size + pdf_size
    return 0, json.loads(result.stdout[16:16 + facts_size]), result.stdout[16 + facts_size:]


def glyphs(data):
    with pymupdf.open(stream=data, filetype="pdf") as document:
        spans = document[0].get_texttrace()
        assert len(spans) == 1, spans
        chars = spans[0]["chars"]
        assert [char[0] for char in chars] == [73, 32, 32, 32, 32, 32, 73], chars
        return spans[0], [pymupdf.Rect(chars[index][3]) for index in (0, 6)]


def image_bytes(data):
    page = PdfReader(io.BytesIO(data)).pages[0]
    images = [ref.get_object() for ref in page["/Resources"]["/XObject"].values()
              if ref.get_object().get("/Subtype") == "/Image"]
    assert len(images) == 1
    return images[0]._data


def text_ink_on_mark(source, mark, font):
    blank = fixture(mark, font=font, label=False)
    with pymupdf.open(stream=source, filetype="pdf") as document:
        painted = document[0].get_pixmap(matrix=pymupdf.Matrix(4, 4))
    with pymupdf.open(stream=blank, filetype="pdf") as document:
        underlying = document[0].get_pixmap(matrix=pymupdf.Matrix(4, 4))
    x0, x1 = (239, 244) if mark == "gap" else (220, 226)
    return sum(max(abs(a - b) for a, b in zip(painted.pixel(x, y),
                                                underlying.pixel(x, y))) > 10
               for y in range(57 * 4, 75 * 4) for x in range(x0 * 4, x1 * 4))


positives = ("plain", "gap")
negatives = ("glyph", "unknown-font-gap")
results = []
for case in (*positives, *negatives):
    mark = "gap" if case in ("gap", "unknown-font-gap") else "glyph" if case == "glyph" else "plain"
    font = "Helvetica" if case == "unknown-font-gap" else "GlyphContrastFixture"
    source = fixture(mark, font=font)
    source_hash = hashlib.sha256(source).hexdigest()
    (scratch / f"{case}-source.pdf").write_bytes(source)
    with pymupdf.open(stream=source, filetype="pdf") as document:
        document[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(scratch / f"{case}-source.png")
    before, boxes = glyphs(source)
    assert boxes[0].x1 < 231 and boxes[1].x0 > 251, boxes
    if mark == "gap":
        assert boxes[0].x1 < 239 and boxes[1].x0 > 244, boxes
    if mark != "plain":
        contact = text_ink_on_mark(source, mark, font)
        assert (contact == 0 if mark == "gap" else contact > 100), (case, contact)
    code, facts, output = invoke(source, "--convert")
    check_code, check_facts, empty = invoke(source, "--inspect")
    assert code == check_code and (code != 0 or facts == check_facts and not empty)
    details = {"exitCode": code, "diagnostic": facts if code else None}
    if mark != "plain":
        details["sourceInkPixelsInMark"] = contact
    if case in negatives:
        passed = code == 3 and not output
        if output:
            (scratch / f"{case}-unexpected-dark.pdf").write_bytes(output)
            with pymupdf.open(stream=output, filetype="pdf") as document:
                document[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(
                    scratch / f"{case}-unexpected-dark.png")
    elif code:
        passed = False
    else:
        (scratch / f"{case}-dark.pdf").write_bytes(output)
        after, converted_boxes = glyphs(output)
        with pymupdf.open(stream=output, filetype="pdf") as document:
            paper = document[0].get_pixmap().pixel(5, 5)
            document[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(
                scratch / f"{case}-dark.png")
        repeat_code, repeat_facts, duplicate = invoke(output, "--convert")
        details.update({"glyphGeometryPreserved": boxes == converted_boxes,
                        "imageRecolored": image_bytes(source) != image_bytes(output),
                        "textPreserved": before["chars"] == after["chars"],
                        "darkPaper": max(paper) < 60,
                        "repeatNoOp": repeat_code == 0 and
                        repeat_facts["changedPages"] == 0 and not duplicate})
        passed = all(details[key] for key in ("glyphGeometryPreserved", "imageRecolored",
                                               "textPreserved", "darkPaper", "repeatNoOp"))
    details["sourceHashUnchanged"] = source_hash == hashlib.sha256(source).hexdigest()
    results.append({"case": case, "passed": passed, "details": details})
    print("PASS" if passed else "FAIL", case, details, flush=True)

report = {"engine": str(engine), "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
          "results": results}
(scratch / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
if not all(result["passed"] for result in results):
    raise AssertionError("Glyph/image contrast cases failed; see scratch report.json")
print(f"Passed {len(results)} glyph/image contrast checks.")
