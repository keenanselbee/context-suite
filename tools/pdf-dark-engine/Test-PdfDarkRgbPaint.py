"""Exercise bounded RGB ICC paint, gray JPEGs, and packed gray image samples.

All profiles and PDFs are authored here. PyMuPDF independently checks resolved
paint, pixels, text, geometry, and repeat stability; pypdf checks retained bytes.
"""

import argparse
import hashlib
import importlib.util
import io
import json
import pathlib
import struct

import pymupdf
from PIL import Image, ImageCms
from pypdf import PdfReader, PdfWriter
from pypdf.generic import ArrayObject, DecodedStreamObject, NameObject, NumberObject


spec = importlib.util.spec_from_file_location(
    'named_colors', pathlib.Path(__file__).with_name('Test-PdfDarkNamedColors.py'))
named = importlib.util.module_from_spec(spec)
spec.loader.exec_module(named)


def rgb_profile(version=2, curve='gamma', swap=False, defect=None, d65=False, gamma=2):
    def xyz(values):
        return b'XYZ ' + bytes(4) + struct.pack('>iii', *(round(v * 65536) for v in values))

    colorants = [(0.4360747, 0.2225045, 0.0139322),
                 (0.3850649, 0.7168786, 0.0971045),
                 (0.1430804, 0.0606169, 0.7141733)]
    if swap:
        colorants.reverse()
    if defect == 'matrix-bound':
        colorants[0] = (5, .2225045, .0139322)
    elif defect == 'matrix-degenerate':
        colorants = [(.9642 / 3, 1 / 3, .8249 / 3)] * 3
    elif defect == 'legacy-colorants':
        colorants[0] = (.4370747, .2225045, .0139322)
    if curve == 'gamma':
        trc = b'curv' + bytes(4) + struct.pack('>IH', 1, round(gamma * 256))
    elif curve == 'table':
        trc = b'curv' + bytes(4) + struct.pack('>IHHH', 3, 0, 16384, 65535)
    elif curve == 'srgb':
        values = [round((x / 1023 / 12.92 if x / 1023 <= .04045 else
                         ((x / 1023 + .055) / 1.055) ** 2.4) * 65535) for x in range(1024)]
        if defect == 'legacy-trc':
            values[500] += 4
        trc = b'curv' + bytes(4) + struct.pack('>I', len(values)) + struct.pack('>1024H', *values)
    elif curve == 'tight-table':
        trc = b'curv' + bytes(4) + struct.pack('>I', 4096) + struct.pack('>4096H', 0, *([65535] * 4095))
    elif curve == 'para-knee':
        trc = b'para' + bytes(4) + struct.pack('>HHiiiii', 3, 0, 32768, 65536, 0, 0, 1)
    else:
        trc = b'para' + bytes(4) + struct.pack('>HHI', 0, 0, round(gamma * 65536))
    white = (.95047, 1, 1.08883) if d65 else (.9642, 1, .8249)
    if defect == 'legacy-white':
        white = (.95047, 1, 1.09)
    tags = [(b'wtpt', xyz(white)), (b'bkpt', xyz((0, 0, 0)))]
    tags += [(name, xyz(value)) for name, value in zip((b'rXYZ', b'gXYZ', b'bXYZ'), colorants)]
    tags += [(name, trc) for name in (b'rTRC', b'gTRC', b'bTRC')]
    if defect == 'black-point':
        tags[1] = (b'bkpt', xyz((.01, .01, .01)))
    elif defect == 'lut':
        tags += [(b'A2B0', b'mft1' + bytes(16))]
    elif defect == 'missing-trc':
        tags.pop()
    elif defect == 'duplicate':
        tags.append(tags[-1])
    elif defect == 'nonmonotone':
        tags[-1] = (b'bTRC', b'curv' + bytes(4) + struct.pack('>IHHHH', 4, 0, 50000, 10000, 65535))
    elif defect == 'curve-budget':
        tags[-1] = (b'bTRC', b'curv' + bytes(4) + struct.pack('>I', 4097) + bytes(8194))
    elif defect == 'tag-budget':
        tags += [(b'desc', bytes(12))] * 65
    offset = 132 + 12 * len(tags)
    table, payload = bytearray(), bytearray()
    for name, value in tags:
        table += name + struct.pack('>II', offset + len(payload), len(value))
        payload += value + bytes((-len(value)) % 4)
    header = bytearray(128)
    header[8] = version
    header[12:24] = b'mntrRGB XYZ '
    header[36:40] = b'acsp'
    header[68:80] = struct.pack('>iii', 63190, 65536, 54061)
    result = header + struct.pack('>I', len(tags)) + table + payload
    struct.pack_into('>I', result, 0, len(result))
    if defect == 'offset':
        struct.pack_into('>I', result, 136, 0)
    elif defect == 'truncated':
        result = result[:-5]
    return bytes(result)


