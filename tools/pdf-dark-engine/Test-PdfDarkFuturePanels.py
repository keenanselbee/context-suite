"""Authored glyph occlusion by a later opaque labeled panel."""

import argparse
import io
import json
from pathlib import Path
import re
import struct
import subprocess
import tempfile

import numpy as np
import pymupdf
from PIL import Image
import reportlab
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
registerFont(TTFont('FuturePanelFixture', str(Path(reportlab.__file__).parent / 'fonts' / 'Vera.ttf')))

PAPER = (.11, .106, .133)
FILL = (.165, .153, .20)
PALETTE_PANEL = (.105, .110, .120)
SLATE = (.239, .224, .282)
RED = (.91, .337, .294)
LIGHT = (.941, .929, .91)
PAGE_HEIGHT = 350
FUTURE_TOP = 210
PRIOR_BOTTOM = 220
CASES = ('labeled-rounded', 'labeled-rectangle', 'paper-only-labeled',
         'paper-prior-outline-labeled', 'paper-prior-thick-outline',
         'prior-palette-near-hidden', 'prior-palette-readable',
         'no-later-label',
         'future-translucent', 'future-unknown-clip', 'future-unsupported-shape',
         'future-thick-outline', 'future-mismatched-outline', 'source-hidden',
         'future-hidden-text', 'earlier-intervening-paint',
         'future-hidden-without-prior', 'future-near-palette-without-prior',
         'future-paper-hidden-without-prior', 'future-prior-stripe-hidden')
POSITIVES = {'labeled-rounded', 'labeled-rectangle', 'paper-only-labeled',
             'paper-prior-outline-labeled', 'prior-palette-readable'}


def panel(c, y, case, *, fill, stroke):
    height = 74.7 if y == 225.3 else 80 if y == 220 else 90
    if case == 'labeled-rectangle':
        c.rect(60, y, 280, height, fill=fill, stroke=stroke)
    else:
        c.roundRect(60, y, 280, height, 10, fill=fill, stroke=stroke)


