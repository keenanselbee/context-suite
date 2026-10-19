"""Preserve same-color overlapping text across a later opaque panel.

The authored scene uses the stable dark palette so every protected glyph pixel
can be compared exactly. A separate pale patch forces a real conversion.
Requires ReportLab, its bundled Vera test font, and PyMuPDF.
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
import reportlab
from reportlab.pdfbase.pdfmetrics import registerFont
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = Path(__file__).resolve().parents[2]
scratch = Path(args.scratch).resolve()
engine = Path(args.engine).resolve(strict=True)
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)
registerFont(TTFont("SyntheticBoldFixture", str(Path(reportlab.__file__).parent / "fonts" / "Vera.ttf")))
PAPER = (18 / 255, 19 / 255, 20 / 255)
INK = (244 / 255, 235 / 255, 216 / 255)
PANEL = (.105, .110, .120)


def fixture(case, with_text=True):
    output = io.BytesIO()
    canvas = Canvas(output, pagesize=(300, 200), pageCompression=0)
    canvas.setFillColorRGB(*PAPER)
    canvas.rect(0, 0, 300, 200, fill=1, stroke=0)
    canvas.setFont("SyntheticBoldFixture", 18)
    if with_text:
        canvas.setFillColorRGB(*INK)
        canvas.drawString(145, 25, "2")
    canvas.setFillColorRGB(*PANEL)
    canvas.rect(90, 30, 120, 13, fill=1, stroke=0)
    if with_text:
        color = (.85, .3, .1) if case == "different-color" else \
            (.956, .921, .847) if case == "near-color" else INK
        canvas.setFillColorRGB(*color)
        if case == "mixed-stroke":
            canvas.setStrokeColorRGB(*INK)
            canvas.setLineWidth(.2)
            canvas._code.append("2 Tr")
        canvas.drawString(145.12, 25, "2")
        if case == "mixed-stroke":
            canvas._code.append("0 Tr")
    canvas.setFillColorRGB(.8, .8, .8)
    canvas.rect(250, 150, 20, 20, fill=1, stroke=0)
    canvas.save()
    return output.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        process = subprocess.run([str(engine), mode], stdin=file, capture_output=True, timeout=45)
        file.seek(0)
        assert file.read() == source
    if process.returncode:
        assert not process.stdout
        return process.returncode, process.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_size + pdf_size
    return (0, json.loads(process.stdout[16:16 + metadata_size]), process.stdout[16 + metadata_size:])


def facts(data):
    with pymupdf.open(stream=data, filetype="pdf") as document:
        page = document[0]
        text = [(span["font"], span["size"], span["chars"], span["color"],
                 span["opacity"], span["type"]) for span in page.get_texttrace()]
        paths = [(drawing["type"], drawing["items"], drawing["width"])
                 for drawing in page.get_drawings()]
        return text, paths, page.get_pixmap(alpha=False)


results = []
for case in ("same-color", "different-color", "near-color", "mixed-stroke"):
    source = fixture(case)
    digest = hashlib.sha256(source).hexdigest()
    (scratch / f"{case}-source.pdf").write_bytes(source)
    inspect_code, inspection, empty = invoke(source, "--inspect")
    code, converted, output = invoke(source, "--convert")
    assert inspect_code == code and (code != 0 or inspection == converted and not empty), \
        (case, inspect_code, inspection, code, converted)
    if case != "same-color":
        assert code == 3 and not output, (case, code, converted)
        details = {"refused": True, "reason": converted}
    else:
        assert code == 0 and output and converted["changedPages"] == 1, (case, code, converted)
        before_text, before_paths, original = facts(source)
        after_text, after_paths, dark = facts(output)
        assert before_text == after_text and before_paths == after_paths, "Text or path geometry changed"
        assert len(before_text) == 2 and all(span[2][0][0] == ord("2") for span in before_text)
        # Prove the visible glyph crosses the later panel's lower boundary,
        # rather than passing only because its entire ink is occluded.
        blank = facts(fixture(case, with_text=False))[2]
        lower = upper = 0
        for y in range(155, 181):
            for x in range(142, 160):
                if max(abs(a - b) for a, b in zip(original.pixel(x, y), blank.pixel(x, y))) > 20:
                    if y < 170:
                        upper += 1
                    else:
                        lower += 1
        assert lower > 5 and upper > 5, (case, lower, upper)
        assert all(original.pixel(x, y) == dark.pixel(x, y)
                   for y in range(150, 185) for x in range(85, 215)), "Protected glyph pixels changed"
        assert min(original.pixel(260, 40)) > 180 and max(dark.pixel(260, 40)) < 70
        for mode in ("--inspect", "--convert"):
            repeat_code, repeated, duplicate = invoke(output, mode)
            assert repeat_code == 0 and repeated["changedPages"] == 0 and \
                repeated["alreadyDarkPages"] == 1 and not duplicate, (case, mode, repeated)
        (scratch / f"{case}-dark.pdf").write_bytes(output)
        original.save(scratch / f"{case}-source.png")
        dark.save(scratch / f"{case}-dark.png")
        details = {"converted": True, "glyphPixelsExact": True, "geometryAndColorsPreserved": True,
                   "visibleInkBothSides": [lower, upper], "repeatNoOp": True}
    assert hashlib.sha256(source).hexdigest() == digest
    results.append({"case": case, "sourceSha256": digest, **details})
    print("PASS", case, flush=True)

(scratch / "results.json").write_text(json.dumps({
    "engine": str(engine), "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
    "suiteSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "cases": results,
}, indent=2), encoding="utf-8")
print(f"Passed {len(results)} synthetic-bold occlusion checks.")
