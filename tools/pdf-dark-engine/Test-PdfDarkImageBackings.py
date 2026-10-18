"""Preserve masked JPEG appearance on a proved local white backing."""

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
from pypdf.generic import (ArrayObject, BooleanObject, DictionaryObject, DecodedStreamObject,
                           NameObject, NumberObject, TextStringObject)

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--engine', required=True)
parser.add_argument('--scratch', required=True)
args = parser.parse_args()
root = Path(args.scratch).resolve()
if root.exists() or not root.is_relative_to(Path(__file__).resolve().parents[2] / '.codex-temp'):
    raise ValueError('Use a fresh repository-local scratch directory')
root.mkdir(parents=True)


def stream(writer, data, entries):
    result = DecodedStreamObject()
    result.set_data(data)
    result.update({NameObject(k): v for k, v in entries.items()})
    return writer._add_object(result)


def fixture(case):
    writer = PdfWriter()
    page = writer.add_blank_page(width=180, height=120)
    image = Image.new('RGB', (80, 60))
    image.putdata([(x * 3, y * 4, (x + y) % 80 * 3) for y in range(60) for x in range(80)])
    encoded = io.BytesIO()
    image.save(encoded, format='JPEG', quality=90)
    supported = case.startswith('support-')
    alpha = bytes((1 if case == 'support-faint' else 0) if supported and (y < 20 or y > 50) else
                  0 if case == 'edge' and (x < 3 or y < 3) else 178
                  for y in range(60) for x in range(80))
    geometry = {'/Type': NameObject('/XObject'), '/Subtype': NameObject('/Image'),
                '/Width': NumberObject(80), '/Height': NumberObject(60), '/BitsPerComponent': NumberObject(8)}
    mask = stream(writer, alpha, {**geometry, '/ColorSpace': NameObject('/DeviceGray')})
    art = stream(writer, encoded.getvalue(), {**geometry, '/ColorSpace': NameObject('/DeviceRGB'),
                 '/Filter': NameObject('/DCTDecode'), '/SMask': mask})
    page[NameObject('/Resources')] = DictionaryObject({NameObject('/XObject'):
                DictionaryObject({NameObject('/Art'): art})})
    content = '' if case == 'unknown-paper' else 'q 1 1 1 rg 0 0 180 120 re f Q\n'
    if supported:
        content += 'q 1 0 0 rg 30 80 80 5 re f Q\n'
        if case == 'support-crossing':
            content += 'q 1 0 0 rg 30 60 80 5 re f Q\n'
        if case == 'support-interpolated':
            mask.get_object()[NameObject('/Interpolate')] = BooleanObject(True)
        if case == 'support-color-interpolated':
            art.get_object()[NameObject('/Interpolate')] = BooleanObject(True)
    if case == 'prior-paint':
        content += 'q 1 0 0 rg 40 40 10 10 re f Q\n'
    if case == 'clip':
        content += 'q 35 35 50 40 re W n\n'
    if case == 'tagged':
        content += '/Figure <</MCID 0>> BDC\n'
    if case == 'pending-path':
        content += '10 10 m 20 20 l\n'
    if case == 'pending-clip':
        content += '35 35 50 40 re W\n'
    transform = '-80 0 0 60 110 30' if case == 'mirrored' else '80 0 0 60 30 30'
    content += f'q {transform} cm /Art Do Q\n'
    if case in ('pending-path', 'pending-clip'):
        content += 'n\n'
    if case == 'tagged':
        content += 'EMC\n'
        tag_root = writer._add_object(DictionaryObject({NameObject('/Type'): NameObject('/StructTreeRoot')}))
        figure = writer._add_object(DictionaryObject({NameObject('/Type'): NameObject('/StructElem'),
            NameObject('/S'): NameObject('/Figure'), NameObject('/P'): tag_root,
            NameObject('/Pg'): page.indirect_reference, NameObject('/K'): NumberObject(0),
            NameObject('/Alt'): TextStringObject('Fixture image')}))
        tag_root.get_object()[NameObject('/K')] = ArrayObject([figure])
        tag_root.get_object()[NameObject('/ParentTree')] = writer._add_object(DictionaryObject({
            NameObject('/Nums'): ArrayObject([NumberObject(0), ArrayObject([figure])])}))
        page[NameObject('/StructParents')] = NumberObject(0)
        writer._root_object[NameObject('/StructTreeRoot')] = tag_root
        writer._root_object[NameObject('/MarkInfo')] = DictionaryObject({NameObject('/Marked'): BooleanObject(True)})
    if case == 'clip':
        content += 'Q\n'
    if case == 'later-paint':
        content += 'q 1 0 0 rg 40 40 10 10 re f Q\n'
    if case == 'mixed-reuse':
        content += 'q 1 0 0 rg 120 30 40 30 re f Q\nq 40 0 0 30 120 30 cm /Art Do Q\n'
    page[NameObject('/Contents')] = stream(writer, content.encode(), {})
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=root) as file:
        file.write(data)
        file.seek(0)
        p = subprocess.run([args.engine, mode], stdin=file, capture_output=True, timeout=45)
    if p.returncode:
        assert not p.stdout
        return p.returncode, p.stderr.decode(errors='replace'), b''
    magic, version, n, m = struct.unpack_from('<IIII', p.stdout)
    assert (magic, version) == (0x31445043, 1) and len(p.stdout) == 16 + n + m
    return 0, json.loads(p.stdout[16:16+n]), p.stdout[16+n:]