def fixture(case, *, arrow=True, future=True):
    buffer = io.BytesIO()
    c = Canvas(buffer, pagesize=(400, PAGE_HEIGHT), pageCompression=0)
    c.setFillColorRGB(*PAPER)
    c.rect(0, 0, 400, PAGE_HEIGHT, fill=1, stroke=0)
    if case not in ('future-hidden-without-prior', 'future-near-palette-without-prior',
                    'future-paper-hidden-without-prior', 'future-prior-stripe-hidden',
                    'paper-only-labeled'):
        prior_y = 225.3 if case in ('paper-prior-outline-labeled',
                                    'paper-prior-thick-outline') else 220
        c.setFillColorRGB(*(PALETTE_PANEL if case.startswith('prior-palette-') else FILL))
        panel(c, prior_y, case, fill=1, stroke=0)
        c.setStrokeColorRGB(*SLATE)
        c.setLineWidth(4 if case == 'paper-prior-thick-outline' else 1)
        panel(c, prior_y, case, fill=0, stroke=1)
        c.setFont('FuturePanelFixture', 15)
        c.setFillColorRGB(*((.108, .113, .123) if case == 'prior-palette-near-hidden' else LIGHT))
        c.drawString(85, 255, 'SOURCE')
        if case.startswith('prior-palette-'):
            c.save()
            return buffer.getvalue()
    if case == 'earlier-intervening-paint':
        c.setFillColorRGB(.72, .65, .58)
        c.rect(190, 206, 25, 22, fill=1, stroke=0)
    if case == 'future-prior-stripe-hidden':
        c.setFillColorRGB(.55, .55, .55)
        c.rect(190, 208, 30, 25, fill=1, stroke=0)
    c.setFont('FuturePanelFixture', 20)
    arrow_color = ((.108, .113, .123) if case == 'future-near-palette-without-prior'
                   else (.112, .108, .135) if case == 'future-paper-hidden-without-prior'
                   else (.55, .55, .55) if case == 'future-prior-stripe-hidden'
                   else FILL if case in ('source-hidden', 'future-hidden-without-prior') else RED)
    c.setFillColorRGB(*arrow_color)
    if arrow:
        c.drawString(200, 214, 'g')
    if future:
        if case in ('future-translucent', 'future-unknown-clip'):
            c.saveState()
        if case == 'future-translucent':
            c.setFillAlpha(.6)
        if case == 'future-unknown-clip':
            path = c.beginPath()
            path.moveTo(60, 120)
            path.lineTo(340, 120)
            path.lineTo(340, 210)
            path.lineTo(180, 210)
            path.lineTo(60, 200)
            path.close()
            c.clipPath(path, fill=0, stroke=0)
        future_color = (PALETTE_PANEL if case == 'future-near-palette-without-prior'
                        else (.30, .30, .34) if case in ('future-paper-hidden-without-prior',
                                                        'future-prior-stripe-hidden')
                        else FILL)
        c.setFillColorRGB(*future_color)
        if case == 'future-unsupported-shape':
            path = c.beginPath()
            path.moveTo(60, 120)
            path.lineTo(340, 120)
            path.lineTo(340, 210)
            path.lineTo(60, 208)
            path.close()
            c.drawPath(path, fill=1, stroke=0)
        else:
            panel(c, 120, case, fill=1, stroke=0)
        if case not in ('future-hidden-without-prior', 'future-near-palette-without-prior',
                        'future-paper-hidden-without-prior', 'future-prior-stripe-hidden'):
            c.setStrokeColorRGB(*SLATE)
            c.setLineWidth(4 if case == 'future-thick-outline' else 1)
            if case == 'future-mismatched-outline':
                c.roundRect(60, 119.7, 280, 90, 10, fill=0, stroke=1)
            elif case == 'future-unsupported-shape':
                c.drawPath(path, fill=0, stroke=1)
            else:
                panel(c, 120, case, fill=0, stroke=1)
        if case in ('future-translucent', 'future-unknown-clip'):
            c.restoreState()
        if case != 'no-later-label':
            c.setFont('FuturePanelFixture', 15)
            c.setFillColorRGB(*(FILL if case == 'future-hidden-text' else LIGHT))
            c.drawString(85, 155, 'TARGET')
    c.save()
    return buffer.getvalue()


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
    page = pymupdf.open(stream=data, filetype='pdf')[0]
    text = [(s['font'], s['size'], tuple(s['bbox']), s['chars'], s['color'])
            for s in page.get_texttrace()]
    drawings = [(d['type'], tuple(d['rect']), d['items'], d['fill'], d['color'],
                 d['width'], d['fill_opacity'], d['stroke_opacity']) for d in page.get_drawings()]
    assert all('BitstreamVeraSans' in span[0] for span in text)
    return text, drawings


def raster(data):
    clip = pymupdf.Rect(197, 116, 217, 148)
    page = pymupdf.open(stream=data, filetype='pdf')[0]
    pix = page.get_pixmap(matrix=pymupdf.Matrix(16, 16), clip=clip, alpha=False)
    return np.frombuffer(pix.samples, np.uint8).reshape(pix.height, pix.width, 3).astype(np.int16)


def visible_ink(case, source):
    pre = fixture(case, future=False)
    pre_blank = fixture(case, future=False, arrow=False)
    blank = fixture(case, arrow=False)
    prior_ink = np.max(abs(raster(pre) - raster(pre_blank)), axis=2) > 20
    visible = np.max(abs(raster(source) - raster(blank)), axis=2) > 20
    y = PAGE_HEIGHT - (np.arange(prior_ink.shape[0]) + 116 * 16 + .5) / 16
    prior_card = y >= PRIOR_BOTTOM
    gap = (y > FUTURE_TOP) & (y < PRIOR_BOTTOM)
    later_card = y <= FUTURE_TOP
    counts = {'priorCardInk': int(prior_ink[prior_card].sum()),
              'visibleGapInk': int(visible[gap].sum()),
              'preOverlayLaterCardInk': int(prior_ink[later_card].sum()),
              'visibleLaterCardInk': int(visible[later_card].sum())}
    assert counts['priorCardInk'] > 50 and counts['visibleGapInk'] > 50, counts
    assert counts['preOverlayLaterCardInk'] > 50 and counts['visibleLaterCardInk'] == 0, counts
    return counts


