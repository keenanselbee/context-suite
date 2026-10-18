"""Exercise exact masked-raster retention on a slightly changing dark PDF paper."""

import argparse
import io
import json
import pathlib
import struct
import subprocess
import tempfile
import zlib

import pymupdf
from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, DecodedStreamObject, DictionaryObject,
                           NameObject, NumberObject)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
if not scratch.is_relative_to(repository / ".codex-temp") or scratch.exists():
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)
engine = pathlib.Path(args.engine).resolve(strict=True)


def stream(writer, data, fields, compressed=False):
    item = DecodedStreamObject()
    item.set_data(zlib.compress(data) if compressed else data)
    item.update({NameObject(key): value for key, value in fields.items()})
    if compressed:
        item[NameObject("/Filter")] = NameObject("/FlateDecode")
    return writer._add_object(item)


def fixture(kind):
    writer = PdfWriter()
    page = writer.add_blank_page(width=140, height=100)
    width, height = 96, 64
    pixels = bytearray(width * height * 3)
    mask = bytearray(width * height)
    for y in range(height):
        for x in range(width):
            index = y * width + x
            border = min(x, y, width - 1 - x, height - 1 - y)
            mask[index] = (0 if border < 2 else 64 if border == 2 else
                           160 if border == 3 else 255)
            if kind == "gray":
                color = (59, 60, 62)
                if x in (30, 31, 60, 61) or y in (20, 21, 40, 41):
                    color = (190, 191, 192)
            else:
                color = (34, 45, 69)
                if x in (30, 31, 60, 61) or y in (20, 21, 40, 41):
                    color = (208, 158, 70) if x < 48 else (80, 184, 193)
            if kind == "lost-feature" and 8 <= x < 18 and y in (0, 1):
                mask[index] = 255
                color = (21, 22, 23)
            if kind == "weakened-feature" and 8 <= x < 18 and y in (0, 1):
                mask[index] = 255
                color = (14, 13, 30)
            pixels[index * 3:index * 3 + 3] = bytes(color)
    alpha = stream(writer, bytes(mask), {
        "/Type": NameObject("/XObject"), "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width), "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8), "/ColorSpace": NameObject("/DeviceGray"),
    }, compressed=True)
    if kind == "matte":
        alpha.get_object()[NameObject("/Matte")] = ArrayObject(
            [NumberObject(0), NumberObject(0), NumberObject(0)])
    image_fields = {
        "/Type": NameObject("/XObject"), "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width), "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8), "/ColorSpace": NameObject("/DeviceRGB"),
        "/SMask": alpha,
    }
    if kind in ("jpeg", "jpeg-nonflat"):
        bitmap = Image.frombytes("RGB", (width, height), bytes(pixels))
        encoded = io.BytesIO()
        bitmap.save(encoded, format="JPEG", quality=90)
        image_fields["/Filter"] = NameObject("/DCTDecode")
        image = stream(writer, encoded.getvalue(), image_fields)
    else:
        image = stream(writer, bytes(pixels), image_fields, compressed=True)
    page[NameObject("/Resources")] = DictionaryObject({
        NameObject("/XObject"): DictionaryObject({NameObject("/Art"): image}),
    })
    paper = "0 0 0" if kind == "distant-paper" else ".05098 .047059 .039216"
    content = f"q {paper} rg 0 0 140 100 re f Q\n"
    if kind in ("nonflat", "jpeg-nonflat"):
        content += "q 1 0 0 rg 25 20 10 10 re f Q\n"
    content += "q 96 0 0 64 22 18 cm /Art Do Q\n"
    page[NameObject("/Contents")] = stream(writer, content.encode("ascii"), {})
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as input_file:
        input_file.write(source)
        input_file.seek(0)
        result = subprocess.run([str(engine), mode], stdin=input_file,
                                capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_length + pdf_length
    facts = json.loads(result.stdout[16:16 + metadata_length])
    return 0, facts, result.stdout[16 + metadata_length:]


def image_bytes(source):
    image = PdfReader(io.BytesIO(source)).pages[0]["/Resources"]["/XObject"]["/Art"].get_object()
    mask = image["/SMask"].get_object()
    return image.get_data(), mask.get_data(), str(image["/Filter"])


def encoded_jpeg_and_mask(source):
    image = PdfReader(io.BytesIO(source)).pages[0]["/Resources"]["/XObject"]["/Art"].get_object()
    mask = image["/SMask"].get_object()
    assert image["/Filter"] == "/DCTDecode"
    return image._data, mask.get_data()


results = []
for kind in ("colored", "gray"):
    source = fixture(kind)
    code, inspected, empty = invoke(source, "--inspect")
    assert code == 0 and not empty, (kind, code, inspected)
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output and facts == inspected and facts["preservedImages"] == 1, (kind, code, facts)
    assert image_bytes(source) == image_bytes(output), kind
    assert {0, 64, 160, 255}.issubset(set(image_bytes(source)[1])), kind
    before = pymupdf.open(stream=source, filetype="pdf")[0].get_pixmap()
    after = pymupdf.open(stream=output, filetype="pdf")[0].get_pixmap()
    for y in range(18, 82):
        for x in range(22, 118):
            assert max(abs(a - b) for a, b in zip(before.pixel(x, y),
                                                  after.pixel(x, y))) <= 12, (kind, x, y)
    code, repeat, repeated = invoke(output, "--convert")
    assert code == 0 and repeat["changedPages"] == 0 and not repeated, (kind, code, repeat)
    results.append({"case": kind, "retainedSamples": True, "repeatNoOp": True})

jpeg_source = fixture("jpeg")
(scratch / "jpeg-source.pdf").write_bytes(jpeg_source)
code, inspected, empty = invoke(jpeg_source, "--inspect")
assert code == 0 and not empty, ("jpeg", code, inspected)
code, facts, jpeg_output = invoke(jpeg_source, "--convert")
assert code == 0 and jpeg_output and facts == inspected and facts["preservedImages"] == 1, ("jpeg", code, facts)
(scratch / "jpeg-dark.pdf").write_bytes(jpeg_output)
assert encoded_jpeg_and_mask(jpeg_source) == encoded_jpeg_and_mask(jpeg_output), "jpeg samples or mask changed"
assert {0, 64, 160, 255}.issubset(set(encoded_jpeg_and_mask(jpeg_source)[1]))
source_pdf = pymupdf.open(stream=jpeg_source, filetype="pdf")
output_pdf = pymupdf.open(stream=jpeg_output, filetype="pdf")
source_page, output_page = source_pdf[0], output_pdf[0]
source_image = source_page.get_images(full=True)[0]
output_image = output_page.get_images(full=True)[0]
source_rect = source_page.get_image_rects(source_image[0])[0]
output_rect = output_page.get_image_rects(output_image[0])[0]
assert all(abs(a - b) <= .001 for a, b in zip(source_rect, output_rect))
assert source_rect.get_area() < source_page.rect.get_area() * .5
before, after = source_page.get_pixmap(), output_page.get_pixmap()
before.save(scratch / "jpeg-source.png")
after.save(scratch / "jpeg-dark.png")
matched = total = 0
for y in range(int(source_rect.y0), int(source_rect.y1)):
    for x in range(int(source_rect.x0), int(source_rect.x1)):
        total += 1
        matched += max(abs(a - b) for a, b in zip(before.pixel(x, y), after.pixel(x, y))) <= 1
assert total == 96 * 64 and matched == total, ("jpeg", matched, total)
assert all(abs(a - b) <= 1 for a, b in zip(after.pixel(5, 5), (18, 19, 20)))
for mode in ("--inspect", "--convert"):
    code, repeated, duplicate = invoke(jpeg_output, mode)
    assert code == 0 and repeated["changedPages"] == 0 and not duplicate, ("jpeg", mode, code, repeated)
results.append({"case": "jpeg", "encodedSamplesAndMaskUnchanged": True,
                "exactLocalPixels": matched, "localPixels": total,
                "inspectConvertParity": True, "repeatNoOp": True})

for kind in ("lost-feature", "weakened-feature", "distant-paper", "nonflat", "matte", "jpeg-nonflat"):
    code, reason, output = invoke(fixture(kind), "--convert")
    assert code in (3, 4) and not output, (kind, code, reason)
    results.append({"case": kind, "refused": True})

(scratch / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"Near-dark retained images: {len(results)} cases passed.")
