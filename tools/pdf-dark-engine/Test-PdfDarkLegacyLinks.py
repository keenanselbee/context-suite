"""Exercise pre-1.7 direct PDF annotations and retained link safety.

Requires pypdf, ReportLab and PyMuPDF. Use a newly built development host.
All PDFs in this suite are authored in memory; no lecture source is used.
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
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, BooleanObject, DecodedStreamObject,
                           DictionaryObject, IndirectObject, NameObject,
                           NullObject, NumberObject,
                           TextStringObject)
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
if not ENGINE.is_file():
    raise ValueError("PDF dark host is missing")
ROOT.mkdir(parents=True)


def name(value):
    return NameObject(value)


def array(*values):
    return ArrayObject(list(values))


def dictionary(**values):
    return DictionaryObject({name("/" + key): value for key, value in values.items()})


def page_source():
    data = io.BytesIO()
    canvas = Canvas(data, pagesize=(420, 300), pageCompression=0, pdfVersion=(1, 4))
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 420, 300, stroke=0, fill=1)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.drawString(30, 240, "Independent legacy link example")
    canvas.drawString(30, 205, "Direct and indirect annotations")
    canvas.save()
    return data.getvalue()


def fixture(*, version="1.4", layout="mixed", defect=None, catalog_version=None,
            tagged=False):
    writer = PdfWriter()
    writer.append(PdfReader(io.BytesIO(page_source())))
    writer.pdf_header = "%PDF-" + version
    page = writer.pages[0]
    page_ref = writer.get_reference(page)
    if tagged:
        original = page["/Contents"].get_object().get_data()
        marked = DecodedStreamObject()
        marked.set_data(b"/P <</MCID 0>> BDC\n" + original + b"\nEMC\n")
        page[name("/Contents")] = writer._add_object(marked)
        page[name("/StructParents")] = NumberObject(0)
        tree = dictionary(Type=name("/StructTreeRoot"))
        tree_ref = writer._add_object(tree)
        element = dictionary(Type=name("/StructElem"), S=name("/P"),
                             P=tree_ref, Pg=page_ref, K=NumberObject(0))
        element_ref = writer._add_object(element)
        tree[name("/K")] = array(element_ref)
        tree[name("/ParentTree")] = writer._add_object(
            dictionary(Nums=array(NumberObject(0), array(element_ref))))
        writer._root_object[name("/StructTreeRoot")] = tree_ref
        writer._root_object[name("/MarkInfo")] = dictionary(Marked=BooleanObject(True))
    uri = dictionary(S=name("/URI"),
                     URI=TextStringObject("https://example.test/lecture?part=1"))
    if defect == "javascript":
        uri = dictionary(S=name("/JavaScript"),
                         JS=TextStringObject("app.alert('unsafe')"))
    elif defect == "launch":
        uri = dictionary(S=name("/Launch"), F=TextStringObject("calc.exe"))
    elif defect == "bad-scheme":
        uri[name("/URI")] = TextStringObject("file:///C:/unsafe")
    first = dictionary(Type=name("/Annot"), Subtype=name("/Link"),
                       Rect=array(NumberObject(28), NumberObject(228),
                                  NumberObject(250), NumberObject(250)),
                       Border=array(NumberObject(0), NumberObject(0),
                                    NumberObject(0)),
                       Contents=TextStringObject("Lecture reference"),
                       A=uri)
    if defect == "additional-action":
        first[name("/AA")] = dictionary(E=dictionary(S=name("/JavaScript"),
            JS=TextStringObject("app.alert('unsafe')")))
    if defect == "bad-rectangle":
        first[name("/Rect")] = array(NumberObject(28), NumberObject(228))
    second = dictionary(Type=name("/Annot"), Subtype=name("/Link"),
                        Rect=array(NumberObject(28), NumberObject(195),
                                   NumberObject(250), NumberObject(215)),
                        Border=array(NumberObject(0), NumberObject(0),
                                     NumberObject(0)),
                        Dest=array(page_ref, name("/Fit")))
    third = dictionary(Type=name("/Annot"), Subtype=name("/Link"),
                       Rect=array(NumberObject(28), NumberObject(160),
                                  NumberObject(250), NumberObject(180)),
                       NM=TextStringObject("mail-contact"),
                       A=dictionary(S=name("/URI"),
                           URI=TextStringObject("mailto:reader@example.test")))
    if layout == "direct":
        annotations = array(first, second, third)
    elif layout == "mixed":
        annotations = array(first, writer._add_object(second), third)
    elif layout == "mixed-indirect-first":
        annotations = array(writer._add_object(first), second,
                            writer._add_object(third))
    elif layout == "indirect":
        annotations = array(writer._add_object(first), writer._add_object(second),
                            writer._add_object(third))
    else:
        raise ValueError(layout)
    if defect == "null-entry":
        annotations[0] = NullObject()
    elif defect == "scalar-entry":
        annotations[0] = TextStringObject("not an annotation")
    elif defect == "duplicate-reference":
        annotations[2] = annotations[1]
    page[name("/Annots")] = writer._add_object(annotations)
    if catalog_version:
        writer._root_object[name("/Version")] = name("/" + catalog_version)
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
    magic, protocol, json_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, protocol) == (0x31445043, 1)
    assert len(result.stdout) == 16 + json_size + pdf_size
    return 0, json.loads(result.stdout[16:16 + json_size]), \
        result.stdout[16 + json_size:]


def links(data):
    reader = PdfReader(io.BytesIO(data), strict=True)
    assert len(reader.pages) == 1
    page = reader.pages[0]
    annotations = page["/Annots"]
    assert isinstance(page.raw_get("/Annots"), IndirectObject)
    found = []
    for raw in annotations:
        annotation = raw.get_object() if isinstance(raw, IndirectObject) else raw
        assert annotation["/Subtype"] == "/Link"
        rect = tuple(float(value) for value in annotation["/Rect"])
        if "/A" in annotation:
            action = annotation["/A"].get_object()
            assert action["/S"] == "/URI"
            destination = ("URI", str(action["/URI"]))
        else:
            destination_array = annotation["/Dest"]
            assert destination_array[0].idnum == page.indirect_reference.idnum
            destination = ("GoTo", str(destination_array[1]))
        found.append((isinstance(raw, IndirectObject), rect, destination,
                      str(annotation.get("/Contents", "")),
                      str(annotation.get("/NM", ""))))
    return tuple(found)


def positive(label, **options):
    source = fixture(**options)
    source_hash = hashlib.sha256(source).digest()
    expected = links(source)
    code, inspection, no_pdf = invoke(source, "--inspect")
    assert code == 0 and not no_pdf, (label, code, inspection)
    code, conversion, output = invoke(source, "--convert")
    assert code == 0 and output and conversion == inspection, (label, code, conversion)
    assert links(output) == expected, label
    code, repeat, repeated_pdf = invoke(output, "--convert")
    assert code == 0 and not repeated_pdf, (label, code, repeat)
    before = pymupdf.open(stream=source, filetype="pdf")
    after = pymupdf.open(stream=output, filetype="pdf")
    assert before[0].get_text() == after[0].get_text(), label
    assert before[0].rect == after[0].rect, label
    assert hashlib.sha256(source).digest() == source_hash, label
    (ROOT / f"{label}-source.pdf").write_bytes(source)
    (ROOT / f"{label}-dark.pdf").write_bytes(output)
    print("PASS", label, conversion)


def refusal(label, fragment, **options):
    source = fixture(**options)
    for mode in ("--inspect", "--convert"):
        code, diagnostic, output = invoke(source, mode)
        assert code in (3, 4, 5) and fragment in diagnostic and not output, \
            (label, mode, code, diagnostic)
    print("PASS", label, "refused:", fragment)


positive("pdf14-direct-links", version="1.4", layout="direct")
positive("pdf14-mixed-links", version="1.4", layout="mixed")
positive("pdf14-mixed-indirect-first", version="1.4",
         layout="mixed-indirect-first")
positive("pdf14-tagged-mixed", version="1.4", layout="mixed", tagged=True)
positive("pdf16-direct-links", version="1.6", layout="direct")
positive("pdf17-indirect-control", version="1.7", layout="indirect")
refusal("pdf17-direct", "pre-1.7", version="1.7", layout="direct")
refusal("pdf17-mixed", "pre-1.7", version="1.7",
        layout="mixed-indirect-first")
refusal("pdf20-direct", "pre-1.7", version="2.0", layout="direct")
refusal("catalog-upgrade", "pre-1.7", version="1.4", layout="direct",
        catalog_version="1.7")
refusal("null-entry", "Invalid PDF annotation reference", defect="null-entry")
refusal("scalar-entry", "Invalid PDF annotation reference", defect="scalar-entry")
refusal("bad-rectangle", "Invalid PDF annotation rectangle", defect="bad-rectangle")
refusal("duplicate-indirect", "reused across pages", layout="indirect",
        defect="duplicate-reference")
refusal("direct-javascript", "Active PDF link action", defect="javascript")
refusal("direct-launch", "Active PDF link action", defect="launch")
refusal("direct-additional-action", "Active PDF annotation",
        defect="additional-action")
refusal("direct-unsafe-scheme", "PDF URI scheme", defect="bad-scheme")
