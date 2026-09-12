"""Cross-check authored PowerPoint packages and retained PDF slide/note observations."""
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
P = '{http://schemas.openxmlformats.org/presentationml/2006/main}'
A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
R = '{http://schemas.openxmlformats.org/officeDocument/2006/relationships}'
CASES = {'reordered', 'hidden-ends', 'notes-excluded', 'notes-control'}


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def read(path):
    require(path.is_relative_to(PARENT) and path.resolve().is_relative_to(PARENT.resolve()), 'Use repository Office scratch')
    for ancestor in (path, *path.parents):
        if ancestor == ROOT:
            break
        require(not ancestor.is_symlink() and not ancestor.is_junction(), 'Linked evidence path')
    require(path.stat().st_size <= 16 * 1024 * 1024, 'Unexpected authored evidence size')
    return path.read_bytes()


def main():
    root = pathlib.Path(sys.argv[1]).absolute()
    report = json.loads(read(root / 'office-evaluation.json'))
    require(report['Mode'] == 'PowerPointSlides' and len(report['Results']) == 4 and
            {item['Source'] for item in report['Results']} == {f'PowerPoint slides {key}.pptx' for key in CASES}, 'Expected four cases')
    notes_sources = []
    for item in report['Results']:
        name = item['Source']
        key = name[len('PowerPoint slides '):-5]
        folder = root / name[:-5]
        source = read(root / 'fixtures' / name)
        pdf = read(folder / (name[:-5] + '.pdf'))
        require(item['Completed'] and hashlib.sha256(source).hexdigest().upper() == item['SourceSha256'] and
                hashlib.sha256(pdf).hexdigest().upper() == item['PdfSha256'], 'Changed or incomplete source/PDF')
        has_notes = key.startswith('notes-')
        if has_notes:
            notes_sources.append(source)
        with zipfile.ZipFile(io.BytesIO(source)) as archive:
            expected_parts = {'[Content_Types].xml', '_rels/.rels', 'ppt/presentation.xml', 'ppt/_rels/presentation.xml.rels',
                              'ppt/slideMasters/slideMaster1.xml', 'ppt/slideMasters/_rels/slideMaster1.xml.rels',
                              'ppt/slideLayouts/slideLayout1.xml', 'ppt/slideLayouts/_rels/slideLayout1.xml.rels', 'ppt/theme/theme1.xml'}
            for index in range(1, 4):
                expected_parts.update({f'ppt/slides/slide{index}.xml', f'ppt/slides/_rels/slide{index}.xml.rels'})
                if has_notes:
                    expected_parts.update({f'ppt/notesSlides/notesSlide{index}.xml', f'ppt/notesSlides/_rels/notesSlide{index}.xml.rels'})
            require(set(archive.namelist()) == expected_parts and len(archive.infolist()) == len(expected_parts) and
                    sum(entry.file_size for entry in archive.infolist()) <= 65536, 'Unexpected authored parts')
            xml = {part: ET.fromstring(archive.read(part)) for part in expected_parts}
            for part, document in xml.items():
                if part.endswith('.rels'):
                    require(all('TargetMode' not in node.attrib and not node.get('Target', '').startswith(('/', '\\')) and
                                ':' not in node.get('Target', '') for node in document), 'Unexpected external relationship')
            presentation = xml['ppt/presentation.xml']
            order = [3, 1, 2] if key == 'reordered' else [1, 2, 3]
            require([node.get(R + 'id') for node in presentation.find(P + 'sldIdLst')] == [f'rId{i}' for i in order], 'Changed source order')
            relationships = {node.get('Id'): node for node in xml['ppt/_rels/presentation.xml.rels']}
            for index in range(1, 4):
                require(relationships[f'rId{index}'].get('Target') == f'slides/slide{index}.xml', 'Changed slide target')
                slide = xml[f'ppt/slides/slide{index}.xml']
                require(slide.get('show') == ('0' if key == 'hidden-ends' and index != 2 else '1') and
                        [node.text for node in slide.iter(A + 't')] == [f'SLIDE_{index}_MARKER'], 'Changed source slide')
                if has_notes:
                    rels = {node.get('Id'): node for node in xml[f'ppt/slides/_rels/slide{index}.xml.rels']}
                    require(rels['notes'].get('Target') == f'../notesSlides/notesSlide{index}.xml' and
                            rels['notes'].get('Type') == R[1:-1] + '/notesSlide', 'Changed notes relationship')
                    note = xml[f'ppt/notesSlides/notesSlide{index}.xml']
                    require(note.tag == P + 'notes' and [node.text for node in note.iter(A + 't')] == [f'NOTE_{index}_MARKER'] and
                            [node.attrib for node in note.iter(P + 'ph')] == [{'type': 'body', 'idx': '1'}], 'Changed note body')
                    back = list(xml[f'ppt/notesSlides/_rels/notesSlide{index}.xml.rels'])
                    require(len(back) == 1 and back[0].get('Target') == f'../slides/slide{index}.xml' and
                            back[0].get('Type') == R[1:-1] + '/slide', 'Changed note slide target')
            require(presentation.find(P + 'sldSz').attrib == {'cx': '9144000', 'cy': '5143500'} and
                    presentation.find(P + 'notesSz').attrib == {'cx': '6858000', 'cy': '9144000'}, 'Changed source geometry')
        text = json.loads(read(folder / 'extracted-text.json'))
        require(text == item['Text'] and len(text) == item['Pages'], 'Changed retained text')
        slides = [re.findall(r'SLIDE_[123]_MARKER', page) for page in text]
        notes = [re.findall(r'NOTE_[123]_MARKER', page) for page in text]
        visible = [2] if key == 'hidden-ends' else order
        control = key == 'notes-control'
        for option in ('ExportNotes', 'ExportNotesPages', 'ExportOnlyNotesPages', 'ExportHiddenSlides'):
            require(item['ExportOptions'][option] == {'type': 'boolean', 'value': 'true' if control and option in
                    ('ExportNotesPages', 'ExportOnlyNotesPages') else 'false'}, 'Changed slide export option')
        matches = (notes if control else slides) == [[f'{"NOTE" if control else "SLIDE"}_{i}_MARKER'] for i in visible]
        matches = matches and (control or all(not page for page in notes))
        observation = {'Case': key, 'SlideMarkers': slides, 'NoteMarkers': notes, 'Matches': matches}
        require(observation == item['SlideObservation'], 'Observation disagrees with retained text')
        width, height = (540, 720) if control else (720, 405)
        require(len(item['Render']['pages']) == len(text) and all(abs(page['widthPoints'] - width) <= 0.1 and abs(page['heightPoints'] - height) <= 0.1
                for page in item['Render']['pages']), 'Changed rendered geometry')
        print(json.dumps(observation))
    require(len(notes_sources) == 2 and notes_sources[0] == notes_sources[1], 'Notes option control inputs differ')
    print('Four source/PDF hashes, slide declarations, note relationships and retained observations cross-checked; inspect Matches before claiming policy evidence.')


if __name__ == '__main__':
    main()
