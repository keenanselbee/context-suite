"""Compare authored isolated Excel results with retained ordinary native controls."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import uuid
from decimal import Decimal

TOOLS = Path(__file__).resolve().parent


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, TOOLS / filename)
    loaded = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loaded)
    return loaded


common = module('office_comparison', 'Inspect-OfficePdfComparison.py')
snapshots = module('date_snapshots', 'Inspect-ExcelDateSnapshots.py')


def main():
    stage = common.local(sys.argv[1], common.SCRATCH / 'office-execution')
    baseline = common.local(sys.argv[2], common.SCRATCH / 'office-engine')
    prepared = common.local(sys.argv[3], common.SCRATCH / 'pdfium-engine')
    report = common.read_json(stage / 'contracts/results.json')
    completion = common.read_json(stage / 'exit.json')
    inputs = common.read_json(stage / 'inputs.json')
    original = common.read_json(baseline / 'office-evaluation.json')
    verified = common.read_json(baseline / 'independent-date-snapshots.json')
    if not report['Passed'] or report['Mode'] != 'ExcelCalculatedDates' or completion != {'ExitCode': 0, 'InputsUnchanged': True}:
        raise ValueError('Expected completed application date evidence')
    if original['Mode'] != 'ExcelNativeDateSnapshots' or not verified['ExpectedCachesMatch'] or not verified['PdfUnchanged']:
        raise ValueError('Expected independently inspected same-document controls')
    if len(report['Profiles']) != 6 or any(not item['Removed'] or not item['ContextRetired'] for item in report['Profiles']):
        raise ValueError('Incomplete native cleanup')
    build = common.read_json(prepared / 'probe-build.json')
    pdfium = common.local(prepared / 'probe-build/bin/Release/ContextSuite.Pdfium.Probe.exe', prepared)
    for path, key in ((pdfium, 'sha256'), (pdfium.parent / 'pdfium.dll', 'pdfiumSha256'),
                      (common.ROOT / 'tools/pdf-engine/PdfiumProbe/Probe.cpp', 'bridgeSourceSha256'),
                      (common.ROOT / 'tools/pdf-engine/PdfiumProbe/CMakeLists.txt', 'buildSourceSha256')):
        if common.sha(path.read_bytes()) != build[key]:
            raise ValueError('PDFium probe identity changed')
    executables = [Path(name) for name in inputs['Binaries'] if name.endswith('\\pdf-engine\\qpdf.exe')]
    if len(executables) != 1:
        raise ValueError('Expected one recorded qpdf engine')
    qpdf = common.local(executables[0], common.SCRATCH / 'office-execution')
    for path in qpdf.parent.iterdir():
        common.local(path, common.SCRATCH / 'office-execution')
        if not path.is_file() or common.sha(path.read_bytes()) != inputs['Binaries'].get(str(path)):
            raise ValueError('Retained qpdf input identity changed')
    cases = {(variant, calculation) for variant in ('1900-default', '1900-explicit', '1904') for calculation in ('cached', 'recalculate')}
    controls = {(item['Source'], item['ProfileStyle']): item for item in original['Results']}
    checked_controls = {(item['Source'], item['ProfileStyle']): item for item in verified['Results']}
    if len(controls) != 6 or len(report['Results']) != 6:
        raise ValueError('Expected six native/application cases')
    target = stage / ('calculated-inspection-' + uuid.uuid4().hex)
    target.mkdir()
    results = []
    for item in report['Results']:
        variant, calculation = item['Variant'], item['Calculation']
        if (variant, calculation) not in cases:
            raise ValueError('Unexpected or repeated case')
        cases.remove((variant, calculation))
        name = 'Excel dates ' + variant + '.xlsx'
        style = 'calc-never' if calculation == 'cached' else 'calc-always'
        control = controls[name, style]
        control_folder = baseline / (name[:-5] + '-' + style)
        folder = stage / 'contracts' / (variant + '-' + calculation)
        source = common.local(item['Source'], common.SCRATCH / 'office-engine')
        if common.sha(source.read_bytes()) != item['SourceSha256'] or item['SourceSha256'] != control['SourceSha256']:
            raise ValueError('Changed authored source identity')
        pdf = common.local(folder / 'candidate.pdf', stage)
        workbook = common.local(folder / 'calculated.xlsx', stage)
        if pdf.stat().st_size > 16 * 1024 * 1024 or workbook.stat().st_size > 1024 * 1024 or common.sha(pdf.read_bytes()) != item['CandidateSha256'] or common.sha(workbook.read_bytes()) != item['WorkbookSha256']:
            raise ValueError('Changed or oversized retained export')
        refused = calculation == 'recalculate' and variant != '1904'
        if item['Refused'] != refused or item['Result']['State'] != (4 if refused else 5):
            raise ValueError('Unexpected publication outcome')
        publication = item['Result']['Publication']
        if refused:
            if publication and publication['IsCommitted'] or 'calculated workbook contains dates' not in item['Result']['Message']:
                raise ValueError('Incorrect date refusal/publication')
        else:
            published = common.local(publication['OutputPath'], stage)
            if not publication['IsCommitted'] or common.sha(published.read_bytes()) != item['CandidateSha256']:
                raise ValueError('Published PDF differs from validated candidate')
        parts, cells, _, _, properties = snapshots.workbook(workbook.read_bytes())
        values = [1, 59, 60, 61, 40729, Decimal('40729.5'), Decimal('1.5')]
        expected = [40729 if calculation == 'cached' and index < 4 else value for index, value in enumerate(values)]
        actual = [Decimal(cells['B' + str(index)].findtext(snapshots.NS + 'v')) for index in range(2, 9)]
        if actual != expected or (properties.get('date1904', 'false') in ('1', 'true')) != (variant == '1904'):
            raise ValueError('Calculated caches or date base changed')
        output = target / (variant + '-' + calculation)
        output.mkdir()
        checked = subprocess.run([str(qpdf), '--check', str(pdf)], capture_output=True, timeout=30)
        (output / 'qpdf.txt').write_bytes(checked.stdout + checked.stderr)
        if checked.returncode:
            raise ValueError('qpdf validation failed')
        rendered = subprocess.run([str(pdfium), 'render', str(pdf), str(output / 'page'), '96', 'opaque', 'no-widgets'], capture_output=True, timeout=30)
        if rendered.returncode or len(rendered.stdout) > 65536 or len(rendered.stderr) > 65536:
            raise ValueError('PDFium rendering failed')
        (output / 'render.json').write_bytes(rendered.stdout)
        render = json.loads(rendered.stdout)
        pages = render['pages']
        if len(pages) != 1 or len(control['Before']['Render']['pages']) != 1:
            raise ValueError('Changed page count')
        actual_page, expected_page = pages[0], control['Before']['Render']['pages'][0]
        if any(actual_page[key] != expected_page[key] for key in ('width', 'height', 'stride', 'widthPoints', 'heightPoints')):
            raise ValueError('Changed date PDF geometry')
        raw = (output / 'page-1.bgra').read_bytes()
        baseline_pixels = (control_folder / 'before/page-1.bgra').read_bytes()
        if common.sha(baseline_pixels) != checked_controls[name, style]['PdfComparison']['PixelSha256'] or len(raw) > 16 * 1024 * 1024 or raw != baseline_pixels:
            raise ValueError('Changed native date PDF pixels')
        if common.sha(pdf.read_bytes()) != item['CandidateSha256'] or common.sha(workbook.read_bytes()) != item['WorkbookSha256']:
            raise ValueError('Exports changed during inspection')
        results.append({'Variant': variant, 'Calculation': calculation, 'Refused': refused, 'ExactControlPixels': True,
                        'CandidateSha256': item['CandidateSha256'], 'WorkbookSha256': item['WorkbookSha256'], 'CacheValues': [str(value) for value in actual]})
    result = {'Passed': not cases, 'Results': results, 'Scope': 'Six authored native outputs; four published copies and two withheld PDFs. Pixel identity is to retained controls, including their known wrong early dates; this is not repaired rendering.'}
    (target / 'results.json').write_text(json.dumps(result, indent=2))
    print('Verified six native date outputs, 42 caches, four publications and two refusals:', target)


if __name__ == '__main__':
    main()
