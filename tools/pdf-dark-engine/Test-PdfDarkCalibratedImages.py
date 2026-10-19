"""Check authored gray ICC/CalRGB images and bounded grayscale JPEG framing.

Lossless line art uses a complete independently calculated sample oracle.
Retained JPEGs must preserve encoded bytes, color definitions, and rendered pixels.
"""

import argparse
import hashlib
import io
import json
import pathlib
import runpy

import pymupdf
from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, DecodedStreamObject, FloatObject,
                           NameObject, NumberObject)


# Reuse the independently authored ICC fixture and framed host reader.
HELPERS = runpy.run_path(str(pathlib.Path(__file__).with_name('Test-PdfDarkNamedColors.py')))
dictionary = HELPERS['dictionary']
gray_profile = HELPERS['gray_profile']
invoke = HELPERS['invoke']
WIDTH, HEIGHT = 64, 32


def fixture(kind, jpeg=False, suffix=b'', mismatch=False, intent=None, matte_channels=None):
    writer = PdfWriter()
    page = writer.add_blank_page(width=200, height=140)
    if kind == 'gray-icc':
        profile = DecodedStreamObject()
        profile.set_data(gray_profile())
        profile.update(dictionary(N=NumberObject(1), Alternate=NameObject('/DeviceGray')))
        space = ArrayObject([NameObject('/ICCBased'), writer._add_object(profile)])
    elif kind == 'calrgb':
        space = ArrayObject([NameObject('/CalRGB'), dictionary(
            WhitePoint=ArrayObject([FloatObject(v) for v in (.9505, 1, 1.089)]),
            Matrix=ArrayObject([FloatObject(v) for v in
                               (.4124, .2126, .0193, .3576, .7152, .1192, .1805, .0722, .9505)]),
            Gamma=ArrayObject([FloatObject(2.22221) for _ in range(3)]))])
    else:
        assert kind == 'device-gray'
        space = NameObject('/DeviceGray')
    channels = 3 if kind == 'calrgb' else 1
    if jpeg:
        pixels = bytes(45 + x * 2 + y + c * 10
                       for y in range(HEIGHT) for x in range(WIDTH) for c in range(channels))
        image = Image.frombytes('RGB' if channels == 3 else 'L', (WIDTH, HEIGHT), pixels)
        encoded = io.BytesIO()
        image.save(encoded, format='JPEG', quality=88, subsampling=0)
        pixels = encoded.getvalue() + suffix
    else:
        # Mostly white diagram: black strokes and a small middle-gray column.
        # Middle values ensure the oracle detects ignored TRCs, not just endpoints.
        pixels = bytes(value for y in range(HEIGHT) for x in range(WIDTH)
                       for value in [0 if x < 4 else 128 if x == 4 else 255] * channels)
    image = DecodedStreamObject()
    image.set_data(pixels)
    image.update(dictionary(Type=NameObject('/XObject'), Subtype=NameObject('/Image'),
                            Width=NumberObject(WIDTH), Height=NumberObject(HEIGHT),
                            BitsPerComponent=NumberObject(8), ColorSpace=space))
    if mismatch:
        image[NameObject('/ColorSpace')] = NameObject('/DeviceRGB')
    if intent is not None:
        image[NameObject('/Intent')] = NameObject('/' + intent)
    if matte_channels is not None:
        mask = DecodedStreamObject()
        mask.set_data(bytes([255]) * (WIDTH * HEIGHT))
        mask.update(dictionary(Type=NameObject('/XObject'), Subtype=NameObject('/Image'),
                               Width=NumberObject(WIDTH), Height=NumberObject(HEIGHT),
                               BitsPerComponent=NumberObject(8), ColorSpace=NameObject('/DeviceGray'),
                               Matte=ArrayObject([NumberObject(0) for _ in range(matte_channels)])))
        image[NameObject('/SMask')] = writer._add_object(mask.flate_encode())
    if jpeg:
        image[NameObject('/Filter')] = NameObject('/DCTDecode')
    else:
        image = image.flate_encode()
    page[NameObject('/Resources')] = dictionary(XObject=dictionary(Im=writer._add_object(image)))
    contents = DecodedStreamObject()
    contents.set_data(b'1 1 1 rg 0 0 200 140 re f\nq 128 0 0 64 20 20 cm /Im Do Q\n')
    page[NameObject('/Contents')] = writer._add_object(contents)
    result = io.BytesIO()
    writer.write(result)
    return result.getvalue()


def image_object(data):
    return PdfReader(io.BytesIO(data)).pages[0]['/Resources']['/XObject']['/Im'].get_object()


def color_definition(image):
    space = image['/ColorSpace']
    if isinstance(space, ArrayObject) and space[0] == '/ICCBased':
        profile = space[1].get_object()
        return str(space[0]), int(profile['/N']), profile.get_data()
    if isinstance(space, ArrayObject) and space[0] == '/CalRGB':
        return str(space[0]), tuple((key, tuple(float(value) for value in space[1][key]))
                                   for key in ('/WhitePoint', '/Matrix', '/Gamma'))
    return str(space)


def image_pixels(data):
    with pymupdf.open(stream=data, filetype='pdf') as document:
        # Stay inside the image placement to exclude surrounding paper/edge AA.
        pixmap = document[0].get_pixmap(matrix=pymupdf.Matrix(2, 2),
                                      clip=pymupdf.Rect(22, 58, 146, 118), alpha=False)
        return pixmap.width, pixmap.height, pixmap.samples


