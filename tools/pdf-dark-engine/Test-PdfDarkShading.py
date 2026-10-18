"""Check bounded RGB axial background shading with authored PDF fixtures.

Requires pypdf, ReportLab and PyMuPDF. Use a fresh repository-local scratch
directory and a newly built development host. No lecture PDF is copied here.
"""

import argparse
import io
import json
import pathlib
import struct
import subprocess
import tempfile

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, BooleanObject, DecodedStreamObject,
                           DictionaryObject, FloatObject, NameObject,
                           NumberObject)
from reportlab.pdfgen.canvas import Canvas


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
engine = pathlib.Path(args.engine).resolve(strict=True)
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)


def name(value):
    return NameObject(value)


def array(*values):
    return ArrayObject(list(values))


def number(value):
    return NumberObject(value) if isinstance(value, int) else FloatObject(value)


def rgb(*channels):
    return array(*(number(channel) for channel in channels))


def dictionary(**entries):
    return DictionaryObject({name("/" + key): value for key, value in entries.items()})


def function(first, last, *, exponent=1):
    return dictionary(FunctionType=number(2), Domain=rgb(0, 1),
                      Range=rgb(0, 1, 0, 1, 0, 1),
                      C0=rgb(*first), C1=rgb(*last), N=number(exponent))


def shading(writer, stops, *, variant=None):
    segments = array(writer._add_object(function(stops[0], stops[1])),
                     writer._add_object(function(stops[1], stops[2])))
    stitched = dictionary(FunctionType=number(3), Domain=rgb(0, 1),
                          Range=rgb(0, 1, 0, 1, 0, 1),
                          Functions=segments, Bounds=rgb(.35),
                          Encode=rgb(0, 1, 0, 1))
    result = dictionary(ShadingType=number(2), ColorSpace=name("/DeviceRGB"),
                        BBox=rgb(20, 70, 400, 260),
                        Coords=rgb(160, 260, 160, 70),
                        Domain=rgb(0, 1),
                        Extend=array(BooleanObject(True), BooleanObject(True)),
                        AntiAlias=BooleanObject(False), Function=writer._add_object(stitched))
    if variant == "radial":
        result[name("/ShadingType")] = number(3)
    elif variant == "gray":
        result[name("/ColorSpace")] = name("/DeviceGray")
    elif variant in ("calculator", "sampled"):
        stream = DecodedStreamObject()
        stream.set_data(b"{ pop 1 1 1 }" if variant == "calculator" else b"\xff\xff\xff")
        stream.update(dictionary(FunctionType=number(4 if variant == "calculator" else 0), Domain=rgb(0, 1),
                                 Range=rgb(0, 1, 0, 1, 0, 1)))
        result[name("/Function")] = writer._add_object(stream)
    elif variant == "bad-bounds":
        stitched[name("/Bounds")] = rgb(1.2)
    elif variant == "huge-geometry":
        result[name("/BBox")] = rgb(20, 70, 2000000, 260)
    elif variant == "background":
        result[name("/Background")] = rgb(1, 1, 1)
    elif variant == "bad-encode":
        stitched[name("/Encode")] = rgb(0, 1, 0, 4)
    elif variant == "child-domain":
        segments[0].get_object()[name("/Domain")] = rgb(-1, 1)
    return result


