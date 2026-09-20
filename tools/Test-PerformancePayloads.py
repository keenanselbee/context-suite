"""Verify Office/PDF benchmark payload admission without building or executing workers."""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--expanded", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    paths = load("performance_paths", root / "tools/heif-engine/Stage-HeifPayload.py")
    baseline, expanded = paths.local(args.baseline), paths.local(args.expanded)
    if (baseline / "heif").exists() or not (expanded / "heif").is_dir():
        parser.error("Supply an original candidate without HEIF and an expanded candidate with HEIF.")
    stage = root / ".codex-temp/performance-payloads" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("Performance payload evidence:", stage, flush=True)
    modules = {family: load(family + "_performance", root / f"tools/{family.lower()}-engine/Test-{family}Performance.py")
               for family in ("Office", "Pdf")}
    checks = []
    commands = []

    def execute(label, command, expected_error=None):
        result = subprocess.run(command, cwd=root, capture_output=True, timeout=600)
        (stage / (label + ".stdout.log")).write_bytes(result.stdout)
        (stage / (label + ".stderr.log")).write_bytes(result.stderr)
        commands.append({"Label": label, "Command": command, "ExitCode": result.returncode})
        (stage / "commands.json").write_text(json.dumps(commands, indent=2) + "\n")
        diagnostics = (result.stdout + result.stderr).decode(errors="replace")
        if (expected_error is None and result.returncode) or (expected_error is not None and
                (result.returncode == 0 or expected_error not in diagnostics)):
            raise RuntimeError("Unexpected payload verification result: " + label)
        print("Passed:", label, flush=True)

    inventories = {}
    for name, candidate in (("baseline", baseline), ("expanded", expanded)):
        inventories[name] = hashlib.sha256((candidate / "payload-inventory.json").read_bytes()).hexdigest()
        execute(name + "-inventory-before", [sys.executable, "-B", str(root / "tools/audio-engine/Stage-AudioPayload.py"),
                "--payload", str(candidate), "--inventory"])
        for family, module in modules.items():
            label = family + "-" + name
            module.verify_payload(root, candidate, lambda _, command: execute(label, command))
            checks.append(label)

    # Reproduce the previous rejection, then corrupt only a fresh independent copy.
    old_command = [arg for arg in commands[-1]["Command"] if arg != "-AllowHeifCandidate"]
    execute("previous-expanded-rejection", old_command, "Unreviewed production file:")
    checks.append("previous-expanded-rejection")
    damaged = stage / "damaged"
    shutil.copytree(expanded, damaged)
    bridge = damaged / "heif/ContextSuite.Heif.Native.dll"
    original = bridge.read_bytes()
    bridge.write_bytes(bytes([original[0] ^ 1]) + original[1:])
    for family, module in modules.items():
        label = family + "-damaged-HEIF"
        module.verify_payload(root, damaged, lambda _, command: execute(label, command, "HEIF payload identity differs:"))
        checks.append(label)
    bridge.write_bytes(original)
    (damaged / "unreviewed-file.txt").write_text("Authored negative control; never shipped.\n")
    for family, module in modules.items():
        label = family + "-unreviewed-file"
        module.verify_payload(root, damaged, lambda _, command: execute(label, command, "Unreviewed production file:"))
        checks.append(label)
    for name, candidate in (("baseline", baseline), ("expanded", expanded)):
        if hashlib.sha256((candidate / "payload-inventory.json").read_bytes()).hexdigest() != inventories[name]:
            raise RuntimeError("Source inventory changed: " + name)
        execute(name + "-inventory-after", [sys.executable, "-B", str(root / "tools/audio-engine/Stage-AudioPayload.py"),
                "--payload", str(candidate), "--inventory"])
    (stage / "results.json").write_text(json.dumps({"Passed": True, "Checks": checks,
        "Baseline": str(baseline), "Expanded": str(expanded), "InventoriesSha256": inventories,
        "CandidatesUnchanged": True,
        "Scope": "Actual payload admission and tamper refusal only; no worker execution or performance measurement."}, indent=2) + "\n")
    print(f"Passed {len(checks)} benchmark payload checks: {stage}", flush=True)


if __name__ == "__main__":
    main()
