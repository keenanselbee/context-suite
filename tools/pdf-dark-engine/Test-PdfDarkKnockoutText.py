"""Refuse white erasing glyphs over strokes while preserving ordinary labels.

Authored Vera Bold Italic fixtures exercise descenders that erase an underline
before a final black glyph run. This is a paint-role refusal, not a font ban.
"""

import argparse
import hashlib
import importlib.util
import io
import json
from pathlib import Path

import pymupdf
import reportlab
from reportlab.pdfbase.pdfmetrics import registerFont
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas


spec = importlib.util.spec_from_file_location('named_colors',
    Path(__file__).with_name('Test-PdfDarkNamedColors.py'))
named = importlib.util.module_from_spec(spec)
spec.loader.exec_module(named)
registerFont(TTFont('KnockoutFixture', str(Path(reportlab.__file__).parent / 'fonts' / 'VeraBI.ttf')))


def fixture(case):
    output = io.BytesIO()
    canvas = Canvas(output, pagesize=(300, 180), invariant=1)
    canvas.setFillColorRGB(*((0, .125, .251) if case == 'navy-paper-label' else (1, 1, 1)))
    canvas.rect(0, 0, 300, 180, fill=1, stroke=0)
    canvas.setFont('KnockoutFixture', 22)
    canvas.setStrokeColorRGB(*((.2, .2, .2) if case == 'gray-stroke-eraser' else (0, 0, 0)))
    canvas.setLineWidth(1)
    if case in ('black-stroke-eraser', 'gray-stroke-eraser', 'covered-stroke-label', 'navy-paper-label'):
        canvas.line(40, 77, 180, 77)
    else:
        canvas.line(40, 140, 180, 140)
    if 'label' in case and case != 'navy-paper-label':
        canvas.setFillColorRGB(0, 0, 0)
        canvas.rect(30, 50, 230, 65, fill=1, stroke=0)
    canvas.setFillColorRGB(1, 1, 1)
    if 'eraser' in case:
        for offset in (-1, -.8, -.6, -.4, -.2, 0, .2, .4, .6, .8, 1):
            canvas.drawString(40 + offset, 80, 'gyp')
        canvas.setFillColorRGB(0, 0, 0)
        canvas.drawString(40, 80, 'gyp')
    else:
        canvas.drawString(40, 80, 'gyp LABEL')
    canvas.save()
    return output.getvalue()


def geometry(data):
    with pymupdf.open(stream=data, filetype='pdf') as document:
        page = document[0]
        text = [(span['font'], span['size'], span['bbox'], span['chars']) for span in page.get_texttrace()]
        paths = [(path['type'], path['items'], path['width']) for path in page.get_drawings()]
        return text, paths


def label_mask(data, ignore_stroke=False):
    with pymupdf.open(stream=data, filetype='pdf') as document:
        pixmap = document[0].get_pixmap(matrix=pymupdf.Matrix(2, 2),
                                      clip=pymupdf.Rect(32, 66, 250, 125), alpha=False)
        samples = pixmap.samples
        # On navy paper the independent black line legitimately becomes ink.
        # Omit its known 1-point stroke band from this glyph-only comparison.
        return [min(samples[i:i + 3]) > 150 for i in range(0, len(samples), 3)
                if not ignore_stroke or abs((i // 3) // pixmap.width - 74) > 3]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--engine', required=True)
    parser.add_argument('--scratch', required=True)
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    scratch = Path(args.scratch).resolve()
    engine = Path(args.engine).resolve(strict=True)
    if scratch.exists() or not scratch.is_relative_to(repository / '.codex-temp'):
        raise ValueError('Use a fresh repository-local scratch directory')
    scratch.mkdir(parents=True)
    results, failures = [], []
    for case in ('black-stroke-eraser', 'gray-stroke-eraser', 'ordinary-panel-label', 'covered-stroke-label', 'navy-paper-label'):
        source = fixture(case)
        (scratch / (case + '-source.pdf')).write_bytes(source)
        try:
            inspect_code, inspection, empty = named.invoke(engine, scratch, source, '--inspect')
            code, facts, output = named.invoke(engine, scratch, source, '--convert')
            if 'eraser' in case:
                assert inspect_code == code == 3 and not empty and not output, (code, facts)
                assert 'knockout text over prior strokes' in inspection and inspection == facts, facts
            else:
                assert inspect_code == code == 0 and facts == inspection and not empty and output, (code, facts)
                assert geometry(source) == geometry(output), 'Text/path geometry changed'
                before = label_mask(source, ignore_stroke=case == 'navy-paper-label')
                after = label_mask(output, ignore_stroke=case == 'navy-paper-label')
                assert sum(before) > 100, 'Fixture has no visible white labels'
                # Palette endpoints differ, while the thresholded glyph
                # footprint must agree except for antialiasing edge rounding.
                assert sum(a != b for a, b in zip(before, after)) <= sum(before) * .08, 'Label glyph footprint changed'
                repeat_code, repeat, duplicate = named.invoke(engine, scratch, output, '--convert')
                assert repeat_code == 0 and repeat['changedPages'] == 0 and not duplicate, repeat
                (scratch / (case + '-dark.pdf')).write_bytes(output)
            results.append({'case': case, 'passed': True, 'expectedRefusal': 'eraser' in case})
            print('PASS', case)
        except AssertionError as error:
            failures.append(case)
            results.append({'case': case, 'passed': False, 'detail': str(error)})
            print('FAIL', case, error)
    (scratch / 'results.json').write_text(json.dumps({'engineSha256': hashlib.sha256(engine.read_bytes()).hexdigest(),
        'results': results, 'failures': failures}, indent=2) + '\n', encoding='utf-8')
    print(f'{len(results) - len(failures)}/{len(results)} knockout-text checks passed.')
    if failures:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