def fixture(stops, *, variant=None):
    source = io.BytesIO()
    canvas = Canvas(source, pagesize=(420, 300), pageCompression=0)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.setFont("Helvetica", 16)
    canvas.drawString(45, 170, "Searchable lecture label")
    canvas.save()
    writer = PdfWriter()
    writer.clone_document_from_reader(PdfReader(io.BytesIO(source.getvalue())))
    page = writer.pages[0]
    page[name("/Resources")][name("/Shading")] = dictionary(
        Sh0=writer._add_object(shading(writer, stops, variant=variant)))
    content = page.get_contents()
    content.set_data(b"q\n/Sh0 sh\nQ\n" + content.get_data())
    page.replace_contents(content)
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        result = subprocess.run([str(engine), mode], stdin=file,
                                capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout, result.stdout[:80]
        return result.returncode, result.stderr.decode(errors="replace"), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_length + pdf_length
    return 0, json.loads(result.stdout[16:16 + metadata_length]), result.stdout[16 + metadata_length:]


def shading_values(data):
    page = PdfReader(io.BytesIO(data)).pages[0]
    resource = page["/Resources"]["/Shading"]["/Sh0"].get_object()
    stitched = resource["/Function"].get_object()
    children = [item.get_object() for item in stitched["/Functions"]]
    return resource, stitched, children


def pixel(data, x, y):
    document = pymupdf.open(stream=data, filetype="pdf")
    page = document[0]
    pixmap = page.get_pixmap(alpha=False)
    offset = (y * pixmap.width + x) * pixmap.n
    return tuple(pixmap.samples[offset:offset + 3])


results = []
for case, stops in (
    ("light-green", ((.855, .992, .655), (.894, .992, .761), (.961, 1, .902))),
    ("light-blue", ((.79, .9, 1), (.85, .94, 1), (.93, .97, 1))),
):
    source = fixture(stops)
    (scratch / f"{case}-source.pdf").write_bytes(source)
    assert min(pixel(source, 200, 100)) > 150, case
    code, inspection, empty = invoke(source, "--inspect")
    assert code == 0 and not empty, (case, inspection)
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output and facts == inspection and facts["changedPages"] == 1, (case, facts)
    old, old_function, old_children = shading_values(source)
    new, new_function, new_children = shading_values(output)
    for key in ("/ShadingType", "/ColorSpace", "/BBox", "/Coords", "/Domain", "/Extend", "/AntiAlias"):
        assert old[key] == new[key], (case, key)
    for key in ("/Bounds", "/Encode", "/Domain", "/Range", "/FunctionType"):
        assert old_function[key] == new_function[key], (case, key)
    for original, changed in zip(old_children, new_children):
        for key in ("/N", "/Domain", "/Range", "/FunctionType"):
            assert original[key] == changed[key], (case, key)
        assert original["/C0"] != changed["/C0"]
        assert original["/C1"] != changed["/C1"]
    original_document = pymupdf.open(stream=source, filetype="pdf")
    output_document = pymupdf.open(stream=output, filetype="pdf")
    original_page = original_document[0]
    output_page = output_document[0]
    assert original_page.rect == output_page.rect
    assert original_page.get_text() == output_page.get_text()
    assert [(span["font"], span["size"], span["chars"]) for span in original_page.get_texttrace()] == [
        (span["font"], span["size"], span["chars"]) for span in output_page.get_texttrace()]
    for y in (100, 210):
        before = pixel(source, 300, y)
        after = pixel(output, 300, y)
        assert max(after) < min(before) - 60, (case, y, before, after)
        assert max(after) > 20, (case, y, after)
    repeat_code, repeated, repeated_output = invoke(output, "--convert")
    assert repeat_code == 0 and not repeated_output and repeated["changedPages"] == 0, (case, repeated)
    (scratch / f"{case}-dark.pdf").write_bytes(output)
    results.append({"case": case, "converted": True, "repeatNoOp": True})
    print("PASS", case)

for case, stops, variant in (
    ("dark-stop", ((.1, .2, .3), (.15, .25, .35), (.2, .3, .4)), None),
    ("radial", ((.9, .9, .9),) * 3, "radial"),
    ("device-gray", ((.9, .9, .9),) * 3, "gray"),
    ("calculator", ((.9, .9, .9),) * 3, "calculator"),
    ("sampled", ((.9, .9, .9),) * 3, "sampled"),
    ("unordered-bounds", ((.9, .9, .9),) * 3, "bad-bounds"),
    ("huge-geometry", ((.9, .9, .9),) * 3, "huge-geometry"),
    ("background-key", ((.9, .9, .9),) * 3, "background"),
    ("bad-encode", ((.9, .9, .9),) * 3, "bad-encode"),
    ("child-domain", ((.9, .9, .9),) * 3, "child-domain"),
):
    source = fixture(stops, variant=variant)
    for mode in ("--inspect", "--convert"):
        code, reason, output = invoke(source, mode)
        assert code in (3, 4, 5) and not output, (case, mode, code, reason)
    results.append({"case": case, "refused": True})
    print("PASS", case, "refused")

(scratch / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
