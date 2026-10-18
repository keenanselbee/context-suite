"""Authored ellipse fill, outline and intervening-paint contrast regressions."""

import argparse
import io
import json
from pathlib import Path
import struct
import subprocess
import tempfile

import pymupdf
import reportlab
from reportlab.pdfbase.pdfmetrics import stringWidth, registerFont
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--engine', required=True)
parser.add_argument('--scratch', required=True)
parser.add_argument('--probe', action='store_true', help='Record current engine behavior without assertions')
args = parser.parse_args()
scratch = Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(Path(__file__).resolve().parents[2] / '.codex-temp'):
    raise ValueError('Use a fresh repository-local scratch directory')
scratch.mkdir(parents=True)
registerFont(TTFont('EllipseFixture', str(Path(reportlab.__file__).parent / 'fonts' / 'Vera.ttf')))


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=scratch) as source:
        source.write(data)
        source.seek(0)
        result = subprocess.run([args.engine, mode], stdin=source, capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors='replace'), b''
    magic, version, facts_size, pdf_size = struct.unpack_from('<IIII', result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + facts_size + pdf_size
    return 0, json.loads(result.stdout[16:16 + facts_size]), result.stdout[16 + facts_size:]


def ellipse(c):
    """Four monotone cardinal cubics, with no rectangle in the content stream."""
    k = .5522847498307936
    rx, ry, cx, cy = 140, 50, 200, 150
    path = c.beginPath()
    path.moveTo(cx - rx, cy)
    path.curveTo(cx - rx, cy + ry * k, cx - rx * k, cy + ry, cx, cy + ry)
    path.curveTo(cx + rx * k, cy + ry, cx + rx, cy + ry * k, cx + rx, cy)
    path.curveTo(cx + rx, cy - ry * k, cx + rx * k, cy - ry, cx, cy - ry)
    path.curveTo(cx - rx * k, cy - ry, cx - rx, cy - ry * k, cx - rx, cy)
    path.close()
    return path


def fake_ellipse(c, case):
    path = c.beginPath()
    path.moveTo(60, 150)
    if case == 'concave':
        path.curveTo(60, 178, 123, 200, 200, 200)
        path.curveTo(322, 200, 218, 100, 340, 150)  # Reversing control walk.
        path.curveTo(340, 122, 277, 100, 200, 100)
        path.curveTo(123, 100, 60, 122, 60, 150)
    else:
        # Two same-direction two-cubic laps cancel under even-odd filling.
        for _ in range(2):
            path.curveTo(60, 217, 340, 217, 340, 150)
            path.curveTo(340, 83, 60, 83, 60, 150)
    path.close()
    return path


def fixture(case, *, draw_text=True, draw_extra=True, text_color=(1, 1, 1)):
    stream = io.BytesIO()
    c = Canvas(stream, pagesize=(400, 300), pageCompression=0)
    c.setFillColorRGB(1, 1, 1)
    c.rect(0, 0, 400, 300, fill=1, stroke=0)
    c.setFillColorRGB(.16, .15, .2)
    if case in ('concave', 'double-winding'):
        c.drawPath(fake_ellipse(c, case), fill=1, stroke=0, fillMode=0)
    else:
        c.drawPath(ellipse(c), fill=1, stroke=0)
    if draw_extra and case in ('ellipse-outline', 'corner', 'thick-stroke', 'connector'):
        c.setStrokeColorRGB(*((.3, .28, .35) if case == 'thick-stroke' else (.7, .65, .56)))
        c.setLineWidth(24 if case == 'thick-stroke' else 1)
        c.drawPath(ellipse(c), fill=0, stroke=1)
    if draw_extra and case == 'connector':
        c.setStrokeColorRGB(.85, .25, .2)
        c.setLineWidth(1.5)
        c.line(160, 184, 240, 184)
    if draw_text:
        c.setFont('EllipseFixture', 14)
        c.setFillColorRGB(*text_color)
        if case == 'corner':
            c.drawString(90, 181, 'X')  # Inside ellipse AABB, straddling the curved edge.
        else:
            for label, baseline in (('Withdraw', 181), ('money', 112)):
                c.drawString(200 - stringWidth(label, 'EllipseFixture', 14) / 2, baseline, label)
    c.save()
    return stream.getvalue()


def ink_overlap(case):
    """Count actual raster pixels painted by both the added stroke and glyphs."""
    def pixels(draw_text, draw_extra):
        pdf = pymupdf.open(stream=fixture(case, draw_text=draw_text, draw_extra=draw_extra))
        pix = pdf[0].get_pixmap(matrix=pymupdf.Matrix(4, 4), alpha=False)
        return memoryview(pix.samples), pix.width, pix.height

    base, width, height = pixels(False, False)
    text, _, _ = pixels(True, False)
    extra, _, _ = pixels(False, True)
    crossings = 0
    for y in range(4 * (300 - 200), 4 * (300 - 170)):
        for x in range(4 * 160, 4 * 240):
            index = 3 * (y * width + x)
            if (max(abs(text[index + channel] - base[index + channel]) for channel in range(3)) > 10 and
                max(abs(extra[index + channel] - base[index + channel]) for channel in range(3)) > 10):
                crossings += 1
    return crossings


def glyph_ellipse_coverage(case):
    """Measure glyph ink against the ellipse itself, independent of text boxes."""
    def pixels(draw_text):
        # Black mask ink exposes the white-on-white portion of the corner glyph.
        color = (0, 0, 0) if case == 'corner' else (1, 1, 1)
        pdf = pymupdf.open(stream=fixture(case, draw_text=draw_text, draw_extra=False,
                                         text_color=color))
        return memoryview(pdf[0].get_pixmap(matrix=pymupdf.Matrix(4, 4), alpha=False).samples)

    base, text = pixels(False), pixels(True)
    ink = outside = 0
    for y in range(4 * 300):
        for x in range(4 * 400):
            index = 3 * (y * 4 * 400 + x)
            if max(abs(text[index + channel] - base[index + channel]) for channel in range(3)) <= 10:
                continue
            ink += 1
            norm = ((x / 4 - 200) / 140) ** 2 + (((300 - y / 4) - 150) / 50) ** 2
            outside += norm >= 1
    assert ink > 0, case
    return {'glyphInkPixels': ink, 'outsideEllipsePixels': outside}


def luminance(rgb):
    linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in rgb]
    return sum(v * weight for v, weight in zip(linear, (.2126, .7152, .0722)))


