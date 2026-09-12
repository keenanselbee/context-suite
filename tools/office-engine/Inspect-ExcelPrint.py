"""Inspect retained passive workbook print evidence without running Office."""
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
EXPECTED = {
    'manual-break': ['TITLE R02 R03', 'R05 R06'],
    'repeat-title': ['TITLE R02 R03', 'TITLE R05 R06'],
    'fit-one-page': ['TITLE R02 R03 R05 R06'],
    'disjoint-areas': ['TITLE R02', 'R05 R06'],
    'hidden-cells': ['TITLE R02 R05 R06'],
}


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


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def main():
    root = pathlib.Path(sys.argv[1]).absolute()
    report = json.loads(read(root / 'office-evaluation.json'))
    require(report['Mode'] == 'ExcelPrint' and len(report['Results']) == len(EXPECTED) and
            {item['Source'] for item in report['Results']} == {f'Excel print {name}.xlsx' for name in EXPECTED}, 'Expected five print cases')
    checked = []
    for item in report['Results']:
        name = item['Source']
        key = name[len('Excel print '):-5]
        folder = root / name[:-5]
        source = read(root / 'fixtures' / name)
        pdf = read(folder / (name[:-5] + '.pdf'))
        require(item['Completed'] and hashlib.sha256(source).hexdigest().upper() == item['SourceSha256'] and
                hashlib.sha256(pdf).hexdigest().upper() == item['PdfSha256'], 'Changed or incomplete source/PDF')
        with zipfile.ZipFile(io.BytesIO(source)) as archive:
            entries = archive.infolist()
            require(len(entries) <= 64 and len({entry.filename for entry in entries}) == len(entries) and
                    sum(entry.file_size for entry in entries) <= 1024 * 1024, 'Unexpected authored package size')
            workbook = ET.fromstring(archive.read('xl/workbook.xml'))
            sheet = ET.fromstring(archive.read('xl/worksheets/sheet1.xml'))
            sheets = workbook.findall('./' + NS + 'sheets/' + NS + 'sheet')
            require(len(sheets) == 2 and sheets[0].get('name') == 'Print area' and sheets[1].get('state') == 'hidden', 'Changed sheet visibility')
            names = workbook.findall('./' + NS + 'definedNames/' + NS + 'definedName')
            definitions = {node.get('name'): node.text for node in names}
            wanted = {'_xlnm.Print_Area': "'Print area'!$A$1:$B$2,'Print area'!$A$5:$B$6" if key == 'disjoint-areas' else "'Print area'!$A$1:$C$6"}
            if key == 'repeat-title':
                wanted['_xlnm.Print_Titles'] = "'Print area'!$1:$1"
            require(len(names) == len(wanted) and definitions == wanted and all(node.get('localSheetId') == '0' for node in names), 'Changed print areas/titles')
            require(not sheet.findall('.//' + NS + 'f'), 'Unexpected formula')
            cells = {cell.get('r'): cell.findtext(NS + 'is/' + NS + 't') for cell in sheet.findall('.//' + NS + 'c')}
            expected_cells = {'A1': 'TITLE', 'A2': 'R02', 'A3': 'R03', 'A5': 'R05', 'A6': 'R06', 'A20': 'OUTSIDE'}
            if key == 'hidden-cells':
                expected_cells['C2'] = 'HIDDEN_COLUMN'
            require(cells == expected_cells, 'Changed marker cells')
            hidden_rows = [row.get('r') for row in sheet.findall('./' + NS + 'sheetData/' + NS + 'row') if row.get('hidden') == '1']
            hidden_columns = [col.get('min') for col in sheet.findall('./' + NS + 'cols/' + NS + 'col') if col.get('hidden') == '1']
            require(hidden_rows == (['3'] if key == 'hidden-cells' else []) and hidden_columns == (['3'] if key == 'hidden-cells' else []), 'Changed hidden cells')
            fit = key in ('fit-one-page', 'hidden-cells')
            require(sheet.find('./' + NS + 'sheetPr/' + NS + 'pageSetUpPr').get('fitToPage') == ('1' if fit else '0'), 'Changed fit policy')
            setup = sheet.find(NS + 'pageSetup')
            require(setup.get('paperSize') == '1' and setup.get('orientation') == 'portrait' and
                    (setup.get('fitToWidth') == setup.get('fitToHeight') == '1' if fit else setup.get('scale') == '100'), 'Changed page setup')
            breaks = sheet.findall('./' + NS + 'rowBreaks/' + NS + 'brk')
            require([node.attrib for node in breaks] == ([{'id': '4', 'min': '0', 'max': '16383', 'man': '1'}]
                    if key in ('manual-break', 'repeat-title', 'fit-one-page') else []), 'Changed manual break')
        text = json.loads(read(folder / 'extracted-text.json'))
        require(text == item['Text'] and len(text) == item['Pages'], 'Changed retained page text')
        actual = [re.sub(r'\s+', ' ', page).strip() for page in text]
        observation = {'ExpectedPages': EXPECTED[key], 'ObservedPages': actual, 'Matches': actual == EXPECTED[key]}
        require(observation == item['PrintObservation'], 'Report disagrees with independent print expectation')
        checked.append({'Source': name, 'SourceSha256': item['SourceSha256'], 'PdfSha256': item['PdfSha256'], **observation})
    result = {'Results': checked, 'MatchingCases': sum(item['Matches'] for item in checked),
              'Scope': 'Saved print declarations and extracted PDF page markers; not a Microsoft Excel rendering baseline or broad layout fidelity.'}
    with (root / 'independent-print.json').open('x') as output:
        json.dump(result, output, indent=2)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
