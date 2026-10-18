"""Authored single-glyph panel-boundary contrast controls."""

import argparse
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
parser.add_argument('--engine', required=True)
parser.add_argument('--scratch', required=True)
parser.add_argument('--probe', action='store_true', help='Record baseline behavior')
args = parser.parse_args()
root = Path(args.scratch).resolve()
if root.exists() or not root.is_relative_to(Path(__file__).resolve().parents[2] / '.codex-temp'):
    raise ValueError('Use a fresh repository-local scratch directory')
root.mkdir(parents=True)
engine = Path(args.engine).resolve(strict=True)
registerFont(TTFont('CrossingFixture', str(Path(reportlab.__file__).parent / 'fonts' / 'Vera.ttf')))

CASES = ('straight-edge', 'curved-edge', 'hidden-source-text',
         'hidden-existing-crossing', 'near-hidden-existing-crossing',
         'low-contrast-existing-crossing', 'intervening-paint', 'bad-source-contrast',
         'different-color-outline')
POSITIVES = {'straight-edge', 'curved-edge'}
PANEL = (.16, .15, .20)


def ellipse(canvas):
    k = .5522847498307936
    path = canvas.beginPath()
    path.moveTo(60, 150)
    path.curveTo(60, 150 + 50 * k, 200 - 140 * k, 200, 200, 200)
    path.curveTo(200 + 140 * k, 200, 340, 150 + 50 * k, 340, 150)
    path.curveTo(340, 150 - 50 * k, 200 + 140 * k, 100, 200, 100)
    path.curveTo(200 - 140 * k, 100, 60, 150 - 50 * k, 60, 150)
    path.close()
    return path


def fixture(case, draw_crossing=True):
    output = io.BytesIO()
    canvas = Canvas(output, pagesize=(400, 300), pageCompression=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 400, 300, fill=1, stroke=0)
    canvas.setFillColorRGB(*PANEL)
    if case == 'curved-edge':
        canvas.drawPath(ellipse(canvas), fill=1, stroke=0)
    else:
        canvas.roundRect(60, 100, 280, 100, 25, fill=1, stroke=0)
    if case == 'different-color-outline':
        canvas.setStrokeColorRGB(.239, .224, .282)
        canvas.setLineWidth(1)
        canvas.roundRect(60, 100, 280, 100, 25, fill=0, stroke=1)
    if case == 'intervening-paint':
        canvas.setFillColorRGB(.8, .7, .6)
        canvas.rect(197, 195, 22, 11, fill=1, stroke=0)
    canvas.setFont('CrossingFixture', 14)
    if case in ('hidden-source-text', 'hidden-existing-crossing',
                'near-hidden-existing-crossing', 'low-contrast-existing-crossing'):
        hidden = ((.1601, .1501, .2001) if case == 'near-hidden-existing-crossing' else
                  (.25, .25, .25) if case == 'low-contrast-existing-crossing' else PANEL)
        canvas.setFillColorRGB(*hidden)
        canvas.drawString(100, 150, 'HIDDEN')
    canvas.setFillColorRGB(*((.98, .98, .98) if case == 'bad-source-contrast' else (.35, .36, .37)))
    if draw_crossing:
        if case in ('hidden-existing-crossing', 'near-hidden-existing-crossing',
                    'low-contrast-existing-crossing'):
            canvas.drawString(290, 145, 'Comment spills')
        else:
            canvas.drawString(*((293, 181) if case == 'curved-edge' else (200, 194.5)), 'X')
    canvas.save()
    return output.getvalue()


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=root) as source:
        source.write(data)
        source.seek(0)
        result = subprocess.run([engine, mode], stdin=source, capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors='replace'), b''
    magic, version, fact_size, pdf_size = struct.unpack_from('<IIII', result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + fact_size + pdf_size
    return 0, json.loads(result.stdout[16:16 + fact_size]), result.stdout[16 + fact_size:]


def contrast(a, b):
    def luminance(color):
        linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in color]
        return sum(v * weight for v, weight in zip(linear, (.2126, .7152, .0722)))
    left, right = sorted((luminance(a), luminance(b)))
    return (right + .05) / (left + .05)


def geometry(data, case):
    doc = pymupdf.open(stream=data, filetype='pdf')
    page = doc[0]
    spans = [(span['font'], span['size'], tuple(span['bbox']), span['chars'], span['color'])
             for span in page.get_texttrace()]
    drawings = [(item['type'], tuple(item['rect']), item['items'], item['fill'], item['color'])
                for item in page.get_drawings()]
    assert spans and all('BitstreamVeraSans' in span[0] for span in spans), spans
    crossing = spans[-1]
    if case not in ('hidden-existing-crossing', 'near-hidden-existing-crossing',
                    'low-contrast-existing-crossing'):
        assert len(crossing[3]) == 1 and crossing[3][0][0] == ord('X'), crossing
    else:
        assert len(crossing[3]) > 1, crossing
    return spans, drawings


