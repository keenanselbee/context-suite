"""Check Office source-record bindings without copying or executing an engine."""

import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import uuid
from unittest.mock import patch


TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("office_payload", TOOLS / "Stage-OfficePayload.py")
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)


def main():
    evidence = payload.ROOT / ".codex-temp/office-source-record" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    original = json.loads(payload.SOURCE_RECORD.read_bytes())
    sources = {str(path.relative_to(payload.ROOT)): payload.digest(path) for path in
               (Path(__file__).resolve(), TOOLS / "Stage-OfficePayload.py", payload.SOURCE_RECORD,
                TOOLS / "payload-candidate.json", payload.REDUCTION_POLICY,
                payload.DERIVATION_TOOL, payload.ROOT / ".gitattributes")}
    checks = []
    payload.selection()
    checks.append("Current source and runtime identities agree")
    attributes = subprocess.run(["git", "check-attr", "eol", "--", "tools/office-engine/source-runtime-candidate.json"],
                                cwd=payload.ROOT, capture_output=True, text=True, check=True)
    if attributes.stdout.strip() != "tools/office-engine/source-runtime-candidate.json: eol: lf":
        raise AssertionError("Packaged source identity needs stable LF checkout bytes.")
    if b"\r" in payload.SOURCE_RECORD.read_bytes():
        raise AssertionError("Source record bytes differ from the LF checkout rule.")
    checks.append("Source record retains LF bytes across checkout")
    changes = [
            ("Unknown record version", ("formatVersion",), 2),
            ("Different engine archive", ("archiveSha256",), "0" * 64),
            ("Different inventory", ("inventorySha256",), "0" * 64),
            ("Different host", ("host", "sha256"), "0" * 64),
            ("Different host size", ("host", "bytes"), original["host"]["bytes"] + 1),
            ("Different runtime count", ("runtime", "files"), original["runtime"]["files"] + 1),
            ("Different runtime size", ("runtime", "bytes"), original["runtime"]["bytes"] + 1)]
    if "runtimeDerivation" in original:
        changes.extend([
            ("Missing derivation", ("runtimeDerivation",), None),
            ("Different base archive", ("runtimeDerivation", "baseArchiveSha256"), "0" * 64),
            ("Different reduction policy", ("runtimeDerivation", "policySha256"), "0" * 64),
            ("Different derivation tool", ("runtimeDerivation", "toolSha256"), "0" * 64),
            ("Missing exclusions", ("runtimeDerivation", "excludedFiles"), []),
            ("Changed retained binaries", ("runtimeDerivation", "retainedBinaryBytesUnchanged"), False),
            ("Different source kit", ("sourceKitManifestSha256",), "0" * 64),
            ("Changed build configuration", ("configuration", "onlineUpdate"), True),
        ])
        for path in (payload.REDUCTION_POLICY, payload.DERIVATION_TOOL):
            relative = path.relative_to(payload.ROOT).as_posix()
            attributes = subprocess.run(["git", "check-attr", "eol", "--", relative], cwd=payload.ROOT,
                                        capture_output=True, text=True, check=True)
            if attributes.stdout.strip() != relative + ": eol: lf" or b"\r" in path.read_bytes():
                raise AssertionError("Runtime recipe needs stable LF checkout bytes: " + relative)
        checks.append("Derived recipe retains LF bytes across checkout")
    for name, path, value in changes:
        changed = copy.deepcopy(original)
        target = changed
        for part in path[:-1]:
            target = target[part]
        target[path[-1]] = value
        fixture = evidence / (name.replace(" ", "-") + ".json")
        fixture.write_text(json.dumps(changed), encoding="utf-8")
        with patch.object(payload, "SOURCE_RECORD", fixture):
            try:
                payload.selection()
            except ValueError:
                checks.append(name + " refused")
            else:
                raise AssertionError(name + " was accepted")
    if any(payload.digest(payload.ROOT / name) != digest for name, digest in sources.items()):
        raise AssertionError("Source record test inputs changed.")
    (evidence / "results.json").write_text(json.dumps({"Passed": True, "Checks": checks, "Sources": sources}, indent=2) + "\n")
    print(f"Passed {len(checks)} Office source-record checks: {evidence}")


if __name__ == "__main__":
    main()
