"""Authored rounded-panel, nesting and corner/occlusion contrast regressions."""

import argparse
import io
import json
from pathlib import Path
import struct
import subprocess
import tempfile

import pymupdf
from reportlab.pdfgen.canvas import Canvas
from reportlab.lib.utils import ImageReader
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--engine', required=True)
parser.add_argument('--scratch', required=True)
args = parser.parse_args()
scratch = Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(Path(__file__).resolve().parents[2] / '.codex-temp'):
    raise ValueError('Use a fresh repository-local scratch directory')
scratch.mkdir(parents=True)


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=scratch) as f:
        f.write(data)
        f.seek(0)
        result = subprocess.run([args.engine, mode], stdin=f, capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors='replace'), b''
    magic, version, count, size = struct.unpack_from('<IIII', result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + count + size
    return 0, json.loads(result.stdout[16:16 + count]), result.stdout[16 + count:]


def fixture(case):
    stream = io.BytesIO()
    c = Canvas(stream, pagesize=(400, 300), pageCompression=0)
    c.setFillColorRGB(1, 1, 1)
    c.rect(0, 0, 400, 300, fill=1, stroke=0)
    if case == 'crossing-image':
        c.drawImage(ImageReader(Image.new('RGB', (40, 80), (170, 80, 90))),
                    345, 110, width=40, height=80)
    if case == 'nested':
        c.setFillColorRGB(.1, .12, .14)
        c.roundRect(30, 60, 340, 180, 25, fill=1, stroke=0)
    if case == 'clipped':
        path = c.beginPath()
        path.rect(0, 0, 210, 300)
        c.clipPath(path, stroke=0)
    c.setFillColorRGB(.16, .15, .2)
    if case == 'crossing-hidden-panel':
        c.setFillColorRGB(.105, .110, .120)
    if case == 'double-winding':
        path = c.beginPath()
        path.moveTo(80, 100)
        for _ in range(2):
            path.lineTo(300, 100)
            path.curveTo(322, 100, 340, 118, 340, 140)
            path.lineTo(340, 200)
            path.lineTo(100, 200)
            path.curveTo(78, 200, 60, 182, 60, 160)
            path.lineTo(60, 100)
            path.lineTo(80, 100)
        path.close()
        c.drawPath(path, fill=1, stroke=0, fillMode=0)
    elif case == 'concave':
        path = c.beginPath()
        path.moveTo(60, 100)
        path.curveTo(70, 100, 180, 190, 190, 100)
        path.curveTo(200, 100, 330, 110, 340, 110)
        path.curveTo(340, 200, 330, 200, 320, 200)
        path.curveTo(60, 200, 60, 190, 60, 100)
        path.close()
        c.drawPath(path, fill=1, stroke=0)
    else:
        c.roundRect(60, 100, 280, 100, 50 if case == 'pill' else 25, fill=1, stroke=0)
    if case in ('border', 'combined-border'):
        c.setStrokeColorRGB(.3, .35, .4)
        c.setLineWidth(1)
        c.roundRect(60, 100, 280, 100, 25, fill=int(case == 'combined-border'), stroke=1)
    if case == 'thick-combined-border':
        c.setStrokeColorRGB(.16, .15, .2)
        c.setLineWidth(80)
        c.roundRect(60, 100, 280, 100, 25, fill=1, stroke=1)
    if case == 'badge':
        c.setFillColorRGB(.92, .32, .26)
        c.roundRect(120, 115, 70, 70, 15, fill=1, stroke=0)
    if case in ('crossing-border', 'crossing-accent-border', 'crossing-thick-border'):
        c.setStrokeColorRGB(*((.7, .2, .1) if case == 'crossing-accent-border' else (.16, .15, .2)))
        c.setLineWidth(4 if case == 'crossing-thick-border' else 1)
        c.roundRect(60, 100, 280, 100, 25, fill=0, stroke=1)
    if case == 'covered':
        c.setFillColorRGB(.7, .7, .7)
        c.rect(130, 110, 70, 80, fill=1, stroke=0)
    c.setFont('Helvetica', 14)
    c.setFillColorRGB(1, 1, 1)
    if case in ('joined-text', 'different-text-overlay', 'near-color-text-overlay'):
        if case == 'different-text-overlay':
            c.setFillColorRGB(.1, .8, .2)
        if case == 'near-color-text-overlay':
            c.setFillColorRGB(.299999, .150001, .150001)
        c.drawString(116, 145, '|')
        c.setFillColorRGB(1, 1, 1)
        if case == 'near-color-text-overlay':
            c.setFillColorRGB(.3, .15, .15)
    if case.startswith('crossing') and case != 'crossing-hidden':
        c.setFillColorRGB(.35, .36, .37)
    if case == 'crossing-hidden-panel':
        c.setFillColorRGB(.105, .110, .120)
    c.drawString(290 if case.startswith('crossing') else 62 if case == 'corner' else 145 if case == 'badge' else 115,
                 103 if case == 'corner' else 132 if case == 'thick-combined-border' else 145,
                 'Comment spills' if case.startswith('crossing') else '4' if case == 'badge' else 'Code = 42')
    if case == 'crossing-overlay':
        c.setFillColorRGB(.1, .2, .3)
        c.rect(345, 130, 40, 40, fill=1, stroke=0)
    if case == 'nested':
        c.drawString(100, 215, 'Outer label')
    if case == 'paint-after':
        c.setFillColorRGB(.1, .2, .1)
        c.roundRect(120, 130, 160, 40, 15, fill=1, stroke=0)
    c.save()
    return stream.getvalue()


results = []
for case in ('rounded', 'pill', 'nested', 'border', 'combined-border', 'badge', 'corner', 'concave',
             'double-winding', 'thick-combined-border', 'covered', 'clipped', 'paint-after',
             'crossing', 'crossing-border', 'crossing-accent-border', 'crossing-thick-border',
             'crossing-image', 'crossing-overlay', 'crossing-hidden', 'crossing-hidden-panel',
             'joined-text', 'different-text-overlay', 'near-color-text-overlay'):
    source = fixture(case)
    (scratch / f'{case}-source.pdf').write_bytes(source)
    positive = case in ('rounded', 'pill', 'nested', 'border', 'combined-border', 'badge', 'crossing', 'crossing-border', 'joined-text')
    code, facts, output = invoke(source, '--convert')
    if not positive:
        assert code == 3 and not output, (case, code, facts)
        results.append({'case': case, 'refused': facts.strip()})
        print('PASS', case, 'refused')
        continue
    assert code == 0 and output, (case, code, facts)
    before = pymupdf.open(stream=source, filetype='pdf')
    after = pymupdf.open(stream=output, filetype='pdf')
    geometry = lambda p: [(s['font'], s['size'], s['chars']) for s in p.get_texttrace()]
    assert geometry(before[0]) == geometry(after[0]), case
    assert [p['items'] for p in before[0].get_drawings()] == [p['items'] for p in after[0].get_drawings()], case
    pix = after[0].get_pixmap(alpha=False)
    # Panel and page samples lie away from glyphs and curved edges.
    assert max(pix.pixel(100, 150)) < 100, case
    if case == 'nested':
        assert max(pix.pixel(45, 150)) < 100, 'Outer panel must also stay dark'
    if case == 'badge':
        background = pix.pixel(155, 125)
        assert max(background) - min(background) > 30, 'Badge lost its accent'
        def luminance(rgb):
            linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in rgb]
            return sum(v * weight for v, weight in zip(linear, (.2126, .7152, .0722)))
        ink = after[0].get_texttrace()[0]['color']
        assert (luminance(ink) + .05) / (luminance([v / 255 for v in background]) + .05) >= 4.5
    assert max(s['color'][0] for s in after[0].get_texttrace()) > .8
    if case == 'crossing-border':
        strokes = [p['color'] for p in after[0].get_drawings() if 's' in p['type']]
        assert len(strokes) == 1 and max(strokes[0]) < .2, 'Crossed matching border must remain background'
    (scratch / f'{case}-dark.pdf').write_bytes(output)
    pix.save(scratch / f'{case}-dark.png')
    for mode in ('--inspect', '--convert'):
        code, facts, repeated = invoke(output, mode)
        assert code == 0 and not repeated and facts['changedPages'] == 0, (case, mode, facts)
    results.append({'case': case, 'passed': True, 'repeatNoOp': True})
    print('PASS', case, 'geometry, contrast and repeat')
(scratch / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
