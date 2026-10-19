"""Preserve neutral text and pure-stroke annotations crossing retained images.

All image samples and PDF geometry are authored here. Dark blue paper prevents
a white original-composite fallback from masking the annotation admission rule.
Requires Pillow, pypdf, and PyMuPDF.
"""

import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import subprocess
import tempfile

import pymupdf
from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ContentStream, DecodedStreamObject, DictionaryObject,
                           NameObject, NumberObject)


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


def dictionary(**entries):
    return DictionaryObject({NameObject("/" + key): value for key, value in entries.items()})


def stream(writer, data, **entries):
    value = DecodedStreamObject()
    value.set_data(data)
    value.update(dictionary(**entries))
    return writer._add_object(value)


def fixture(case):
    image = Image.new("RGB", (180, 110))
    image.putdata([(170 + (x * 37 + y * 17) % 61,) * 3 for y in range(110) for x in range(180)])
    jpeg = io.BytesIO()
    image.save(jpeg, format="JPEG", quality=94, subsampling=0)
    writer = PdfWriter()
    page = writer.add_blank_page(width=360, height=240)
    art = stream(writer, jpeg.getvalue(), Type=NameObject("/XObject"), Subtype=NameObject("/Image"),
                 Width=NumberObject(180), Height=NumberObject(110), BitsPerComponent=NumberObject(8),
                 ColorSpace=NameObject("/DeviceRGB"), Filter=NameObject("/DCTDecode"))
    font = writer._add_object(dictionary(Type=NameObject("/Font"), Subtype=NameObject("/Type1"),
                                         BaseFont=NameObject("/Helvetica")))
    page[NameObject("/Resources")] = dictionary(XObject=dictionary(Im1=art), Font=dictionary(F1=font))
    content = ["0 .125 .251 rg 0 0 360 240 re f"]
    if case == "prior-paint":
        content.append("q .8 .1 .1 rg 225 104 12 8 re f Q")
    content.append("q 180 0 0 110 40 60 cm /Im1 Do Q")
    if case == "yellow-stroke":
        # Bright yellow would normally be dimmed into the stable luminance
        # range. Original annotation admission must retain its exact source ink.
        content.append("1 1 0 RG 2 w 205 120 m 260 120 l 250 114 m 260 120 l 250 126 l S")
    elif case == "mixed-fill-stroke":
        content.append("1 1 0 rg .09 .12 .16 RG 2 w 205 117 m 250 117 l 250 112 l "
                       "262 120 l 250 128 l 250 123 l 205 123 l h B")
    else:
        color = ".09 .12 .16" if case == "dark-label" else "1 1 1"
        content.append(f"{color} rg BT /F1 14 Tf 1 0 0 1 208 105 Tm (WHITE) Tj ET")
    page[NameObject("/Contents")] = stream(writer, ("\n".join(content) + "\n").encode("ascii"))
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(data)
        file.seek(0)
        process = subprocess.run([str(engine), mode], stdin=file, capture_output=True, timeout=45)
        file.seek(0)
        assert file.read() == data
    if process.returncode:
        assert not process.stdout
        return process.returncode, process.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_size + pdf_size
    return (0, json.loads(process.stdout[16:16 + metadata_size]), process.stdout[16 + metadata_size:])


def facts(data):
    reader = PdfReader(io.BytesIO(data))
    page = reader.pages[0]
    image = page["/Resources"]["/XObject"]["/Im1"]
    geometry = [(op, tuple(float(value) for value in operands))
                for operands, op in ContentStream(page.get_contents(), reader).operations
                if op in (b"m", b"l", b"h", b"S", b"B")]
    with pymupdf.open(stream=data, filetype="pdf") as document:
        page = document[0]
        text = [(span["font"], span["size"], span["chars"], span["color"])
                for span in page.get_texttrace()]
        strokes = [(item["items"], item["width"], item["color"], item["stroke_opacity"])
                   for item in page.get_drawings() if item["color"] is not None]
        placements = [(item["bbox"], item["transform"], item["width"], item["height"])
                      for item in page.get_image_info()]
        return image._data, geometry, text, strokes, placements, page.get_pixmap(alpha=False)


def contrast(first, second):
    def luminance(color):
        values = [value / 12.92 if value <= .04045 else ((value + .055) / 1.055) ** 2.4
                  for value in color]
        return sum(value * weight for value, weight in zip(values, (.2126, .7152, .0722)))
    low, high = sorted((luminance(first), luminance(second)))
    return (high + .05) / (low + .05)


results = []
for case in ("neutral-white", "yellow-stroke", "dark-label", "mixed-fill-stroke", "prior-paint"):
    source = fixture(case)
    digest = hashlib.sha256(source).hexdigest()
    (scratch / f"{case}-source.pdf").write_bytes(source)
    inspect_code, inspection, empty = invoke(source, "--inspect")
    code, converted, output = invoke(source, "--convert")
    assert inspect_code == code and (code != 0 or inspection == converted and not empty), \
        (case, inspect_code, inspection, code, converted)
    if case not in ("neutral-white", "yellow-stroke"):
        assert code == 3 and not output, (case, code, converted)
        details = {"refused": True, "reason": converted}
    else:
        assert code == 0 and output, (case, code, converted)
        before, after = facts(source), facts(output)
        assert before[:5] == after[:5], "Image bytes, ink colors, or annotation geometry changed"
        original, dark = before[5], after[5]
        assert all(original.pixel(x, y) == dark.pixel(x, y)
                   for y in range(71, 179) for x in range(41, 219)), "Image/ink overlap pixels changed"
        ink = (255, 255, 255) if case == "neutral-white" else (255, 255, 0)
        opaque_ink = [(x, y) for y in range(108, 140) for x in range(221, 265)
                      if original.pixel(x, y) == ink]
        assert len(opaque_ink) > 10 and all(dark.pixel(x, y) == ink for x, y in opaque_ink), \
            "Opaque original ink beyond the image changed"
        color = tuple(value / 255 for value in ink)
        original_paper = tuple(value / 255 for value in original.pixel(300, 120))
        dark_paper = tuple(value / 255 for value in dark.pixel(300, 120))
        assert max(dark.pixel(300, 120)) < 60 and \
            contrast(color, dark_paper) >= max(4.5, contrast(color, original_paper))
        for mode in ("--inspect", "--convert"):
            repeat_code, repeated, duplicate = invoke(output, mode)
            assert repeat_code == 0 and repeated["changedPages"] == 0 and \
                repeated["alreadyDarkPages"] == 1 and not duplicate, (case, mode, repeated)
        (scratch / f"{case}-dark.pdf").write_bytes(output)
        original.save(scratch / f"{case}-source.png")
        dark.save(scratch / f"{case}-dark.png")
        details = {"converted": True, "imageAndInkPixelsExact": True,
                   "geometryAndColorsPreserved": True, "outsideContrastPreserved": True,
                   "repeatNoOp": True}
    assert hashlib.sha256(source).hexdigest() == digest
    results.append({"case": case, "sourceSha256": digest, **details})
    print("PASS", case, flush=True)

(scratch / "results.json").write_text(json.dumps({
    "engine": str(engine), "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
    "suiteSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "cases": results,
}, indent=2), encoding="utf-8")
print(f"Passed {len(results)} crossing-annotation checks.")
