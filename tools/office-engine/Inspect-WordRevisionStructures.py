"""Inspect authored structural revision declarations and retained PDF controls."""
import calendar
import datetime
import hashlib
import io
import json
import pathlib
import re
import runpy
import sys
import xml.etree.ElementTree as ET
import zipfile

BASE = runpy.run_path(str(pathlib.Path(__file__).with_name('Inspect-WordRevisions.py')))
read, require, W = BASE['read'], BASE['require'], BASE['W']
KINDS = ('format', 'table', 'move')
VARIANTS = ('clean', 'tracked', 'before')


def declarations(source, kind, variant):
    with zipfile.ZipFile(io.BytesIO(source)) as archive:
        entries = archive.infolist()
        require(len(entries) == 5 and {entry.filename for entry in entries} ==
                {'[Content_Types].xml', '_rels/.rels', 'word/_rels/document.xml.rels', 'word/document.xml', 'word/settings.xml'} and
                sum(entry.file_size for entry in entries) <= 65536, 'Unexpected authored package parts')
        parts = {entry.filename: archive.read(entry) for entry in entries}
    require(all(b'<!DOCTYPE' not in data and b'<!ENTITY' not in data for data in parts.values()), 'Unexpected XML declaration')
    for path, target in [('_rels/.rels', 'word/document.xml'), ('word/_rels/document.xml.rels', 'settings.xml')]:
        links = list(ET.fromstring(parts[path]))
        require(len(links) == 1 and links[0].get('Target') == target and links[0].get('TargetMode') is None,
                'Unexpected authored relationship')
    settings = ET.fromstring(parts['word/settings.xml'])
    require(len(settings) == 1 and settings[0].tag == W + 'revisionView' and settings[0].attrib ==
            {W + 'markup': 'true', W + 'insDel': 'true', W + 'formatting': 'true'}, 'Changed saved markup settings')
    document = ET.fromstring(parts['word/document.xml'])
    require(document.tag == W + 'document' and len(document) == 1, 'Changed document root')
    body = document.find(W + 'body')
    require(body is not None and len(body) == 4 and [child.tag for child in body] ==
            [W + 'p', W + ('tbl' if kind == 'table' else 'p'), W + 'p', W + 'sectPr'], 'Changed authored body shape')
    require([node.text for node in body[0].iter(W + 't')] == ['CONTROL_MARKER'] and
            [node.text for node in body[2].iter(W + 't')] == ['END_MARKER'], 'Changed control text')
    require(body[3].find(W + 'pgSz').attrib == {W + 'w': '12240', W + 'h': '15840'}, 'Changed source page geometry')
    tracked = variant == 'tracked'
    if kind == 'format':
        run = body[1].find(W + 'r')
        properties = run.find(W + 'rPr')
        require(len(body[1]) == 1 and run.findtext(W + 't') == 'FORMAT_MARKER' and
                (properties.find(W + 'b') is not None) == (variant != 'before') and
                (properties.find(W + 'i') is not None) == (variant == 'before'), 'Changed current formatting control')
        changes = list(document.iter(W + 'rPrChange'))
        require(len(changes) == int(tracked), 'Changed formatting revision count')
        if tracked:
            old = changes[0].find(W + 'rPr')
            require(old is not None and old.find(W + 'i') is not None and old.find(W + 'b') is None,
                    'Changed former formatting')
    elif kind == 'table':
        rows = body[1].findall(W + 'tr')
        require(len(rows) == (3 if tracked else 2), 'Changed row count')
        row_text = [''.join(node.text or '' for node in row.iter() if node.tag in (W + 't', W + 'delText')) for row in rows]
        expected = ['ROW_KEEP'] + (['ROW_OLD'] if variant != 'clean' else []) + (['ROW_NEW'] if variant != 'before' else [])
        require(row_text == expected, 'Changed table text/order')
        require(len(body[1].findall('.//' + W + 'trPr/' + W + 'del')) == int(tracked) and
                len(body[1].findall('.//' + W + 'trPr/' + W + 'ins')) == int(tracked) and
                len(body[1].findall('.//' + W + 'p/' + W + 'del')) == int(tracked) and
                len(body[1].findall('.//' + W + 'p/' + W + 'ins')) == int(tracked), 'Changed row/content revision markers')
        require([node.text for node in body[1].iter(W + 'delText')] == (['ROW_OLD'] if tracked else []),
                'Deleted row content not marked independently')
    else:
        for location, start, end in [('moveFrom', '30', '30'), ('moveTo', '32', '32')]:
            runs = body[1].findall(W + location)
            starts = body[1].findall(W + location + 'RangeStart')
            ends = body[1].findall(W + location + 'RangeEnd')
            require(len(runs) == len(starts) == len(ends) == int(tracked), 'Changed move range count')
            if tracked:
                require(starts[0].get(W + 'name') == 'FixtureMove' and starts[0].get(W + 'id') == start and
                        ends[0].get(W + 'id') == end and [node.text for node in runs[0].iter(W + 't')] == ['MOVED_MARKER '],
                        'Changed paired move identity/text')
        text = [node.text for node in body[1].iter(W + 't')]
        require(text == (['MOVED_MARKER '] if variant != 'clean' else []) + ['MOVE_GAP '] +
                (['MOVED_MARKER '] if variant != 'before' else []), 'Changed source move order')


