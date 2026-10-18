"""Check token-scoped PDF drawing coordinate recovery and refusal boundaries."""

import argparse
import importlib.util
import io
import json
import pathlib
import struct
import subprocess

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import DecodedStreamObject, DictionaryObject, NameObject


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)
engine = pathlib.Path(args.engine).resolve(strict=True)
spec = importlib.util.spec_from_file_location("probe", pathlib.Path(__file__).with_name("Test-PdfDarkNumericInterpretation.py"))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


def invoke(data, mode, label):
    path = scratch / f"{label}.pdf"
    path.write_bytes(data)
    with path.open("rb") as source:
        reply = subprocess.run([str(engine), mode], stdin=source, capture_output=True, timeout=90)
    assert path.read_bytes() == data
    if reply.returncode:
        assert not reply.stdout
        return reply.returncode, reply.stderr.decode(errors="replace"), b""
    magic, version, count, size = struct.unpack_from("<4I", reply.stdout)
    assert (magic, version) == (0x31445043, 1) and len(reply.stdout) == 16 + count + size
    return 0, json.loads(reply.stdout[16:16 + count]), reply.stdout[16 + count:]


def pixels(data):
    with pymupdf.open(stream=data, filetype="pdf") as doc:
        return [page.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False).samples for page in doc]


def with_content(content, attached=False):
    writer = PdfWriter()
    page = writer.add_blank_page(160, 120)
    font = DictionaryObject({NameObject("/Type"): NameObject("/Font"),
        NameObject("/Subtype"): NameObject("/Type1"), NameObject("/BaseFont"): NameObject("/Helvetica")})
    page[NameObject("/Resources")] = DictionaryObject({NameObject("/Font"):
        DictionaryObject({NameObject("/F1"): writer._add_object(font)})})
    stream = DecodedStreamObject()
    stream.set_data(content)
    page[NameObject("/Contents")] = writer._add_object(stream)
    if attached:
        writer.add_attachment("original-drawing.txt", content)
        # Deliberately alias the page stream as passive attachment bytes. Repair
        # must not change those bytes through a shared object reference.
        filespec = writer._root_object["/Names"]["/EmbeddedFiles"]["/Names"][1]
        filespec["/EF"][NameObject("/F")] = page.raw_get("/Contents")
    result = io.BytesIO()
    writer.write(result)
    return result.getvalue()


results = []
for token, normalized, kind in probe.TOKENS:
    contexts = probe.DRAWING_CONTEXTS if kind == "candidate" else ("m", "cm", "re")
    for context in contexts:
        case = f"{len(results):03d}-{context}"
        data = probe.fixture(context, token)
        code, facts, output = invoke(data, "--convert", case + "-source")
        inspect_code, inspection, no_output = invoke(data, "--inspect", case + "-inspect")
        assert code == inspect_code and not no_output and (code != 0 or facts == inspection), (token, context)
        if kind == "candidate":
            assert code == 0 and facts["recoveredCoordinates"] == 1 and output, (token, context, facts)
            reference_code, reference_facts, reference = invoke(probe.fixture(context, normalized), "--convert", case + "-reference")
            assert reference_code == 0 and reference_facts["recoveredCoordinates"] == 0
            assert pixels(output) == pixels(reference), (token, context)
            repeated, repeat_facts, repeat_output = invoke(output, "--convert", case + "-dark")
            assert repeated == 0 and repeat_facts["changedPages"] == 0 and repeat_facts["recoveredCoordinates"] == 0 and not repeat_output
        else:
            assert code in (3, 4) and not output, (token, context, facts)
        results.append({"token": token, "context": context, "exitCode": code, "facts": facts})

for context in probe.OTHER_CONTEXTS:
    code, facts, output = invoke(probe.fixture(context, "0.00-60"), "--convert", "excluded-" + context)
    assert code in (3, 4) and not output, (context, facts)
    results.append({"excludedContext": context, "exitCode": code})

base = b"1 1 1 rg 0 0 160 120 re f 0 0 0 rg BT /F1 12 Tf 10 50 Td (0.00-60) Tj ET\n"
for name, content in {
    "literal-string": base,
    "escaped-string": base.replace(b"(0.00-60)", b"(before\\(0.00-60\\)after)"),
    "hex-string": base.replace(b"(0.00-60)", b"<302E30302D3630>"),
    "comment": base + b"% 0.00-60 20 m\n",
    "name": base + b"/0.00-60 MP\n",
    "array-string": base.replace(b"(0.00-60) Tj", b"[(0.00-60)] TJ"),
}.items():
    code, facts, output = invoke(with_content(content), "--convert", name)
    assert code == 0 and output and facts["recoveredCoordinates"] == 0, (name, facts)
    assert PdfReader(io.BytesIO(output)).pages[0].extract_text() == PdfReader(io.BytesIO(with_content(content))).pages[0].extract_text()
    results.append({"lexicalCase": name, "exitCode": code})

for name, content in {
    "wrong-arity": b"0.00-60 20 40 m\n",
    "non-number": b"0.00-60 /Name m\n",
    "incomplete": b"0.00-60 20",
    "mixed-malformed": b"0.00-60 0.00+60 m\n",
    "array-number": b"BT /F1 12 Tf [(text) 0.00-60] TJ ET\n",
    "inline-image": b"BI /W 1 /H 1 /BPC 8 /CS /RGB ID 0.00-60 EI\n",
}.items():
    code, facts, output = invoke(with_content(content), "--convert", name)
    assert code in (3, 4) and not output, (name, facts)
    results.append({"negativeCase": name, "exitCode": code})

attached_content = probe.content("m", "0.00-60")
code, facts, output = invoke(with_content(attached_content, attached=True), "--convert", "attachment-alias")
assert code == 0 and output and facts["recoveredCoordinates"] == 1, facts
assert PdfReader(io.BytesIO(output)).attachments["original-drawing.txt"] == [attached_content]
reference_code, _, reference = invoke(with_content(probe.content("m", "0.00")), "--convert", "attachment-reference")
assert reference_code == 0 and pixels(output) == pixels(reference)
repeated, repeat_facts, repeat_output = invoke(output, "--convert", "attachment-dark")
assert repeated == 0 and repeat_facts["changedPages"] == 0 and repeat_facts["recoveredCoordinates"] == 0 and not repeat_output
results.append({"preservationCase": "attachment-alias", "exitCode": code, "facts": facts})
(scratch / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"Passed {len(results)} drawing coordinate recovery cases")
