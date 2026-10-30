"""Exercise authored passive form shells, navigation, and exact URI preservation.

Requires pypdf, ReportLab, and PyMuPDF. Use a newly built development host.
Source/output PDFs retained in the fresh scratch directory are regression evidence.
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
from pypdf.generic import (ArrayObject, BooleanObject, DictionaryObject,
                           IndirectObject, NameObject, NullObject, NumberObject,
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


def fixture(*, navigation="direct", names="legacy", uri="https://example.test/a?q=1", omit_fields=False,
            defect=None):
    data = io.BytesIO()
    canvas = Canvas(data, pagesize=(420, 300), pageCompression=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 420, 300, fill=1, stroke=0)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.drawString(30, 240, "Passive navigation and empty form shell")
    canvas.save()
    writer = PdfWriter()
    writer.append(PdfReader(io.BytesIO(data.getvalue())))
    page = writer.pages[0]
    page_ref = writer.get_reference(page)
    font = writer._add_object(dictionary(Type=name("/Font"), Subtype=name("/Type1"),
                                         BaseFont=name("/Helvetica")))
    form = dictionary(Fields=array(), DA=TextStringObject("/Helv 10 Tf 0 g"),
        DR=dictionary(Font=dictionary(Helv=font), ProcSet=array(name("/PDF"), name("/Text"))),
        Q=NumberObject(0), NeedAppearances=BooleanObject(False), SigFlags=NumberObject(0),
        CO=array())
    if omit_fields:
        del form[name("/Fields")]
    encoding = dictionary(Type=name("/Encoding"),
                          Differences=array(NumberObject(24), name("/breve"), name("/caron")))
    form["/DR"][name("/Encoding")] = dictionary(PDFDocEncoding=writer._add_object(encoding))
    writer._root_object[name("/AcroForm")] = writer._add_object(form)
    direct = array(page_ref, name("/FitH"), NullObject())
    target = name("/Lecture") if names == "legacy" else TextStringObject("Lecture")
    if names == "legacy":
        writer._root_object[name("/Dests")] = dictionary(Lecture=dictionary(D=direct))
    else:
        leaf = dictionary(Names=array(TextStringObject("Lecture"), dictionary(D=direct)))
        if names == "tree-kids":
            leaf[name("/Limits")] = array(TextStringObject("Lecture"), TextStringObject("Lecture"))
            tree = dictionary(Kids=array(writer._add_object(leaf)))
        else:
            tree = leaf
        writer._root_object[name("/Names")] = dictionary(Dests=writer._add_object(tree))
    action = dictionary(Type=name("/Action"), S=name("/GoTo"), D=target)
    writer._root_object[name("/OpenAction")] = direct if navigation == "direct" else action
    link = dictionary(Type=name("/Annot"), Subtype=name("/Link"),
        Rect=array(NumberObject(28), NumberObject(225), NumberObject(350), NumberObject(250)),
        Dest=target)
    external = dictionary(Type=name("/Annot"), Subtype=name("/Link"),
        Rect=array(NumberObject(28), NumberObject(190), NumberObject(350), NumberObject(215)),
        A=dictionary(Type=name("/Action"), S=name("/URI"), URI=TextStringObject(uri),
                     IsMap=BooleanObject(False)))
    page[name("/Annots")] = array(writer._add_object(link), writer._add_object(external))
    outlines = dictionary(Type=name("/Outlines"), Count=NumberObject(1))
    outlines_ref = writer._add_object(outlines)
    bookmark = writer._add_object(dictionary(Title=TextStringObject("Lecture bookmark"),
                                             Parent=outlines_ref, A=action))
    outlines[name("/First")] = bookmark
    outlines[name("/Last")] = bookmark
    writer._root_object[name("/Outlines")] = outlines_ref
    if uri.startswith("../"):
        writer._root_object[name("/URI")] = dictionary(Base=TextStringObject("https://example.test/base/"))

    if defect == "field":
        form[name("/Fields")].append(writer._add_object(dictionary(FT=name("/Tx"), T=TextStringObject("Field"))))
    elif defect == "widget":
        link[name("/Subtype")] = name("/Widget")
    elif defect == "xfa":
        form[name("/XFA")] = TextStringObject("Active form data")
    elif defect == "signature-flags":
        form[name("/SigFlags")] = NumberObject(3)
    elif defect == "bad-fields":
        form[name("/Fields")] = NumberObject(0)
    elif defect == "bad-form-resources":
        form[name("/DR")] = dictionary(Font=TextStringObject("invalid"))
    elif defect == "bad-form-flag":
        form[name("/NeedAppearances")] = NumberObject(1)
    elif defect == "bad-encoding-type":
        encoding[name("/Type")] = name("/Action")
    elif defect == "bad-encoding-position":
        encoding["/Differences"][0] = NumberObject(256)
    elif defect == "bad-encoding-glyph":
        encoding["/Differences"][1] = TextStringObject("not a glyph name")
    elif defect == "duplicate-encoding-position":
        encoding[name("/Differences")] = array(NumberObject(24), name("/breve"),
                                              NumberObject(24), name("/caron"))
    elif defect == "encoding-budget":
        encoding[name("/Differences")] = array(*[NumberObject(0) for _ in range(513)])
    elif defect == "open-javascript":
        writer._root_object[name("/OpenAction")] = dictionary(S=name("/JavaScript"), JS=TextStringObject("app.alert(1)"))
    elif defect == "chain":
        action[name("/Next")] = dictionary(S=name("/GoTo"), D=direct)
    elif defect == "remote":
        action[name("/S")] = name("/GoToR")
    elif defect == "missing-name":
        link[name("/Dest")] = TextStringObject("Missing")
    elif defect == "destination-alias":
        writer._root_object[name("/Dests")][name("/Lecture")] = name("/Lecture")
    elif defect == "bad-fit":
        direct[1] = name("/Unknown")
    elif defect == "bad-arity":
        direct.append(NumberObject(1))
    elif defect == "bad-coordinate":
        direct[2] = TextStringObject("not a number")
    elif defect == "not-page":
        direct[0] = font
    elif defect == "duplicate-names":
        leaf[name("/Names")].extend([TextStringObject("Lecture"), direct])
    elif defect == "bad-limits":
        leaf[name("/Limits")] = array(TextStringObject("Wrong"), TextStringObject("Wrong"))
    elif defect == "cyclic-tree":
        tree_ref = writer._root_object["/Names"].raw_get("/Dests")
        tree[name("/Kids")] = array(tree_ref)
    elif defect == "bad-map":
        external["/A"][name("/IsMap")] = NumberObject(0)
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
    return 0, json.loads(result.stdout[16:16 + json_size]), result.stdout[16 + json_size:]


def passive_view(data):
    reader = PdfReader(io.BytesIO(data), strict=True)
    pages = {page.indirect_reference.idnum: index for index, page in enumerate(reader.pages)}
    seen = {}

    def encode(value):
        if isinstance(value, IndirectObject):
            if value.idnum in pages:
                return ("page", pages[value.idnum])
            key = (value.idnum, value.generation)
            if key in seen:
                return ("reference", seen[key])
            seen[key] = len(seen)
            return ("object", seen[key], encode(value.get_object()))
        if isinstance(value, DictionaryObject):
            items = tuple((str(key), encode(value.raw_get(key))) for key in sorted(value)
                          if key not in ("/Length", "/Filter", "/DecodeParms"))
            return (items, value.get_data()) if hasattr(value, "get_data") else items
        if isinstance(value, ArrayObject):
            return tuple(encode(item) for item in value)
        return value.__class__.__name__, str(value)

    root = reader.trailer["/Root"]
    return tuple((key, encode(root.raw_get(key))) for key in
                 ("/AcroForm", "/OpenAction", "/Dests", "/Names", "/Outlines", "/URI")
                 if key in root), tuple(encode(page.raw_get("/Annots")) for page in reader.pages)


def positive(label, **options):
    source = fixture(**options)
    digest = hashlib.sha256(source).digest()
    expected = passive_view(source)
    code, inspection, no_pdf = invoke(source, "--inspect")
    assert code == 0 and not no_pdf, (label, code, inspection)
    code, conversion, output = invoke(source, "--convert")
    assert code == 0 and output and conversion == inspection, (label, code, conversion)
    assert passive_view(output) == expected, label
    code, repeat, repeated = invoke(output, "--convert")
    assert code == 0 and not repeated, (label, code, repeat)
    assert passive_view(output) == expected, label
    with pymupdf.open(stream=source, filetype="pdf") as before, \
            pymupdf.open(stream=output, filetype="pdf") as after:
        assert before[0].get_text() == after[0].get_text(), label
        assert before[0].rect == after[0].rect, label
    assert hashlib.sha256(source).digest() == digest, label
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


positive("empty-shell-direct-open")
positive("absent-form-fields", omit_fields=True)
positive("legacy-named-goto", navigation="goto")
positive("string-named-goto", navigation="goto", names="tree")
positive("nested-name-tree", navigation="goto", names="tree-kids")
positive("uppercase-http", uri="HTTPS://Example.test/A%20B?q=One#Part")
positive("mailto", uri="mailto:Reader@example.test")
positive("schemeless-uri", uri="www.example.test/lecture")
positive("relative-uri-base", uri="../lecture/one.pdf#page=2")
refusal("populated-form", "Interactive PDF forms", defect="field")
refusal("widget", "annotation subtype", defect="widget")
refusal("absent-fields-widget", "annotation subtype", omit_fields=True, defect="widget")
refusal("xfa", "Interactive PDF forms", defect="xfa")
refusal("signature-flags", "Signed PDF form", defect="signature-flags")
refusal("bad-fields", "Invalid PDF form fields", defect="bad-fields")
refusal("bad-form-resources", "Invalid PDF form font resources", defect="bad-form-resources")
refusal("bad-form-flag", "Invalid PDF form appearance flag", defect="bad-form-flag")
refusal("bad-encoding-type", "Invalid PDF form encoding resource", defect="bad-encoding-type")
refusal("bad-encoding-position", "Invalid PDF form encoding position", defect="bad-encoding-position")
refusal("bad-encoding-glyph", "Invalid PDF form encoding glyph", defect="bad-encoding-glyph")
refusal("duplicate-encoding-position", "Invalid PDF form encoding glyph", defect="duplicate-encoding-position")
refusal("encoding-budget", "encoding difference budget", defect="encoding-budget")
refusal("active-open", "Active PDF navigation", defect="open-javascript")
refusal("action-chain", "Active PDF navigation", navigation="goto", defect="chain")
refusal("remote-goto", "Active PDF navigation", navigation="goto", defect="remote")
refusal("missing-name", "Named PDF destination is missing", defect="missing-name")
refusal("cyclic-destination-alias", "Invalid PDF internal destination", defect="destination-alias")
refusal("unknown-fit", "destination fit parameters", defect="bad-fit")
refusal("bad-fit-arity", "destination fit parameters", defect="bad-arity")
refusal("bad-fit-coordinate", "destination coordinate", defect="bad-coordinate")
refusal("destination-not-page", "does not target a page", defect="not-page")
refusal("duplicate-name", "duplicate PDF destination names", names="tree", defect="duplicate-names")
refusal("bad-name-tree-limits", "destination tree limits", names="tree-kids", defect="bad-limits")
refusal("cyclic-name-tree", "Cyclic or reused PDF destination tree", names="tree-kids", defect="cyclic-tree")
refusal("file-uri", "PDF URI scheme", uri="file:///C:/unsafe")
refusal("javascript-uri", "PDF URI scheme", uri="javascript:alert(1)")
refusal("uri-whitespace", "Invalid PDF URI characters", uri="www.example.test/a b")
refusal("uri-control", "Invalid PDF URI characters", uri="https://example.test/a\nb")
refusal("uri-map-flag", "Invalid PDF URI map flag", defect="bad-map")
