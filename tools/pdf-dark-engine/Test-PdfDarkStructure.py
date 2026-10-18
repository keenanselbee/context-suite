"""Exercise passive PDF structure preservation with independently authored PDFs.

Requires pypdf, ReportLab and PyMuPDF. Use a newly built development host;
this test deliberately does not require the previous payload pin.
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


def ref(writer, value):
    return writer._add_object(value)


def name(value):
    return NameObject(value)


def array(*values):
    return ArrayObject(list(values))


def dictionary(**values):
    return DictionaryObject({name("/" + key): value for key, value in values.items()})


def stream(data, **entries):
    value = DecodedStreamObject()
    value.set_data(data)
    value.update(dictionary(**entries))
    return value


def source_page():
    data = io.BytesIO()
    canvas = Canvas(data, pagesize=(420, 300), pageCompression=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 420, 300, fill=1, stroke=0)
    canvas.setFillColorRGB(0, 0, 0)
    canvas.drawString(30, 240, "Tagged searchable lecture example")
    canvas.drawString(30, 210, "A link and a passive handout")
    canvas.save()
    return data.getvalue()


def write(writer):
    data = io.BytesIO()
    writer.write(data)
    return data.getvalue()


def authored_fixture(*, link="uri", attachment=False, layer=False,
                     malformed_parent=False, active_action=False,
                     conditional_layer=False, tagged_form=False,
                     orphan_mcid=False, missing_parent_link=False,
                     mismatched_parent_link=False,
                     detached_artifact_parent=False):
    writer = PdfWriter()
    writer.append(PdfReader(io.BytesIO(source_page())))
    page = writer.pages[0]
    page_ref = writer.get_reference(page)
    original = page["/Contents"].get_object().get_data()
    page[name("/Contents")] = ref(writer, stream(
        b"/P <</MCID 0>> BDC\n" + original + b"\nEMC\n"))
    if (orphan_mcid or missing_parent_link or mismatched_parent_link or
            detached_artifact_parent):
        tag = b"/Artifact" if detached_artifact_parent else b"/Span"
        page["/Contents"].get_object().set_data(
            page["/Contents"].get_object().get_data() +
            tag + b" <</MCID 1>> BDC\nq 0 g BT /F1 8 Tf 20 20 Td "
            b"(Preserved orphan marker) Tj ET Q\nEMC\n")
    page[name("/StructParents")] = NumberObject(0)
    page[name("/Tabs")] = name("/S")

    tree = dictionary(Type=name("/StructTreeRoot"))
    tree_ref = ref(writer, tree)
    element = dictionary(Type=name("/StructElem"), S=name("/P"),
                         P=tree_ref, Pg=page_ref,
                         Alt=TextStringObject("A short lecture example"))
    element_ref = ref(writer, element)
    kids = array(NumberObject(0))
    parents = array(element_ref)
    tree[name("/K")] = array(element_ref)
    if orphan_mcid:
        parents.append(element_ref)
    if missing_parent_link or mismatched_parent_link:
        kids.append(NumberObject(1))
    if mismatched_parent_link:
        other = ref(writer, dictionary(Type=name("/StructElem"), S=name("/Span"),
                                     P=tree_ref, Pg=page_ref))
        tree[name("/K")].append(other)
        parents.append(other)
    if detached_artifact_parent:
        parents.append(ref(writer, dictionary(S=name("/Artifact"),
            K=NumberObject(1), Pg=page_ref)))
    tree[name("/RoleMap")] = dictionary(Example=name("/P"))
    tree[name("/ParentTreeNextKey")] = NumberObject(2)

    annotations = array()
    if link:
        action = dictionary(S=name("/JavaScript"), JS=TextStringObject("app.alert(1)")) \
            if active_action else dictionary(S=name("/URI"),
                URI=TextStringObject("https://example.test/lecture"))
        link_object = dictionary(Type=name("/Annot"), Subtype=name("/Link"),
            Rect=array(NumberObject(28), NumberObject(205), NumberObject(240), NumberObject(225)),
            Border=array(NumberObject(0), NumberObject(0), NumberObject(0)),
            StructParent=NumberObject(1))
        if link == "internal":
            link_object[name("/Dest")] = array(page_ref, name("/Fit"))
        else:
            link_object[name("/A")] = ref(writer, action)
        link_ref = ref(writer, link_object)
        annotations.append(link_ref)
        kids.append(dictionary(Type=name("/OBJR"), Pg=page_ref, Obj=link_ref))
    element[name("/K")] = kids

    if attachment:
        payload = b"Independent passive attachment\x00\x01\xff"
        embedded = ref(writer, stream(payload, Type=name("/EmbeddedFile")))
        file_spec = ref(writer, dictionary(Type=name("/Filespec"),
            F=TextStringObject("handout.bin"), UF=TextStringObject("handout.bin"),
            Desc=TextStringObject("Example handout"),
            EF=dictionary(F=embedded, UF=embedded)))
        annotations.append(ref(writer, dictionary(Type=name("/Annot"),
            Subtype=name("/FileAttachment"),
            Rect=array(NumberObject(25), NumberObject(175), NumberObject(45), NumberObject(195)),
            Name=name("/PushPin"), FS=file_spec)))
    if annotations:
        page[name("/Annots")] = annotations

    parent_items = array(NumberObject(0), parents)
    if link:
        parent_items.extend([NumberObject(1), element_ref])
    if malformed_parent:
        parents[0] = tree_ref
    tree[name("/ParentTree")] = ref(writer, dictionary(Nums=parent_items))
    writer._root_object[name("/StructTreeRoot")] = tree_ref
    writer._root_object[name("/MarkInfo")] = dictionary(Marked=BooleanObject(True))
    writer._root_object[name("/Lang")] = TextStringObject("en-US")
    writer.add_outline_item("Lecture example", 0)

    if layer:
        group = ref(writer, dictionary(Type=name("/OCG"),
            Name=TextStringObject("Background"),
            Usage=dictionary(View=dictionary(ViewState=name("/ON")))))
        configuration = dictionary(Order=array(group), RBGroups=array())
        writer._root_object[name("/OCProperties")] = dictionary(
            OCGs=array(group), D=configuration)
        resources = page["/Resources"].get_object()
        resources[name("/Properties")] = dictionary(BG=group)
        page[name("/Contents")].get_object().set_data(
            b"/OC /BG BDC\n" + page["/Contents"].get_object().get_data() + b"\nEMC\n")
        if conditional_layer:
            membership = ref(writer, dictionary(Type=name("/OCMD"),
                OCGs=array(group), P=name("/AllOn")))
            resources[name("/Properties")][name("/BG")] = membership

    if tagged_form:
        form = ref(writer, stream(b"q Q\n", Type=name("/XObject"),
            Subtype=name("/Form"), BBox=array(NumberObject(0), NumberObject(0),
                NumberObject(1), NumberObject(1)), StructParents=NumberObject(3)))
        resources = page["/Resources"].get_object()
        resources[name("/XObject")] = dictionary(Fm=form)
    return write(writer)


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
    magic, version, json_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + json_size + pdf_size
    facts = json.loads(result.stdout[16:16 + json_size])
    return 0, facts, result.stdout[16 + json_size:]


def structure_view(data):
    reader = PdfReader(io.BytesIO(data), strict=True)
    root = reader.trailer["/Root"]
    page = reader.pages[0]
    tree = root["/StructTreeRoot"].get_object()
    element_ref = tree["/K"][0]
    element = element_ref.get_object()
    assert element.raw_get("/P").idnum == root.raw_get("/StructTreeRoot").idnum
    assert element.raw_get("/Pg").idnum == page.indirect_reference.idnum
    assert str(element["/Alt"]) == "A short lecture example"
    parent_nums = tree["/ParentTree"]["/Nums"]
    assert int(parent_nums[0]) == 0
    assert parent_nums[1][0].idnum == element_ref.idnum
    assert str(tree["/RoleMap"]["/Example"]) == "/P"
    assert int(page["/StructParents"]) == 0
    content = page["/Contents"].get_object().get_data()
    assert b"/MCID 0" in content
    annotations = []
    for annot_ref in page.get("/Annots", []):
        annot = annot_ref.get_object()
        if annot["/Subtype"] == "/Link":
            assert int(annot["/StructParent"]) == 1
            assert parent_nums[3].idnum == element_ref.idnum
            objr = next(item for item in element["/K"]
                        if isinstance(item, DictionaryObject) and item.get("/Type") == "/OBJR")
            assert objr.raw_get("/Obj").idnum == annot_ref.idnum
            if "/Dest" in annot:
                assert annot["/Dest"][0].idnum == page.indirect_reference.idnum
                annotations.append(("GoTo", "/Fit"))
            else:
                action = annot["/A"].get_object()
                assert action["/S"] == "/URI"
                annotations.append(("URI", str(action["/URI"])))
        elif annot["/Subtype"] == "/FileAttachment":
            spec = annot["/FS"].get_object()
            payload = spec["/EF"]["/F"].get_object().get_data()
            annotations.append(("FileAttachment", str(spec["/F"]),
                                hashlib.sha256(payload).hexdigest(),
                                tuple(float(x) for x in annot["/Rect"])))
        else:
            raise AssertionError("Unexpected annotation")
    layer = root.get("/OCProperties")
    if layer:
        group_ref = layer["/OCGs"][0]
        assert layer["/D"]["/Order"][0].idnum == group_ref.idnum
        assert page["/Resources"]["/Properties"].raw_get("/BG").idnum == group_ref.idnum
        layer = (str(group_ref.get_object()["/Name"]),
                 str(group_ref.get_object()["/Usage"]["/View"]["/ViewState"]))
    assert str(root["/Outlines"]["/First"]["/Title"]) == "Lecture example"
    return (tuple(annotations), layer, str(root["/Lang"]),
            str(element["/S"]), str(page["/Tabs"]),
            b"/MCID 1" in content)


def positive(label, **options):
    source = authored_fixture(**options)
    digest = hashlib.sha256(source).digest()
    expected = structure_view(source)
    code, inspection, no_pdf = invoke(source, "--inspect")
    assert code == 0 and not no_pdf, (label, code, inspection)
    code, conversion, output = invoke(source, "--convert")
    assert code == 0 and output and conversion == inspection, (label, code, conversion)
    assert structure_view(output) == expected, label
    before = pymupdf.open(stream=source, filetype="pdf")
    after = pymupdf.open(stream=output, filetype="pdf")
    assert before[0].get_text() == after[0].get_text(), label
    assert before[0].rect == after[0].rect, label
    assert hashlib.sha256(source).digest() == digest, label
    (ROOT / f"{label}-source.pdf").write_bytes(source)
    (ROOT / f"{label}-dark.pdf").write_bytes(output)
    print("PASS", label, conversion)


def refusal(label, fragment, **options):
    source = authored_fixture(**options)
    for mode in ("--inspect", "--convert"):
        code, diagnostic, output = invoke(source, mode)
        assert code in (3, 4, 5) and fragment in diagnostic and not output, \
            (label, mode, code, diagnostic)
    print("PASS", label, "refused:", fragment)


positive("tagged-uri", link="uri")
positive("tagged-internal", link="internal")
positive("tagged-attachment", link="uri", attachment=True)
positive("tagged-static-layer", link="uri", layer=True)
positive("tagged-source-orphan", link="uri", orphan_mcid=True)
positive("tagged-missing-parent-link", link="uri", missing_parent_link=True)
positive("tagged-mismatched-parent-link", link="uri", mismatched_parent_link=True)
positive("tagged-detached-artifact", link="uri", detached_artifact_parent=True)
refusal("broken-parent", "unreachable structure element", malformed_parent=True)
refusal("active-link", "Active PDF link", active_action=True)
refusal("tagged-form", "Tagged form streams", tagged_form=True)
refusal("conditional-layer", "Conditional PDF layer membership",
        layer=True, conditional_layer=True)
