"""Check that tiny rounded-card tangents cannot hide a long outline miter.

The joined cubics have nonzero endpoint tangents just above the native path
threshold. Their eight-way subdivisions have first/last control vectors below
that threshold, so substituting a farther chord would underestimate a miter.
All PDFs are authored here and outputs stay in a fresh scratch directory.
"""

import argparse
import hashlib
import io
import json
import math
import pathlib
import struct
import subprocess
import tempfile

import numpy as np
import pymupdf
import reportlab
from reportlab.pdfbase.pdfmetrics import registerFont
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
parser.add_argument("--probe", action="store_true")
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)
engine = pathlib.Path(args.engine).resolve(strict=True)
registerFont(TTFont("ShortTangentsFixture",
                  str(pathlib.Path(reportlab.__file__).parent / "fonts" / "Vera.ttf")))


def fixture(*, text=True, stroke=True, dangerous=True, text_x=102, text_y=99.25,
            safe_x=38.5, safe_y=100, apex=True):
    output = io.BytesIO()
    canvas = Canvas(output, pagesize=(200, 200), pageCompression=0)
    canvas.setFillColorRGB(.11, .106, .133)
    canvas.rect(0, 0, 200, 200, fill=1, stroke=0)
    outgoing = "99.9999989 100.0000004" if dangerous else "100 100.00002"
    incoming = "99.9999989" if dangerous else "99.99998"
    path = (f"50 50 m 80 70 {incoming} 100 100 100 c "
            f"{outgoing} 80 150 50 150 c "
            "45 150 40 145 40 140 c 40 60 l "
            "40 55 45 50 50 50 c h ")
    canvas._code.append("q 0.165 0.153 0.20 rg " + path + "f Q")
    if stroke:
        canvas._code.append("q 0.239 0.224 0.282 RG 1 w 0 j 10 M " + path + "S Q")
    if text:
        canvas.setFillColorRGB(.91, .337, .294)
        canvas.setFont("ShortTangentsFixture", 5)
        canvas.drawString(safe_x, safe_y, "X")
        if apex:
            canvas.setFillColorRGB(.30, .28, .35)
            canvas.drawString(text_x, text_y, "I")
    canvas.save()
    return output.getvalue()


def render(data, scale=16):
    with pymupdf.open(stream=data, filetype="pdf") as document:
        return document[0].get_pixmap(matrix=pymupdf.Matrix(scale, scale), alpha=False)


