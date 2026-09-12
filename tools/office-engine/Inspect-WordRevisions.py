"""Cross-check retained passive Word revision fixtures and PDF observations."""
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
    require(report['Mode'] == 'WordRevisions' and len(report['Results']) == 4 and
            {item['Source'] for item in report['Results']} == {f'Word revisions {name}.docx' for name in CASES}, 'Expected four revision cases')
    for item in report['Results']:
        name = item['Source']
        key = name[len('Word revisions '):-5]
        folder = root / name[:-5]
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
        require(len(item['Render']['pages']) == 1 and item['Render']['pages'][0]['widthPoints'] == 612 and
                item['Render']['pages'][0]['heightPoints'] == 792, 'Changed rendered page geometry')
        print(json.dumps(observation))
    print('Four source/PDF hash, declaration and text observations cross-checked; no Word baseline or export-policy acceptance implied.')


if __name__ == '__main__':
    main()
