"""Independently exercise named PDF colors, bounded gray ICC, and overprint.

Fixtures are authored here with reportlab/pypdf; PyMuPDF verifies retained text,
geometry, and resolved output colors against equivalent direct-color commands.
"""

import argparse
import hashlib
import io
import json
import pathlib
import struct
import subprocess
import tempfile

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, BooleanObject, DecodedStreamObject,
                           DictionaryObject, FloatObject, NameObject, NumberObject)
from reportlab.pdfgen import canvas


def dictionary(**values):
    return DictionaryObject({NameObject('/' + key): value for key, value in values.items()})


def gray_profile(version=2, samples=None, defect=None, d65=False):
    # A minimal authored monitor profile: D50 PCS white and one gray TRC.
    white_values = (62290, 65536, 71358) if d65 else (63190, 65536, 54061)
    white = b'XYZ ' + bytes(4) + struct.pack('>iii', *white_values)
    if samples is None:
        curve = b'curv' + bytes(4) + struct.pack('>IH', 1, 512)
    else:
        curve = b'curv' + bytes(4) + struct.pack('>I', len(samples))
        curve += struct.pack('>' + 'H' * len(samples), *samples)
    tags = [(b'wtpt', white), (b'kTRC', curve)]
    if defect == 'missing-trc':
        tags.pop()
    offset = 132 + 12 * len(tags)
    table, payload = bytearray(), bytearray()
    for signature, value in tags:
        table += signature + struct.pack('>II', offset + len(payload), len(value))
        payload += value + bytes((-len(value)) % 4)
    header = bytearray(128)
    header[8] = version
    header[12:24] = b'mntrGRAYXYZ '
    header[36:40] = b'acsp'
    header[68:80] = struct.pack('>iii', 63190, 65536, 54061)
    profile = header + struct.pack('>I', len(tags)) + table + payload
    struct.pack_into('>I', profile, 0, len(profile))
    if defect == 'signature':
        profile[36:40] = b'bad!'
    elif defect == 'non-d50':
        struct.pack_into('>i', profile, offset + 8, 32768)
    elif defect == 'truncated':
        profile = profile[:-3]
    return bytes(profile)


def fixture(commands, profile=None, state=None, pattern=False, calibrated=False,
            bad_matrix=False, image_obstacle=False, second_state=None, default_space=None,
            compressed_profile=False):
    base = io.BytesIO()
    document = canvas.Canvas(base, pagesize=(240, 180), invariant=1)
    document.setFillColorRGB(1, 1, 1)
    document.rect(0, 0, 240, 180, fill=1, stroke=0)
    document.setFillColorRGB(0, 0, 0)
    document.drawString(20, 150, 'NAMED COLOR REGRESSION')
    document.save()
    writer = PdfWriter(clone_from=PdfReader(io.BytesIO(base.getvalue())))
    page = writer.pages[0]
    resources = page['/Resources']
    colors = dictionary(Gray=NameObject('/DeviceGray'), RGB=NameObject('/DeviceRGB'),
                        CMYK=NameObject('/DeviceCMYK'))
    if default_space is not None:
        colors[NameObject('/Default' + default_space)] = NameObject('/Device' + default_space)
    if profile is not None:
        stream = DecodedStreamObject()
        stream.set_data(profile)
        stream.update(dictionary(N=NumberObject(1), Alternate=NameObject('/DeviceGray')))
        if compressed_profile:
            stream = stream.flate_encode()
        colors[NameObject('/CalGray')] = ArrayObject([
            NameObject('/ICCBased'), writer._add_object(stream)])
    if pattern:
        colors[NameObject('/PatternColor')] = NameObject('/Pattern')
    if calibrated:
        matrix = [.4124, .2126, .0193, .3576, .7152, .1192, .1805, .0722, .9505]
        if bad_matrix:
            matrix[0] = .5
        colors[NameObject('/CalRGB')] = ArrayObject([NameObject('/CalRGB'), dictionary(
            WhitePoint=ArrayObject([FloatObject(v) for v in (.9505, 1, 1.089)]),
            Matrix=ArrayObject([FloatObject(v) for v in matrix]),
            Gamma=ArrayObject([FloatObject(2.22221) for _ in range(3)]))])
    resources[NameObject('/ColorSpace')] = colors
    if state is not None:
        resources[NameObject('/ExtGState')] = dictionary(GS=dictionary(**state))
    if second_state is not None:
        resources[NameObject('/ExtGState')][NameObject('/GS2')] = dictionary(**second_state)
    if image_obstacle:
        image = DecodedStreamObject()
        image.set_data(bytes(value for y in range(16) for x in range(16)
                             for value in (80 + x * 5, 90 + y * 5, 150)))
        image.update(dictionary(Type=NameObject('/XObject'), Subtype=NameObject('/Image'),
                                Width=NumberObject(16), Height=NumberObject(16),
                                BitsPerComponent=NumberObject(8), ColorSpace=NameObject('/DeviceRGB')))
        resources[NameObject('/XObject')] = dictionary(Im=writer._add_object(image.flate_encode()))
    contents = DecodedStreamObject()
    contents.set_data(page.get_contents().get_data() + b'\n' + commands.encode('ascii') + b'\n')
    page[NameObject('/Contents')] = writer._add_object(contents)
    result = io.BytesIO()
    writer.write(result)
    return result.getvalue()


