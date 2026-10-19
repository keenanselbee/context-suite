"""Prove unchanged later images preserve retained earlier image appearance.

All photographic samples, light-ink diagrams, masks, and PDF content are
independently authored. Requires Pillow, pypdf, and PyMuPDF.
"""

import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import subprocess
import tempfile
import zlib

import pymupdf
from PIL import Image, ImageDraw, ImageFilter
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (DecodedStreamObject, DictionaryObject, FloatObject,
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


def photo(writer, width, height, seed):
    image = Image.new("RGB", (width, height))
    image.putdata([(35 + (x * 7 + y * 13 + seed) % 155,
                    40 + (x * 11 + y * 5 + seed) % 150,
                    45 + (x * 3 + y * 17 + seed) % 145)
                   for y in range(height) for x in range(width)])
    encoded = io.BytesIO()
    image.save(encoded, format="JPEG", quality=94, subsampling=0)
    return stream(writer, encoded.getvalue(), Type=NameObject("/XObject"), Subtype=NameObject("/Image"),
                  Width=NumberObject(width), Height=NumberObject(height), BitsPerComponent=NumberObject(8),
                  ColorSpace=NameObject("/DeviceRGB"), Filter=NameObject("/DCTDecode"))


def diagram(writer, changed):
    width, height = 40, 30
    alpha = Image.new("L", (width, height), 0)
    draw = ImageDraw.Draw(alpha)
    for rectangle in ((5, 4, 9, 25), (30, 4, 34, 25), (5, 13, 34, 17)):
        draw.rectangle(rectangle, fill=255)
    alpha = alpha.filter(ImageFilter.GaussianBlur(.35))
    mask = stream(writer, zlib.compress(alpha.tobytes()), Type=NameObject("/XObject"),
                  Subtype=NameObject("/Image"), Width=NumberObject(width), Height=NumberObject(height),
                  BitsPerComponent=NumberObject(8), ColorSpace=NameObject("/DeviceGray"),
                  Filter=NameObject("/FlateDecode"))
    color = (0, 0, 0) if changed else (244, 235, 216)
    return stream(writer, zlib.compress(bytes(color) * (width * height)), Type=NameObject("/XObject"),
                  Subtype=NameObject("/Image"), Width=NumberObject(width), Height=NumberObject(height),
                  BitsPerComponent=NumberObject(8), ColorSpace=NameObject("/DeviceRGB"),
                  Filter=NameObject("/FlateDecode"), SMask=mask)


def fixture(case):
    writer = PdfWriter()
    page = writer.add_blank_page(width=300, height=200)
    background = photo(writer, 140, 90, 0)
    foreground = diagram(writer, changed=case == "changed-masked") if "masked" in case else photo(writer, 40, 30, 23)
    resources = dictionary(XObject=dictionary(Back=background, Front=foreground))
    if case == "fractional-opaque":
        resources[NameObject("/ExtGState")] = dictionary(Half=writer._add_object(dictionary(
            Type=NameObject("/ExtGState"), ca=FloatObject(.5))))
    page[NameObject("/Resources")] = resources
    opacity = "/Half gs " if case == "fractional-opaque" else ""
    # The earlier image occupies 84% of the page, preventing an 80%-bounded
    # original-composite fallback from disguising a later-image guard mistake.
    content = ("q 1 1 1 rg 0 0 300 200 re f Q\n"
               "q 280 0 0 180 10 10 cm /Back Do Q\n"
               f"q {opacity}40 0 0 30 40 100 cm /Front Do Q\n")
    page[NameObject("/Contents")] = stream(writer, content.encode("ascii"))
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
    page = PdfReader(io.BytesIO(data)).pages[0]
    images = {}
    for name, reference in page["/Resources"]["/XObject"].items():
        image = reference.get_object()
        mask = image.get("/SMask")
        images[str(name)] = (image._data, image["/ColorSpace"], image["/Width"], image["/Height"],
                             mask.get_object().get_data() if mask else None)
    with pymupdf.open(stream=data, filetype="pdf") as document:
        page = document[0]
        placement = [(image["bbox"], image["transform"], image["width"], image["height"])
                     for image in page.get_image_info()]
        return images, placement, tuple(page.mediabox), page.get_pixmap(alpha=False)


results = []
for case in ("opaque", "unchanged-masked", "changed-masked", "fractional-opaque"):
    source = fixture(case)
    digest = hashlib.sha256(source).hexdigest()
    (scratch / f"{case}-source.pdf").write_bytes(source)
    inspect_code, inspection, empty = invoke(source, "--inspect")
    code, converted, output = invoke(source, "--convert")
    assert inspect_code == code and (code != 0 or inspection == converted and not empty), \
        (case, inspect_code, inspection, code, converted)
    if case in ("changed-masked", "fractional-opaque"):
        assert code == 3 and not output, (case, code, converted)
        details = {"refused": True, "reason": converted}
    else:
        assert code == 0 and output, (case, code, converted)
        before, after = facts(source), facts(output)
        assert before[:3] == after[:3], "Image/mask data, placement, or page geometry changed"
        original, dark = before[3], after[3]
        assert all(original.pixel(x, y) == dark.pixel(x, y)
                   for y in range(10, 190) for x in range(10, 290)), "Layered source pixels changed"
        assert max(dark.pixel(5, 5)) < 60, "Unrelated paper did not darken"
        for mode in ("--inspect", "--convert"):
            repeat_code, repeated, duplicate = invoke(output, mode)
            assert repeat_code == 0 and repeated["changedPages"] == 0 and \
                repeated["alreadyDarkPages"] == 1 and not duplicate, (case, mode, repeated)
        (scratch / f"{case}-dark.pdf").write_bytes(output)
        original.save(scratch / f"{case}-source.png")
        dark.save(scratch / f"{case}-dark.png")
        details = {"converted": True, "imageAndMaskBytesPreserved": True,
                   "layeredPixelsExact": True, "placementsPreserved": True, "repeatNoOp": True}
    assert hashlib.sha256(source).hexdigest() == digest
    results.append({"case": case, "sourceSha256": digest, **details})
    print("PASS", case, flush=True)

(scratch / "results.json").write_text(json.dumps({
    "engine": str(engine), "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
    "suiteSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "cases": results,
}, indent=2), encoding="utf-8")
print(f"Passed {len(results)} image-layering checks.")
