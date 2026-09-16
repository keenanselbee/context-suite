"""Check retained authored Excel date evidence without executing an Office engine."""
import datetime
import hashlib
import io
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
PARENT = ROOT / '.codex-temp/office-engine'
NS = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'


def read(path):
    if not path.is_relative_to(PARENT) or not path.resolve().is_relative_to(PARENT.resolve()):
        raise ValueError('Use retained repository Office scratch')
    for ancestor in (path, *path.parents):
        if ancestor == ROOT:
            break
        if ancestor.is_symlink() or ancestor.is_junction():
            raise ValueError('Linked evidence path')
    if path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError('Unexpected authored evidence size')
    return path.read_bytes()


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def expected(serial, use1904, style):
    if style == '3':
        return '36:00:00'
    # Excel retains the fictitious 1900 leap day for compatibility. Datetime
    # cannot represent that date, so handle it before Gregorian arithmetic.
    if not use1904 and serial == 60:
        return '1900-02-29'
    epoch = datetime.datetime(1904, 1, 1) if use1904 else datetime.datetime(1899, 12, 31)
    adjusted = serial if use1904 or serial < 60 else serial - 1
    value = epoch + datetime.timedelta(days=adjusted)
    return value.strftime('%Y-%m-%d %H:%M:%S' if style == '2' else '%Y-%m-%d')


