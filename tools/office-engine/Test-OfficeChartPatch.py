"""Check the draft chart patch against exact upstream source; never build or adopt an engine."""

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import uuid
import xml.etree.ElementTree as ET


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-id', type=uuid.UUID, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    source = root / '.codex-temp' / ('office-chart-source-' + args.source_id.hex) / 'originals'
    manifest_path = root / 'tools/office-engine/patches/chart-source.json'
    patch = root / 'tools/office-engine/patches/allow-native-chart-rendering.patch'
    manifest = json.loads(manifest_path.read_text())
    if manifest['BuildId'] != '8221e31b3ac356a1623c672912a3d2b492f7e3d1':
        raise ValueError('The draft is bound to the recorded 26.2.6 engine build.')
    expected = {}
    for item in manifest['Files']:
        name = item['Path']
        if PurePosixPath(name).is_absolute() or '..' in PurePosixPath(name).parts or '\\' in name or ':' in name or name in expected:
            raise ValueError('Unexpected source path.')
        path = source / name
        for ancestor in (path, *path.parents):
            if ancestor == root:
                break
            if ancestor.is_symlink() or ancestor.is_junction():
                raise ValueError('Linked source snapshot.')
        if path.stat().st_size != item['Bytes'] or digest(path) != item['Sha256']:
            raise ValueError('Upstream source drift: ' + name)
        expected[name] = item['Sha256']
    if {path.relative_to(source).as_posix() for path in source.rglob('*') if path.is_file()} != expected.keys():
        raise ValueError('Unexpected snapshot membership.')
    stage = root / '.codex-temp' / ('office-chart-patch-' + uuid.uuid4().hex)
    stage.mkdir()
    checks = []

    def check(value, message):
        if not value:
            raise ValueError(message)
        checks.append(message)

    def git(*arguments):
        result = subprocess.run(['git', '-C', str(root), '-c', 'core.autocrlf=false', '-c', 'core.eol=lf', *arguments], capture_output=True)
        if result.returncode:
            raise RuntimeError(result.stderr.decode(errors='replace'))
        return result.stdout.decode()

    modified = {'embeddedobj/source/general/xcreator.cxx', 'embeddedobj/source/commonembedding/xfactory.cxx',
                'embeddedobj/source/commonembedding/embedobj.cxx'}
    check({line.split('\t')[2] for line in git('apply', '--numstat', str(patch)).splitlines()} == modified,
          'exactly three embedded-object source files are changed')
    candidate = stage / 'candidate'
    shutil.copytree(source, candidate)
    directory = candidate.relative_to(root).as_posix()
    git('apply', '--check', '--directory', directory, str(patch))
    git('apply', '--directory', directory, str(patch))
    actual = {path.relative_to(candidate).as_posix(): digest(path) for path in candidate.rglob('*') if path.is_file()}
    check(actual.keys() == expected.keys(), 'patch preserves source snapshot membership')
    check({name for name in expected if actual[name] != expected[name]} == modified, 'only the declared source bytes change')
    ns = '{http://openoffice.org/2001/registry}'
    embedding = ET.parse(candidate / 'officecfg/registry/data/org/openoffice/Office/Embedding.xcu')
    chart = [node for node in embedding.iter('node') if node.get(ns + 'name') == '12DCAE26-281F-416F-A234-C3086127382E']
    check(len(chart) == 1, 'one exact current native chart class is declared upstream')
    properties = {node.get(ns + 'name'): node.findtext('value') for node in chart[0].findall('prop')}
    check(properties['ObjectFactory'] == 'com.sun.star.embed.OOoEmbeddedObjectFactory' and
          properties['ObjectDocumentServiceName'] == 'com.sun.star.chart2.ChartDocument',
          'factory and document-service restrictions match the pinned chart declaration')

    # These exact untouched function tails include all import/media/link/user-init
    # refusals; preserving their source is narrower evidence than executing them.
    for name, marker in (
            ('embeddedobj/source/general/xcreator.cxx', 'UNOEmbeddedObjectCreator::createInstanceInitFromEntry('),
            ('embeddedobj/source/commonembedding/xfactory.cxx', 'OOoEmbeddedObjectFactory::createInstanceUserInit(')):
        before = (source / name).read_text(); after = (candidate / name).read_text()
        check(before[before.index(marker):] == after[after.index(marker):], 'unchanged import/link/user-init tail: ' + name)
    name = 'embeddedobj/source/commonembedding/xfactory.cxx'
    before = (source / name).read_text(); after = (candidate / name).read_text()
    begin = 'OOoEmbeddedObjectFactory::createInstanceInitFromEntry('; end = 'OOoEmbeddedObjectFactory::createInstanceInitNew('
    check(before[before.index(begin):before.index(end)] == after[after.index(begin):after.index(end)],
          'existing-entry and media-descriptor creation retain their refusal bodies')
    roundtrip = stage / 'roundtrip'; shutil.copytree(candidate, roundtrip)
    git('apply', '--reverse', '--directory', roundtrip.relative_to(root).as_posix(), str(patch))
    check(all(digest(roundtrip / name) == value for name, value in expected.items()), 'reverse application restores every original byte')
    check(all(digest(source / name) == value for name, value in expected.items()), 'original source snapshot remains unchanged')
    receipt = stage / 'results.json'
    receipt.write_text(json.dumps({'Passed': True, 'Checks': checks, 'BuildId': manifest['BuildId'],
        'ManifestSha256': digest(manifest_path), 'PatchSha256': digest(patch), 'Source': str(source),
        'OriginalFiles': expected, 'PatchedFiles': actual,
        'Scope': 'Static source identity/application review only. No compilation, renderer execution, security enforcement or adoption acceptance.'}, indent=2) + '\n')
    print(f'{len(checks)} static source-patch checks passed. Native compilation and behavior remain unverified.')
    print(receipt)


if __name__ == '__main__':
    main()
