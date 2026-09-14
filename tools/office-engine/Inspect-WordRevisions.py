"""Cross-check retained passive Word revision fixtures and PDF observations."""
import hashlib
import calendar
import datetime
import io
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
PARENT = ROOT / '.codex-temp/office-engine'
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
CASES = {'clean', 'shown', 'hidden', 'unspecified'}


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
    final_text = report['Mode'] == 'WordFinalText'
    styles = {'final-text', 'show-changes-control'} if final_text else {'default'}
    expected = {(f'Word revisions {name}.docx', style) for name in CASES for style in styles}
    require(report['Mode'] in ('WordRevisions', 'WordFinalText') and len(report['Results']) == len(expected) and
            {(item['Source'], item['ProfileStyle']) for item in report['Results']} == expected, 'Expected exact revision case/profile set')
    rendered_pages = {}
    for item in report['Results']:
        name = item['Source']
        key = name[len('Word revisions '):-5]
        style = item['ProfileStyle']
        folder = root / (name[:-5] + ('-' + style if final_text else ''))
        source = read(root / 'fixtures' / name)
        pdf = read(folder / (name[:-5] + '.pdf'))
        require(item['Completed'] and hashlib.sha256(source).hexdigest().upper() == item['SourceSha256'] and
                hashlib.sha256(pdf).hexdigest().upper() == item['PdfSha256'], 'Changed or incomplete source/PDF')
        with zipfile.ZipFile(io.BytesIO(source)) as archive:
            entries = archive.infolist()
            require(len(entries) == 5 and {entry.filename for entry in entries} ==
                    {'[Content_Types].xml', '_rels/.rels', 'word/_rels/document.xml.rels', 'word/document.xml', 'word/settings.xml'} and
                    sum(entry.file_size for entry in entries) <= 65536, 'Unexpected authored parts')
            document = ET.fromstring(archive.read('word/document.xml'))
            settings = ET.fromstring(archive.read('word/settings.xml'))
            views = settings.findall(W + 'revisionView')
            expected_view = [{W + 'markup': 'true' if key == 'shown' else 'false', W + 'insDel': 'true' if key == 'shown' else 'false'}] if key in ('shown', 'hidden') else []
            require([node.attrib for node in views] == expected_view and len(settings) == len(views), 'Changed revision visibility')
            body = document.find(W + 'body')
            paragraphs = body.findall(W + 'p')
            require(len(paragraphs) == 3 and paragraphs[0].findtext(W + 'r/' + W + 't') == 'CONTROL_MARKER' and
                    paragraphs[2].findtext(W + 'r/' + W + 't') == 'END_MARKER', 'Changed control text')
            require([node.text for node in paragraphs[1].iter(W + 't')] == ['INSERTED_MARKER'] and
                    [node.text for node in paragraphs[1].iter(W + 'delText')] == ([] if key == 'clean' else ['DELETED_MARKER']), 'Changed revision text')
            require(len(list(document.iter(W + 'ins'))) == len(list(document.iter(W + 'del'))) == (0 if key == 'clean' else 1), 'Changed revision elements')
            require(body.find(W + 'sectPr/' + W + 'pgSz').attrib == {W + 'w': '12240', W + 'h': '15840'}, 'Changed page geometry')
        text = json.loads(read(folder / 'extracted-text.json'))
        require(text == item['Text'] and len(text) == item['Pages'] == 1, 'Changed retained page text')
        pages = [re.sub(r'\s+', ' ', page).strip() for page in text]
        require('CONTROL_MARKER' in pages[0] and 'END_MARKER' in pages[0], 'Missing control text')
        observation = {'Case': key, 'ObservedPages': pages, 'InsertedTextPresent': 'INSERTED_MARKER' in pages[0], 'DeletedTextPresent': 'DELETED_MARKER' in pages[0]}
        require(observation == item['RevisionObservation'], 'Observation does not match retained text')
        if final_text:
            show = style == 'show-changes-control'
            require(item['ExportOptions']['ExportTrackedChanges'] == {'type': 'boolean', 'value': 'true' if show else 'false'},
                    'Changed explicit revision export option')
            require(observation['InsertedTextPresent'] and observation['DeletedTextPresent'] == (show and key != 'clean'),
                    'Final text or explicit show-changes control mismatch')
            timestamp = re.fullmatch(r'(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?Z', item['SourceWriteTimeUtc'])
            require(timestamp is not None, 'Expected recorded UTC source write time')
            seconds = calendar.timegm(datetime.datetime.fromisoformat(timestamp[1]).timetuple())
            ticks = seconds * 10_000_000 + int((timestamp[2] or '').ljust(7, '0'))
            require((root / 'fixtures' / name).stat().st_mtime_ns // 100 == ticks, 'Source write time changed')
            conversion = json.loads(read(folder / 'conversion.json'))
            require(conversion['JobActiveProcessesAfterCleanup'] == 0, 'Evaluation job did not finish cleanup')
        require(len(item['Render']['pages']) == 1 and item['Render']['pages'][0]['widthPoints'] == 612 and
                item['Render']['pages'][0]['heightPoints'] == 792, 'Changed rendered page geometry')
        if final_text:
            page = item['Render']['pages'][0]
            require((page['width'], page['height'], page['stride']) == (816, 1056, 3264), 'Changed 96 DPI render extent')
            pixels = read(folder / 'page-1.bgra')
            require(len(pixels) == page['stride'] * page['height'], 'Changed rendered buffer extent')
            rendered_pages[(key, style)] = pixels
        print(json.dumps({'profile': style, **observation}))
    if final_text:
        baseline = rendered_pages[('clean', 'final-text')]
        for key in CASES:
            require(rendered_pages[(key, 'final-text')] == baseline, 'Final-text pixels differ from clean authored control')
            require((rendered_pages[(key, 'show-changes-control')] == baseline) == (key == 'clean'),
                    'Show-changes pixel control was ineffective')
        print('Eight explicit revision-policy exports cross-checked; final pixels equal the clean authored control, with effective text/pixel controls. No broad revision or isolation acceptance implied.')
    else:
        print('Four source/PDF hash, declaration and text observations cross-checked; no Word baseline or export-policy acceptance implied.')


if __name__ == '__main__':
    main()
