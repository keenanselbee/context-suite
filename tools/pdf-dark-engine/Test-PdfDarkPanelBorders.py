"""Authored near-tangent glyph crossings of a different-color panel outline."""

import argparse
import io
import json
from pathlib import Path
import struct
import subprocess
import tempfile

import numpy as np
import pymupdf
from PIL import Image
import reportlab
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase.pdfmetrics import registerFont
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--engine', required=True)
parser.add_argument('--scratch', required=True)
parser.add_argument('--probe', action='store_true', help='Record baseline refusals')
args = parser.parse_args()
root = Path(args.scratch).resolve()
if root.exists() or not root.is_relative_to(Path(__file__).resolve().parents[2] / '.codex-temp'):
    raise ValueError('Use a fresh repository-local scratch directory')
root.mkdir(parents=True)
engine = Path(args.engine).resolve(strict=True)
registerFont(TTFont('PanelBorderFixture', str(Path(reportlab.__file__).parent / 'fonts' / 'Vera.ttf')))

PAPER = (.11, .106, .133)
FILL = (.165, .153, .20)
SLATE = (.239, .224, .282)
RED = (.91, .337, .294)
BASELINE = 199.8
CASES = ('rounded', 'rectangle', 'mismatched', 'thick', 'translucent',
         'source-hidden', 'intervening-paint', 'intervening-image',
         'intervening-form', 'unknown-clip', 'second-conflicting-text')
POSITIVES = {'rounded', 'rectangle'}


def panel(canvas, case, *, fill, stroke):
    if case == 'rectangle':
        canvas.rect(60, 100, 280, 100, fill=fill, stroke=stroke)
    else:
        canvas.roundRect(60, 100, 280, 100, 25, fill=fill, stroke=stroke)


def fixture(case, draw_text=True):
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(400, 300), pageCompression=0)
    canvas.setFillColorRGB(*PAPER)
    canvas.rect(0, 0, 400, 300, fill=1, stroke=0)
    if case == 'unknown-clip':
        clip = canvas.beginPath()
        clip.moveTo(60, 100)
        clip.lineTo(340, 100)
        clip.lineTo(340, 200)
        clip.close()
        canvas.clipPath(clip, fill=0, stroke=0)
    canvas.setFillColorRGB(*FILL)
    panel(canvas, case, fill=1, stroke=0)
    canvas.setStrokeColorRGB(*SLATE)
    canvas.setLineWidth(4 if case == 'thick' else 1)
    if case == 'translucent':
        canvas.setStrokeAlpha(.65)
    if case == 'mismatched':
        canvas.roundRect(60, 99.8, 280, 100, 25, fill=0, stroke=1)
    else:
        panel(canvas, case, fill=0, stroke=1)
    if case == 'intervening-paint':
        canvas.setFillColorRGB(.7, .65, .55)
        canvas.rect(197, 197, 22, 7, fill=1, stroke=0)
    if case == 'intervening-image':
        canvas.drawImage(ImageReader(Image.new('RGB', (20, 7), (180, 150, 130))),
                         197, 197, width=22, height=7)
    if case == 'intervening-form':
        canvas.beginForm('Interfere', 0, 0, 22, 7)
        canvas.setFillColorRGB(.7, .65, .55)
        canvas.rect(0, 0, 22, 7, fill=1, stroke=0)
        canvas.endForm()
        canvas.saveState()
        canvas.translate(197, 197)
        canvas.doForm('Interfere')
        canvas.restoreState()
    canvas.setFont('PanelBorderFixture', 12.775)
    canvas.setFillColorRGB(*(SLATE if case == 'source-hidden' else RED))
    if draw_text:
        canvas.drawString(200, BASELINE, 'X')
        if case == 'second-conflicting-text':
            canvas.setFillColorRGB(.32, .30, .35)
            canvas.drawString(240, BASELINE, 'X')
    canvas.save()
    return stream.getvalue()


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


def luminance(color):
    linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in color]
    return sum(v * weight for v, weight in zip(linear, (.2126, .7152, .0722)))


def contrast(a, b):
    x, y = sorted((luminance(a), luminance(b)))
    return (y + .05) / (x + .05)


def geometry(data):
    doc = pymupdf.open(stream=data, filetype='pdf')
    spans = [(s['font'], s['size'], tuple(s['bbox']), s['chars'], s['color'])
             for s in doc[0].get_texttrace()]
    drawings = [(d['type'], tuple(d['rect']), d['items'], d['fill'], d['color'],
                 d['width'], d['stroke_opacity']) for d in doc[0].get_drawings()]
    assert spans and all('BitstreamVeraSans' in span[0] for span in spans), spans
    assert all(len(span[3]) == 1 and span[3][0][0] == ord('X') for span in spans), spans
    return spans, drawings


