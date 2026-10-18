"""Render an explicitly supplied local document for PDF dark-mode fidelity review.

This is an opt-in engineering review, not a claim of automatic visual acceptance.
Customer PDFs are never added to the repository or changed. Test-only dependencies:
PyMuPDF, Pillow and pypdf.
"""
import argparse
import hashlib
import html
import json
import pathlib
import re
import struct
import subprocess

import pymupdf
from PIL import Image, ImageDraw
from pypdf import PdfReader

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--source", required=True)
parser.add_argument("--reference")
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
root = pathlib.Path(args.scratch).resolve()
if root.exists() or not root.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
engine = pathlib.Path(args.engine).resolve(strict=True)
source = pathlib.Path(args.source).resolve(strict=True)
reference = pathlib.Path(args.reference).resolve(strict=True) if args.reference else None
root.mkdir(parents=True)


def identity(path):
    return dict(path=str(path), bytes=path.stat().st_size,
                sha256=hashlib.sha256(path.read_bytes()).hexdigest().upper())


originals = [identity(path) for path in [source, reference] if path]
with source.open("rb") as stream:
    process = subprocess.run([str(engine), "--convert"], stdin=stream,
                             capture_output=True, timeout=120)
report = dict(engine=identity(engine), originals=originals, exitCode=process.returncode,
              diagnostic=process.stderr.decode(errors="replace"), pages=[],
              visualAcceptance="pending human/agent rendered review")
if process.returncode:
    assert not process.stdout, "A refused document returned output bytes"
    (root / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    raise SystemExit(process.returncode)
magic, version, facts_length, pdf_length = struct.unpack_from("<IIII", process.stdout)
assert (magic, version) == (0x31445043, 1)
assert len(process.stdout) == 16 + facts_length + pdf_length and pdf_length > 0
report["facts"] = json.loads(process.stdout[16:16 + facts_length])
output = root / "reading-copy.pdf"
output.write_bytes(process.stdout[16 + facts_length:])
before = pymupdf.open(source)
after = pymupdf.open(output)
control = pymupdf.open(reference) if reference else None
assert len(before) == len(after)
if control:
    assert len(control) == len(before)
source_reader, output_reader = PdfReader(source), PdfReader(output)


def glyphs(page):
    # Color changes are intentional; placement, text, font and rendering mode are not.
    return [(span["font"], span["size"], span["dir"], span["type"], span["chars"])
            for span in page.get_texttrace()]


def paths(page):
    return [(entry["items"], entry["rect"], entry["closePath"], entry["even_odd"])
            for entry in page.get_drawings()]


def images(page):
    resources = page["/Resources"].get_object()
    result = {}

    def walk(resources, prefix, depth):
        assert depth < 16
        for name, ref in resources.get("/XObject", {}).get_object().items() if "/XObject" in resources else []:
            obj = ref.get_object()
            key = prefix + name
            if obj.get("/Subtype") == "/Image":
                mask = obj.get("/SMask")
                result[key] = (obj["/Width"], obj["/Height"],
                               hashlib.sha256(mask.get_object().get_data()).hexdigest() if mask else None)
            elif obj.get("/Subtype") == "/Form":
                walk(obj.get("/Resources", resources).get_object(), key, depth + 1)
    walk(resources, "", 0)
    return result


cards = []
thumbnails = []
geometry_copy = pymupdf.open(stream=output.read_bytes(), filetype='pdf')
backing_pattern = re.compile(rb'/Artifact BMC\nq\n([0-9.]+) ([0-9.]+) ([0-9.]+) rg\n([0-9.]+) ([0-9.]+) ([0-9.]+) ([0-9.]+) re\nf\nQ\nEMC\n')
for index, (left, right) in enumerate(zip(before, after)):
    assert left.rect == right.rect and left.rotation == right.rotation, (index + 1, "page geometry")
    assert left.get_text() == right.get_text(), (index + 1, "searchable text")
    assert glyphs(left) == glyphs(right), (index + 1, "glyph placement")
    # Native validation proves placement and exact typed backing insertions.
    # Independently compare the original vectors after removing only additional
    # generated flat backdrops from a disposable geometry-check copy. Render
    # the full output, including every backing, for the separate visual review.
    source_content = left.read_contents()
    added_backings = []
    seen = {}
    def remove_added_backing(match):
        marker = match.group(0)
        seen[marker] = seen.get(marker, 0) + 1
        if seen[marker] <= source_content.count(marker):
            return marker
        red, green, blue, x, y, width, height = [float(value) for value in match.groups()]
        assert all(0 <= value <= 1 for value in (red, green, blue))
        assert 0 <= x < x + width <= 1.000001 and 0 <= y < y + height <= 1.000001
        added_backings.append(dict(rectangle=[x, y, width, height], color=[red, green, blue]))
        return b''
    filtered = backing_pattern.sub(remove_added_backing, right.read_contents())
    if added_backings:
        clone = geometry_copy[index]
        clone.clean_contents()
        geometry_copy.update_stream(clone.get_contents()[0], filtered)
    original_paths, result_paths = paths(left), paths(geometry_copy[index])
    # The native converter adds one paper rectangle ahead of unchanged drawing geometry.
    assert result_paths == original_paths or result_paths[1:] == original_paths, (index + 1, "vector geometry")
    assert images(source_reader.pages[index]) == images(output_reader.pages[index]), (index + 1, "image dimensions/masks")
    row = []
    views = [("Original", left), ("Converted", right)]
    if control:
        views.append(("Reference", control[index]))
    sheet = Image.new("RGB", (540 * len(views), 328), "#16181b")
    draw = ImageDraw.Draw(sheet)
    for column, (label, page) in enumerate(views):
        filename = f"page-{index + 1:02}-{label.lower()}.png"
        page.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False).save(root / filename)
        with Image.open(root / filename) as image:
            image.thumbnail((534, 298))
            sheet.paste(image, (column * 540 + 3, 27))
        draw.text((column * 540 + 8, 7), f"Page {index + 1} - {label}", fill="white")
        row.append(f'<figure><figcaption>{label}</figcaption><a href="{filename}"><img loading="lazy" src="{filename}"></a></figure>')
    thumbnails.append(sheet)
    cards.append(f'<section id="page-{index + 1}"><h2>Page {index + 1}</h2><div class="views">' + "".join(row) + "</div></section>")
    report["pages"].append(dict(page=index + 1, text=True, glyphs=True, vectors=True, masks=True,
                                addedImageBackings=added_backings))
