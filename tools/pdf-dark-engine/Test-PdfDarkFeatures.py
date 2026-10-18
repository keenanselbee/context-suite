"""Independent native PDF-dark feature and refusal fixtures.

Test-only dependencies: PyMuPDF, pypdf, ReportLab and Pillow. All generated
PDFs and renders are confined to a fresh repository-local scratch directory.
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
from pypdf.generic import (ArrayObject, BooleanObject, DecodedStreamObject,
                           DictionaryObject, NameObject, NumberObject)
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen.canvas import Canvas

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
REPOSITORY = pathlib.Path(__file__).resolve().parents[2]
ROOT = pathlib.Path(args.scratch).absolute()
ENGINE = pathlib.Path(args.engine).absolute()
if ".." in ROOT.parts or not ROOT.is_relative_to(REPOSITORY / ".codex-temp") or ROOT.exists():
    raise ValueError("Use a fresh repository-local scratch directory")
pin = json.loads((REPOSITORY / "tools/pdf-dark-engine/payload-candidate.json").read_text())
if hashlib.sha256(ENGINE.read_bytes()).hexdigest().upper() != pin["host"]["sha256"]:
    raise ValueError("Use the reviewed native dark-mode host")
ROOT.mkdir(parents=True)
FAILURES = []


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=ROOT) as source:
        source.write(data)
        source.flush()
        source.seek(0)
        process = subprocess.run([str(ENGINE), mode], stdin=source,
                                 capture_output=True, timeout=30)
    if process.returncode:
        assert not process.stdout, "Error response published stdout"
        return process.returncode, process.stderr.decode(errors="replace").strip(), b""
    assert len(process.stdout) >= 16
    magic, version, json_size, pdf_size = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + json_size + pdf_size
    facts = json.loads(process.stdout[16:16 + json_size])
    assert facts["policy"] == "pdf-dark-8"
    return 0, facts, process.stdout[16 + json_size:]


def canvas_pdf(draw, pagesize=(420, 300)):
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=pagesize, pageCompression=1)
    draw(canvas)
    canvas.save()
    return stream.getvalue()


def write_pdf(writer):
    stream = io.BytesIO()
    writer.write(stream)
    return stream.getvalue()


def mutate(source, edit):
    writer = PdfWriter()
    writer.append(PdfReader(io.BytesIO(source)))
    edit(writer)
    return write_pdf(writer)


def stream(data):
    result = DecodedStreamObject()
    result.set_data(data)
    return result


def image_objects(data):
    reader = PdfReader(io.BytesIO(data))
    objects = []
    for page in reader.pages:
        xobjects = page["/Resources"].get_object().get("/XObject", {})
        for value in xobjects.get_object().values():
            obj = value.get_object()
            if obj.get("/Subtype") == "/Image":
                objects.append(obj)
    return objects


def check_positive(name, source, *, changed, dark=0, recolored=None, preserved=None,
                   same_image_streams=False):
    digest = hashlib.sha256(source).digest()
    code, inspected, inspect_pdf = invoke(source, "--inspect")
    assert code == 0 and not inspect_pdf, (name, code, inspected)
    code, converted, output = invoke(source, "--convert")
    assert code == 0 and converted == inspected, (name, code, converted)
    assert (converted["changedPages"], converted["alreadyDarkPages"]) == (changed, dark), (name, converted)
    if recolored is not None:
        assert converted["recoloredImages"] == recolored, (name, converted)
    if preserved is not None:
        assert converted["preservedImages"] == preserved, (name, converted)
    assert bool(output) == bool(changed), (name, converted)
    if output:
        before = pymupdf.open(stream=source, filetype="pdf")
        after = pymupdf.open(stream=output, filetype="pdf")
        assert len(before) == len(after)
        for left, right in zip(before, after):
            assert left.rect == right.rect and left.get_text() == right.get_text(), name
            assert len(left.get_text("words")) == len(right.get_text("words")), name
        if same_image_streams:
            src_images = image_objects(source)
            dst_images = image_objects(output)
            assert len(src_images) == len(dst_images) and src_images, name
            for src, dst in zip(src_images, dst_images):
                assert src.get_data() == dst.get_data(), name
        (ROOT / f"{name}-source.pdf").write_bytes(source)
        (ROOT / f"{name}-dark.pdf").write_bytes(output)
        after[0].get_pixmap().save(ROOT / f"{name}-dark.png")
    assert hashlib.sha256(source).digest() == digest
    print("PASS", name, converted)
    return output


def check_refusal(name, source, code_expected, fragment):
    for mode in ("--inspect", "--convert"):
        code, reason, output = invoke(source, mode)
        if code != code_expected or fragment not in reason or output:
            FAILURES.append((name, mode, code, reason, code_expected, fragment))
    print("PASS" if not any(item[0] == name for item in FAILURES) else "FAIL",
          name, f"expected exit {code_expected}: {fragment}")


base = canvas_pdf(lambda c: c.drawString(24, 240, "Feature policy"))

encrypted_writer = PdfWriter()
encrypted_writer.append(PdfReader(io.BytesIO(base)))
encrypted_writer.encrypt("feature-test-password")
check_refusal("encrypted", write_pdf(encrypted_writer), 3, "invalid password")


def add_signature(w):
    signature = DictionaryObject({NameObject("/Type"): NameObject("/Sig")})
    w._add_object(signature)


check_refusal("signature-dictionary", mutate(base, add_signature), 3, "Signed PDFs")
check_refusal("acroform", mutate(base, lambda w: w._root_object.__setitem__(
    NameObject("/AcroForm"), DictionaryObject({NameObject("/Fields"): ArrayObject()}))),
    3, "Interactive PDF forms")


def add_annotation(w):
    note = DictionaryObject({NameObject("/Type"): NameObject("/Annot"),
        NameObject("/Subtype"): NameObject("/Text"),
        NameObject("/Rect"): ArrayObject([NumberObject(12), NumberObject(12),
                                         NumberObject(32), NumberObject(32)])})
    w.pages[0][NameObject("/Annots")] = ArrayObject([w._add_object(note)])


check_refusal("annotation", mutate(base, add_annotation), 3, "annotation subtype")


def add_tags(w):
    w._root_object[NameObject("/StructTreeRoot")] = DictionaryObject({
        NameObject("/Type"): NameObject("/StructTreeRoot")})
    w._root_object[NameObject("/MarkInfo")] = DictionaryObject({
        NameObject("/Marked"): BooleanObject(True)})


check_refusal("incomplete-tag-tree", mutate(base, add_tags), 4, "structure parent tree is missing")


photo = Image.new("RGB", (120, 80))
for y in range(80):
    for x in range(120):
        shade = 200 + ((x * 11 + y * 7) % 55)
        photo.putpixel((x, y), (shade, shade, shade))
ImageDraw.Draw(photo).ellipse((43, 25, 75, 58), fill=(25, 25, 25))


def photo_page(c):
    c.drawImage(ImageReader(photo), 40, 80, width=280, height=180)
    c.drawString(24, 260, "Photo remains a photograph")


photo_pdf = canvas_pdf(photo_page)
check_positive("high-key-photo", photo_pdf, changed=1, preserved=1,
               same_image_streams=True)


def add_opaque_mask(w):
    image = next(v.get_object() for v in w.pages[0]["/Resources"]["/XObject"].get_object().values()
                 if v.get_object().get("/Subtype") == "/Image")
    width, height = int(image["/Width"]), int(image["/Height"])
    mask = stream(bytes([255]) * (width * height))
    mask.update({NameObject("/Type"): NameObject("/XObject"),
        NameObject("/Subtype"): NameObject("/Image"),
        NameObject("/Width"): NumberObject(width),
        NameObject("/Height"): NumberObject(height),
        NameObject("/ColorSpace"): NameObject("/DeviceGray"),
        NameObject("/BitsPerComponent"): NumberObject(8)})
    image[NameObject("/SMask")] = w._add_object(mask)


check_positive("opaque-soft-mask", mutate(photo_pdf, add_opaque_mask),
               changed=1, preserved=1, same_image_streams=True)


def lineart_page(c):
    art = Image.new("RGBA", (100, 70), (0, 0, 0, 0))
    draw = ImageDraw.Draw(art)
    draw.rectangle((12, 12, 88, 58), outline=(0, 0, 0, 255), width=3)
    draw.line((15, 15, 85, 55), fill=(0, 0, 0, 255), width=3)
    c.drawImage(ImageReader(art), 25, 70, width=250, height=175, mask="auto")
    c.drawString(24, 260, "Masked diagram")


check_positive("transparent-lineart", canvas_pdf(lineart_page), changed=1, recolored=1)


def scan_page(c):
    scan = Image.new("RGB", (420, 300))
    for y in range(300):
        for x in range(420):
            shade = 209 + ((x * 17 + y * 23) % 46)
            scan.putpixel((x, y), (shade, shade, shade))
    draw = ImageDraw.Draw(scan)
    for row in range(15):
        y = 20 + row * 18
        draw.line((20, y, 270 + (row * 13) % 100, y), fill=(30, 30, 30), width=2)
    draw.ellipse((290, 65, 380, 155), fill=(80, 90, 95))
    c.drawImage(ImageReader(scan), 0, 0, width=420, height=300)


check_positive("opaque-scan", canvas_pdf(scan_page), changed=1,
               preserved=1, same_image_streams=True)


def shared_pages(c):
    c.setFillColorRGB(1, 1, 1)
    c.rect(0, 0, 420, 300, stroke=0, fill=1)
    c.drawImage(ImageReader(photo), 30, 70, width=220, height=150)
    c.setFillColorRGB(0, 0, 0)
    c.drawString(25, 260, "Shared light")
    c.showPage()
    c.setFillColorRGB(18 / 255, 19 / 255, 20 / 255)
    c.rect(0, 0, 420, 300, stroke=0, fill=1)
    c.drawImage(ImageReader(photo), 30, 70, width=220, height=150)
    c.setFillColorRGB(244 / 255, 235 / 255, 216 / 255)
    c.drawString(25, 260, "Shared dark")


def force_shared_resource_dictionary(w):
    resources = w._add_object(w.pages[0]["/Resources"].get_object())
    for page in w.pages:
        page[NameObject("/Resources")] = resources


shared = mutate(canvas_pdf(shared_pages), force_shared_resource_dictionary)
shared_reader = PdfReader(io.BytesIO(shared))
assert shared_reader.pages[0].raw_get("/Resources").idnum == shared_reader.pages[1].raw_get("/Resources").idnum
shared_ids = [next(v.idnum for v in page["/Resources"]["/XObject"].get_object().values()
                   if v.get_object().get("/Subtype") == "/Image") for page in shared_reader.pages]
assert shared_ids[0] == shared_ids[1], "Fixture does not share an image resource"
shared_output = check_positive("shared-light-dark-resources", shared,
                               changed=1, dark=1, preserved=2)
source_dark_stream = shared_reader.pages[1].get_contents().get_data()
output_dark_stream = PdfReader(io.BytesIO(shared_output)).pages[1].get_contents().get_data()
assert source_dark_stream == output_dark_stream, "Already-dark page stream changed"
print("PASS already-dark shared page stream unchanged")


def form_page(inherited):
    def edit(w):
        form_content = b"30 40 140 70 re f\n" if inherited else b"0 0 0 rg\n30 40 140 70 re f\n"
        inner = stream(form_content)
        inner.update({NameObject("/Type"): NameObject("/XObject"),
            NameObject("/Subtype"): NameObject("/Form"),
            NameObject("/BBox"): ArrayObject([NumberObject(0), NumberObject(0),
                                             NumberObject(200), NumberObject(120)]),
            NameObject("/Resources"): DictionaryObject()})
        outer = stream(b"0 0 0 rg\n/Inner Do\n")
        outer.update({NameObject("/Type"): NameObject("/XObject"),
            NameObject("/Subtype"): NameObject("/Form"),
            NameObject("/BBox"): ArrayObject([NumberObject(0), NumberObject(0),
                                             NumberObject(200), NumberObject(120)]),
            NameObject("/Resources"): DictionaryObject({NameObject("/XObject"):
                DictionaryObject({NameObject("/Inner"): w._add_object(inner)})})})
        resources = w.pages[0]["/Resources"].get_object()
        resources[NameObject("/XObject")] = DictionaryObject({
            NameObject("/Outer"): w._add_object(outer)})
        contents = stream(b"q\n1 0 0 1 40 50 cm\n/Outer Do\nQ\n")
        w.pages[0][NameObject("/Contents")] = w._add_object(contents)
    return mutate(base, edit)


check_positive("nested-forms-explicit-color", form_page(False), changed=1)
check_refusal("nested-form-inherited-color", form_page(True), 3, "unknown fill color")

check_refusal("malformed-pdf", b"%PDF-1.7\n1 0 obj\n", 4, "")
check_refusal("source-limit", b"X" * (64 * 1024 * 1024 + 1), 5, "size limit")
pages = PdfWriter()
for _ in range(513):
    pages.add_blank_page(width=120, height=120)
check_refusal("page-limit", write_pdf(pages), 5, "page budget")

print("PASS feature suite complete")
if FAILURES:
    print("FAILURES:")
    for failure in FAILURES:
        print(failure)
    raise SystemExit(1)
