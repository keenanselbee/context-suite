"""Prove later opaque images or panels preserve partially exposed earlier text."""

import argparse
import io
import json
from pathlib import Path
import struct
import subprocess

import pymupdf
from PIL import Image, ImageDraw
import reportlab
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase.pdfmetrics import registerFont
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--engine', required=True)
parser.add_argument('--scratch', required=True)
parser.add_argument('--probe', action='store_true', help='Record the pre-fix repeat refusal')
args = parser.parse_args()
repository = Path(__file__).resolve().parents[2]
scratch = Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(repository / '.codex-temp'):
    raise ValueError('Use a fresh repository-local scratch directory')
scratch.mkdir(parents=True)
engine = Path(args.engine).resolve(strict=True)
registerFont(TTFont('LaterImageFixture',
                    str(Path(reportlab.__file__).parent / 'fonts' / 'Vera.ttf')))

POSITIVES = ('opaque-covered', 'exposed-image', 'prior-image', 'rectangular-clip', 'mirrored-image')
NEGATIVES = ('translucent-image', 'masked-image', 'unknown-clip', 'rotated-image', 'prior-exposed')


def fixture(case):
    masked = case == 'masked-image'
    art = Image.new('RGBA' if masked else 'RGB', (80, 60), 'white')
    pen = ImageDraw.Draw(art)
    pen.rectangle((3, 3, 76, 35), outline='black', width=2)
    pen.line((0, 20, 79, 20), fill='black', width=2)
    if masked:
        art.putalpha(170)

    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(300, 200), pageCompression=0)
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 300, 200, fill=1, stroke=0)

    def draw_image():
        transformed = case in ('translucent-image', 'unknown-clip', 'rotated-image',
                               'rectangular-clip', 'mirrored-image')
        if transformed:
            canvas.saveState()
        if case == 'translucent-image':
            canvas.setFillAlpha(.6)
        if case == 'unknown-clip':
            path = canvas.beginPath()
            path.moveTo(90, 30)
            path.lineTo(210, 30)
            path.lineTo(210, 130)
            path.close()
            canvas.clipPath(path, stroke=0, fill=0)
        if case == 'rotated-image':
            canvas.translate(150, 80)
            canvas.rotate(10)
            canvas.translate(-150, -80)
        if case == 'rectangular-clip':
            path = canvas.beginPath()
            path.rect(100, 32, 100, 90)
            canvas.clipPath(path, stroke=0, fill=0)
        if case == 'mirrored-image':
            canvas.translate(300, 0)
            canvas.scale(-1, 1)
        canvas.drawImage(ImageReader(art), 90, 30, width=120, height=100,
                         mask='auto' if masked else None)
        if transformed:
            canvas.restoreState()

    if case in ('prior-image', 'prior-exposed'):
        draw_image()
    canvas.setFillColorRGB(.35, .35, .35)
    canvas.setFont('LaterImageFixture', 18)
    canvas.drawString(145, 25, '2')
    if case not in ('prior-image', 'prior-exposed'):
        draw_image()
    canvas.setFillColorRGB(1, 1, 1)
    # The image begins at y=30. The positive panel covers every image pixel
    # within the glyph's conservative bounds, while its lower ink stays exposed.
    # Only later opaque images can hide earlier text outside that band. An
    # earlier image remains safe only where the later panel hides the overlap.
    band_height = 5 if case in ('exposed-image', 'masked-image',
                                'unknown-clip', 'rotated-image', 'prior-exposed',
                                'rectangular-clip', 'mirrored-image') else 13
    canvas.rect(90, 30, 120, band_height, fill=1, stroke=0)
    canvas.save()
    return stream.getvalue()


def invoke(data, mode):
    process = subprocess.run([engine, mode], input=data, capture_output=True, timeout=45)
    if process.returncode:
        assert not process.stdout
        return process.returncode, process.stderr.decode(errors='replace').strip(), b''
    magic, version, facts_size, pdf_size = struct.unpack_from('<4I', process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + facts_size + pdf_size
    facts = json.loads(process.stdout[16:16 + facts_size])
    return 0, facts, process.stdout[16 + facts_size:]


def geometry(data):
    with pymupdf.open(stream=data, filetype='pdf') as document:
        page = document[0]
        text = [(span['font'], span['size'], span['bbox'], span['chars'])
                for span in page.get_texttrace()]
        drawings = [drawing['items'] for drawing in page.get_drawings()]
        images = page.get_images(full=True)
        placements = [tuple(rect) for image in images
                      for rect in page.get_image_rects(image[0])]
        return text, drawings, placements


results = []
for case in (*POSITIVES, *NEGATIVES):
    source = fixture(case)
    (scratch / (case + '-source.pdf')).write_bytes(source)
    first_code, first, dark = invoke(source, '--convert')
    result = {'case': case, 'firstExit': first_code}
    if first_code == 0:
        assert dark and first['changedPages'] == 1, (case, first)
        (scratch / (case + '-dark.pdf')).write_bytes(dark)
        repeat_code, repeated, duplicate = invoke(dark, '--inspect')
        result['repeatInspectExit'] = repeat_code
        result['repeatInspect'] = repeated
        assert not duplicate, case
    else:
        result['firstRefusal'] = first

    if case in POSITIVES:
        assert first_code == 0, (case, first)
        assert geometry(source) == geometry(dark), 'Text, drawing or image placement changed'
        with pymupdf.open(stream=source, filetype='pdf') as before, \
                pymupdf.open(stream=dark, filetype='pdf') as after:
            image = before[0].get_images(full=True)
            assert len(image) == 1 and image[0][1] == 0, image
            glyph = pymupdf.Rect(before[0].get_texttrace()[0]['bbox'])
            image_bounds = before[0].get_image_rects(image[0][0])[0]
            band = pymupdf.Rect(90, 157, 210, 170)
            if case in ('opaque-covered', 'prior-image'):
                assert band.contains(glyph & image_bounds) and glyph.y1 > band.y1, \
                    (glyph, image_bounds, band)
            source_pixels = before[0].get_pixmap(alpha=False)
            dark_pixels = after[0].get_pixmap(alpha=False)
            assert min(source_pixels.pixel(130, 175)) > 240
            assert max(dark_pixels.pixel(130, 175)) < 35
            if case in ('opaque-covered', 'prior-image'):
                assert max(dark_pixels.pixel(130, 163)) < 50
            assert any(min(dark_pixels.pixel(x, y)) > 180
                       for x in range(145, 157) for y in range(170, 180)), \
                'The lower part of the original glyph must remain visible'
            dark_pixels.save(scratch / (case + '-dark.png'))
        if args.probe:
            assert repeat_code == 3 and 'Ambiguous dark fill and light text' in repeated, repeated
        else:
            assert repeat_code == 0 and repeated['changedPages'] == 0, repeated
            convert_code, converted, second = invoke(dark, '--convert')
            assert convert_code == 0 and converted['changedPages'] == 0 and not second, \
                (convert_code, converted)
        result['geometryPreserved'] = True
    else:
        if first_code == 0:
            assert repeat_code == 3 and 'Page 1:' in repeated, (case, repeated)
        else:
            assert first_code == 3 and 'Page 1:' in first, (case, first)
    results.append(result)

(scratch / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
print(f'PASS {len(results)} later-image occlusion cases' + (' (baseline probe)' if args.probe else ''))
