"""Authored masked-photo title panels with bounded overlay relationships."""

import argparse
import io
import json
from pathlib import Path
import re
import struct
import subprocess
import tempfile

import pymupdf
from PIL import Image
import reportlab
from reportlab.pdfbase.pdfmetrics import registerFont
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (DecodedStreamObject, DictionaryObject, FloatObject,
                           NameObject, NumberObject)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--engine', required=True)
parser.add_argument('--scratch', required=True)
parser.add_argument('--probe', action='store_true', help='Record baseline refusals before overlay support')
args = parser.parse_args()
root = Path(args.scratch).resolve()
if root.exists() or not root.is_relative_to(Path(__file__).resolve().parents[2] / '.codex-temp'):
    raise ValueError('Use a fresh repository-local scratch directory')
root.mkdir(parents=True)
engine = Path(args.engine).resolve(strict=True)
registerFont(TTFont('OverlayFixture', str(Path(reportlab.__file__).parent / 'fonts' / 'Vera.ttf')))


def stream(writer, data, entries):
    result = DecodedStreamObject()
    result.set_data(data)
    result.update({NameObject(key): value for key, value in entries.items()})
    return writer._add_object(result)


def fixture(case):
    # Embed outlines so native containment measures actual glyphs rather than
    # Standard 14 conservative fallback bounds beyond the title's advance.
    page_bytes = io.BytesIO()
    canvas = Canvas(page_bytes, pagesize=(220, 130), pageCompression=0)
    canvas._code.append('q 1 1 1 rg 0 0 220 130 re f Q')
    if case == 'prior-conflict':
        canvas._code.append('q 1 0 0 rg 34 42 20 14 re f Q')
    if case == 'unknown-clip':
        canvas._code.append('q 25 25 m 125 25 l 125 105 l h W n')
    canvas._code.append('q 100 0 0 80 25 25 cm /Photo Do Q')
    if case == 'unknown-clip':
        canvas._code.append('Q')
    panel = (90, 70, 95, 23) if case == 'edge-panel' else (30, 70, 90, 23)
    if case == 'translucent-panel':
        canvas._code.append('q /Partial gs 1 1 1 rg 30 70 90 23 re f Q')
    else:
        canvas._code.append(f'q 1 1 1 rg {panel[0]} {panel[1]} {panel[2]} {panel[3]} re f Q')
    text_x = 100 if case == 'uncovered-text' else 121 if case == 'edge-panel' else 59
    canvas.setFont('OverlayFixture', 12)
    canvas.setFillColorRGB(.23, .15, .09)
    canvas.drawString(text_x, 76, 'TITLE')
    if case == 'shared-invalid':
        canvas._code.append('q 65 0 0 80 135 25 cm /Photo Do Q')
        canvas.drawString(140, 76, 'TITLE')
    canvas.save()
    writer = PdfWriter()
    writer.append(PdfReader(io.BytesIO(page_bytes.getvalue())))
    page = writer.pages[0]
    art = Image.new('RGB', (80, 60))
    art.putdata([(50 + 2 * x, 70 + 2 * y, 120 + (x + 2 * y) % 70)
                 for y in range(60) for x in range(80)])
    encoded = io.BytesIO()
    art.save(encoded, format='JPEG', quality=89)
    geometry = {'/Type': NameObject('/XObject'), '/Subtype': NameObject('/Image'),
                '/Width': NumberObject(80), '/Height': NumberObject(60),
                '/BitsPerComponent': NumberObject(8)}
    mask = stream(writer, bytes([178]) * (80 * 60),
                  {**geometry, '/ColorSpace': NameObject('/DeviceGray')})
    image = stream(writer, encoded.getvalue(), {**geometry,
        '/ColorSpace': NameObject('/DeviceRGB'), '/Filter': NameObject('/DCTDecode'),
        '/SMask': mask})
    resources = page['/Resources'].get_object()
    resources[NameObject('/XObject')] = DictionaryObject({NameObject('/Photo'): image})
    if case == 'translucent-panel':
        transparent = writer._add_object(DictionaryObject({NameObject('/Type'): NameObject('/ExtGState'),
            NameObject('/BM'): NameObject('/Normal'), NameObject('/ca'): FloatObject(.65)}))
        states = resources.get('/ExtGState')
        if states is None:
            states = DictionaryObject()
            resources[NameObject('/ExtGState')] = states
        states[NameObject('/Partial')] = transparent
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue(), panel


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=root) as source:
        source.write(data)
        source.seek(0)
        process = subprocess.run([engine, mode], stdin=source, capture_output=True, timeout=45)
    if process.returncode:
        assert not process.stdout
        return process.returncode, process.stderr.decode(errors='replace'), b''
    magic, version, facts_size, pdf_size = struct.unpack_from('<IIII', process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + facts_size + pdf_size
    return 0, json.loads(process.stdout[16:16 + facts_size]), process.stdout[16 + facts_size:]


def image_samples(data):
    photo = PdfReader(io.BytesIO(data)).pages[0]['/Resources']['/XObject']['/Photo'].get_object()
    mask = photo['/SMask'].get_object()
    assert photo['/Filter'] == '/DCTDecode' and photo['/Width'] == 80 and photo['/Height'] == 60
    return photo._data, mask.get_data()


def luminance(rgb):
    linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in rgb]
    return sum(value * weight for value, weight in zip(linear, (.2126, .7152, .0722)))