def line_art_oracle(kind):
    result = bytearray()
    gamma = 2 if kind == 'gray-icc' else 2.22221
    paper, ink = (18, 19, 20), (244, 235, 216)
    for y in range(HEIGHT):
        for x in range(WIDTH):
            component = 0 if x < 4 else 128 if x == 4 else 255
            linear = (component / 255) ** gamma
            encoded = 12.92 * linear if linear <= .0031308 else 1.055 * linear ** (1 / 2.4) - .055
            # Expansion stores sRGB bytes before the neutral dark palette mapping.
            light = int(encoded * 255 + .5) / 255
            result.extend(int(p * light + i * (1 - light) + .5) for p, i in zip(paper, ink))
    return bytes(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--engine', required=True)
    parser.add_argument('--scratch', required=True)
    args = parser.parse_args()
    repository = pathlib.Path(__file__).resolve().parents[2]
    scratch = pathlib.Path(args.scratch).resolve()
    engine = pathlib.Path(args.engine).resolve(strict=True)
    if scratch.exists() or not scratch.is_relative_to(repository / '.codex-temp') or not engine.is_file():
        raise ValueError('Use an existing engine and a fresh repository-local scratch directory')
    scratch.mkdir(parents=True)
    rows, failures = [], []
    cases = [('gray-icc-lossless', 'gray-icc', False, b'', False, True),
             ('calrgb-lossless', 'calrgb', False, b'', False, True)]
    cases += [(kind + '-jpeg', kind, True, b'', False, True)
              for kind in ('gray-icc', 'calrgb')]
    cases += [('gray-jpeg-whitespace', 'gray-icc', True, b' \r\n\t\f' * 3 + b' ', False, True),
              ('gray-jpeg-too-much-padding', 'gray-icc', True, b' ' * 17, False, False),
              ('gray-jpeg-binary-suffix', 'gray-icc', True, b'\x00', False, False),
              ('gray-jpeg-channel-mismatch', 'gray-icc', True, b'', True, False)]
    cases = [(*case, None) for case in cases]
    cases += [('gray-jpeg-relative-intent', 'gray-icc', True, b'', False, True, 'RelativeColorimetric'),
              ('gray-jpeg-perceptual-intent', 'gray-icc', True, b'', False, False, 'Perceptual'),
              ('gray-jpeg-absolute-intent', 'gray-icc', True, b'', False, False, 'AbsoluteColorimetric')]
    cases = [(*case, None) for case in cases]
    cases += [('gray-jpeg-opaque-gray-matte', 'gray-icc', True, b'', False, True, None, 1),
              ('gray-jpeg-invalid-rgb-matte', 'gray-icc', True, b'', False, False, None, 3)]
    for label, kind, jpeg, suffix, mismatch, accepted, intent, matte_channels in cases:
        source = fixture(kind, jpeg, suffix, mismatch, intent, matte_channels)
        (scratch / (label + '-source.pdf')).write_bytes(source)
        try:
            inspect_code, inspection, empty = invoke(engine, scratch, source, '--inspect')
            code, facts, output = invoke(engine, scratch, source, '--convert')
            if not accepted:
                assert inspect_code in (3, 4) and code in (3, 4) and not empty and not output, (code, facts)
                rows.append({'case': label, 'refused': True})
                print('PASS', label, 'refused')
                continue
            assert inspect_code == code == 0 and not empty and output and facts == inspection, (code, facts)
            before, after = image_object(source), image_object(output)
            assert (after['/Width'], after['/Height']) == (WIDTH, HEIGHT), 'Image dimensions changed'
            if jpeg:
                assert before.get_data() == after.get_data(), 'JPEG encoded bytes changed'
                assert color_definition(before) == color_definition(after), 'JPEG color definition changed'
                assert before.get('/Intent') == after.get('/Intent'), 'JPEG rendering intent changed'
                if matte_channels is not None:
                    before_mask, after_mask = before['/SMask'], after['/SMask']
                    assert before_mask['/Matte'] == after_mask['/Matte'], 'JPEG mask matte changed'
                    assert before_mask.get_data() == after_mask.get_data(), 'JPEG alpha samples changed'
                assert image_pixels(source) == image_pixels(output), 'JPEG rendered pixels changed'
                assert Image.open(io.BytesIO(before.get_data())).tobytes() == Image.open(io.BytesIO(after.get_data())).tobytes()
            else:
                assert after['/ColorSpace'] == '/DeviceRGB', 'Line art not expanded to RGB'
                assert after.get_data() == line_art_oracle(kind), 'Full line-art pixel oracle mismatch'
            (scratch / (label + '-dark.pdf')).write_bytes(output)
            repeat_code, repeat, duplicate = invoke(engine, scratch, output, '--convert')
            assert repeat_code == 0 and not duplicate and repeat['changedPages'] == 0, ('repeat', repeat_code, repeat)
            rows.append({'case': label, 'pixelOraclePassed': True, 'repeatNoOp': True})
            print('PASS', label)
        except AssertionError as error:
            failures.append((label, str(error)))
            rows.append({'case': label, 'failure': str(error)})
            print('FAIL', label, error)
    report = {'engine': str(engine), 'engineSha256': hashlib.sha256(engine.read_bytes()).hexdigest(), 'results': rows}
    (scratch / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    if failures:
        raise AssertionError(f'{len(failures)} calibrated-image checks failed; see report.json')
    print(f'Passed {len(rows)} calibrated-image checks.')


if __name__ == '__main__':
    main()
