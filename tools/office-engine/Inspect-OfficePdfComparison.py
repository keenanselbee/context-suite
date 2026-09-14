"""Inspect retained authored Office PDF comparisons without running Office."""
import base64
import collections
import difflib
import hashlib
import json
import pathlib
import struct
import subprocess
import sys
import uuid
import zipfile


ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRATCH = ROOT / ".codex-temp"


def local(value, parent):
    path = pathlib.Path(value).absolute()
    if not path.is_relative_to(parent) or path == parent:
        raise ValueError("Use retained repository scratch inputs")
    for ancestor in (path, *path.parents):
        if ancestor == ROOT:
            break
        if ancestor.is_symlink() or ancestor.is_junction():
            raise ValueError("Linked inspection path")
    if not path.resolve().is_relative_to(parent.resolve()):
        raise ValueError("Inspection path escapes scratch")
    return path


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def read_json(path):
    if path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError("Oversized inspection record")
    return json.loads(path.read_bytes().decode("utf-8-sig"))


def font_tables(data):
    if len(data) < 12 or data[:4] not in (b'\x00\x01\x00\x00', b'OTTO', b'true'):
        raise ValueError('Expected a retained SFNT font program')
    count = struct.unpack_from('>H', data, 4)[0]
    if not 0 < count <= 128 or 12 + count * 16 > len(data):
        raise ValueError('Invalid font table directory extent')
    tables = {}
    for index in range(count):
        tag, checksum, offset, length = struct.unpack_from('>4sIII', data, 12 + index * 16)
        if tag in tables or any(value < 32 or value > 126 for value in tag) or offset < 12 + count * 16 or offset + length > len(data):
            raise ValueError('Invalid font table name or extent')
        tables[tag] = {'tag': tag.decode('ascii'), 'offset': offset, 'bytes': length,
                       'declaredChecksum': checksum, 'sha256': sha(data[offset:offset + length])}
    extents = sorted((table['offset'], table['offset'] + table['bytes']) for table in tables.values() if table['bytes'])
    if any(before[1] > after[0] for before, after in zip(extents, extents[1:])):
        raise ValueError('Overlapping font tables')
    name = tables.get(b'name')
    return {'signature': data[:4].hex(), 'nameTablePresent': name is not None, 'tables': list(tables.values()),
            'names': font_names(data[name['offset']:name['offset'] + name['bytes']]) if name else None}


def font_names(data):
    if len(data) < 6:
        raise ValueError('Truncated font naming header')
    version, count, storage = struct.unpack_from('>HHH', data)
    if version not in (0, 1):
        return {'status': 'unsupported naming version', 'version': version}
    end = 6 + count * 12
    if count > 1024 or end > len(data):
        raise ValueError('Font naming records exceed their extent or count limit')
    languages = []
    if version == 1:
        if end + 2 > len(data):
            raise ValueError('Truncated font language count')
        language_count = struct.unpack_from('>H', data, end)[0]
        end += 2
        if language_count > 1024 or end + 4 * language_count > len(data):
            raise ValueError('Invalid font language record extent')
        languages = [struct.unpack_from('>HH', data, end + index * 4) for index in range(language_count)]
        end += 4 * language_count
    if not end <= storage <= len(data):
        raise ValueError('Font naming storage overlaps its records or exceeds the table')
    def string(length, offset):
        if length > 4096 or offset + length > len(data) - storage:
            raise ValueError('Font naming string exceeds its extent or byte limit')
        return data[storage + offset:storage + offset + length]
    decoded_bytes = sum(length for length, _ in languages)
    if decoded_bytes > 65536:
        raise ValueError('Font language strings exceed the aggregate byte limit')
    language_tags = [string(length, offset).decode('utf-16-be') for length, offset in languages]
    names = []
    for index in range(count):
        platform, encoding, language, identifier, length, offset = struct.unpack_from('>6H', data, 6 + index * 12)
        if offset + length > len(data) - storage:
            raise ValueError('Font name points beyond string storage')
        if identifier not in (1, 2, 4, 6, 16, 17):
            continue
        if language >= 0x8000 and ((version == 1 and language - 0x8000 >= len(language_tags)) or
                                   (version == 0 and not 240 <= platform <= 255)):
            raise ValueError('Selected font name has no corresponding language tag')
        raw = string(length, offset)
        decoded_bytes += len(raw)
        if decoded_bytes > 65536:
            raise ValueError('Selected font names exceed the aggregate byte limit')
        codec = 'utf-16-be' if platform == 0 or platform == 3 and encoding in (0, 1, 10) else 'mac_roman' if platform == 1 and encoding == 0 else None
        names.append({'platform': platform, 'encoding': encoding, 'language': language, 'nameId': identifier,
                      'languageTag': language_tags[language - 0x8000] if version == 1 and language >= 0x8000 else None,
                      'value': raw.decode(codec) if codec else None, 'decoding': codec or 'unsupported encoding'})
    return {'status': 'selected name declarations parsed', 'version': version, 'records': names}


