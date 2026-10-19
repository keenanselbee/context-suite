"""Ignore only exact consecutive duplicate vertices when proving a safe border.

All image samples, PDF paths, and text are independently authored. Requires
Pillow, pypdf, PyMuPDF, and a fresh repository-local scratch directory.
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
engine = Path(args.engine).resolve(strict=True)
scratch = Path(args.scratch).resolve()
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
    image = Image.new("RGB", (140, 90))
    image.putdata([(180 + (x * 37 + y * 17) % 51,) * 3 for y in range(90) for x in range(140)])
    encoded = io.BytesIO()
    image.save(encoded, format="JPEG", quality=94, subsampling=0)
    writer = PdfWriter()
    page = writer.add_blank_page(width=300, height=200)
    art = stream(writer, encoded.getvalue(), Type=NameObject("/XObject"),
                 Subtype=NameObject("/Image"), Width=NumberObject(140), Height=NumberObject(90),
                 BitsPerComponent=NumberObject(8), ColorSpace=NameObject("/DeviceRGB"),
                 Filter=NameObject("/DCTDecode"))
    font = writer._add_object(dictionary(Type=NameObject("/Font"), Subtype=NameObject("/Type1"),
                                         BaseFont=NameObject("/Helvetica")))
    page[NameObject("/Resources")] = dictionary(XObject=dictionary(Im1=art), Font=dictionary(F1=font))
    if case == "duplicates":
        points = ((5, 5), (5, 5), (295, 5), (295, 195), (5, 195), (5, 5), (5, 5))
    elif case == "crossing":
        points = ((5, 5), (5, 5), (295, 5), (150, 100), (295, 195), (5, 195), (5, 5))
    elif case == "too-many-distinct":
        points = ((5, 5), (150, 5), (295, 5), (295, 100), (295, 195),
                  (150, 195), (5, 195), (5, 100), (5, 5))
    else:
        raise ValueError(case)
    path = " ".join(f"{x} {y} {'m' if index == 0 else 'l'}" for index, (x, y) in enumerate(points))
    content = ("q 1 1 1 rg 0 0 300 200 re f Q\n"
               "q 2 2 296 196 re W n\n"
               "q 280 0 0 180 10 10 cm /Im1 Do Q\n"
               f"0 0 0 RG .5 w {path} S\n"
               "0 0 0 rg BT /F1 3 Tf 1 0 0 1 20 193 Tm (Border) Tj ET\nQ\n")
    # The image occupies 84% of the page, so the 80%-bounded composite fallback
    # cannot hide an overly broad stroke footprint by preserving the whole frame.
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
    reader = PdfReader(io.BytesIO(data))
    page = reader.pages[0]
    image = page["/Resources"]["/XObject"]["/Im1"]
    path = [(op, tuple(float(value) for value in values))
            for values, op in ContentStream(page.get_contents(), reader).operations if op in (b"m", b"l", b"S")]
    with pymupdf.open(stream=data, filetype="pdf") as document:
        page = document[0]
        text = [(entry["font"], entry["size"], entry["chars"]) for entry in page.get_texttrace()]
        placements = [(entry["bbox"], entry["transform"], entry["width"], entry["height"])
                      for entry in page.get_image_info()]
        return image._data, path, text, placements, page.get_pixmap(alpha=False)


results = []
for case in ("duplicates", "crossing", "too-many-distinct"):
    source = fixture(case)
    digest = hashlib.sha256(source).hexdigest()
    (scratch / f"{case}-source.pdf").write_bytes(source)
    inspect_code, inspection, empty = invoke(source, "--inspect")
    code, converted, output = invoke(source, "--convert")
    assert code == inspect_code and (code != 0 or converted == inspection and not empty), \
        (case, inspect_code, inspection, code, converted)
    if case == "duplicates":
        assert code == 0 and output, (case, code, converted)
        before = facts(source)
        after = facts(output)
        assert before[:4] == after[:4], "Image bytes, source vertices, text, or placement changed"
        original, dark = before[4], after[4]
        assert all(original.pixel(x, y) == dark.pixel(x, y)
                   for y in range(10, 190) for x in range(10, 290)), "Retained image pixels changed"
        with pymupdf.open(stream=source, filetype="pdf") as source_document, \
                pymupdf.open(stream=output, filetype="pdf") as output_document:
            # Resolve the half-point stroke rather than sampling its blended
            # one-pixel fringe at the ordinary image comparison resolution.
            original_frame = source_document[0].get_pixmap(matrix=pymupdf.Matrix(4, 4), alpha=False)
            converted_frame = output_document[0].get_pixmap(matrix=pymupdf.Matrix(4, 4), alpha=False)
        assert max(original_frame.pixel(20, 400)) < 60 and min(converted_frame.pixel(20, 400)) > 180, \
            "The formerly dark frame must follow the light foreground palette"
        assert max(dark.pixel(1, 1)) < 60 and max(dark.pixel(8, 100)) < 60, \
            "Paper outside the frame/image must darken"
        for mode in ("--inspect", "--convert"):
            repeat_code, repeated, duplicate = invoke(output, mode)
            assert repeat_code == 0 and repeated["changedPages"] == 0 and \
                repeated["alreadyDarkPages"] == 1 and not duplicate, (case, mode, repeated)
        (scratch / f"{case}-dark.pdf").write_bytes(output)
        original.save(scratch / f"{case}-source.png")
        dark.save(scratch / f"{case}-dark.png")
        details = {"converted": True, "imagePixelsExact": True, "sourceVerticesPreserved": True,
                   "frameRecolored": True, "repeatNoOp": True}
    else:
        assert code == 3 and not output, (case, code, converted)
        details = {"refused": True, "reason": converted}
    assert hashlib.sha256(source).hexdigest() == digest
    results.append({"case": case, "sourceSha256": digest, **details})
    print("PASS", case, flush=True)

(scratch / "results.json").write_text(json.dumps({
    "engine": str(engine), "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
    "suiteSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "cases": results,
}, indent=2), encoding="utf-8")
print(f"Passed {len(results)} duplicate-border checks.")
