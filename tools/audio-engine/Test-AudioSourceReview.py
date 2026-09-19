"""Reject altered patch chains using disposable copies of a completed build."""
import copy
import importlib.util
import json
import pathlib
import shutil
import sys
import uuid

TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('audio_review', TOOLS / 'Check-AudioBuild.py')
review = importlib.util.module_from_spec(spec)
spec.loader.exec_module(review)


def main():
    if len(sys.argv) != 2:
        raise ValueError('Use <completed matrix-repair build>')
    build = review.build_tool.local_path(sys.argv[1])
    record = json.loads((build / 'build-result.json').read_text())
    if not record.get('wmaProMatrixRepair') or record['exitCode']:
        raise ValueError('Use a completed matrix-repair build')
    evidence = build.parent / ('audio-source-review-' + uuid.uuid4().hex)
    evidence.mkdir()
    targets = {target for patch in record['patches'] for target in patch['modifiedSources']}
    results = []
    for name in ('control', 'unselected', 'missing-patch', 'patch-hash', 'wrong-target',
                 'intermediate-hash', 'effective-inventory', 'source-bytes'):
        directory = evidence / name
        directory.mkdir()
        for filename in ('source-inventory.json', 'effective-sources.json'):
            shutil.copyfile(build / filename, directory / filename)
        for target in targets:
            destination = directory / 'source' / target
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(build / 'source' / target, destination)
        altered = copy.deepcopy(record)
        if name == 'unselected':
            altered['wmaProMatrixRepair'] = False
        elif name == 'missing-patch':
            altered['patches'].pop(1)
        elif name == 'patch-hash':
            altered['patches'][1]['sha256'] = '0' * 64
        elif name == 'wrong-target':
            altered['patches'][1]['modifiedSources'] = {'libavcodec/wmadec.c': '0' * 64}
        elif name == 'intermediate-hash':
            altered['patches'][0]['modifiedSources']['libavcodec/wmaprodec.c'] = '0' * 64
        elif name == 'effective-inventory':
            with (directory / 'effective-sources.json').open('ab') as stream:
                stream.write(b' ')
        elif name == 'source-bytes':
            with (directory / 'source/libavcodec/wmaprodec.c').open('ab') as stream:
                stream.write(b'\n/* altered */\n')
        try:
            review.review_source_changes(directory, altered)
        except ValueError as error:
            if name == 'control':
                raise
            results.append(dict(name=name, refused=str(error)))
        else:
            if name != 'control':
                raise AssertionError('Altered provenance accepted: ' + name)
            results.append(dict(name=name, passed=True))
    (evidence / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    print('Passed eight source-review control/mutation checks: ' + str(evidence))


if __name__ == '__main__':
    main()