def main():
    root = pathlib.Path(sys.argv[1]).absolute()
    report = json.loads(read(root / 'office-evaluation.json'))
    expected = {(f'Word structures {kind} {variant}.docx', profile) for kind in KINDS for variant in VARIANTS
                for profile in (('final-text', 'show-changes-control') if variant == 'tracked' else ('final-text',))}
    require(report['Mode'] == 'WordRevisionStructures' and len(report['Results']) == 12 and
            {(item['Source'], item['ProfileStyle']) for item in report['Results']} == expected, 'Expected exact twelve-export set')
    pixels, observations = {}, []
    for item in report['Results']:
        name, profile = item['Source'], item['ProfileStyle']
        kind, variant = name[:-5].split(' ')[2:]
        folder = root / (name[:-5] + '-' + profile)
        source_path = root / 'fixtures' / name
        source, pdf = read(source_path), read(folder / (name[:-5] + '.pdf'))
        require(item['Completed'] and hashlib.sha256(source).hexdigest().upper() == item['SourceSha256'] and
                hashlib.sha256(pdf).hexdigest().upper() == item['PdfSha256'], 'Changed source/PDF or incomplete export')
        declarations(source, kind, variant)
        timestamp = re.fullmatch(r'(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?Z', item['SourceWriteTimeUtc'])
        require(timestamp is not None, 'Expected UTC write time')
        ticks = calendar.timegm(datetime.datetime.fromisoformat(timestamp[1]).timetuple()) * 10_000_000 + int((timestamp[2] or '').ljust(7, '0'))
        require(source_path.stat().st_mtime_ns // 100 == ticks, 'Source write time changed')
        shown = profile == 'show-changes-control'
        require(item['ExportOptions']['ExportTrackedChanges'] == {'type': 'boolean', 'value': 'true' if shown else 'false'},
                'Changed export option')
        text = json.loads(read(folder / 'extracted-text.json'))
        require(text == item['Text'] and len(text) == item['Pages'] == 1, 'Changed retained page text')
        body = 'FORMAT_MARKER' if kind == 'format' else ('ROW_KEEP ROW_OLD' if variant == 'before' else
                'ROW_KEEP ROW_OLD ROW_NEW' if shown else 'ROW_KEEP ROW_NEW') if kind == 'table' else (
                'MOVED_MARKER MOVE_GAP' if variant == 'before' else 'MOVED_MARKER MOVE_GAP MOVED_MARKER' if shown else 'MOVE_GAP MOVED_MARKER')
        expected_text = 'CONTROL_MARKER ' + body + ' END_MARKER'
        actual_text = re.sub(r'\s+', ' ', text[0]).strip()
        observation = {'Kind': kind, 'Variant': variant, 'ExpectedText': expected_text, 'ObservedText': actual_text,
                       'Matches': actual_text == expected_text}
        require(observation == item['StructureObservation'], 'Changed structural text observation')
        require(len(item['Render']['pages']) == 1 and item['Render']['pages'][0] ==
                {'widthPoints': 612, 'heightPoints': 792, 'width': 816, 'height': 1056, 'stride': 3264}, 'Changed rendered page extent')
        raw = read(folder / 'page-1.bgra')
        require(len(raw) == 3264 * 1056, 'Changed rendered buffer length')
        require(json.loads(read(folder / 'conversion.json'))['JobActiveProcessesAfterCleanup'] == 0, 'Job cleanup incomplete')
        pixels[(kind, variant, profile)] = raw
        observations.append({'source': name, 'profile': profile, **observation, 'renderSha256': hashlib.sha256(raw).hexdigest()})
    comparisons = []
    for kind in KINDS:
        clean = pixels[(kind, 'clean', 'final-text')]
        final = pixels[(kind, 'tracked', 'final-text')]
        before = pixels[(kind, 'before', 'final-text')]
        shown = pixels[(kind, 'tracked', 'show-changes-control')]
        comparisons.append({'kind': kind, 'finalMatchesClean': final == clean,
                            'changedFinalPixels': sum(final[index:index + 4] != clean[index:index + 4] for index in range(0, len(clean), 4)),
                            'beforeDiffers': before != clean, 'shownDiffers': shown != final})
    passed = all(item['Matches'] for item in observations) and all(item['finalMatchesClean'] and item['beforeDiffers'] and
             (item['kind'] == 'format' or item['shownDiffers']) for item in comparisons)
    print(json.dumps({'passed': passed, 'observations': observations, 'comparisons': comparisons,
                      'scope': 'Authored retained structural revision controls, not broad Word fidelity or isolation acceptance'}, indent=2))
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())
