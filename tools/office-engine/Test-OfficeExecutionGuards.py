"""Check packaged Office test selection without building or creating native profiles."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("office_execution", Path(__file__).with_name("Test-OfficeExecution.py"))
execution = importlib.util.module_from_spec(spec)
spec.loader.exec_module(execution)


def main():
    stage = ROOT / ".codex-temp/office-execution-guards" / uuid.uuid4().hex
    payload = stage / "payload"; payload.mkdir(parents=True)
    (payload / "ContextSuite.Worker.exe").write_text("Non-executable path fixture.")
    (payload / "payload-inventory.json").write_text("[]")
    for name in ("office-engine", "pdf-engine", "pdf-renderer"):
        (payload / name).mkdir()
    checks = []

    def check(condition, name):
        if not condition:
            raise AssertionError(name)
        checks.append(name)

    selected, flags = execution.packaged_payload(ROOT, payload, True)
    check(selected == payload and flags == ["-AllowAudioCandidate", "-AllowPdfCandidate", "-AllowOfficeCandidate"],
          "scratch selection preserves exact path and existing-engine verification")
    (payload / "heif").mkdir()
    selected, flags = execution.packaged_payload(ROOT, payload, True)
    check(selected == payload and flags[-1] == "-AllowHeifCandidate", "HEIF presence adds its pinned verification")
    for name, path, scratch in (
            ("scratch is not a formal candidate", payload, False),
            ("outside-scratch paths refused", ROOT / "uncreated-office-execution-test", True),
            ("path traversal refused", payload / ".." / "payload", True),
            ("incomplete packaged payload refused", stage, True)):
        try:
            execution.packaged_payload(ROOT, path, scratch)
        except ValueError:
            checks.append(name)
        else:
            raise AssertionError(name)
    before = sorted(str(path.relative_to(stage)) for path in stage.rglob("*"))
    for name, args, text in (
            ("profile opt-in is mandatory", ["--scratch-payload", str(payload)], "Explicit disposable Office profile authorization"),
            ("formal and scratch selection are exclusive", ["--production-stage", str(payload), "--scratch-payload", str(payload)], "not allowed with argument"),
            ("packaged and separate engines cannot mix", ["--create-disposable-profiles", "--direct", "--fixtures", str(stage),
                "--scratch-payload", str(payload), "--office-engine", str(payload)], "omit separate worker/engine paths"),
            ("packaged and rebuilt workers cannot mix", ["--create-disposable-profiles", "--direct", "--fixtures", str(stage),
                "--scratch-payload", str(payload), "--retained-worker", str(payload / "ContextSuite.Worker.exe")], "omit separate worker/engine paths")):
        result = subprocess.run([sys.executable, "-B", str(Path(__file__).with_name("Test-OfficeExecution.py")), *args],
                                cwd=ROOT, capture_output=True, text=True, timeout=30)
        check(result.returncode != 0 and text in result.stderr, name)
    check(before == sorted(str(path.relative_to(stage)) for path in stage.rglob("*")), "selection guards leave authored fixtures unchanged")
    (stage / "results.json").write_text(json.dumps({"Passed": True, "Checks": checks,
        "Scope": "Path and argument admission only; no build, native Office execution or profile creation."}, indent=2) + "\n")
    print(f"Passed {len(checks)} Office execution guards: {stage}")


if __name__ == "__main__":
    main()