def crossing_ink(source, blank):
    clip = pymupdf.Rect(197, 88, 217, 113)
    matrix = pymupdf.Matrix(16, 16)
    a = pymupdf.open(stream=source, filetype='pdf')[0].get_pixmap(matrix=matrix, clip=clip)
    b = pymupdf.open(stream=blank, filetype='pdf')[0].get_pixmap(matrix=matrix, clip=clip)
    foreground = np.frombuffer(a.samples, dtype=np.uint8).reshape(a.height, a.width, 3).astype(np.int16)
    background = np.frombuffer(b.samples, dtype=np.uint8).reshape(b.height, b.width, 3).astype(np.int16)
    rows, cols = np.nonzero(np.max(np.abs(foreground - background), axis=2) > 20)
    ys = 300 - (rows + 88 * 16 + .5) / 16
    inside, outside = ys[ys < 200], ys[ys >= 200]
    assert len(inside) > 20 and len(outside) > 20, (len(inside), len(outside))
    penetration = 200 - inside.min()
    assert 0 < penetration <= .18, penetration
    return {'insidePixels16x': len(inside), 'outsidePixels16x': len(outside),
            'deepestInsidePt': penetration}


sources = {case: fixture(case) for case in CASES}
for case, source in sources.items():
    (root / f'{case}-source.pdf').write_bytes(source)
    pymupdf.open(stream=source, filetype='pdf')[0].get_pixmap().save(root / f'{case}-source.png')

results = []
for case, source in sources.items():
    original_spans, original_drawings = geometry(source)
    outline = [d for d in original_drawings if 's' in d[0] and d[4] is not None]
    assert len(outline) == 1, (case, outline)
    assert all(abs(a - b) < .0001 for a, b in zip(outline[0][4], SLATE)), case
    if case in POSITIVES:
        ink = crossing_ink(source, fixture(case, draw_text=False))
        assert 1.5 <= contrast(original_spans[0][4], SLATE) < 4.5
    if case == 'mismatched':
        assert outline[0][1] != (60., 100., 340., 200.)
    if case == 'thick':
        assert outline[0][5] > 2
    if case == 'translucent':
        assert outline[0][6] < 1
    if case == 'source-hidden':
        assert contrast(original_spans[0][4], SLATE) < 1.5
    if case == 'second-conflicting-text':
        assert len(original_spans) == 2 and contrast(original_spans[1][4], SLATE) < 1.5
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
    spans, drawings = geometry(output)
    assert [(a, b, c, d) for a, b, c, d, _ in spans] == [
        (a, b, c, d) for a, b, c, d, _ in original_spans], case
    assert [(a, b, c, f, g) for a, b, c, _, _, f, g in drawings] == [
        (a, b, c, f, g) for a, b, c, _, _, f, g in original_drawings], case
    final_outline = [d for d in drawings if 's' in d[0] and d[4] is not None]
    assert len(final_outline) == 1 and all(abs(a - b) < .0001 for a, b in zip(final_outline[0][4], SLATE))
    panel_color = next(d[3] for d in drawings if d[3] is not None and d[1] == (60., 100., 340., 200.))
    assert max(panel_color) < .3, panel_color
    pixel = pymupdf.open(stream=output, filetype='pdf')[0].get_pixmap().pixel(5, 5)
    paper = tuple(v / 255 for v in pixel)
    assert max(pixel) < 100, pixel
    assert contrast(SLATE, panel_color) + .0001 >= min(3, contrast(SLATE, FILL)), case
    assert contrast(SLATE, paper) + .0001 >= min(3, contrast(SLATE, PAPER)), case
    assert contrast(spans[0][4], SLATE) + .0001 >= min(4.5, contrast(original_spans[0][4], SLATE)), case
    assert contrast(spans[0][4], panel_color) >= 4.5 and contrast(spans[0][4], paper) >= 4.5, case
    (root / f'{case}-dark.pdf').write_bytes(output)
    pymupdf.open(stream=output, filetype='pdf')[0].get_pixmap().save(root / f'{case}-dark.png')
    for mode in ('--inspect', '--convert'):
        repeat_code, repeat, duplicate = invoke(output, mode)
        assert repeat_code == 0 and not duplicate and repeat['changedPages'] == 0, (case, mode, repeat)
    results.append({'case': case, 'actualInkCrossing': ink, 'originalSlateStroke': True,
                    'geometryUnchanged': True, 'contrastProof': True, 'repeatNoOp': True})
    print('PASS', case, 'thin slate outline, crossing ink, contrast, geometry and repeat')
(root / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
