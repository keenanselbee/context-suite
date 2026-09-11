"""Check PDF deployment refusals and native import closure in disposable copies."""
import importlib.util
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import uuid


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("pdf_payload", TOOLS / "Stage-PdfPayload.py")
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)


def imports(source, scratch, record):
    vswhere = pathlib.Path(os.environ["ProgramFiles(x86)"]) / "Microsoft Visual Studio/Installer/vswhere.exe"
    vs = subprocess.check_output([str(vswhere), "-latest", "-products", "*", "-requires",
                                  "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"], text=True).strip()
    dumpbin = sorted((pathlib.Path(vs) / "VC/Tools/MSVC").glob("*/bin/Hostx64/x64/dumpbin.exe"))[-1]
    windows = {"kernel32.dll", "advapi32.dll", "bcrypt.dll", "crypt32.dll", "gdi32.dll", "user32.dll",
               "ws2_32.dll", "api-ms-win-core-synch-l1-2-0.dll"}
    cache, report = {}, []
    for item in record["files"]:
        path = source / item["path"]
        if path.suffix.lower() not in (".exe", ".dll"):
            continue
        if item["sha256"] not in cache:
            result = subprocess.run([str(dumpbin), "/dependents", str(path)], capture_output=True, text=True, timeout=30)
            if result.returncode:
                raise ValueError("Cannot inspect native PDF dependencies")
            (scratch / (path.name + ".imports.txt")).write_text(result.stdout)
            dependencies = re.findall(r"(?im)^\s+([a-z0-9_.-]+\.dll)\s*$", result.stdout)
            if not dependencies:
                raise ValueError("No native imports found: " + path.name)
            cache[item["sha256"]] = dependencies
        dependencies = cache[item["sha256"]]
        for name in dependencies:
            if name.lower() not in windows and not name.lower().startswith("api-ms-win-crt-") and not (path.parent / name).is_file():
                raise ValueError("Missing adjacent native dependency: " + item["path"] + " -> " + name)
        report.append({"path": item["path"], "dependencies": dependencies})
    (scratch / "native-imports.json").write_text(json.dumps(report, indent=2) + "\n")
    return len(report)


def main():
    if len(sys.argv) != 2:
        raise ValueError("Usage: python Test-PdfPayload.py <completed combined production stage>")
    source = payload.local_path(sys.argv[1])
    record = payload.verify(source)
    scratch = payload.ROOT / ".codex-temp" / ("pdf-payload-tests-" + uuid.uuid4().hex)
    scratch.mkdir()
    results = ["Complete pinned PDF deployment accepted"]
    count = imports(source, scratch, record)
    results.append(f"Adjacent dependency closure verified for {count} native files; Windows imports recorded")

    def refused(name, operation):
        try:
            operation()
        except ValueError:
            results.append(name)
        else:
            raise AssertionError("Expected refusal: " + name)

    changes = [
        ("changed-qpdf", "pdf-engine/qpdf.exe", b"changed"),
        ("changed-renderer", "pdf-renderer/ContextSuite.PdfRenderer.exe", b"changed"),
        ("changed-pdfium", "pdf-renderer/pdfium.dll", b"changed"),
        ("changed-validator", "pdf-validator/ContextSuite.ImagePdfValidator.exe", b"changed"),
        ("changed-qpdf-library", "pdf-validator/qpdf30.dll", b"changed"),
        ("missing-renderer-runtime", "pdf-renderer/vcruntime140_1.dll", None),
        ("missing-qpdf-license", "pdf-engine/LICENSE.txt", None),
        ("changed-qpdf-notice", "pdf-validator/NOTICE.md", b"changed"),
        ("missing-pdfium-notice", "pdf-renderer/licenses/libjpeg_turbo.ijg", None),
        ("extra-runtime", "pdf-renderer/unreviewed.dll", b"extra"),
    ]
    for name, relative, value in changes:
        target = scratch / name
        for directory in payload.DIRECTORIES:
            shutil.copytree(source / directory, target / directory)
        path = target / relative
        if value is None:
            path.unlink()
        else:
            path.write_bytes(value)
        refused(name, lambda: payload.verify(target))
    refused("Development payload refused", lambda: payload.verify(payload.ROOT / "artifacts/production/Release"))
    refused("Existing PDF deployment refused", lambda: payload.stage(source, "absent", "absent", "absent"))
    target = scratch / "invalid-input-stage"
    target.mkdir()
    fake_qpdf = scratch / "invalid-qpdf"
    fake_qpdf.mkdir()
    fake_archive = fake_qpdf / "upstream.zip"
    for name, length in [("Wrong archive size refused before writes", 8),
                         ("Wrong archive hash refused before writes", record["archives"]["qpdf"]["bytes"])]:
        fake_archive.write_bytes(bytes(length))
        refused(name, lambda: payload.stage(target, fake_qpdf, scratch / "absent-pdfium", scratch / "absent-source.tar.gz"))
        if any(target.iterdir()):
            raise AssertionError("Invalid inputs changed the target stage")
    guards = [
        ("No default development staging", ["tools/Build-Production.ps1", "-QpdfPreparedDirectory", "absent"],
         "PDF candidate requires a new StagingId"),
        ("Incomplete PDF inputs refused", ["tools/Build-Production.ps1", "-StagingId", str(uuid.uuid4()), "-QpdfPreparedDirectory", "absent"],
         "PDF candidate requires a new StagingId"),
        ("Default release allowlist rejects PDF", ["tools/curated-engine/Test-ProductionPayload.ps1", "-Payload", str(source), "-AllowAudioCandidate"],
         "Unreviewed production file"),
    ]
    for name, arguments, expected in guards:
        result = subprocess.run(["powershell", "-NoProfile", "-File", *arguments], cwd=payload.ROOT,
                                capture_output=True, text=True, timeout=90)
        (scratch / (name.replace(" ", "-") + ".log")).write_text(result.stdout + result.stderr)
        if result.returncode == 0 or expected not in result.stdout + result.stderr:
            raise AssertionError("Entry-point refusal failed: " + name)
        results.append(name)
    (scratch / "results.json").write_text(json.dumps(results, indent=2) + "\n")
    print(f"{len(results)} PDF payload checks passed: {scratch}")


if __name__ == "__main__":
    main()
