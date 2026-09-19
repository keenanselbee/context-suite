"""Check scoped read-only network query plans without elevation or profile creation."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
PARENT = ROOT / '.codex-temp/office-isolation'
FILES = ('build.json', 'case/allowed/probe.exe', 'case/profile-name.txt', 'case/isolated.json')


def inventory(directory):
    return {str(path.relative_to(directory)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in directory.rglob('*') if path.is_file()}


def main():
    if len(sys.argv) != 2:
        raise ValueError('Use the GUID of a completed current isolated network observation')
    original = PARENT / uuid.UUID(sys.argv[1]).hex
    before = inventory(original)
    evidence = ROOT / '.codex-temp' / ('office-network-plan-tests-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('Network plan evidence: ' + str(evidence), flush=True)
    results = []

    def run(identifier, plan=True):
        return subprocess.run(['powershell', '-NoProfile', '-File',
            str(ROOT / 'tools/office-engine/Read-OfficeNetworkEvents.ps1'), '-StagingId', identifier,
            *(['-PlanOnly'] if plan else [])], cwd=ROOT, capture_output=True, timeout=20)

    accepted = run(original.name)
    (evidence / 'accepted.log').write_bytes(accepted.stdout + accepted.stderr)
    if accepted.returncode:
        raise ValueError('Expected current observation query plan')
    plan = json.loads(accepted.stdout)
    observation = json.loads((original / 'case/isolated.json').read_text())
    assert plan['processId'] == observation['networkProcessId'] and len(plan['queries']) == 4
    for query in plan['queries']:
        suffix = '6' if query['family'] == 'ipv6' else ''
        args = query['arguments']
        assert args[:3] == ['wfp', 'show', query['kind']]
        assert 'remoteport=' + str(observation['networkRemotePort' + suffix]) in args
        assert 'localport=' + str(observation['networkLocalPort' + suffix]) in args
        assert 'protocol=6' in args and 'appid=' + str(original / 'case/allowed/probe.exe') in args
        assert ('timewindow=600' if query['kind'] == 'netevents' else 'verbose=ON') in args
    assert not Path(plan['output']).exists() and inventory(original) == before
    results.append('Four exact app/port/protocol queries; no dry-run writes')

    changes = {
        'missing-process': lambda value: value.pop('networkProcessId'),
        'string-process': lambda value: value.update(networkProcessId='1'),
        'wrong-token': lambda value: value.update(appContainer=0),
        'capability-added': lambda value: value.update(capabilityCount=1),
        'missing-port': lambda value: value.pop('networkLocalPort6'),
        'port-string': lambda value: value.update(networkLocalPort='56898 extra=value'),
        'port-range': lambda value: value.update(networkRemotePort6=65536),
        'time-string': lambda value: value.update(networkStartedFileTime=str(value['networkStartedFileTime'])),
        'reversed-time': lambda value: value.update(networkEndedFileTime=value['networkStartedFileTime'] - 1),
        'long-observation': lambda value: value.update(networkEndedFileTime=value['networkStartedFileTime'] + 310000000),
    }
    for name in (*changes, 'changed-executable', 'wrong-profile'):
        identifier = uuid.uuid4().hex
        directory = PARENT / identifier
        for relative in FILES:
            destination = directory / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original / relative, destination)
        (directory / 'case/profile-name.txt').write_text('ContextSuite.Office.Evaluation.' + identifier + '\n')
        value = json.loads((directory / 'case/isolated.json').read_text())
        if name in changes:
            changes[name](value)
            (directory / 'case/isolated.json').write_text(json.dumps(value))
        elif name == 'changed-executable':
            (directory / 'case/allowed/probe.exe').write_bytes(b'Authored invalid executable canary')
        else:
            (directory / 'case/profile-name.txt').write_text('wrong-profile')
        copy_before = inventory(directory)
        result = run(identifier)
        (evidence / (name + '.log')).write_bytes(result.stdout + result.stderr)
        if result.returncode == 0:
            raise AssertionError('Changed observation was accepted: ' + name)
        assert inventory(directory) == copy_before
        results.append(dict(name=name, refused=True, evidence=str(directory)))
    # A non-administrator cannot execute queries. Do not run this guard elevated:
    # the test never authorizes collection, firewall changes or profile creation.
    import ctypes
    if not ctypes.windll.shell32.IsUserAnAdmin():
        result = run(original.name, False)
        (evidence / 'non-admin.log').write_bytes(result.stdout + result.stderr)
        assert result.returncode != 0 and b'requires an elevated PowerShell' in result.stdout + result.stderr
        results.append('Non-administrator query refused without output writes')
    else:
        results.append('Non-administrator refusal untested in elevated session')
    assert inventory(original) == before
    (evidence / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    print('Passed ' + str(len(results)) + ' network-plan checks: ' + str(evidence))


if __name__ == '__main__':
    main()
