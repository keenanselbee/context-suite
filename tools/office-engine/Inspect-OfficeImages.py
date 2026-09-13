"""Inspect only retained, authored EmbeddedImages evidence; never launch Office."""
import base64
import hashlib
import io
import json
import pathlib
import struct
import sys
import zipfile
import zlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
PARENT = ROOT / '.codex-temp/office-engine'


def read(path, limit=16 * 1024 * 1024):
    if not path.is_relative_to(PARENT) or not path.resolve().is_relative_to(PARENT.resolve()):
        raise ValueError('Use retained repository Office evaluation evidence')
    for ancestor in (path, *path.parents):
        if ancestor == ROOT:
            break
        if ancestor.is_symlink() or ancestor.is_junction():
            raise ValueError('Linked evidence path')
    if path.stat().st_size > limit:
        raise ValueError('Evidence exceeds the inspection budget')
    return path.read_bytes()


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def authored_pixels(data):
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('Expected authored PNG')
    position = 8
    compressed = b''
    header = None
    while position < len(data):
        length = int.from_bytes(data[position:position + 4], 'big')
        kind = data[position + 4:position + 8]
        payload = data[position + 8:position + 8 + length]
        crc = int.from_bytes(data[position + 8 + length:position + 12 + length], 'big')
        if zlib.crc32(kind + payload) != crc:
            raise ValueError('PNG CRC mismatch')
        if kind == b'IHDR':
            header = struct.unpack('>IIBBBBB', payload)
        elif kind == b'IDAT':
            compressed += payload
        elif kind != b'IEND':
            raise ValueError('Unexpected authored PNG chunk')
        position += length + 12
    if header != (1024, 1024, 8, 6, 0, 0, 0) or position != len(data):
        raise ValueError('Unexpected source image geometry or encoding')
    decoder = zlib.decompressobj()
    raw = decoder.decompress(compressed, 1024 * 4097 + 1)
    if not decoder.eof or decoder.unused_data or len(raw) != 1024 * 4097 or any(raw[row * 4097] for row in range(1024)):
        raise ValueError('Unexpected PNG inflation or row filters')
    rgba = b''.join(raw[row * 4097 + 1:(row + 1) * 4097] for row in range(1024))
    # Independent geometric oracle, not merely copying the authoring algorithm.
    patches = {(0, 0): bytes((255, 0, 0, 255)), (1, 0): bytes((0, 255, 0, 128)),
               (0, 1): bytes((255, 0, 255, 0)), (1, 1): bytes((0, 0, 255, 64))}
    for (column, row), pixel in patches.items():
        for y in range(row * 512, (row + 1) * 512):
            if rgba[y * 4096 + column * 2048:y * 4096 + (column + 1) * 2048] != pixel * 512:
                raise ValueError('Authored source does not match its quadrant oracle')
    return rgba


def image_samples(objects):
    images = [(key, item['stream']) for key, item in objects.items()
              if item.get('stream', {}).get('dict', {}).get('/Subtype') == '/Image']
    color = [(key, stream) for key, stream in images if stream['dict'].get('/ColorSpace') == '/DeviceRGB']
    if len(color) != 1:
        raise ValueError('Expected one explicit DeviceRGB image in authored output')
    key, image = color[0]
    info = image['dict']
    mask_ref = info.get('/SMask')
    if not isinstance(mask_ref, str) or 'obj:' + mask_ref not in objects:
        raise ValueError('Required alpha mask missing')
    mask = objects['obj:' + mask_ref]['stream']
    for stream in (image, mask):
        if stream['dict'].get('/BitsPerComponent') != 8 or any(field in stream['dict'] for field in ('/Filter', '/DecodeParms')):
            raise ValueError('Expected fully decoded eight-bit image samples')
    if info.get('/Decode', [0, 1, 0, 1, 0, 1]) != [0, 1, 0, 1, 0, 1] or mask['dict'].get('/Decode', [0, 1]) not in ([0, 1], [1, 0]):
        raise ValueError('Unsupported image or soft-mask sample mapping')
    if mask['dict'].get('/ColorSpace') != '/DeviceGray' or len(images) != 2:
        raise ValueError('Unexpected image/mask inventory')
    if any(field in stream['dict'] for stream in (image, mask) for field in ('/Matte', '/Mask', '/ImageMask', '/SMaskInData')):
        raise ValueError('Unexpected additional transparency semantics')
    width, height = info['/Width'], info['/Height']
    if not (0 < width <= 1024 and 0 < height <= 1024) or any(mask['dict'][field] != info[field] for field in ('/Width', '/Height')):
        raise ValueError('Image and mask extents disagree')
    rgb, alpha = (base64.b64decode(stream['data'], validate=True) for stream in (image, mask))
    if mask['dict'].get('/Decode') == [1, 0]:
        alpha = alpha.translate(bytes(range(255, -1, -1)))
    if len(rgb) != width * height * 3 or len(alpha) != width * height:
        raise ValueError('Decoded image extent differs from its dictionary')
    return key, width, height, rgb, alpha


def matches(rgba, width, height, rgb, alpha):
    if (width, height) != (1024, 1024):
        return False
    return alpha == rgba[3::4] and all(
        rgb[index * 3:index * 3 + 3] == rgba[index * 4:index * 4 + 3]
        for index, value in enumerate(alpha) if value != 0)