def collision(case, text_x, text_y):
    source = fixture(dangerous=case, text_x=text_x, text_y=text_y)
    no_text = fixture(text=False, dangerous=case, text_x=text_x, text_y=text_y)
    no_stroke = fixture(stroke=False, dangerous=case, text_x=text_x, text_y=text_y)
    paper = fixture(text=False, stroke=False, dangerous=case, text_x=text_x, text_y=text_y)
    pixmaps = [render(data) for data in (source, no_text, no_stroke, paper)]
    arrays = [np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.width, 3).astype(np.int16)
              for pix in pixmaps]
    glyph = np.max(np.abs(arrays[2] - arrays[3]), axis=2) > 12
    outline = np.max(np.abs(arrays[1] - arrays[3]), axis=2) > 12
    intersection = glyph & outline
    ys, xs = np.nonzero(intersection)
    miter = outline[:, int(101 * 16):int(105 * 16)]
    oy, ox = np.nonzero(outline)
    gy, gx = np.nonzero(glyph)
    # PDF's miter is the intersection of the two exterior offset tangents.
    # This continuous-geometry witness is independent of the renderer's
    # flattening tolerance for a 1e-6 pt curve-end control vector.
    outgoing = (99.9999989 - 100, 100.0000004 - 100)
    length = math.hypot(*outgoing)
    ux, uy = outgoing[0] / length, outgoing[1] / length
    a = (100, 99.5)
    b = (100 + .5 * uy, 100 - .5 * ux)
    t = (99.5 - b[1]) / uy
    tip = (b[0] + t * ux, 99.5)
    def side(p, q, x, y):
        return (q[0] - p[0]) * (y - p[1]) - (q[1] - p[1]) * (x - p[0])
    page_x, page_y = (gx + .5) / 16, 200 - (gy + .5) / 16
    total = side(a, b, *tip)
    triangle = ((side(a, b, page_x, page_y) * total >= 0) &
                (side(b, tip, page_x, page_y) * total >= 0) &
                (side(tip, a, page_x, page_y) * total >= 0))
    return source, {"actualInkOutlinePixels16x": int(intersection.sum()),
                    "apexInkOutlinePixels16x": int(intersection[:, int(100.7 * 16):].sum()),
                    "farMiterPixels16x": int(miter.sum()),
                    "theoreticalMiterTipX": tip[0] if case else None,
                    "miterLengthToHalfWidth": math.hypot(tip[0] - 100, tip[1] - 100) / .5
                    if case else None,
                    "glyphInkInsideMiterTriangle16x": int(triangle.sum()) if case else None,
                    "outlineBounds16x": [int(ox.min()), int(oy.min()), int(ox.max()), int(oy.max())],
                    "glyphBounds16x": [int(gx.min()), int(gy.min()), int(gx.max()), int(gy.max())],
                    "contactBounds16x": [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())]
                    if len(xs) else None}


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=scratch) as source:
        source.write(data)
        source.seek(0)
        reply = subprocess.run([str(engine), mode], stdin=source, capture_output=True, timeout=60)
    if reply.returncode:
        assert not reply.stdout
        return reply.returncode, reply.stderr.decode(errors="replace").strip(), b""
    magic, version, facts_size, pdf_size = struct.unpack_from("<IIII", reply.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(reply.stdout) == 16 + facts_size + pdf_size
    return 0, json.loads(reply.stdout[16:16 + facts_size]), reply.stdout[16 + facts_size:]


results = []
for name, dangerous in (("short-opposed-tangents", True), ("long-aligned-tangents", False)):
    source, witness = collision(dangerous, 102, 99.25)
    (scratch / f"{name}-source.pdf").write_bytes(source)
    render(source, 4).save(scratch / f"{name}-source.png")
    code, facts, output = invoke(source, "--convert")
    inspected_code, inspected, empty = invoke(source, "--inspect")
    assert code == inspected_code and (code != 0 or (facts == inspected and not empty))
    entry = {"case": name, "exitCode": code, "facts": facts, **witness}
    if output:
        (scratch / f"{name}-dark.pdf").write_bytes(output)
        render(output, 4).save(scratch / f"{name}-dark.png")
        with pymupdf.open(stream=source, filetype="pdf") as before, \
                pymupdf.open(stream=output, filetype="pdf") as after:
            text_facts = lambda page: [(span["font"], span["size"], span["bbox"], span["chars"])
                                       for span in page.get_texttrace()]
            entry["textGeometryPreserved"] = text_facts(before[0]) == text_facts(after[0])
            entry["pathGeometryPreserved"] = [d["items"] for d in before[0].get_drawings()] == \
                [d["items"] for d in after[0].get_drawings()]
            strokes = [drawing["color"] for drawing in after[0].get_drawings()
                       if drawing["color"] is not None]
            entry["originalOutlinePreserved"] = len(strokes) == 1 and all(
                abs(a - b) < .0001 for a, b in zip(strokes[0], (.239, .224, .282)))
            entry["outsidePaperDark"] = max(after[0].get_pixmap().pixel(5, 5)) < 100
        repeat_code, repeat, duplicate = invoke(output, "--convert")
        entry["repeatNoOp"] = repeat_code == 0 and repeat["changedPages"] == 0 and not duplicate
    results.append(entry)
    print("PROBE" if args.probe else "CHECK", name, entry, flush=True)

(scratch / "results.json").write_text(json.dumps({
    "engine": str(engine), "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
    "results": results}, indent=2), encoding="utf-8")
if not args.probe:
    assert results[0]["actualInkOutlinePixels16x"] > 0, results[0]
    # A renderer may flatten away this tiny apex or render the ideal miter.
    # Either behavior is compatible with the conservative geometric refusal.
    assert results[0]["glyphInkInsideMiterTriangle16x"] > 0, results[0]
    assert 2 < results[0]["miterLengthToHalfWidth"] < 10, results[0]
    assert results[0]["exitCode"] == 3, results[0]
    assert results[1]["exitCode"] == 0, results[1]
    assert all(results[1][key] for key in ("textGeometryPreserved", "pathGeometryPreserved", "originalOutlinePreserved",
                                       "outsidePaperDark", "repeatNoOp")), results[1]
