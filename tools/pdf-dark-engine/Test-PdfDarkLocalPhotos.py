"""Authored local JPEG photo scenes with translucent title overlays."""

import argparse
import io
import json
from pathlib import Path
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
from pypdf.generic import (ArrayObject, DecodedStreamObject, DictionaryObject,
                           FloatObject, NameObject, NumberObject)


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
registerFont(TTFont('LocalPhotoFixture', str(Path(reportlab.__file__).parent / 'fonts' / 'Vera.ttf')))

CASES = ('basic', 'edge-clipped', 'masked-logo', 'partial-outside',
         'unknown-clip', 'masked-base', 'shared-logo-outside', 'interfering-form',
         'full-page-repaint', 'crossing-child-image', 'disjoint-stroke')
POSITIVES = {'basic', 'edge-clipped', 'masked-logo'}
VISIBLE = (50, 40, 125, 80)


def add_stream(writer, data, entries):
    obj = DecodedStreamObject()
    obj.set_data(data)
    obj.update({NameObject(key): value for key, value in entries.items()})
    return writer._add_object(obj)


def jpeg(width, height, logo=False):
    image = Image.new('RGB', (width, height))
    if logo:
        image.putdata([(35 + 2 * x, 55 + 4 * y, 130 + (3 * x) % 90)
                       for y in range(height) for x in range(width)])
    else:
        image.putdata([(30 + (3 * x) % 180, 75 + (2 * y) % 150,
                        110 + (x + 2 * y) % 100)
                       for y in range(height) for x in range(width)])
    encoded = io.BytesIO()
    image.save(encoded, format='JPEG', quality=91)
    return encoded.getvalue()


def image_object(writer, width, height, data, mask=None):
    base = {'/Type': NameObject('/XObject'), '/Subtype': NameObject('/Image'),
            '/Width': NumberObject(width), '/Height': NumberObject(height),
            '/BitsPerComponent': NumberObject(8)}
    entries = {**base, '/ColorSpace': NameObject('/DeviceRGB'),
               '/Filter': NameObject('/DCTDecode')}
    if mask is not None:
        entries['/SMask'] = add_stream(writer, mask,
            {**base, '/ColorSpace': NameObject('/DeviceGray')})
    return add_stream(writer, data, entries)