def ink_sides(source, blank, case):
    scale = 8
    a = pymupdf.open(stream=source, filetype='pdf')[0].get_pixmap(matrix=pymupdf.Matrix(scale, scale))
    b = pymupdf.open(stream=blank, filetype='pdf')[0].get_pixmap(matrix=pymupdf.Matrix(scale, scale))
    inside = outside = 0
    x0, x1 = (290, 310) if case == 'curved-edge' else (197, 218)
    y0, y1 = (102, 126) if case == 'curved-edge' else (88, 112)
    for y in range(y0 * scale, y1 * scale):
        for x in range(x0 * scale, x1 * scale):
            if max(abs(a.pixel(x, y)[i] - b.pixel(x, y)[i]) for i in range(3)) <= 12:
                continue
            px, py = (x + .5) / scale, 300 - (y + .5) / scale
            covered = (((px - 200) / 140) ** 2 + ((py - 150) / 50) ** 2 < 1
                       if case == 'curved-edge' else 60 < px < 340 and 100 < py < 200)
            if covered:
                inside += 1
            else:
                outside += 1
    assert inside > 20 and outside > 20, (case, inside, outside)
    return inside, outside


sources = {case: fixture(case) for case in CASES}
for case, source in sources.items():
    (root / f'{case}-source.pdf').write_bytes(source)
    pymupdf.open(stream=source, filetype='pdf')[0].get_pixmap().save(root / f'{case}-source.png')

results = []
for case, source in sources.items():
    source_spans, source_drawings = geometry(source, case)
    crossing = source_spans[-1]
    assert len(source_spans) == (2 if case in ('hidden-source-text', 'hidden-existing-crossing',
                                            'near-hidden-existing-crossing',
                                            'low-contrast-existing-crossing') else 1)
    if case in POSITIVES:
        counts = ink_sides(source, fixture(case, draw_crossing=False), case)
        # One and only one glyph; its conservative box must cross the panel.
        box = pymupdf.Rect(crossing[2])
        if case == 'straight-edge':
            assert box.y0 < 100 - .3 and box.y1 > 100 + .3, box
        else:
            assert box.x0 < 340 and box.x1 > 290, box
    if case in ('hidden-source-text', 'hidden-existing-crossing'):
        assert all(abs(a - b) < .00001 for a, b in zip(source_spans[0][4], PANEL))
    if case in ('near-hidden-existing-crossing', 'low-contrast-existing-crossing'):
        assert any(abs(a - b) > .00001 for a, b in zip(source_spans[0][4], PANEL))
        assert contrast(source_spans[0][4], PANEL) < 1.5
    if case == 'bad-source-contrast':
        assert contrast(crossing[4], (1, 1, 1)) < 1.5
    code, facts, output = invoke(source, '--convert')
    if args.probe:
        results.append({'case': case, 'code': code,
                        'diagnostic': facts if code else facts['changedPages']})
        print('PROBE', case, code, facts if code else 'converted')
        continue
    if case not in POSITIVES:
        assert code == 3 and not output, (case, code, facts)
        results.append({'case': case, 'refused': facts.strip()})
        print('PASS', case, 'refused')
        continue
    assert code == 0 and output, (case, code, facts)
    spans, drawings = geometry(output, case)
    assert [(a, b, c, d) for a, b, c, d, _ in spans] == [
        (a, b, c, d) for a, b, c, d, _ in source_spans], case
    assert [(a, b, c) for a, b, c, _, _ in drawings] == [
        (a, b, c) for a, b, c, _, _ in source_drawings], case
    panel = next(item[3] for item in drawings if item[3] and item[1] == (60., 100., 340., 200.))
    paper = pymupdf.open(stream=output, filetype='pdf')[0].get_pixmap().pixel(5, 5)
    assert max(paper) < 100 and max(panel) < .3, (case, paper, panel)
    assert contrast(spans[0][4], panel) >= 4.5 and contrast(
        spans[0][4], tuple(v / 255 for v in paper)) >= 4.5, case
    (root / f'{case}-dark.pdf').write_bytes(output)
    pymupdf.open(stream=output, filetype='pdf')[0].get_pixmap().save(root / f'{case}-dark.png')
    for mode in ('--inspect', '--convert'):
        repeat_code, repeat, duplicate = invoke(output, mode)
        assert repeat_code == 0 and not duplicate and repeat['changedPages'] == 0, (case, mode, repeat)
    results.append({'case': case, 'actualInkBothSides': counts,
                    'geometryUnchanged': True, 'contrast': True, 'repeatNoOp': True})
    print('PASS', case, 'crossing ink, geometry, contrast and repeat')
(root / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
