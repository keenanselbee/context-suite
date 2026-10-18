"""Check bounded runtime derivation, identity and preservation failures on fixtures."""

import hashlib
import importlib.util
import json
from pathlib import Path
import uuid


TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("office_reduction", TOOLS / "Derive-ReducedRuntime.py")
reduction = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reduction)


def pin(path):
    return {"bytes": path.stat().st_size, "sha256": reduction.stage.digest(path)}


def main():
    root = TOOLS.parents[1] / ".codex-temp/office-reduction-tests" / uuid.uuid4().hex
    source = root / "source"
    source.mkdir(parents=True)
    content = {
        "program/renderer.dll": b"renderer fixture",
        "share/config/images_colibre.zip": b"fallback fixture",
        "share/config/images_breeze.zip": b"alternative fixture",
        "sdk/bin/compiler.exe": b"development fixture",
    }
    for name, data in content.items():
        path = source / "runtime" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    host = source / "ContextSuite.OfficeHost.exe"
    host.write_bytes(b"host fixture")
    inventory = "".join(f"{name}\t{len(data)}\t{hashlib.sha256(data).hexdigest().upper()}\n"
                        for name, data in sorted(content.items())).encode()
    (source / "runtime-files.txt").write_bytes(inventory)
    record = {"host": {"path": host.name, **pin(host)},
              "inventory": {"path": "runtime-files.txt", **pin(source / "runtime-files.txt"),
                            "files": len(content), "runtimeBytes": sum(map(len, content.values()))},
              "emptyDirectories": [], "hostDependencies": []}
    policy = {"formatVersion": 1, "baseSelection": record,
              "retainedTheme": "share/config/images_colibre.zip",
              "excludedFiles": [{"path": name, **pin(source / "runtime" / name)} for name in
                                ("sdk/bin/compiler.exe", "share/config/images_breeze.zip")]}
    reduction.POLICY = root / "policy.json"
    reduction.POLICY.write_text(json.dumps(policy))
    success = root / "success"
    reduction.derive(source, success)
    assert (success / "office-engine/runtime/program/renderer.dll").read_bytes() == content["program/renderer.dll"]
    assert not (success / "office-engine/runtime/sdk").exists()
    assert (source / "runtime/sdk/bin/compiler.exe").is_file()
    checks = ["unchanged retained bytes; source preserved; excluded SDK directory absent"]

    def refused(label, destination, action=None, restore=None):
        if action:
            action()
        try:
            try:
                reduction.derive(source, destination)
            except (ValueError, FileNotFoundError):
                checks.append(label)
            else:
                raise AssertionError("Expected refusal: " + label)
            if destination != success and destination != source and destination.is_relative_to(root):
                assert not destination.exists(), "Failure wrote a destination: " + label
        finally:
            if restore:
                restore()

    refused("existing output preserved", success)
    refused("overlap refused", source)
    refused("outside scratch refused", TOOLS / "refused-output")
    renderer = source / "runtime/program/renderer.dll"
    refused("same-size source corruption refused before copy", root / "corrupt",
            lambda: renderer.write_bytes(b"x" * len(content["program/renderer.dll"])),
            lambda: renderer.write_bytes(content["program/renderer.dll"]))
    extra = source / "runtime/extra.txt"
    refused("extra runtime file refused", root / "extra-file",
            lambda: extra.write_bytes(b"extra"), lambda: extra.unlink())
    extra_dir = source / "runtime/unlisted"
    refused("extra empty directory refused", root / "extra-directory", extra_dir.mkdir, extra_dir.rmdir)
    def invalid_policy():
        altered = json.loads(json.dumps(policy))
        altered["excludedFiles"].append({"path": "program/renderer.dll", **pin(renderer)})
        reduction.POLICY.write_text(json.dumps(altered))
    refused("non-theme/SDK exclusion refused", root / "unsafe-policy", invalid_policy,
            lambda: reduction.POLICY.write_text(json.dumps(policy)))
    reduction.verify_tree(source, record)
    results = {"passed": True, "checks": checks}
    (root / "results.json").write_text(json.dumps(results, indent=2) + "\n")
    print(json.dumps({"evidence": str(root), **results}))


if __name__ == "__main__":
    main()