def main():
    root = pathlib.Path(sys.argv[1]).absolute()
    report = json.loads(read(root / 'office-evaluation.json'))
    names = {f'Excel dates {variant}.xlsx' for variant in ('1900-default', '1900-explicit', '1904')}
    formulas = report['Mode'] == 'ExcelFormulaDates'
    expected_cases = {(name, profile) for name in names for profile in ('calc-always', 'calc-never')} if formulas else {(name, 'default') for name in names}
    if report['Mode'] not in ('ExcelDates', 'ExcelFormulaDates') or len(report['Results']) != len(expected_cases) or {(item['Source'], item['ProfileStyle']) for item in report['Results']} != expected_cases:
        raise ValueError('Expected complete date-system/calculation cases')
    checked = []
    for item in report['Results']:
        name = item['Source']
        profile = item['ProfileStyle']
        folder = root / (name[:-5] + ('-' + profile if formulas else ''))
        source = read(root / 'fixtures' / name)
        if not item['Completed'] or item['Pages'] != 1 or sha(source) != item['SourceSha256'] or sha(read(folder / (name[:-5] + '.pdf'))) != item['PdfSha256']:
            raise ValueError('Incomplete case or changed source/PDF')
        use1904 = name == 'Excel dates 1904.xlsx'
        with zipfile.ZipFile(io.BytesIO(source)) as archive:
            entries = archive.infolist()
            if len(entries) > 64 or len({entry.filename for entry in entries}) != len(entries) or sum(entry.file_size for entry in entries) > 1024 * 1024:
                raise ValueError('Unexpected authored package contents')
            workbook = ET.fromstring(archive.read('xl/workbook.xml'))
            properties = workbook.find(NS + 'workbookPr')
            declaration = properties.get('date1904') if properties is not None else None
            if declaration != ('1' if use1904 else '0' if 'explicit' in name else None):
                raise ValueError('Unexpected source date-system declaration')
            sheet = ET.fromstring(archive.read('xl/worksheets/sheet1.xml'))
            if not formulas and sheet.findall('.//' + NS + 'f'):
                raise ValueError('Date fixture must not depend on formula calculation')
            cells = {cell.get('r'): cell for cell in sheet.findall('.//' + NS + 'c')}
            values = ['1', '59', '60', '61', '40729', '40729.5', '1.5']
            styles = ['1', '1', '1', '1', '1', '2', '3']
            style_root = ET.fromstring(archive.read('xl/styles.xml'))
            formats = {node.get('numFmtId'): node.get('formatCode') for node in style_root.findall('./' + NS + 'numFmts/' + NS + 'numFmt')}
            if formats != {'164': 'yyyy-mm-dd', '165': 'yyyy-mm-dd hh:mm:ss', '166': '[h]:mm:ss'}:
                raise ValueError('Source number formats changed')
            xfs = style_root.findall('./' + NS + 'cellXfs/' + NS + 'xf')
            if [xf.get('numFmtId') for xf in xfs] != ['0', '164', '165', '166']:
                raise ValueError('Source style mappings changed')
            expected_values = []
            for index, (value, style) in enumerate(zip(values, styles), 2):
                cell = cells['B' + str(index)]
                cached = '40729' if formulas and index < 6 else value
                if formulas and cell.findtext(NS + 'f') != '0+' + value:
                    raise ValueError('Authored arithmetic formula changed')
                if cell.get('s') != style or cell.findtext(NS + 'v') != cached or cells['A' + str(index)].findtext(NS + 'is/' + NS + 't') != f'R{index - 1:02d}':
                    raise ValueError('Authored numeric value/style/label changed')
                expected_values.append(expected(float(cached if formulas and profile == 'calc-never' else value), use1904, style))
        pages = json.loads(read(folder / 'extracted-text.json'))
        if pages != item['Text'] or len(pages) != 1 or 'HIDDEN' in pages[0] or 'OUTSIDE' in pages[0]:
            raise ValueError('Retained text or print policy changed')
        rows = re.findall(r'R(0[1-7])\s+(.*?)(?=R0[1-7]|$)', pages[0], re.S)
        if [row for row, _ in rows] != [f'{index:02d}' for index in range(1, 8)]:
            raise ValueError('Expected seven ordered labeled PDF values')
        observations = [{'Row': index, 'ExpectedExcelDisplay': target, 'Observed': re.sub(r'\s+', ' ', actual).strip(),
                         'Matches': target == re.sub(r'\s+', ' ', actual).strip()}
                        for index, ((_, actual), target) in enumerate(zip(rows, expected_values), 1)]
        if observations != item['DateObservations']:
            raise ValueError('Recorded date observations disagree with independent source/text inspection')
        if formulas:
            preflight = item['DatePreflight']
            dates = preflight['StoredDates']
            if preflight['FormatId'] != 'xlsx' or preflight['Refusal'] is not None or not dates['Complete'] or dates['EarlyDateCells'] != 0 or dates['DateFormulaCells'] != 6:
                raise ValueError('Formula preflight evidence changed')
            profile_path = pathlib.Path(item['Profile']).absolute()
            if profile_path.parent != ROOT / '.codex-temp' or not re.fullmatch(r'office-profile-[0-9a-f]{32}u', profile_path.name) or profile_path.is_symlink() or profile_path.is_junction():
                raise ValueError('Unexpected formula profile path')
            settings_path = profile_path / 'settings-verification.json'
            if settings_path.is_symlink() or settings_path.is_junction() or settings_path.stat().st_size > 4096:
                raise ValueError('Invalid profile verification receipt')
            settings = json.loads(settings_path.read_bytes())
            if not settings['Verified'] or settings['CalculationMode'] != (0 if profile == 'calc-always' else 1):
                raise ValueError('Explicit calculation profile was not verified')
        checked.append({'Source': name, 'ProfileStyle': profile, 'SourceSha256': item['SourceSha256'], 'PdfSha256': item['PdfSha256'], 'Date1904': declaration,
                        'MatchingValues': sum(row['Matches'] for row in observations), 'Observations': observations})
    result = {'Results': checked, 'Mode': report['Mode'], 'FidelityPassed': all(row['Matches'] for case in checked for row in case['Observations']), 'Scope': 'Authored numeric/arithmetic-date and elapsed-time cells only; differences are fidelity findings, not accepted output or general locale/formula compatibility.'}
    with (root / ('independent-formula-dates.json' if formulas else 'independent-dates.json')).open('x') as output:
        json.dump(result, output, indent=2)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
