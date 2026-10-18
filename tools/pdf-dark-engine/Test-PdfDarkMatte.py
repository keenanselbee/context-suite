"""Authored exact-zero black-matte normalization and malformed-mask refusals."""

import argparse
import io
import json
from pathlib import Path
import struct
import subprocess
import tempfile

import pymupdf
from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.generic import ArrayObject, FloatObject, NameObject, NumberObject
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen.canvas import Canvas

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--engine', required=True)
parser.add_argument('--scratch', required=True)
args = parser.parse_args()
scratch = Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(Path(__file__).resolve().parents[2] / '.codex-temp'):
    raise ValueError('Use a fresh repository-local scratch directory')
scratch.mkdir(parents=True)


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=scratch) as stream:
        stream.write(data)
        stream.seek(0)
        result = subprocess.run([args.engine, mode], stdin=stream, capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors='replace'), b''
    magic, version, size, length = struct.unpack_from('<IIII', result.stdout)
    assert (magic, version) == (0x31445043, 1) and len(result.stdout) == 16 + size + length
    return 0, json.loads(result.stdout[16:16 + size]), result.stdout[16 + size:]


def image_object(data):
    reader = PdfReader(io.BytesIO(data))
    return next(value.get_object() for value in reader.pages[0]['/Resources']['/XObject'].values())


def fixture(case):
    image = Image.new('RGBA', (120, 80), (0, 0, 0, 0))
    for y in range(80):
        for x in range(120):
            alpha = min(110, min(x, y, 119 - x, 79 - y) * 10)
            image.putpixel((x, y), (0, 0, 0, alpha))
    stream = io.BytesIO()
    canvas = Canvas(stream, pagesize=(300, 200))
    canvas.setFillColorRGB(1, 1, 1)
    canvas.rect(0, 0, 300, 200, fill=1, stroke=0)
    canvas.drawImage(ImageReader(image), 30, 40, width=120, height=80, mask='auto')
    canvas.save()
    writer = PdfWriter()
    writer.clone_document_from_reader(PdfReader(io.BytesIO(stream.getvalue())))
    obj = next(value.get_object() for value in writer.pages[0]['/Resources']['/XObject'].values())
    mask = obj['/SMask'].get_object()
    components = 1 if case == 'gray' else 3
    if components == 1:
        obj[NameObject('/ColorSpace')] = NameObject('/DeviceGray')
        # DecodedStreamObject avoids codec-specific set_data restrictions.
        from pypdf.generic import DecodedStreamObject
        replacement = DecodedStreamObject()
        replacement.update({key: value for key, value in obj.items() if key not in ('/Filter', '/DecodeParms')})
        replacement.set_data(bytes(120 * 80))
        writer.pages[0]['/Resources']['/XObject'][next(iter(writer.pages[0]['/Resources']['/XObject']))] = writer._add_object(replacement)
        obj = replacement
    matte = [0] * components
    if case == 'nonblack-matte':
        matte[0] = .2
    if case == 'wrong-components':
        matte = [0]
    mask[NameObject('/Matte')] = ArrayObject([FloatObject(value) for value in matte])
    if case == 'nonzero-samples':
        from pypdf.generic import DecodedStreamObject
        replacement = DecodedStreamObject()
        replacement.update({key: value for key, value in obj.items() if key not in ('/Filter', '/DecodeParms')})
        replacement.set_data(bytes([12]) * (120 * 80 * 3))
        writer.pages[0]['/Resources']['/XObject'][next(iter(writer.pages[0]['/Resources']['/XObject']))] = writer._add_object(replacement)
    if case == 'inverted-alpha':
        mask[NameObject('/Decode')] = ArrayObject([NumberObject(1), NumberObject(0)])
    if case == 'wrong-size':
        mask[NameObject('/Width')] = NumberObject(119)
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


results = []
for case in ('rgb', 'gray', 'nonblack-matte', 'wrong-components', 'nonzero-samples', 'inverted-alpha', 'wrong-size'):
    source = fixture(case)
    (scratch / f'{case}-source.pdf').write_bytes(source)
    code, facts, output = invoke(source, '--convert')
    if case not in ('rgb', 'gray'):
        assert code in (3, 4) and not output, (case, code, facts)
        results.append(dict(case=case, refused=True, diagnostic=facts.strip()))
        continue
    assert code == 0 and output, (case, code, facts)
    before, after = image_object(source), image_object(output)
    assert before['/SMask'].get_data() == after['/SMask'].get_data()
    assert '/Matte' not in after['/SMask']
    assert set(after.get_data()[0::3]) == {244}
    assert set(after.get_data()[1::3]) == {235}
    assert set(after.get_data()[2::3]) == {216}
    rendered = pymupdf.open(stream=output)
    pix = rendered[0].get_pixmap(alpha=False)
    expected = [round(ink * 110 / 255 + paper * 145 / 255) for ink, paper in zip((244, 235, 216), (18, 19, 20))]
    assert all(abs(a - b) <= 2 for a, b in zip(pix.pixel(80, 120), expected)), (case, pix.pixel(80, 120), expected)
    (scratch / f'{case}-dark.pdf').write_bytes(output)
    pix.save(scratch / f'{case}-dark.png')
    for mode in ('--inspect', '--convert'):
        repeat_code, repeated, duplicate = invoke(output, mode)
        assert repeat_code == 0 and not duplicate and repeated['changedPages'] == 0, (case, mode, repeated)
    results.append(dict(case=case, passed=True, repeatNoOp=True, alphaUnchanged=True))
(scratch / 'results.json').write_text(json.dumps(results, indent=2))
print(f'Matte normalization: {len(results)} cases passed.')