positive = {'ellipse-fill', 'ellipse-outline'}
cases = ('ellipse-fill', 'ellipse-outline', 'corner', 'thick-stroke', 'concave',
         'double-winding', 'connector')
# Save every independently authored fixture before invoking the engine.
sources = {case: fixture(case) for case in cases}
for case, source in sources.items():
    (scratch / f'{case}-source.pdf').write_bytes(source)
coverage = {case: glyph_ellipse_coverage(case) for case in ('ellipse-fill', 'corner')}
assert coverage['ellipse-fill']['outsideEllipsePixels'] == 0, coverage
assert 0 < coverage['corner']['outsideEllipsePixels'] < coverage['corner']['glyphInkPixels'], coverage
crossings = {case: ink_overlap(case) for case in ('ellipse-outline', 'thick-stroke', 'connector')}
assert crossings['ellipse-outline'] == 0, crossings
assert crossings['thick-stroke'] > 0 and crossings['connector'] > 0, crossings
(scratch / 'source-geometry-proof.json').write_text(
    json.dumps({'glyphCoverage': coverage, 'strokeGlyphCrossings': crossings}, indent=2), encoding='utf-8')

results = []
for case, source in sources.items():
    code, facts, output = invoke(source, '--convert')
    if args.probe:
        results.append({'case': case, 'code': code, 'facts': facts if code else facts['changedPages']})
        print('PROBE', case, code, facts if code else 'converted')
        continue
    if case not in positive:
        assert code == 3 and not output, (case, code, facts)
        results.append({'case': case, 'refused': facts.strip()})
        print('PASS', case, 'refused')
        continue
    assert code == 0 and output, (case, code, facts)
    before = pymupdf.open(stream=source, filetype='pdf')
    after = pymupdf.open(stream=output, filetype='pdf')
    text_geometry = lambda p: [(s['font'], s['size'], s['chars']) for s in p.get_texttrace()]
    assert text_geometry(before[0]) == text_geometry(after[0]), case
    drawing_geometry = lambda p: [(d['type'], d['items'], d['width']) for d in p.get_drawings()]
    assert drawing_geometry(before[0]) == drawing_geometry(after[0]), case
    pix = after[0].get_pixmap(alpha=False)
    panel = pix.pixel(200, 150)
    assert max(panel) < 100, (case, panel)
    for span in after[0].get_texttrace():
        assert (luminance(span['color']) + .05) / (luminance([v / 255 for v in panel]) + .05) >= 4.5, case
    (scratch / f'{case}-dark.pdf').write_bytes(output)
    pix.save(scratch / f'{case}-dark.png')
    for mode in ('--inspect', '--convert'):
        repeat_code, repeated, duplicate = invoke(output, mode)
        assert repeat_code == 0 and not duplicate and repeated['changedPages'] == 0, (case, mode, repeated)
    results.append({'case': case, 'passed': True, 'repeatNoOp': True})
    print('PASS', case, 'geometry, contrast and repeat')
(scratch / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
