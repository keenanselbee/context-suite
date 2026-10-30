"""Preserve a bounded exporter Artifact/Slide graph without repairing its tags.

Requires pypdf, ReportLab, and PyMuPDF. All fixtures are independently authored.
Retained source/output PDFs in the scratch directory are regression evidence.
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
from pypdf.generic import (ArrayObject, BooleanObject, ContentStream, DecodedStreamObject,
                           DictionaryObject, NameObject, NumberObject,
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


def fixture(*, count=1, defect=None, leading_artifact=False):
    data = io.BytesIO()
    canvas = Canvas(data, pagesize=(420, 300), pageCompression=0)
    if not leading_artifact:
        canvas.setFillColorRGB(1, 1, 1)
        canvas.rect(0, 0, 420, 300, fill=1, stroke=0)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.drawString(30, 240, "Preserved exporter Artifact and Slide relationships")
    canvas.save()
    writer = PdfWriter()
    writer.append(PdfReader(io.BytesIO(data.getvalue())))
    page = writer.pages[0]
    page_ref = writer.get_reference(page)
    original = page["/Contents"].get_object().get_data()
    content = DecodedStreamObject()
    prefix = b"/Artifact BMC\nq Q\nEMC\n" if leading_artifact else b""
    content.set_data(prefix + b"/P <</MCID 0>> BDC\n" + original + b"\nEMC\n" + b"".join(
        f"/Span <</MCID {i}>> BDC q Q EMC\n".encode() for i in range(1, count + 1)))
    page[name("/Contents")] = writer._add_object(content)
    page[name("/StructParents")] = NumberObject(0)
    tree = dictionary(Type=name("/StructTreeRoot"))
    tree_ref = writer._add_object(tree)
    slide = dictionary(Type=name("/StructElem"), S=name("/Slide"), P=tree_ref, Pg=page_ref)
    slide_ref = writer._add_object(slide)
    paragraph = writer._add_object(dictionary(Type=name("/StructElem"), S=name("/P"),
        P=slide_ref, Pg=page_ref, K=NumberObject(0)))
    slide[name("/K")] = array(paragraph)
    tree[name("/K")] = array(slide_ref)
    artifacts = []
    for i in range(1, count + 1):
        artifacts.append(writer._add_object(dictionary(Type=name("/StructElem"), S=name("/Artifact"),
            P=slide_ref, Pg=page_ref, K=NumberObject(i))))
    parents = array(paragraph, *artifacts)
    tree[name("/ParentTree")] = writer._add_object(dictionary(Nums=array(NumberObject(0), parents)))
    tree[name("/ParentTreeNextKey")] = NumberObject(1)
    writer._root_object[name("/StructTreeRoot")] = tree_ref
    writer._root_object[name("/MarkInfo")] = dictionary(Marked=BooleanObject(True))
    artifact = artifacts[0].get_object()
    if defect == "wrong-parent-role":
        slide[name("/S")] = name("/Div")
    elif defect == "unreachable-parent":
        artifact[name("/P")] = writer._add_object(dictionary(Type=name("/StructElem"),
            S=name("/Slide"), P=tree_ref, Pg=page_ref, K=array()))
    elif defect == "different-page":
        second = writer.add_blank_page(width=420, height=300)
        artifact[name("/Pg")] = writer.get_reference(second)
    elif defect == "mismatched-k":
        artifact[name("/K")] = NumberObject(0)
    elif defect == "nested-k":
        artifact[name("/K")] = array(NumberObject(1))
    elif defect == "cycle":
        artifact[name("/P")] = artifacts[0]
    elif defect == "active-extra":
        artifact[name("/A")] = dictionary(S=name("/JavaScript"), JS=TextStringObject("app.alert(1)"))
    elif defect == "parent-extra":
        slide[name("/Unrecognized")] = TextStringObject("Not admitted by this exception")
    elif defect == "missing-parent-tree":
        del tree[name("/ParentTree")]
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


def structure_view(data):
    reader = PdfReader(io.BytesIO(data), strict=True)
    page = reader.pages[0]
    tree = reader.trailer["/Root"]["/StructTreeRoot"]
    slide_ref = tree["/K"][0]
    slide = slide_ref.get_object()
    assert slide.raw_get("/P").idnum == reader.trailer["/Root"].raw_get("/StructTreeRoot").idnum
    assert slide.raw_get("/Pg").idnum == page.indirect_reference.idnum
    assert slide["/S"] == "/Slide" and len(slide["/K"]) == 1
    paragraph_ref = slide["/K"][0]
    parents = tree["/ParentTree"]["/Nums"][1]
    assert parents[0].idnum == paragraph_ref.idnum
    roles = []
    for mcid, raw in enumerate(parents[1:], 1):
        artifact = raw.get_object()
        assert set(artifact) == {"/Type", "/S", "/P", "/Pg", "/K"}
        assert artifact["/Type"] == "/StructElem" and artifact["/S"] == "/Artifact"
        assert artifact.raw_get("/P").idnum == slide_ref.idnum
        assert artifact.raw_get("/Pg").idnum == page.indirect_reference.idnum
        assert artifact["/K"] == mcid
        assert raw.idnum not in {item.idnum for item in slide["/K"]}
        roles.append((str(artifact["/S"]), int(artifact["/K"])))
    markers = [(str(operands[0]), int(operands[1]["/MCID"]))
               for operands, operation in ContentStream(page.get_contents(), reader).operations
               if operation == b"BDC" and len(operands) == 2 and
               isinstance(operands[1], DictionaryObject) and "/MCID" in operands[1]]
    assert markers == [("/P", 0)] + [("/Span", i) for i in range(1, len(roles) + 1)]
    return tuple(roles), str(slide["/S"]), int(page["/StructParents"])


def marker_view(data):
    reader = PdfReader(io.BytesIO(data), strict=True)
    markers = []
    for operands, operation in ContentStream(reader.pages[0].get_contents(), reader).operations:
        if operation == b"BMC":
            markers.append(("BMC", str(operands[0])))
        elif operation == b"BDC":
            markers.append(("BDC", str(operands[0]), int(operands[1]["/MCID"])))
        elif operation == b"EMC":
            markers.append(("EMC",))
    return tuple(markers)


def positive(label, count, **options):
    source = fixture(count=count, **options)
    digest = hashlib.sha256(source).digest()
    expected = structure_view(source)
    code, inspection, no_pdf = invoke(source, "--inspect")
    assert code == 0 and not no_pdf, (label, code, inspection)
    code, conversion, output = invoke(source, "--convert")
    assert code == 0 and output and conversion == inspection, (label, code, conversion)
    assert structure_view(output) == expected, label
    source_markers = marker_view(source)
    output_markers = marker_view(output)
    if options.get("leading_artifact"):
        # This fixture has no painted paper, so exactly one generated background
        # Artifact precedes every original marker. The original Artifact stays.
        assert output_markers[:2] == (("BMC", "/Artifact"), ("EMC",)), label
        output_markers = output_markers[2:]
    assert output_markers == source_markers, label
    code, repeat, repeated = invoke(output, "--convert")
    assert code == 0 and not repeated, (label, code, repeat)
    with pymupdf.open(stream=source, filetype="pdf") as before, \
            pymupdf.open(stream=output, filetype="pdf") as after:
        assert before[0].get_text() == after[0].get_text(), label
        assert before[0].rect == after[0].rect, label
    assert hashlib.sha256(source).digest() == digest, label
    (ROOT / f"{label}-source.pdf").write_bytes(source)
    (ROOT / f"{label}-dark.pdf").write_bytes(output)
    print("PASS", label, conversion)


def refusal(label, fragment="unreachable structure element", **options):
    source = fixture(**options)
    for mode in ("--inspect", "--convert"):
        code, diagnostic, output = invoke(source, mode)
        assert code in (3, 4, 5) and fragment in diagnostic and not output, \
            (label, mode, code, diagnostic)
    print("PASS", label, "refused:", fragment)


positive("artifact-slide-parent", 1)
positive("artifact-slide-parent-boundary", 64)
positive("leading-source-artifact", 1, leading_artifact=True)
refusal("wrong-parent-role", defect="wrong-parent-role")
refusal("unreachable-parent", defect="unreachable-parent")
refusal("different-page", defect="different-page")
refusal("mismatched-k", defect="mismatched-k")
refusal("nested-k", defect="nested-k")
refusal("parent-cycle", defect="cycle")
refusal("active-extra-key", defect="active-extra")
refusal("parent-extra-key", defect="parent-extra")
refusal("artifact-budget", "detached artifact budget", count=65)
refusal("missing-parent-tree", "parent tree is missing", defect="missing-parent-tree")