def serialize(writer):
    result = io.BytesIO()
    writer.write(result)
    return result.getvalue()


def append_tag(profile, name, payload):
    count = struct.unpack_from('>I', profile, 128)[0]
    tags = []
    for i in range(count):
        signature, offset, length = struct.unpack_from('>4sII', profile, 132 + i * 12)
        tags.append((signature, profile[offset:offset + length]))
    tags.append((name, payload))
    offset = 132 + len(tags) * 12
    table, data = bytearray(), bytearray()
    for signature, value in tags:
        table += signature + struct.pack('>II', offset + len(data), len(value))
        data += value + bytes((-len(value)) % 4)
    result = bytearray(profile[:128]) + struct.pack('>I', len(tags)) + table + data
    struct.pack_into('>I', result, 0, len(result))
    return bytes(result)


def paint_fixture(commands, profile, components=3, compressed=True, profile_range=None, alternate=None):
    writer = PdfWriter(clone_from=PdfReader(io.BytesIO(named.fixture(commands, profile=profile))))
    stream = writer.pages[0]['/Resources']['/ColorSpace']['/CalGray'][1].get_object()
    stream[NameObject('/N')] = NumberObject(components)
    stream[NameObject('/Alternate')] = NameObject(alternate or ('/DeviceRGB' if components == 3 else '/DeviceGray'))
    if profile_range is not None:
        stream[NameObject('/Range')] = ArrayObject([NumberObject(value) for value in profile_range])
    if compressed:
        writer.pages[0]['/Resources']['/ColorSpace']['/CalGray'][1] = writer._add_object(stream.flate_encode())
    return serialize(writer)