def invoke(engine, scratch, data, mode):
    with tempfile.TemporaryFile(dir=scratch) as source:
        source.write(data)
        source.seek(0)
        result = subprocess.run([str(engine), mode], stdin=source,
                                capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout, 'Refusal published bytes'
        return result.returncode, result.stderr.decode(errors='replace'), b''
    assert len(result.stdout) >= 16, 'Short response'
    magic, version, metadata_size, pdf_size = struct.unpack_from('<IIII', result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_size + pdf_size
    return (0, json.loads(result.stdout[16:16 + metadata_size]),
            result.stdout[16 + metadata_size:])


def appearance(data):
    with pymupdf.open(stream=data, filetype='pdf') as document:
        page = document[0]
        geometry = [(drawing['type'], tuple(drawing['rect']))
                    for drawing in page.get_drawings()]
        colors = [(drawing['fill'], drawing['color']) for drawing in page.get_drawings()]
        text = [(span['bbox'], tuple(char[0] for char in span['chars']))
                for span in page.get_texttrace()]
        return geometry, colors, text


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
    rows = []
    failures = []

    def positive(label, commands, direct, **options):
        source = fixture(commands, **options)
        (scratch / (label + '-source.pdf')).write_bytes(source)
        code, inspection, empty = invoke(engine, scratch, source, '--inspect')
        assert code == 0 and not empty, (label, code, inspection)
        code, facts, output = invoke(engine, scratch, source, '--convert')
        assert code == 0 and output and facts == inspection, (label, code, facts)
        reference_code, _, reference = invoke(engine, scratch, fixture(direct), '--convert')
        assert reference_code == 0 and reference, label
        if label.startswith('graphics-style-'):
            with pymupdf.open(stream=output, filetype='pdf') as actual_doc, \
                    pymupdf.open(stream=reference, filetype='pdf') as expected_doc:
                assert actual_doc[0].get_pixmap().samples == expected_doc[0].get_pixmap().samples, label
        geometry, colors, text = appearance(output)
        reference_geometry, reference_colors, reference_text = appearance(reference)
        assert geometry == reference_geometry and text == reference_text, (label, 'geometry/text')
        assert len(colors) == len(reference_colors), label
        for pair, expected in zip(colors, reference_colors):
            for color, expected_color in zip(pair, expected):
                assert (color is None) == (expected_color is None), label
                if color is not None:
                    assert max(abs(a - b) for a, b in zip(color, expected_color)) < .012, (label, color, expected_color)
        (scratch / (label + '-dark.pdf')).write_bytes(output)
        rows.append({'case': label, 'accepted': True})
        print('PASS', label)

    def refuse(label, commands, expected_detail=None, **options):
        source = fixture(commands, **options)
        (scratch / (label + '-source.pdf')).write_bytes(source)
        for mode in ('--inspect', '--convert'):
            code, diagnostic, output = invoke(engine, scratch, source, mode)
            # Unsupported features and malformed input have distinct failure codes.
            assert code in (3, 4, 5) and not output, (label, mode, code, diagnostic)
            if expected_detail is not None:
                assert expected_detail in diagnostic, (label, mode, code, diagnostic)
        rows.append({'case': label, 'accepted': False})
        print('PASS', label, 'refused')

    def collect(check):
        def run(label, *arguments, **options):
            try:
                check(label, *arguments, **options)
            except AssertionError as error:
                failures.append((label, str(error)))
                rows.append({'case': label, 'failure': str(error)})
                print('FAIL', label, error)
        return run

    positive = collect(positive)
    refuse = collect(refuse)
    fill = '30 40 60 50 re f'
    stroke = '3 w 110 40 60 50 re S'
    for alias, components, direct in (('Gray', '.4', '.4 g'),
                                      ('RGB', '.2 .4 .6', '.2 .4 .6 rg'),
                                      ('CMYK', '.2 .3 .4 .1', '.2 .3 .4 .1 k')):
        for operator in ('sc', 'scn'):
            positive(f'{alias}-{operator}', f'/{alias} cs {components} {operator} {fill}', f'{direct} {fill}')
            positive(f'{alias}-{operator.upper()}', f'/{alias} CS {components} {operator.upper()} {stroke}',
                     f'{direct.upper()} {stroke}')
    positive('state-restoration', f'/Gray cs .4 sc q /RGB cs 1 0 0 sc Q .6 sc {fill}', f'.6 g {fill}')
    positive('stroke-state-restoration', f'/Gray CS .4 SC q /RGB CS 1 0 0 SC Q .6 SCN {stroke}', f'.6 G {stroke}')
    positive('default-black', f'1 0 0 rg /RGB cs {fill}', f'0 g {fill}')
    positive('stroke-default-black', f'1 0 0 RG /RGB CS {stroke}', f'0 G {stroke}')
    for direct, numeric in (('.3 g', '.6'), ('.1 .2 .3 rg', '.2 .4 .6'), ('0 0 0 .2 k', '0 0 0 .5')):
        positive('direct-reset-' + direct.split()[-1], f'/RGB cs {direct} {numeric} sc {fill}',
                 f'{numeric} ' + direct.split()[-1] + ' ' + fill)
    for version in (2, 4):
        for kind, samples, linear in (('gamma', None, .25), ('table', (0, 16384, 65535), 16384 / 65535)):
            srgb = 1.055 * linear ** (1 / 2.4) - .055
            positive(f'icc-v{version}-{kind}', f'/CalGray cs .5 scn {fill}',
                     f'{srgb:.8f} g {fill}', profile=gray_profile(version, samples))
    positive('icc-v2-d65', f'/CalGray cs .5 scn {fill}', '.53709873 g ' + fill,
             profile=gray_profile(d65=True))
    refuse('icc-v4-d65', f'/CalGray cs .5 scn {fill}', profile=gray_profile(version=4, d65=True))
    calibrated_values = []
    for component in (.2, .4, .6):
        linear = component ** 2.22221
        calibrated_values.append(12.92 * linear if linear <= .0031308 else 1.055 * linear ** (1 / 2.4) - .055)
    calibrated_direct = ' '.join(f'{value:.8f}' for value in calibrated_values) + ' rg ' + fill
    positive('calrgb', '/CalRGB cs .2 .4 .6 scn ' + fill, calibrated_direct, calibrated=True)
    positive('calrgb-overprint', '/GS gs /CalRGB cs .2 .4 .6 scn ' + fill,
             calibrated_direct, calibrated=True, state={'op': BooleanObject(True)})
    refuse('calrgb-matrix', '/CalRGB cs .2 .4 .6 sc ' + fill, calibrated=True, bad_matrix=True)
    for value in (0, .5, 1):
        positive(f'smoothness-{value}', f'/GS gs .4 g {fill}', f'.4 g {fill}', state={'SM': FloatObject(value)})
    positive('rgb-overprint', f'/GS gs /RGB cs .2 .4 .6 sc {fill}', f'.2 .4 .6 rg {fill}',
             state={'OP': BooleanObject(True)})
    positive('icc-overprint', f'/GS gs /CalGray cs .5 sc {fill}', f'.53709873 g {fill}',
             profile=gray_profile(), state={'OP': BooleanObject(True)})
    positive('explicit-fill-overprint-off', f'/GS gs .4 g {fill}', f'.4 g {fill}',
             state={'OP': BooleanObject(True), 'op': BooleanObject(False)})
    for space, operands in (('Gray', '.4'), ('CMYK', '0 0 0 .4')):
        refuse('overprint-' + space, f'/GS gs /{space} cs {operands} sc {fill}', state={'OP': BooleanObject(True)})
    refuse('overprint-direct-gray-reset', f'/GS gs /RGB cs .2 .4 .6 sc .4 g {fill}',
           state={'op': BooleanObject(True)})
    refuse('overprint-stroke', f'/GS gs /CMYK CS 0 0 0 .4 SC {stroke}',
           state={'OP': BooleanObject(True), 'op': BooleanObject(False)})
    clip = '20 20 100 60 re W n '
    outside_text = 'BT /F1 12 Tf 30 120 Td (CLIPPED) Tj ET'
    positive('overprint-fully-clipped-text', clip + '/GS gs .4 g ' + outside_text,
             clip + '.4 g ' + outside_text, state={'op': BooleanObject(True)})
    plain_text = 'BT /F1 12 Tf 30 45 Td (GRAY ON PAPER) Tj ET'
    positive('overprint-pristine-paper-text', '/GS gs .4 g ' + plain_text,
             '.4 g ' + plain_text, state={'op': BooleanObject(True)})
    repeated_text = ' '.join(f'BT /F1 18 Tf {30 + dx} {45 + dy} Td (HEADING) Tj ET'
                             for dx, dy in ((0, 0), (.12, 0), (0, .12), (.12, .12)))
    positive('overprint-repeated-black-heading', '/GS gs 0 g ' + repeated_text,
             '0 g ' + repeated_text, state={'op': BooleanObject(True)})
    first_text = 'BT /F1 18 Tf 30 45 Td (HEADING) Tj ET '
    refuse('overprint-black-on-gray-text', '.4 g ' + first_text + '/GS gs 0 g ' + first_text,
           state={'op': BooleanObject(True)})
    refuse('overprint-black-on-translucent-text',
           'q /GS gs 0 g ' + first_text + 'Q /GS2 gs /Gray cs 0 sc ' + first_text,
           state={'ca': FloatObject(.5)}, second_state={'op': BooleanObject(True)})
    colored_backing = '1 0 0 rg 20 20 120 70 re f '
    refuse('overprint-text-colored-backing', colored_backing + '/GS gs .4 g ' + plain_text,
           state={'op': BooleanObject(True)})
    refuse('overprint-text-image-backing', 'q 120 0 0 70 20 20 cm /Im Do Q /GS gs .4 g ' + plain_text,
           state={'op': BooleanObject(True)}, image_obstacle=True)
    style_stroke = '110 40 60 50 re S'
    for cap in range(3):
        for join in range(3):
            positive(f'graphics-style-{cap}-{join}', '/GS gs .4 G ' + style_stroke,
                     f'3 w 7 M {cap} J {join} j .4 G ' + style_stroke,
                     state={'LW': NumberObject(3), 'ML': NumberObject(7),
                            'LC': NumberObject(cap), 'LJ': NumberObject(join)})
    positive('graphics-style-restore', 'q /GS gs Q .4 G ' + style_stroke, '.4 G ' + style_stroke,
             state={'LW': NumberObject(9), 'ML': NumberObject(2)})
    for key, bad in [('LW', NumberObject(-1)), ('LW', NumberObject(10001)),
                     ('ML', NumberObject(0)), ('ML', NumberObject(10001)),
                     ('LC', NumberObject(3)), ('LJ', FloatObject(.5))]:
        refuse('invalid-style-' + key + '-' + str(bad), '/GS gs .4 G ' + stroke, state={key: bad})
    for label, baseline, mode in (('inside', 45, 0), ('partial', 75, 0), ('stroke', 120, 2)):
        refuse('overprint-clipped-text-' + label,
               colored_backing + clip + f'/GS gs .4 g .4 G BT /F1 12 Tf {mode} Tr 30 {baseline} Td (CLIPPED) Tj ET',
               state={'OP': BooleanObject(True)})
    for label, commands in (
        ('unknown', f'/Missing cs .4 sc {fill}'),
        ('wrong-count', f'/RGB cs .4 sc {fill}'),
        ('extra-count', f'/Gray cs .4 .5 scn {fill}'),
        ('wrong-stroke-count', f'/RGB CS .4 SC {stroke}'),
        ('named-scn', f'/RGB cs .2 .4 .6 /Spot scn {fill}'),
        ('pattern', f'/PatternColor cs /P1 scn {fill}'),
    ):
        refuse(label, commands, pattern=label == 'pattern')
    for space in ('Gray', 'RGB', 'CMYK'):
        refuse('default-substitution-' + space, '.4 g ' + fill, default_space=space)
    for defect in ('signature', 'non-d50', 'truncated', 'missing-trc'):
        refuse('icc-' + defect, f'/CalGray cs .5 sc {fill}', profile=gray_profile(defect=defect))
    oversized_profile = bytearray(gray_profile())
    oversized_profile.extend(bytes(4 * 1024 * 1024 + 1 - len(oversized_profile)))
    struct.pack_into('>I', oversized_profile, 0, len(oversized_profile))
    oversized_options = {'profile': bytes(oversized_profile), 'compressed_profile': True}
    assert len(fixture(f'/CalGray cs .5 sc {fill}', **oversized_options)) < 32768
    refuse('icc-compressed-decoded-limit', f'/CalGray cs .5 sc {fill}',
           expected_detail='size limit', **oversized_options)
    for label, samples in (('nonmonotonic', (0, 40000, 30000, 65535)),
                           ('bad-start', (1, 32768, 65535)), ('bad-end', (0, 32768, 65000))):
        refuse('icc-' + label, f'/CalGray cs .5 sc {fill}', profile=gray_profile(samples=samples))
    for label, value in (('negative', FloatObject(-.1)), ('large', FloatObject(1.1)), ('name', NameObject('/Bad'))):
        refuse('smoothness-' + label, f'/GS gs .4 g {fill}', state={'SM': value})
    report = {'engine': str(engine), 'engineSha256': hashlib.sha256(engine.read_bytes()).hexdigest(), 'results': rows}
    (scratch / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    if failures:
        raise AssertionError(f'{len(failures)} named-color checks failed; see report.json')
    print(f'Passed {len(rows)} named-color checks.')


if __name__ == '__main__':
    main()