def uncovered_glyph_pixels(data, clip):
    """Prove the partial title paints real ink on photo beyond its panel."""
    doc = pymupdf.open(stream=data, filetype='pdf')
    xref = doc[0].get_contents()[0]
    content = doc.xref_stream(xref)
    matrix = pymupdf.Matrix(8, 8)
    with_text = doc[0].get_pixmap(matrix=matrix, clip=clip, alpha=False)
    without_text = re.sub(rb'BT.*?ET', b'BT ET', content, flags=re.S)
    assert without_text != content
    doc.update_stream(xref, without_text)
    blank = doc[0].get_pixmap(matrix=matrix, clip=clip, alpha=False)
    return sum(max(abs(with_text.samples[i + channel] - blank.samples[i + channel])
                   for channel in range(3)) > 20
               for i in range(0, len(blank.samples), 3))


def geometry(data, panel):
    doc = pymupdf.open(stream=data, filetype='pdf')
    page = doc[0]
    images = page.get_images(full=True)
    assert len(images) == 1, images
    placements = [tuple(rect) for rect in page.get_image_rects(images[0][0])]
    target = pymupdf.Rect(panel[0], 130 - panel[1] - panel[3],
                          panel[0] + panel[2], 130 - panel[1])
    panels = [(tuple(drawing['rect']), drawing['fill_opacity']) for drawing in page.get_drawings()
              if drawing['type'] == 'f' and drawing['rect'] == target]
    text = [(span['font'], span['size'], tuple(span['bbox']), span['chars'])
            for span in page.get_texttrace()]
    assert len(panels) == 1 and panels[0][0] == tuple(target), (panels, target)
    assert text and all('BitstreamVeraSans' in span[0] for span in text), text
    return placements, panels, text


cases = ('inside-panel', 'edge-panel', 'uncovered-text', 'translucent-panel',
         'prior-conflict', 'unknown-clip', 'shared-invalid')
positive = {'inside-panel', 'edge-panel', 'uncovered-text', 'shared-invalid'}
# Preserve every authored source before any engine invocation.
sources = {case: fixture(case) for case in cases}
for case, (source, _) in sources.items():
    (root / f'{case}-source.pdf').write_bytes(source)
    source_doc = pymupdf.open(stream=source, filetype='pdf')
    source_doc[0].get_pixmap().save(root / f'{case}-source.png')