def samples(data):
    image = PdfReader(io.BytesIO(data)).pages[0]['/Resources']['/XObject']['/Art'].get_object()
    return image.get_data(), image['/SMask'].get_data()


results = []
for case in ('uniform', 'edge', 'clip', 'tagged', 'mirrored', 'unknown-paper', 'prior-paint',
             'later-paint', 'mixed-reuse', 'pending-path', 'pending-clip',
             'support-clear', 'support-crossing', 'support-faint', 'support-interpolated',
             'support-color-interpolated'):
    source = fixture(case)
    (root / f'{case}-source.pdf').write_bytes(source)
    code, facts, output = invoke(source, '--convert')
    if case in ('uniform', 'edge', 'clip', 'tagged', 'mirrored', 'support-clear', 'support-color-interpolated'):
        assert code == 0 and output, (case, code, facts)
        assert samples(source) == samples(output), case
        if case == 'tagged':
            reader = PdfReader(io.BytesIO(output))
            figure = reader.trailer['/Root']['/StructTreeRoot']['/K'][0].get_object()
            assert figure['/Alt'] == 'Fixture image' and figure['/K'] == 0
            assert reader.pages[0]['/StructParents'] == 0
            assert b'/Artifact BMC' in reader.pages[0].get_contents().get_data()
        before = pymupdf.open(stream=source)[0].get_pixmap()
        after = pymupdf.open(stream=output)[0].get_pixmap()
        for y in range(30, 90):
            for x in range(30, 110):
                if case.startswith('support-') and not (50 <= y <= 80):
                    continue
                if case == 'clip' and not (35 <= x < 85 and 45 <= y < 85):
                    continue
                assert before.pixel(x, y) == after.pixel(x, y), (case, x, y)
        if case.startswith('support-'):
            header = after.pixel(60, 37)
            assert header[0] > header[1] + 40, 'Backing erased the earlier header'
        assert all(abs(a - b) <= 1 for a, b in zip(after.pixel(5, 5), (18, 19, 20)))
        for mode in ('--inspect', '--convert'):
            code, repeated, duplicate = invoke(output, mode)
            assert code == 0 and not duplicate and repeated['changedPages'] == 0, (case, mode, repeated)
        (root / f'{case}-dark.pdf').write_bytes(output)
        after.save(root / f'{case}-dark.png')
        results.append(dict(case=case, exactImageAppearance=True, repeatNoOp=True))
    else:
        assert code == 3 and not output, (case, code, facts)
        results.append(dict(case=case, refused=True))
(root / 'results.json').write_text(json.dumps(results, indent=2))
print(f'Image backings: {len(results)} cases passed.')