def fixture(case):
    # ReportLab emits a real embedded TrueType font; page paint order is
    # explicit in the literal operators so each scene relation is independent.
    buf = io.BytesIO()
    canvas = Canvas(buf, pagesize=(240, 160), pageCompression=0)
    canvas._code.append('q 1 1 1 rg 0 0 240 160 re f Q')
    if case == 'edge-clipped':
        canvas._code.append('q 50 40 125 80 re W n')
    if case == 'unknown-clip':
        canvas._code.append('q 40 35 m 180 35 l 180 125 l h W n')
    canvas._code.append('q 140 0 0 90 40 35 cm /Photo Do Q')
    if case in ('edge-clipped', 'unknown-clip'):
        canvas._code.append('Q')
    panel = (50, 80, 125, 26) if case == 'edge-clipped' else (
        (30, 80, 105, 26) if case == 'partial-outside' else (60, 80, 100, 26))
    canvas._code.append(f'q /Fade gs 1 1 1 rg {panel[0]} {panel[1]} {panel[2]} {panel[3]} re f Q')
    if case in ('masked-logo', 'shared-logo-outside'):
        canvas._code.append('q 20 0 0 15 66 85 cm /Logo Do Q')
    canvas.setFont('LocalPhotoFixture', 12)
    canvas.setFillColorRGB(.13, .1, .08)
    canvas.drawString(92, 87, 'STORY')
    if case == 'disjoint-stroke':
        # One S operation has separate footprints. Preserving it for its
        # photo-contained segment must not preserve its off-photo segment.
        canvas._code.append('q 1 1 1 RG 1 w 60 60 m 70 60 l 190 60 m 205 60 l S Q')
    if case == 'shared-logo-outside':
        canvas._code.append('q 20 0 0 15 12 55 cm /Logo Do Q')
    if case == 'crossing-child-image':
        canvas._code.append('q 20 0 0 15 170 85 cm /Logo Do Q')
    if case == 'interfering-form':
        canvas._code.append('q 20 0 0 20 74 84 cm /Interfere Do Q')
    if case == 'full-page-repaint':
        canvas._code.append('q 1 1 1 rg 0 0 240 160 re f Q')
    canvas.setFillColorRGB(.08, .1, .14)
    canvas.drawString(12, 142, 'OUTSIDE')
    canvas.save()

    writer = PdfWriter()
    writer.append(PdfReader(io.BytesIO(buf.getvalue())))
    page = writer.pages[0]
    mask = bytes([220]) * (100 * 70) if case == 'masked-base' else None
    photo = image_object(writer, 100, 70, jpeg(100, 70), mask)
    objects = DictionaryObject({NameObject('/Photo'): photo})
    if case in ('masked-logo', 'shared-logo-outside', 'crossing-child-image'):
        logo_mask = bytes(255 if (x - 10) ** 2 / 90 + (y - 7) ** 2 / 40 < 1 else 0
                          for y in range(15) for x in range(20))
        objects[NameObject('/Logo')] = image_object(writer, 20, 15, jpeg(20, 15, True), logo_mask)
    if case == 'interfering-form':
        form = add_stream(writer, b'0 0 1 rg 0 0 1 1 re f\n', {
            '/Type': NameObject('/XObject'), '/Subtype': NameObject('/Form'),
            '/BBox': ArrayObject([NumberObject(0), NumberObject(0),
                                  NumberObject(1), NumberObject(1)])})
        objects[NameObject('/Interfere')] = form
    resources = page['/Resources'].get_object()
    resources[NameObject('/XObject')] = objects
    fade = writer._add_object(DictionaryObject({NameObject('/Type'): NameObject('/ExtGState'),
        NameObject('/BM'): NameObject('/Normal'), NameObject('/ca'): FloatObject(.65)}))
    states = resources.get('/ExtGState')
    if states is None:
        states = DictionaryObject()
        resources[NameObject('/ExtGState')] = states
    states[NameObject('/Fade')] = fade
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue(), panel


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


def images(data):
    objects = PdfReader(io.BytesIO(data)).pages[0]['/Resources']['/XObject']
    result = {}
    for name in ('/Photo', '/Logo'):
        if name not in objects:
            continue
        obj = objects[name].get_object()
        assert obj['/Filter'] == '/DCTDecode'
        mask = obj.get('/SMask')
        result[name] = (obj._data, mask.get_object().get_data() if mask else None)
    return result


def geometry(data, panel):
    doc = pymupdf.open(stream=data, filetype='pdf')
    page = doc[0]
    names = sorted((item[7] for item in page.get_images(full=True)))
    photos = [tuple(rect) for rect in page.get_image_rects(
        next(item[0] for item in page.get_images(full=True) if item[7] == 'Photo'))]
    target = pymupdf.Rect(panel[0], 160 - panel[1] - panel[3],
                          panel[0] + panel[2], 160 - panel[1])
    fills = [(tuple(item['rect']), item['fill_opacity']) for item in page.get_drawings()
             if item['type'] == 'f' and item['rect'] == target]
    text = [(item['font'], item['size'], tuple(item['bbox']), item['color'], item['chars'])
            for item in page.get_texttrace()]
    assert len(fills) == 1 and .64 < fills[0][1] < .66, (fills, target)
    assert len(text) == 2 and all('BitstreamVeraSans' in item[0] for item in text), text
    return {'imageNames': names, 'photoPlacements': photos, 'panel': fills[0],
            'text': [(item[0], item[1], item[2], item[4]) for item in text]}, text


sources = {case: fixture(case) for case in CASES}
for case, (data, _) in sources.items():
    (root / f'{case}-source.pdf').write_bytes(data)
    pymupdf.open(stream=data, filetype='pdf')[0].get_pixmap().save(root / f'{case}-source.png')