def main():
    evaluation = local(sys.argv[1], SCRATCH / "office-engine")
    prepared = local(sys.argv[2], SCRATCH / "pdf-engine")
    pin = read_json(ROOT / "tools/pdf-engine/evaluation.json")
    archive = local(prepared / "upstream.zip", prepared)
    if archive.stat().st_size > 128 * 1024 * 1024 or sha(archive.read_bytes()) != pin["archiveSha256"]:
        raise ValueError("qpdf evaluation archive identity changed")
    with zipfile.ZipFile(archive) as retained:
        inventory = read_json(prepared / "inventory.json")
        names = {item.filename for item in retained.infolist() if not item.is_dir()}
        actual = {path.relative_to(prepared / "unpacked").as_posix() for path in (prepared / "unpacked").rglob("*") if path.is_file()}
        if len(inventory) != len(names) or {item["path"].replace("\\", "/") for item in inventory} != names or actual != names:
            raise ValueError("qpdf payload membership differs from the pinned archive")
        for item in inventory:
            name = item["path"].replace("\\", "/")
            path = local(prepared / "unpacked" / name, prepared)
            expected = retained.read(name)
            if len(expected) != item["bytes"] or sha(expected) != item["sha256"] or path.read_bytes() != expected:
                raise ValueError("qpdf runtime differs from the pinned archive")
    engine = local(prepared / "unpacked/qpdf-12.4.1-msvc64/bin/qpdf.exe", prepared)
    evaluation_record = read_json(evaluation / "office-evaluation.json")
    mode = evaluation_record.get("Mode")
    if mode not in ('LegacyPdf', 'FontSubstitution'):
        raise ValueError("Use a retained LegacyPdf or FontSubstitution run")
    results = evaluation_record["Results"]
    if mode == 'LegacyPdf':
        selected = [item for item in results if item["Source"] in {"Excel ü.xlsx", "Excel ü.xls", "PowerPoint ü.pptx", "PowerPoint ü.ppt"}]
        if len(selected) != 4 or len({item["Source"] for item in selected}) != 4 or any(not item.get("Completed") or
                item["ProfileStyle"] != ("modern" if item["Source"].endswith((".xlsx", ".pptx")) else "legacy") for item in selected):
            raise ValueError("Use a completed authored modern/legacy comparison")
    else:
        expected = {f'{family} font {variant}.{extension}': (requested, pages)
                    for family, extension, pages in [('Word', 'docx', 2), ('Excel', 'xlsx', 1), ('PowerPoint', 'pptx', 2)]
                    for variant, requested in [('control', 'Arial'), ('missing', 'ContextSuiteAbsentFont9361')]}
        selected = results
        if len(selected) != 6 or {item['Source'] for item in selected} != expected.keys() or any(
                not item.get('Completed') or item['ProfileStyle'] != 'default' or
                (item['RequestedFont'], item['Pages']) != expected[item['Source']] for item in selected):
            raise ValueError('Use the six completed authored font-substitution cases')
    output = SCRATCH / ("office-pdf-inspection-" + uuid.uuid4().hex)
    output.mkdir()
    report, streams = [], {}
    for item in selected:
        stem = pathlib.Path(item["Source"]).stem
        label = stem + "-" + item["ProfileStyle"] if mode == 'LegacyPdf' else stem
        if mode == 'FontSubstitution':
            source = local(evaluation / 'fixtures' / item['Source'], evaluation)
            if source.stat().st_size > 1024 * 1024 or sha(source.read_bytes()) != item['SourceSha256']:
                raise ValueError('Retained authored Office source changed')
        path = local(evaluation / label / (stem + ".pdf"), evaluation)
        if path.stat().st_size > 16 * 1024 * 1024:
            raise ValueError("Oversized retained PDF")
        before = path.read_bytes()
        if sha(before) != item["PdfSha256"]:
            raise ValueError("Retained authored PDF identity changed")
        run = subprocess.run([str(engine), "--json", "--json-stream-data=inline", str(path)], capture_output=True, check=True, timeout=20)
        if len(run.stdout) > 16 * 1024 * 1024 or len(run.stderr) > 65536:
            raise ValueError("Inspection output exceeds the retained-fixture budget")
        (output / (label + ".json")).write_bytes(run.stdout)
        facts = json.loads(run.stdout)
        objects = facts["qpdf"][1]
        pages, fonts, roles = [], [], collections.Counter()
        for index, page in enumerate(facts["pages"], 1):
            value = objects["obj:" + page["object"]]["value"]
            content = b"\n".join(base64.b64decode(objects["obj:" + ref]["stream"]["data"], validate=True) for ref in page["contents"])
            (output / (label + "-" + str(index) + ".txt")).write_bytes(content)
            streams[(stem, item["ProfileStyle"], index)] = content.decode("latin1").splitlines()
            pages.append({"page": index, "mediaBox": value.get("/MediaBox"), "decodedContentSha256": sha(content)})
        for key, obj in objects.items():
            value = obj.get("value")
            if not isinstance(value, dict):
                continue
            if "/BaseFont" in value:
                fonts.append({"object": key, "dictionary": value})
            if "/FontFile2" in value:
                data = base64.b64decode(objects["obj:" + value["/FontFile2"]]["stream"]["data"], validate=True)
                program = output / (label + '-font-' + str(len(fonts)) + '.ttf')
                program.write_bytes(data)
                fonts.append({"descriptorObject": key, 'fontName': value.get('/FontName'), 'fontFileObject': value['/FontFile2'],
                              "fontProgramSha256": sha(data), "bytes": len(data), 'program': str(program), 'directory': font_tables(data)})
            if value.get("/Type") == "/StructElem":
                roles[value.get("/S", "missing")] += 1
        if path.read_bytes() != before:
            raise ValueError("Authored PDF changed during inspection")
        if mode == 'FontSubstitution':
            observed = sorted({font['dictionary']['/BaseFont'] for font in fonts if 'dictionary' in font})
            if observed != sorted(item['PdfFontNames']) or len(pages) != item['Pages']:
                raise ValueError('Fresh PDF font declarations or page count differ from the recorded export')
        report.append({"source": str(path), "sha256": sha(before), "pages": pages, "fonts": fonts, "structureRoles": dict(roles)})
    for (stem, style, page), content in streams.items():
        if mode != 'LegacyPdf' or style != "modern":
            continue
        difference = "\n".join(difflib.unified_diff(content, streams[(stem, "legacy", page)], fromfile="modern", tofile="legacy", n=2))
        (output / (stem + "-" + str(page) + ".diff")).write_text(difference, encoding="utf-8")
    (output / "report.json").write_text(json.dumps({"scope": "Authored retained fixture diagnostics; no fidelity acceptance or complete PDF/tag validation",
        "mode": mode, "inspectionScriptSha256": sha(pathlib.Path(__file__).read_bytes()), "qpdfArchiveSha256": pin["archiveSha256"], "results": report}, indent=2) + "\n", encoding="utf-8")
    print("Office PDF inspection evidence: " + str(output))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise ValueError("Usage: Inspect-OfficePdfComparison.py <authored legacy/font evaluation> <retained qpdf directory>")
    main()