for start in range(0, len(thumbnails), 5):
    group = thumbnails[start:start + 5]
    sheet = Image.new("RGB", (group[0].width, sum(item.height for item in group)), "#16181b")
    for offset, item in enumerate(group):
        sheet.paste(item, (0, offset * item.height))
    sheet.save(root / f"contact-{start + 1:02}-{start + len(group):02}.png")
assert originals == [identity(path) for path in [source, reference] if path], "Original changed"
report["output"] = identity(output)
report["structuralChecksPassed"] = True
(root / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
(root / "index.html").write_text('''<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>PDF dark-mode fidelity review</title>
<style>body{background:#141619;color:#ece9e1;font:16px system-ui;margin:24px}h1{font-size:26px}
p{max-width:850px;line-height:1.5}a{color:#a9cbff}.views{display:flex;gap:12px}figure{margin:0;flex:1;min-width:0}
img{width:100%;height:auto}figcaption{margin:8px 0}section{margin:36px 0}h2{font-size:19px}
@media(max-width:850px){.views{display:block}figure{margin-bottom:20px}}</style>
<h1>PDF dark-mode fidelity review</h1><p>Independent text, glyph placement, vector geometry and mask checks passed.
These checks do not prove visual fidelity. Inspect every page for readable labels, arrows, photos and transparent overlaps.
Click an image for its full-size render. Preserved embedded images may retain light backgrounds;
the converted copy is not intended to match the reference pixel for pixel.</p><p>Source: ''' + html.escape(source.name) +
    '</p><p><a href="reading-copy.pdf">Open converted PDF</a> &middot; <a href="report.json">Verification evidence</a></p>' +
    "".join(cards) + "</html>", encoding="utf-8")
print(json.dumps(dict(output=str(output), pages=len(after), structuralChecksPassed=True,
                     review=str(root / "index.html"))))