results = []
for case, (source, panel) in sources.items():
    source_geometry, source_text = geometry(source, panel)
    photo = pymupdf.Rect(source_geometry['photoPlacements'][0])
    target = pymupdf.Rect(source_geometry['panel'][0])
    if case == 'edge-clipped':
        assert target == pymupdf.Rect(50, 54, 175, 80)
        assert target.x0 == VISIBLE[0] and target.x1 == VISIBLE[0] + VISIBLE[2]
    elif case == 'partial-outside':
        assert target.x0 < photo.x0 < target.x1
    elif case in POSITIVES:
        assert photo.contains(target)
    assert target.contains(pymupdf.Rect(source_text[0][2]))
    assert not photo.intersects(pymupdf.Rect(source_text[1][2]))
    if case == 'shared-logo-outside':
        assert source_geometry['imageNames'].count('Logo') == 1
        logo = next(item for item in pymupdf.open(stream=source, filetype='pdf')[0].get_images(full=True)
                    if item[7] == 'Logo')
        assert len(pymupdf.open(stream=source, filetype='pdf')[0].get_image_rects(logo[0])) == 2
    if case == 'crossing-child-image':
        doc = pymupdf.open(stream=source, filetype='pdf')
        logo = next(item for item in doc[0].get_images(full=True) if item[7] == 'Logo')
        child = doc[0].get_image_rects(logo[0])[0]
        assert child.x0 < photo.x1 < child.x1 and child.intersects(photo)
    if case == 'full-page-repaint':
        assert pymupdf.open(stream=source, filetype='pdf')[0].get_pixmap().pixel(45, 50) == (255, 255, 255)
    if case == 'disjoint-stroke':
        content = PdfReader(io.BytesIO(source)).pages[0].get_contents().get_data()
        assert b'60 60 m 70 60 l 190 60 m 205 60 l S' in content
        drawing = pymupdf.open(stream=source, filetype='pdf')[0].get_drawings()
        assert any(item['type'] == 's' and item['rect'].x0 < photo.x1 < item['rect'].x1
                   for item in drawing)
    code, facts, output = invoke(source, '--convert')
    if args.probe:
        results.append({'case': case, 'code': code, 'diagnostic': facts if code else facts['changedPages']})
        print('PROBE', case, code, facts if code else 'converted')
        continue
    if case not in POSITIVES:
        assert code == 3 and not output, (case, code, facts)
        results.append({'case': case, 'refused': facts.strip()})
        print('PASS', case, 'refused')
        continue
    assert code == 0 and output, (case, code, facts)
    assert images(output) == images(source), case
    converted_geometry, converted_text = geometry(output, panel)
    assert converted_geometry == source_geometry, case
    original = pymupdf.open(stream=source, filetype='pdf')[0].get_pixmap(alpha=False)
    converted = pymupdf.open(stream=output, filetype='pdf')[0].get_pixmap(alpha=False)
    visible = pymupdf.Rect(50, 40, 175, 120) if case == 'edge-clipped' else photo
    for y in range(int(visible.y0), int(visible.y1)):
        for x in range(int(visible.x0), int(visible.x1)):
            assert original.pixel(x, y) == converted.pixel(x, y), (case, x, y)
    assert max(converted.pixel(5, 5)) < 100, case
    assert max(source_text[1][3]) < .3 and min(converted_text[1][3]) > .65, case
    (root / f'{case}-dark.pdf').write_bytes(output)
    converted.save(root / f'{case}-dark.png')
    for mode in ('--inspect', '--convert'):
        repeat_code, repeat, duplicate = invoke(output, mode)
        assert repeat_code == 0 and not duplicate and repeat['changedPages'] == 0, (case, mode, repeat)
    results.append({'case': case, 'imageAndMaskUnchanged': True,
                    'scenePixelsUnchanged': True, 'geometryUnchanged': True,
                    'outerPageDarkened': True, 'repeatNoOp': True})
    print('PASS', case, 'retained scene, outer dark palette and repeat')
(root / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
