"""Compare only retained authored FontSubstitution results; never launch Office."""
import hashlib
import json
import pathlib
import re
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
PARENT = ROOT / '.codex-temp/office-engine'
MISSING = 'ContextSuiteAbsentFont9361'


def read(path, limit=16 * 1024 * 1024):
    if not path.is_relative_to(PARENT) or not path.resolve().is_relative_to(PARENT.resolve()):
        raise ValueError('Use repository Office evaluation scratch')
    for ancestor in (path, *path.parents):
        if ancestor == ROOT:
            break
        if ancestor.is_symlink() or ancestor.is_junction():
            raise ValueError('Linked evidence path')
    if path.stat().st_size > limit:
        raise ValueError('Evidence exceeds inspection limit')
    return path.read_bytes()


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def main():
    root = pathlib.Path(sys.argv[1]).absolute()
    record = json.loads(read(root / 'office-evaluation.json'))
    if record['Mode'] != 'FontSubstitution' or len(record['Results']) != 6:
        raise ValueError('Expected six completed FontSubstitution results')
    results = {item['Source']: item for item in record['Results']}
    comparisons = []
    for family, extension, pages in [('Word', 'docx', 2), ('Excel', 'xlsx', 1), ('PowerPoint', 'pptx', 2)]:
        pair = []
        packages = []
        for variant, requested in [('control', 'Arial'), ('missing', MISSING)]:
            name = f'{family} font {variant}'
            item = results[name + '.' + extension]
            if not item['Completed'] or item['RequestedFont'] != requested or item['Pages'] != pages:
                raise ValueError('Unexpected case identity or completion')
            source = root / 'fixtures' / (name + '.' + extension)
            if sha(read(source)) != item['SourceSha256'] or sha(read(root / name / (name + '.pdf'))) != item['PdfSha256']:
                raise ValueError('Source or PDF changed since conversion')
            observed = set()
            def fonts(value):
                if isinstance(value, dict):
                    for key, child in value.items():
                        if key == '/BaseFont' and isinstance(child, str):
                            observed.add(child)
                        else:
                            fonts(child)
                elif isinstance(value, list):
                    for child in value:
                        fonts(child)
            fonts(json.loads(read(root / name / 'pdf-font-objects.json')))
            if not observed or sorted(observed) != item['PdfFontNames']:
                raise ValueError('PDF font declarations disagree with recorded observation')
            with zipfile.ZipFile(source) as archive:
                if len(archive.infolist()) > 64 or sum(entry.file_size for entry in archive.infolist()) > 1024 * 1024:
                    raise ValueError('Unexpected authored package size')
                parts = {part: archive.read(part) for part in archive.namelist()}
                if not any(requested.encode() in content for content in parts.values()):
                    raise ValueError('Requested source font declaration is absent')
                packages.append({part: content.replace(MISSING.encode(), b'Arial') for part, content in parts.items()})
            rendered = []
            for index, page in enumerate(item['Render']['pages'], 1):
                width, height, stride = page['width'], page['height'], page['stride']
                if not (0 < width <= 4096 and 0 < height <= 4096 and width * 4 <= stride <= width * 4 + 16):
                    raise ValueError('Unexpected render dimensions')
                pixels = read(root / name / f'page-{index}.bgra', 32 * 1024 * 1024)
                if len(pixels) != stride * height:
                    raise ValueError('Render length disagrees with geometry')
                rendered.append((page, pixels))
            if len(rendered) != pages:
                raise ValueError('Unexpected rendered page count')
            pair.append((item, rendered))
        if packages[0] != packages[1]:
            raise ValueError('Paired source parts differ beyond the requested font name')
        page_results = []
        for (before, a), (after, b) in zip(pair[0][1], pair[1][1]):
            same = all(before[key] == after[key] for key in ('width', 'height', 'stride', 'widthPoints', 'heightPoints'))
            changed = None
            if same:
                changed = sum(a[row * before['stride'] + col * 4:row * before['stride'] + col * 4 + 4] !=
                              b[row * after['stride'] + col * 4:row * after['stride'] + col * 4 + 4]
                              for row in range(before['height']) for col in range(before['width']))
            page_results.append({'SameGeometry': same, 'DifferentPixels': changed, 'ControlPixelSha256': sha(a), 'MissingPixelSha256': sha(b)})
        normalize = lambda pages: [re.sub(r'\s+', ' ', page).strip() for page in pages]
        comparisons.append({'Family': family, 'ControlPdfFonts': pair[0][0]['PdfFontNames'],
                            'MissingPdfFonts': pair[1][0]['PdfFontNames'], 'OnlySourceFontNamesDiffer': True,
                            'NormalizedTextEqual': normalize(pair[0][0]['Text']) == normalize(pair[1][0]['Text']), 'Pages': page_results})
    report = {'Comparisons': comparisons, 'Scope': 'Authored passive pairs; PDF name declarations and exact 96 DPI pixel differences, not accepted fidelity tolerances or general substitution detection.'}
    with (root / 'font-comparison.json').open('x') as output:
        json.dump(report, output, indent=2)
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