def image_fixture(kind, defect=None):
    writer = PdfWriter(clone_from=PdfReader(io.BytesIO(named.fixture('q 160 0 0 96 30 20 cm /Im Do Q'))))
    width, height = (19, 17) if kind in ('packed', 'expanded') else (64, 48)
    image = DecodedStreamObject()
    if kind in ('packed', 'expanded'):
        # Non-byte-aligned rows and nonzero padding prove row-stride handling.
        rows = bytearray()
        for y in range(height):
            row = [0xff] * ((width + 7) // 8)
            for x in range(width):
                if x in (4, 5, 6) and 2 <= y < 15:
                    row[x // 8] &= ~(1 << (7 - x % 8))
            rows += bytes(row)
        if kind == 'expanded':
            image.set_data(bytes(0 if x in (4, 5, 6) and 2 <= y < 15 else 255
                                 for y in range(height) for x in range(width)))
            precision = 8
        else:
            image.set_data(bytes(rows[:-1] if defect == 'length' else rows))
            precision = 1
    else:
        pixels = bytes((x * 3 + y * 2) % 256 for y in range(height) for x in range(width))
        buffer = io.BytesIO()
        Image.frombytes('L', (width, height), pixels).save(buffer, format='JPEG', quality=95)
        image.set_data(buffer.getvalue())
        precision = 8
    image.update(named.dictionary(Type=NameObject('/XObject'), Subtype=NameObject('/Image'),
                                  Width=NumberObject(width), Height=NumberObject(height),
                                  BitsPerComponent=NumberObject(precision),
                                  ColorSpace=NameObject('/DeviceGray')))
    if kind == 'jpeg':
        image[NameObject('/Filter')] = NameObject('/DCTDecode')
    else:
        image = image.flate_encode()
        if defect == 'decode':
            image[NameObject('/Decode')] = ArrayObject([NumberObject(1), NumberObject(0)])
    writer.pages[0]['/Resources'][NameObject('/XObject')] = named.dictionary(Im=writer._add_object(image))
    return serialize(writer)


def pixels(data):
    with pymupdf.open(stream=data, filetype='pdf') as document:
        return document[0].get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False).samples


def srgb(linear):
    return 12.92 * linear if linear <= .0031308 else 1.055 * linear ** (1 / 2.4) - .055


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
    results, failures = [], []

    def accept(label, source, reference=None, retained=False):
        before = hashlib.sha256(source).hexdigest()
        code, inspection, empty = named.invoke(engine, scratch, source, '--inspect')
        assert code == 0 and not empty, (code, inspection)
        code, facts, output = named.invoke(engine, scratch, source, '--convert')
        assert code == 0 and output and facts == inspection, (code, facts)
        assert hashlib.sha256(source).hexdigest() == before
        geometry, colors, text = named.appearance(output)
        source_geometry, _, source_text = named.appearance(source)
        assert geometry == source_geometry and text == source_text, 'Source geometry/text changed'
        if reference is not None:
            code, _, direct = named.invoke(engine, scratch, reference, '--convert')
            assert code == 0 and direct
            _, expected_colors, _ = named.appearance(direct)
            for pair, expected in zip(colors, expected_colors):
                for color, expected_color in zip(pair, expected):
                    assert (color is None) == (expected_color is None)
                    if color is not None:
                        assert max(abs(a - b) for a, b in zip(color, expected_color)) < .002, (color, expected_color)
            assert max(abs(a - b) for a, b in zip(pixels(output), pixels(direct))) <= 2, 'Reference pixels'
        if retained:
            first = PdfReader(io.BytesIO(source)).pages[0]['/Resources']['/XObject']['/Im']
            second = PdfReader(io.BytesIO(output)).pages[0]['/Resources']['/XObject']['/Im']
            assert first._data == second._data, 'Retained JPEG bytes changed'
            with pymupdf.open(stream=source, filetype='pdf') as a, pymupdf.open(stream=output, filetype='pdf') as b:
                clip = pymupdf.Rect(30, 64, 190, 160)
                assert a[0].get_pixmap(clip=clip).samples == b[0].get_pixmap(clip=clip).samples, 'Retained image pixels changed'
        code, repeat_facts, repeat = named.invoke(engine, scratch, output, '--convert')
        assert code == 0 and not repeat and repeat_facts['changedPages'] == 0, (code, repeat_facts)
        (scratch / (label + '-dark.pdf')).write_bytes(output)

    def refuse(label, source):
        for mode in ('--inspect', '--convert'):
            code, diagnostic, output = named.invoke(engine, scratch, source, mode)
            assert code in (3, 4, 5) and not output, (mode, code, diagnostic)

    def check(label, source, **options):
        (scratch / (label + '-source.pdf')).write_bytes(source)
        try:
            (refuse if options.pop('negative', False) else accept)(label, source, **options)
            results.append({'case': label, 'passed': True})
            print('PASS', label)
        except AssertionError as error:
            failures.append(label)
            results.append({'case': label, 'passed': False, 'detail': str(error)})
            print('FAIL', label, error)

    shape = '30 40 60 50 re f'
    for version in (2, 4):
        for curve in ('gamma', 'table', 'para'):
            for swap in (False, True):
                values = [.23123, .41234, .67891]
                linear = [v * v if curve != 'table' else
                          (v * 2 * (16384 / 65535) if v < .5 else
                           16384 / 65535 + (v - .5) * 2 * (1 - 16384 / 65535)) for v in values]
                direct = ' '.join(f'{srgb(v):.8f}' for v in (linear[::-1] if swap else linear))
                check(f'rgb-v{version}-{curve}-swap{int(swap)}',
                      paint_fixture('/CalGray cs .23123 .41234 .67891 scn ' + shape,
                                    rgb_profile(version, curve, swap)),
                      reference=named.fixture(direct + ' rg ' + shape))
    check('rgb-stroke-uncompressed', paint_fixture('/CalGray CS .3 .5 .7 SC 3 w 30 40 60 50 re S',
          rgb_profile(), compressed=False), reference=named.fixture(
          ' '.join(f'{srgb(v*v):.8f}' for v in (.3, .5, .7)) + ' RG 3 w 30 40 60 50 re S'))
    # Fractional paint operands must evaluate the parsed TRC itself. An 8-bit
    # image lookup interpolated near zero can lose over 200 channel levels.
    fractional = [('sqrt-gamma', 'gamma', .5, (.0001, .01, .4), lambda x: x ** .5),
                  ('minimum-gamma', 'gamma', 1 / 256, (0, .0001, .01), lambda x: x ** (1 / 256)),
                  ('sqrt-parametric', 'para', .5, (.0001, .01, .4), lambda x: x ** .5),
                  ('tight-table', 'tight-table', 2, (.0001, 0, .004), lambda x: min(1, x * 4095)),
                  ('parametric-knee', 'para-knee', 2, (.0001, .01, .4),
                   lambda x: x ** .5 if x >= 1 / 65536 else 0)]
    for label, curve, gamma, values, linearize in fractional:
        operands = ' '.join(f'{value:.8f}' for value in values)
        direct = ' '.join(f'{srgb(linearize(value)):.8f}' for value in values)
        check('rgb-fractional-' + label, paint_fixture('/CalGray cs ' + operands + ' scn ' + shape,
              rgb_profile(curve=curve, gamma=gamma)), reference=named.fixture(direct + ' rg ' + shape))
    for defect in ('black-point', 'lut', 'missing-trc', 'duplicate', 'nonmonotone',
                   'curve-budget', 'tag-budget', 'offset', 'truncated', 'matrix-bound', 'matrix-degenerate'):
        check('rgb-' + defect, paint_fixture('/CalGray cs .2 .4 .6 sc ' + shape,
              rgb_profile(defect=defect)), negative=True)
    check('rgb-wrong-components', paint_fixture('/CalGray cs .5 sc ' + shape,
          rgb_profile(), components=1), negative=True)
    check('rgb-wrong-operands', paint_fixture('/CalGray cs .5 sc ' + shape,
          rgb_profile()), negative=True)
    commands = '/CalGray cs .3 .5 .7 sc ' + shape
    reference = named.fixture(' '.join(f'{srgb(v*v):.8f}' for v in (.3, .5, .7)) + ' rg ' + shape)
    check('rgb-identity-range', paint_fixture(commands, rgb_profile(), profile_range=[0, 1] * 3),
          reference=reference)
    check('rgb-nonidentity-range', paint_fixture(commands, rgb_profile(), profile_range=[0, 2] * 3), negative=True)
    check('rgb-range-length', paint_fixture(commands, rgb_profile(), profile_range=[0, 1]), negative=True)
    check('rgb-wrong-alternate', paint_fixture(commands, rgb_profile(), alternate='/DeviceCMYK'), negative=True)
    check('rgb-version-3', paint_fixture(commands, rgb_profile(version=3)), negative=True)
    legacy = rgb_profile(curve='srgb', d65=True)
    samples = bytes(value for x in range(256) for value in (x, (x * 47) % 256, (x * 91) % 256))
    source_image = Image.frombytes('RGB', (256, 1), samples)
    cms_image = ImageCms.profileToProfile(source_image, ImageCms.ImageCmsProfile(io.BytesIO(legacy)),
                                         ImageCms.createProfile('sRGB'), renderingIntent=1, outputMode='RGB')
    oracle = cms_image.tobytes()
    assert max(abs(a - b) for a, b in zip(samples, oracle)) <= 1, 'Authored legacy profile/LittleCMS disagreement'
    named_commands, direct_commands = [], []
    for i, pixel in enumerate((1, 12, 31, 49, 72, 95, 120, 143, 167, 191, 215, 249)):
        rectangle = f'{20 + i % 4 * 50} {20 + i // 4 * 38} 40 28 re f'
        named_commands.append('/CalGray cs ' + ' '.join(f'{v / 255:.8f}' for v in samples[pixel * 3:pixel * 3 + 3]) +
                              ' scn ' + rectangle)
        direct_commands.append(' '.join(f'{v / 255:.8f}' for v in oracle[pixel * 3:pixel * 3 + 3]) + ' rg ' + rectangle)
    check('rgb-legacy-srgb-littlecms', paint_fixture(' '.join(named_commands), legacy),
          reference=named.fixture(' '.join(direct_commands)))
    for defect in ('legacy-colorants', 'legacy-trc', 'legacy-white'):
        check('rgb-' + defect, paint_fixture(commands, rgb_profile(curve='srgb', d65=True, defect=defect)), negative=True)
    check('rgb-legacy-v4', paint_fixture(commands, rgb_profile(version=4, curve='srgb', d65=True)), negative=True)
    check('rgb-legacy-gamma', paint_fixture(commands, rgb_profile(curve='gamma', d65=True)), negative=True)
    check('rgb-legacy-swapped-primaries', paint_fixture(commands, rgb_profile(curve='srgb', d65=True, swap=True)), negative=True)
    for value in (0, 655):
        gray = append_tag(named.gray_profile(), b'bkpt',
                          b'XYZ ' + bytes(4) + struct.pack('>iii', value, value, value))
        check('gray-black-point-' + str(value), paint_fixture('/CalGray cs .5 sc ' + shape,
              gray, components=1), **({'negative': True} if value else
              {'reference': named.fixture(f'{srgb(.25):.8f} g ' + shape)}))
    check('rgb-passive-description', paint_fixture('/CalGray cs .3 .5 .7 sc ' + shape,
          append_tag(rgb_profile(), b'dmnd', b'desc' + bytes(12))), reference=named.fixture(
          ' '.join(f'{srgb(v*v):.8f}' for v in (.3, .5, .7)) + ' rg ' + shape))
    check('gray-jpeg-retained', image_fixture('jpeg'), retained=True)
    check('packed-gray-stride', image_fixture('packed'), reference=image_fixture('expanded'))
    check('packed-gray-length', image_fixture('packed', 'length'), negative=True)
    check('packed-gray-inverted-decode', image_fixture('packed', 'decode'), negative=True)
    (scratch / 'results.json').write_text(json.dumps({'engineSha256': hashlib.sha256(engine.read_bytes()).hexdigest(),
        'checks': results, 'failures': failures}, indent=2), encoding='utf-8')
    print(f'{len(results) - len(failures)}/{len(results)} checks passed; authored fixtures and evidence retained at {scratch}')
    if failures:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