def prior_outline_without_fill_crossing(data):
    doc = pymupdf.open(stream=data, filetype='pdf')
    page = doc[0]
    glyph = next(t for t in page.get_texttrace() if len(t['chars']) == 1 and
                 t['chars'][0][0] == ord('g'))
    font_xref = next(item[0] for item in page.get_fonts(full=True)
                     if 'BitstreamVeraSans' in item[3])
    font_data = doc.extract_font(font_xref)[3]
    tables = {}
    for i in range(struct.unpack_from('>H', font_data, 4)[0]):
        tag, _, offset, length = struct.unpack_from('>4sIII', font_data, 12 + 16 * i)
        tables[tag.decode('ascii')] = (offset, length)
    head = tables['head'][0]
    units = struct.unpack_from('>H', font_data, head + 18)[0]
    index_format = struct.unpack_from('>h', font_data, head + 50)[0]
    loca, glyf = tables['loca'][0], tables['glyf'][0]
    offset = (lambda i: struct.unpack_from('>I', font_data, loca + 4 * i)[0]
              if index_format else 2 * struct.unpack_from('>H', font_data, loca + 2 * i)[0])
    gid = glyph['chars'][0][1]
    y_max = struct.unpack_from('>h', font_data, glyf + offset(gid) + 8)[0]
    top = glyph['chars'][0][2][1] - y_max / units * glyph['size']
    prior_bottom = PAGE_HEIGHT - 225.3
    assert prior_bottom < top < prior_bottom + .5, (prior_bottom, top)
    return {'glyphTop': top, 'priorFillBottom': prior_bottom,
            'outlineHalfWidth': .5}


def final_visible_ink(data):
    doc = pymupdf.open(stream=data, filetype='pdf')
    for xref in doc[0].get_contents():
        stream = doc.xref_stream(xref)
        blocks = list(re.finditer(rb'BT.*?ET', stream, re.S))
        matches = [m for m in blocks if b'(g) Tj' in m.group(0) or b'(g) TJ' in m.group(0)]
        if not matches:
            continue
        assert len(matches) == 1
        match = matches[0]
        doc.update_stream(xref, stream[:match.start()] + b'BT ET' + stream[match.end():])
        blank = doc.tobytes(garbage=0, deflate=False)
        visible = np.max(abs(raster(data) - raster(blank)), axis=2) > 20
        y = PAGE_HEIGHT - (np.arange(visible.shape[0]) + 116 * 16 + .5) / 16
        counts = {'visibleGapInk': int(visible[(y > FUTURE_TOP) & (y < PRIOR_BOTTOM)].sum()),
                  'visibleLaterCardInk': int(visible[y <= FUTURE_TOP].sum())}
        assert counts['visibleGapInk'] > 50 and counts['visibleLaterCardInk'] == 0, counts
        return counts
    raise AssertionError('Converted arrow text operator not found')


sources = {case: fixture(case) for case in CASES}
for case, source in sources.items():
    (root / f'{case}-source.pdf').write_bytes(source)
    pymupdf.open(stream=source, filetype='pdf')[0].get_pixmap().save(root / f'{case}-source.png')

