"""Prove rounded-card containment survives exact cubic subdivision."""

import argparse
import io
import json
import pathlib
import struct
import subprocess

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import ContentStream, FloatObject, NameObject
from reportlab.pdfgen.canvas import Canvas


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)


def split(points):
    midpoint = lambda a, b: tuple((x + y) / 2 for x, y in zip(a, b))
    a, b, c = [midpoint(points[i], points[i + 1]) for i in range(3)]
    d, e = midpoint(a, b), midpoint(b, c)
    f = midpoint(d, e)
    return [points[0], a, d, f], [f, e, c, points[3]]


def fixture(divisions, border=False, outside=False, mirrored=False, concave=False, kink=0):
    buffer = io.BytesIO()
    c = Canvas(buffer, pagesize=(300, 200), pageCompression=0)
    c.setFillColorRGB(1, 1, 1)
    c.rect(0, 0, 300, 200, fill=1, stroke=0)
    if mirrored:
        c.translate(300, 0)
        c.scale(-1, 1)
    c.setFillColorRGB(.1, .6, .53)
    c.setStrokeColorRGB(.1, .6, .53)
    c.setLineWidth(1)
    c.roundRect(45, 50, 210, 100, 20, fill=1, stroke=int(border))
    c.setFillColorRGB(1, 1, 1)
    c.setFont("Helvetica", 14)
    # The built-in font intentionally has no supplied widths. Keep its label
    # within even the native conservative one-em advance so this tests curves.
    c.drawString(48 if outside else 85, 52 if outside else 95, "Code = 42")
    c.save()
    reader = PdfReader(io.BytesIO(buffer.getvalue()))
    writer = PdfWriter()
    writer.clone_document_from_reader(reader)
    page = writer.pages[0]
    content = ContentStream(page["/Contents"], writer)
    operations, point, curve = [], None, 0
    for operands, op in content.operations:
        if op == b"c":
            points = [point] + [(float(operands[i]), float(operands[i + 1])) for i in range(0, 6, 2)]
            parts = [points]
            count = divisions if divisions != -1 else int(curve == 0)
            for _ in range(count):
                parts = [piece for item in parts for piece in split(item)]
            if concave and curve == 0:
                parts[0][1] = (150, 120)
            if kink and len(parts) > 1:
                # Serialized split tangents need not be exactly collinear.
                # Keep original path tokens; only containment may change.
                x, y = parts[1][1]
                parts[1][1] = (x, y + kink)
            for part in parts:
                operations.append(([FloatObject(v) for p in part[1:] for v in p], b"c"))
            point = points[-1]
            curve += 1
        else:
            operations.append((operands, op))
            if op in (b"m", b"l"):
                point = tuple(float(v) for v in operands)
    content.operations = operations
    page[NameObject("/Contents")] = writer._add_object(content)
    result = io.BytesIO()
    writer.write(result)
    return result.getvalue()


def invoke(data, label):
    path = scratch / (label + ".pdf")
    path.write_bytes(data)
    with path.open("rb") as handle:
        reply = subprocess.run([args.engine, "--convert"], stdin=handle, capture_output=True, timeout=90)
    if reply.returncode:
        assert not reply.stdout
        return reply.returncode, reply.stderr.decode(errors="replace"), b""
    magic, version, n, size = struct.unpack_from("<4I", reply.stdout)
    assert (magic, version) == (0x31445043, 1) and len(reply.stdout) == 16 + n + size
    return 0, json.loads(reply.stdout[16:16 + n]), reply.stdout[16 + n:]


results = []
for case, divisions, border, outside, mirrored, concave, kink in [
    ("one-corner", -1, False, False, False, False, 0),
    ("all-corners", 1, False, False, False, False, 0),
    ("sixteen-cubics", 2, False, False, False, False, 0),
    ("split-border", 1, True, False, False, False, 0),
    ("mirrored", 1, False, False, True, False, 0),
    ("positive-kink", 1, False, False, False, False, .00001),
    ("negative-kink", 1, False, False, False, False, -.00001),
    ("kink-border", 1, True, False, False, False, .00001),
    ("kink-mirrored", 1, False, False, True, False, -.00001),
    ("kink-cutout", 1, False, True, False, False, .00001),
    ("corner-cutout", 1, False, True, False, False, 0),
    ("concave-control", 1, False, False, False, True, 0),
    ("excessive-curves", 3, False, False, False, False, 0),
]:
    source = fixture(divisions, border, outside, mirrored, concave, kink)
    code, facts, output = invoke(source, case + "-source")
    if outside or concave or divisions == 3:
        assert code == 3 and not output, (case, facts)
    else:
        assert code == 0 and output, (case, facts)
        (scratch / (case + "-dark.pdf")).write_bytes(output)
        with pymupdf.open(stream=source, filetype="pdf") as before, pymupdf.open(stream=output, filetype="pdf") as after:
            glyphs = lambda page: [(s["font"], s["size"], s["chars"]) for s in page.get_texttrace()]
            assert glyphs(before[0]) == glyphs(after[0])
            assert [d["items"] for d in before[0].get_drawings()] == [d["items"] for d in after[0].get_drawings()]
            pix = after[0].get_pixmap(alpha=False)
            pix.save(scratch / (case + "-dark.png"))
            assert max(pix.pixel(60, 100)) < 150 and max(pix.pixel(10, 10)) < 30
        repeated, repeat, empty = invoke(output, case + "-repeat")
        assert repeated == 0 and repeat["changedPages"] == 0 and not empty, (case, repeat)
    results.append({"case": case, "exitCode": code, "facts": facts})
(scratch / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"Passed {len(results)} subdivided panel cases")