source_geometry = {}
results = []
for case, (source, panel) in sources.items():
    before_geometry = geometry(source, panel)
    placements, panels, text = before_geometry
    panel_rect = pymupdf.Rect(panels[0][0])
    first_title = pymupdf.Rect(text[0][2])
    if case in {'inside-panel', 'edge-panel'}:
        assert panels[0][1] == 1 and panel_rect.contains(first_title), case
    if case == 'inside-panel':
        assert pymupdf.Rect(placements[0]).contains(panel_rect), case
    if case == 'edge-panel':
        assert panel_rect.x0 < placements[0][2] < panel_rect.x1, case
    if case == 'uncovered-text':
        assert first_title.x0 < panel_rect.x1 < first_title.x1 and first_title.x0 < placements[0][2], case
        exposed = pymupdf.Rect(panel_rect.x1, first_title.y0,
                               placements[0][2], first_title.y1)
        assert uncovered_glyph_pixels(source, exposed) > 0, case
    if case == 'translucent-panel':
        assert .6 < panels[0][1] < .7, case
    if case == 'shared-invalid':
        assert len(placements) == 2 and len(text) == 2, case
        assert pymupdf.Rect(placements[1]).contains(pymupdf.Rect(text[1][2])), case
        assert not panel_rect.intersects(pymupdf.Rect(text[1][2])), case
    source_geometry[case] = {'imagePlacements': placements, 'panel': panels[0],
                             'titles': [item[2] for item in text]}
    source_image = image_samples(source)
    code, facts, output = invoke(source, '--convert')
    if args.probe:
        results.append({'case': case, 'code': code, 'diagnostic': facts if code else facts['changedPages']})
        print('PROBE', case, code, facts if code else 'converted')
        continue
    if case not in positive:
        assert code == 3 and not output, (case, code, facts)
        results.append({'case': case, 'refused': facts.strip()})
        print('PASS', case, 'refused')
        continue
    assert code == 0 and output, (case, code, facts)
    assert image_samples(output) == source_image, case
    assert geometry(output, panel) == before_geometry, case
    before = pymupdf.open(stream=source, filetype='pdf')
    after = pymupdf.open(stream=output, filetype='pdf')
    source_pix = before[0].get_pixmap(alpha=False)
    output_pix = after[0].get_pixmap(alpha=False)
    if case == 'shared-invalid':
        # The two uses of one image can take different safe paths: the first
        # panel and title darken together, while the second title keeps its
        # original color on a preserved local image and white backing.
        assert all(source_pix.pixel(x, y) == output_pix.pixel(x, y)
                   for y in range(25, 105) for x in range(135, 200)), case
        assert all(source_pix.pixel(x, y) == output_pix.pixel(x, y)
                   for y in range(25, 105) for x in range(25, 125)
                   if not panel_rect.contains(pymupdf.Point(x + .5, y + .5))), case
        background = output_pix.pixel(35, 45)
        assert max(background) < 110, (case, background)
        first_ink = after[0].get_texttrace()[0]['color']
        ratio = (luminance(first_ink) + .05) / (luminance([v / 255 for v in background]) + .05)
        assert ratio >= 4.5, (case, ratio)
        assert max(output_pix.pixel(5, 5)) < 60, case
        content = PdfReader(io.BytesIO(output)).pages[0].get_contents().get_data()
        assert content.count(b'/Artifact BMC') == 2, case
        (root / f'{case}-dark.pdf').write_bytes(output)
        output_pix.save(root / f'{case}-dark.png')
        for mode in ('--inspect', '--convert'):
            repeated_code, repeated, duplicate = invoke(output, mode)
            assert repeated_code == 0 and not duplicate and repeated['changedPages'] == 0, (case, mode, repeated)
        results.append({'case': case, 'preservedImage': True, 'preservedGeometry': True,
                        'secondCompositePixelsExact': True, 'firstPanelContrast': ratio,
                        'outsidePaperDark': True, 'repeatNoOp': True})
        print('PASS', case, 'safe split placements and repeat')
        continue
    if case == 'uncovered-text':
        # The title crosses the panel, so the bounded original-color image,
        # panel and title are kept together on a local white backing.
        assert all(source_pix.pixel(x, y) == output_pix.pixel(x, y)
                   for y in range(24, 106) for x in range(24, 141)), case
        assert max(output_pix.pixel(5, 5)) < 60, case
        content = PdfReader(io.BytesIO(output)).pages[0].get_contents().get_data()
        assert b'/Artifact BMC' in content, case
        (root / f'{case}-dark.pdf').write_bytes(output)
        output_pix.save(root / f'{case}-dark.png')
        for mode in ('--inspect', '--convert'):
            repeated_code, repeated, duplicate = invoke(output, mode)
            assert repeated_code == 0 and not duplicate and repeated['changedPages'] == 0, (case, mode, repeated)
        results.append({'case': case, 'preservedImage': True, 'preservedGeometry': True,
                        'originalCompositePixelsExact': True, 'outsidePaperDark': True,
                        'repeatNoOp': True})
        print('PASS', case, 'original image, panel, title and repeat')
        continue
    for y in range(130 - 105 + 1, 130 - 25 - 1):
        for x in range(25 + 1, 125 - 1):
            in_panel = (panel[0] - 1 <= x <= panel[0] + panel[2] + 1 and
                        130 - panel[1] - panel[3] - 1 <= y <= 130 - panel[1] + 1)
            if not in_panel:
                assert source_pix.pixel(x, y) == output_pix.pixel(x, y), (case, x, y)
    background = output_pix.pixel(panel[0] + 3, 130 - panel[1] - 3)
    assert max(background) < 110, (case, background)
    for span in after[0].get_texttrace():
        ink = span['color']
        ratio = (luminance(ink) + .05) / (luminance([v / 255 for v in background]) + .05)
        assert ratio >= 4.5, (case, ratio, ink, background)
    (root / f'{case}-dark.pdf').write_bytes(output)
    output_pix.save(root / f'{case}-dark.png')
    for mode in ('--inspect', '--convert'):
        repeated_code, repeated, duplicate = invoke(output, mode)
        assert repeated_code == 0 and not duplicate and repeated['changedPages'] == 0, (case, mode, repeated)
    results.append({'case': case, 'preservedImage': True, 'preservedGeometry': True,
                    'readablePanelText': True, 'repeatNoOp': True})
    print('PASS', case, 'image, geometry, contrast and repeat')
(root / 'source-geometry.json').write_text(json.dumps(source_geometry, indent=2), encoding='utf-8')
(root / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
