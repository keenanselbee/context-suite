"""Verify PDF dark-mode staging refusal and exact membership on disposable copies."""
import argparse
import copy
import importlib.util
import json
import pathlib
import shutil


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("pdf_dark_payload", TOOLS / "Stage-PdfDarkPayload.py")
stage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage)
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--payload", required=True)
parser.add_argument("--build-directory", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
source = stage.local_path(args.payload, verification=True)
scratch = stage.local_path(args.scratch, build=True)
if scratch.exists():
    raise ValueError("Use a fresh PDF dark-mode packaging evidence directory")
original = stage.verify(source)
scratch.mkdir(parents=True)
candidate = scratch / "candidate"
candidate.mkdir()
for folder in ("pdf-engine", "pdf-renderer", "pdf-validator"):
    shutil.copytree(source / folder, candidate / folder)
checks = []


def check(name, action, refusal=False):
    try:
        action()
    except (ValueError, FileNotFoundError, KeyError):
        if not refusal:
            raise
    else:
        if refusal:
            raise AssertionError("Unexpected acceptance: " + name)
    checks.append(name)
    print("PASS: " + name)


# Exercise failed preflight before a dark-mode directory exists. These monkey
# patches change only test-process values, never repository pins or native files.
real_pins = stage.pins
host, dependencies = real_pins()
for name, modify in (
        ("wrong native host digest refuses staging before writes", lambda value: value.update(sha256="0" * 64)),
        ("changed native source refuses staging before writes", lambda value: value["sources"][0].update(sha256="0" * 64))):
    changed = copy.deepcopy(host)
    modify(changed)
    stage.pins = lambda: (changed, dependencies)
    try:
        check(name, lambda: stage.stage(candidate, args.build_directory), refusal=True)
        assert not (candidate / "pdf-dark").exists()
    finally:
        stage.pins = real_pins

check("complete reviewed native payload stages", lambda: stage.stage(candidate, args.build_directory))
check("existing native payload cannot be overwritten", lambda: stage.stage(candidate, args.build_directory), refusal=True)


def altered_file(relative, data):
    path = stage.local_path(candidate / relative, build=True)
    previous = path.read_bytes()
    try:
        path.write_bytes(data)
        stage.verify(candidate)
    finally:
        path.write_bytes(previous)


for relative in ("pdf-dark/ContextSuite.PdfDark.exe", "pdf-dark/qpdf30.dll",
                 "pdf-dark/LICENSE.txt", "pdf-dark/Qpdf.Dependency.Notices.txt",
                 "pdf-renderer/pdfium.dll"):
    file = candidate / relative
    data = file.read_bytes()
    changed = bytes([data[0] ^ 1]) + data[1:]
    check("changed bytes rejected: " + relative, lambda: altered_file(relative, changed), refusal=True)

missing = candidate / "pdf-dark/NOTICE.md"
saved = missing.read_bytes()
try:
    missing.unlink()
    check("missing native notice rejected", lambda: stage.verify(candidate), refusal=True)
finally:
    missing.write_bytes(saved)

extra = candidate / "pdf-dark/unreviewed.dll"
try:
    extra.write_bytes(b"unreviewed")
    check("extra native runtime rejected", lambda: stage.verify(candidate), refusal=True)
finally:
    extra.unlink()

check("parent traversal in payload path rejected", lambda: stage.verify(candidate / ".." / "candidate"), refusal=True)
check("restored isolated candidate verifies", lambda: stage.verify(candidate))
assert stage.verify(source) == original
checks.append("original reviewed payload remains unchanged")
(scratch / "results.json").write_text(json.dumps({"checks": checks, "passed": len(checks)}, indent=2) + "\n")
print(f"Passed {len(checks)} PDF dark-mode packaging checks.")