results = []
for case, source in sources.items():
    original_text, original_drawings = geometry(source)
    assert len(original_text) == (1 if case.startswith('prior-palette-') else
                                  2 if case in ('no-later-label', 'future-hidden-without-prior',
                                               'future-near-palette-without-prior',
                                               'future-paper-hidden-without-prior',
                                               'future-prior-stripe-hidden', 'paper-only-labeled') else 3), case
    ink = visible_ink(case, source) if case in POSITIVES and not case.startswith('prior-palette-') else None
    if case == 'paper-prior-outline-labeled':
        ink['priorOutlineOnly'] = prior_outline_without_fill_crossing(source)
    code, facts, output = invoke(source, '--convert')
    if args.probe:
        results.append({'case': case, 'code': code,
                        'diagnostic': facts if code else facts['changedPages'],
                        'sourceInk': ink})
        print('PROBE', case, code, facts if code else 'converted')
        continue
    if case not in POSITIVES:
        assert code == 3 and not output, (case, code, facts)
        results.append({'case': case, 'refused': facts.strip()})
        print('PASS', case, 'refused')
        continue
    assert code == 0 and output, (case, code, facts)
    text, drawings = geometry(output)
    assert [(a, b, c, d) for a, b, c, d, _ in text] == [
        (a, b, c, d) for a, b, c, d, _ in original_text], case
    assert [(a, b, c, f, g, h) for a, b, c, _, _, f, g, h in drawings] == [
        (a, b, c, f, g, h) for a, b, c, _, _, f, g, h in original_drawings], case
    if case == 'prior-palette-readable':
        assert contrast(original_text[0][4], PALETTE_PANEL) >= 1.5, case
        assert contrast(text[0][4], PALETTE_PANEL) >= min(4.5, contrast(original_text[0][4], PALETTE_PANEL)), case
        (root / f'{case}-dark.pdf').write_bytes(output)
        for mode in ('--inspect', '--convert'):
            repeat_code, repeat, duplicate = invoke(output, mode)
            assert repeat_code == 0 and not duplicate and repeat['changedPages'] == 0, (case, mode, repeat)
        results.append({'case': case, 'geometryUnchanged': True, 'sourceContrast':
                        contrast(original_text[0][4], PALETTE_PANEL), 'repeatNoOp': True})
        print('PASS', case, 'visible source text and repeat')
        continue
    panels = [d for d in drawings if d[3] is not None and d[1][0] == 60 and d[1][2] == 340]
    assert len(panels) == (1 if case == 'paper-only-labeled' else 2)
    assert all(max(p[3]) < .3 for p in panels), (case, panels)
    later = next(p for p in panels if p[1][3] == 230.)
    assert later[4] is not None and all(abs(a - b) < .0001 for a, b in zip(later[4], SLATE)), case
    if case == 'paper-prior-outline-labeled':
        earlier = next(p for p in panels if abs(p[1][3] - 124.7) < .001)
        assert earlier[4] is not None and all(abs(a - b) < .0001 for a, b in zip(earlier[4], SLATE)), case
    arrow = next(t for t in text if len(t[3]) == 1 and t[3][0][0] == ord('g'))
    original_arrow = next(t for t in original_text if len(t[3]) == 1 and t[3][0][0] == ord('g'))
    source_future = next(p for p in original_drawings if p[3] is not None and p[1][3] == 230.)
    assert contrast(original_arrow[4], source_future[3]) >= 1.5, case
    assert contrast(arrow[4], later[3]) + .0001 >= min(4.5, contrast(original_arrow[4], source_future[3])), case
    assert contrast(arrow[4], later[3]) >= 4.5, case
    assert contrast(arrow[4], later[4]) + .0001 >= min(4.5, contrast(original_arrow[4], SLATE)), case
    if case == 'paper-only-labeled':
        page = pymupdf.open(stream=output, filetype='pdf')[0]
        pixel = page.get_pixmap().pixel(5, 5)
        final_paper = tuple(v / 255 for v in pixel)
        assert contrast(original_arrow[4], PAPER) >= 1.5, case
        assert contrast(arrow[4], final_paper) >= 4.5, case
    actual = final_visible_ink(output)
    assert actual['visibleLaterCardInk'] == 0 and actual['visibleGapInk'] > 50, case
    (root / f'{case}-dark.pdf').write_bytes(output)
    pymupdf.open(stream=output, filetype='pdf')[0].get_pixmap().save(root / f'{case}-dark.png')
    for mode in ('--inspect', '--convert'):
        repeat_code, repeat, duplicate = invoke(output, mode)
        assert repeat_code == 0 and not duplicate and repeat['changedPages'] == 0, (case, mode, repeat)
    results.append({'case': case, 'sourceInk': ink, 'finalInk': actual,
                    'originalFutureOutline': True, 'geometryUnchanged': True,
                    'contrastProof': True, 'repeatNoOp': True})
    print('PASS', case, 'future occlusion, stroke, geometry, contrast and repeat')
(root / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