def rendered_patches(folder, render, page_number):
    page = render['pages'][page_number - 1]
    width, height, stride = (page[key] for key in ('width', 'height', 'stride'))
    if not (0 < width <= 4096 and 0 < height <= 4096 and width * 4 <= stride <= width * 4 + 16):
        raise ValueError('Unexpected render geometry')
    data = read(folder / f'page-{page_number}.bgra', 32 * 1024 * 1024)
    if len(data) != height * stride:
        raise ValueError('Render length disagrees with geometry')
    # These colors appear nowhere in the base documents. Count solid interiors,
    # allowing one byte of PDFium's alpha rounding. Do not infer screen-reader access.
    colors = {'red': (0, 0, 255), 'half_green': (127, 255, 127), 'quarter_blue': (255, 191, 191)}
    points = {name: [] for name in colors}
    for y in range(height):
        for x in range(width):
            pixel = data[y * stride + x * 4:y * stride + x * 4 + 4]
            for name, expected in colors.items():
                if pixel[3] == 255 and all(abs(pixel[i] - expected[i]) <= 1 for i in range(3)):
                    points[name].append((x, y))
    if any(len(value) < 100 for value in points.values()):
        raise ValueError('Rendered opaque and translucent patch interiors are missing')
    centers = {name: (sum(x for x, _ in value) / len(value), sum(y for _, y in value) / len(value)) for name, value in points.items()}
    if not (centers['red'][0] < centers['half_green'][0] and centers['half_green'][1] < centers['quarter_blue'][1]):
        raise ValueError('Rendered quadrant order changed')
    return {'Page': page_number, 'RenderSha256': sha(data), 'Pixels': {name: len(value) for name, value in points.items()}, 'Centers': centers}


def main():
    root = pathlib.Path(sys.argv[1]).absolute()
    record = json.loads(read(root / 'office-evaluation.json'))
    if record['Mode'] != 'EmbeddedImages' or len(record['Results']) != 6:
        raise ValueError('Expected six completed EmbeddedImages exports')
    source_png = read(root / 'fixtures/authored-rgba.png')
    rgba = authored_pixels(source_png)
    reports = []
    for family, extension, pages, image_page in [('Word', 'docx', 2, 2), ('Excel', 'xlsx', 1, 1), ('PowerPoint', 'pptx', 2, 1)]:
        packages = []
        settings = []
        for variant in ('retained', 'reduced'):
            name = f'{family} image {variant}'
            item = next(row for row in record['Results'] if row['Source'] == name + '.' + extension)
            folder = root / name
            options = dict(item['ExportOptions'])
            if options['UseLosslessCompression'] != {'type': 'boolean', 'value': 'true'} or options['ReduceImageResolution'] != {'type': 'boolean', 'value': 'true' if variant == 'reduced' else 'false'}:
                raise ValueError('Unexpected image export settings')
            options.pop('ReduceImageResolution')
            if variant == 'reduced' and options.pop('MaxImageResolution') != {'type': 'long', 'value': '150'}:
                raise ValueError('Reduction control lacks its explicit resolution')
            settings.append(options)
            source = read(root / 'fixtures' / item['Source'])
            pdf = read(folder / (name + '.pdf'))
            if not item['Completed'] or item['Pages'] != pages or sha(source) != item['SourceSha256'] or sha(pdf) != item['PdfSha256']:
                raise ValueError('Source/PDF identity or completion changed')
            with zipfile.ZipFile(io.BytesIO(source)) as archive:
                if len(archive.infolist()) > 64 or sum(part.file_size for part in archive.infolist()) > 2 * 1024 * 1024:
                    raise ValueError('Authored package exceeds its bound')
                parts = {part: archive.read(part) for part in archive.namelist()}
                if [value for part, value in parts.items() if part.endswith('/media/authored.png')] != [source_png]:
                    raise ValueError('Embedded image differs from authored PNG')
                packages.append(parts)
            conversion = json.loads(read(folder / 'conversion.json'))
            if conversion['JobActiveProcessesAfterCleanup'] != 0:
                raise ValueError('Office conversion retained a job member')
            objects = json.loads(read(folder / 'pdf-image-objects.json'))['qpdf'][1]
            key, width, height, rgb, alpha = image_samples(objects)
            equal = matches(rgba, width, height, rgb, alpha)
            if variant == 'retained':
                if not equal:
                    raise ValueError('Retained image lost resolution, visible color or alpha')
                damaged = bytes([rgb[0] ^ 1]) + rgb[1:]
                if matches(rgba, width, height, damaged, alpha) or matches(rgba, width, height, rgb, bytes([alpha[0] ^ 1]) + alpha[1:]):
                    raise ValueError('Negative color/alpha control escaped comparison')
            elif equal or not (0 < width < 1024 and 0 < height < 1024):
                raise ValueError('Explicit reduction control did not reduce image dimensions')
            reports.append({'Family': family, 'Variant': variant, 'SourceSha256': sha(source), 'PdfSha256': sha(pdf),
                            'Object': key, 'Width': width, 'Height': height, 'ExactVisibleColorAndAlpha': equal,
                            'RgbSha256': sha(rgb), 'AlphaSha256': sha(alpha),
                            'Rendered': rendered_patches(folder, item['Render'], image_page)})
        if packages[0] != packages[1] or settings[0] != settings[1]:
            raise ValueError('Control inputs or settings differ beyond resolution reduction')
    evidence = {'SourcePngSha256': sha(source_png), 'RgbaSha256': sha(rgba), 'Results': reports,
                'Scope': 'Authored embedded PNGs only; exact visible source samples/alpha, dimensions, rendered patches and reduction controls. Hidden RGB, ICC/CMYK, other codecs and broad document fidelity are not established.'}
    output = root / 'image-comparison.json'
    if output.exists():
        if json.loads(read(output)) != json.loads(json.dumps(evidence)):
            raise ValueError('Recomputed comparison differs; retain prior evidence')
    else:
        output.write_text(json.dumps(evidence, indent=2) + '\n')
    print(json.dumps(evidence, indent=2))


if __name__ == '__main__':
    main()
