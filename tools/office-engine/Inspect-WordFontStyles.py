"""Independently inspect an authored application Word font-style export matrix."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execution-id", type=uuid.UUID, required=True)
    parser.add_argument("--pdf-prepared", type=Path, required=True)
    parser.add_argument("--pdfium-prepared", type=Path, required=True)
    parser.add_argument("--powerpoint-slides", action="store_true", help="Inspect the three authored application PowerPoint slide cases instead.")
    parser.add_argument("--powerpoint-layout", action="store_true", help="Inspect the authored three-slide image, diagram/table and typography deck.")
    parser.add_argument("--excel-layout", action="store_true", help="Inspect four authored Excel chart/merged/wrapped application exports instead.")
    parser.add_argument("--chart-policy-control", action="store_true", help="With --excel-layout, compare active-content settings only on freshly generated passive workbooks in ordinary bounded jobs.")
    args = parser.parse_args()
    if sum((args.powerpoint_slides, args.excel_layout, args.powerpoint_layout)) > 1:
        parser.error("Choose one inspection scenario.")
    if args.chart_policy_control and not args.excel_layout:
        parser.error("The diagnostic requires --excel-layout.")
    prefix = "powerpoint-layout" if args.powerpoint_layout else "chart-policy" if args.chart_policy_control else "layout" if args.excel_layout else "slides" if args.powerpoint_slides else "style"
    root = Path(__file__).resolve().parents[2]
    stage = root / ".codex-temp/office-execution" / args.execution_id.hex
    qpdf_root = args.pdf_prepared.resolve(strict=True)
    pdfium_root = args.pdfium_prepared.resolve(strict=True)
    qpdf_root.relative_to(root / ".codex-temp/pdf-engine")
    pdfium_root.relative_to(root / ".codex-temp/pdfium-engine")
    report = stage / "contracts/results.json"
    exit_report = read(stage / "exit.json")
    if not exit_report["InputsUnchanged"]:
        raise RuntimeError("Native inputs changed; retain the failed evidence.")
    binaries = {}
    if args.chart_policy_control:
        runtime = stage / "worker/office-engine/runtime"
        expected_runtime = {name: value for name, value in read(stage / "inputs.json")["Binaries"].items()
                            if Path(name).is_relative_to(runtime)}
        actual_runtime = {str(path): digest(path) for path in runtime.rglob("*") if path.is_file()}
        if not expected_runtime or actual_runtime != expected_runtime:
            raise RuntimeError("Diagnostic Office runtime differs from verified execution.")
        binaries.update(actual_runtime)
    for item in read(qpdf_root / "inventory.json"):
        path = (qpdf_root / "unpacked" / item["path"]).resolve(strict=True)
        path.relative_to(qpdf_root / "unpacked")
        if digest(path) != item["sha256"].upper():
            raise RuntimeError("Independent qpdf payload changed.")
        binaries[str(path)] = item["sha256"].upper()
    build = read(pdfium_root / "probe-build.json")
    pdfium = pdfium_root / "probe-build/bin/Release/ContextSuite.Pdfium.Probe.exe"
    for path, expected in (
        (pdfium, build["sha256"]), (pdfium.parent / "pdfium.dll", build["pdfiumSha256"]),
        (root / "tools/pdf-engine/PdfiumProbe/Probe.cpp", build["bridgeSourceSha256"]),
        (root / "tools/pdf-engine/PdfiumProbe/CMakeLists.txt", build["buildSourceSha256"])
    ):
        if digest(path) != expected.upper():
            raise RuntimeError("Independent PDFium probe or source changed.")
        binaries[str(path)] = expected.upper()
    qpdf = qpdf_root / "unpacked/qpdf-12.4.1-msvc64/bin/qpdf.exe"
    paths = {Path(__file__).resolve()}
    for directory in ("tools/office-engine/Probe", "src/ContextSuite.Core"):
        paths.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Version.props", "global.json")
                 if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(paths)}
    identifier = uuid.uuid4().hex
    log = stage / (prefix + "-inspection-run-" + identifier + ".log")
    before = set(stage.glob(prefix + "-inspection-*"))
    with log.open("wb") as output:
        result = subprocess.run(["dotnet", "build", str(root / "tools/office-engine/Probe/Office.Evaluation.csproj"),
                                 "-c", "Release", "--no-restore", "--verbosity", "quiet"], cwd=root, stdout=output, stderr=output)
        if result.returncode:
            raise RuntimeError("Inspection build failed: " + str(log))
        executable = root / "artifacts/managed/bin/Office.Evaluation/Release/net10.0-windows/Office.Evaluation.exe"
        binaries.update({str(path): digest(path) for path in executable.parent.iterdir() if path.is_file()})
        result = subprocess.run([str(executable), "--inspect-powerpoint-layout" if args.powerpoint_layout else "--diagnose-excel-chart-policy" if args.chart_policy_control else "--inspect-excel-layout" if args.excel_layout else "--inspect-powerpoint-publications" if args.powerpoint_slides else "--inspect-word-font-styles", str(report), str(qpdf), str(pdfium)],
                                cwd=root, stdout=output, stderr=output)
    unchanged = all(digest(root / name) == expected for name, expected in sources.items()) and all(
        digest(Path(name)) == expected for name, expected in binaries.items())
    created = [path for path in set(stage.glob(prefix + "-inspection-*")) - before if path.is_dir()]
    passed = result.returncode == 0 and unchanged and len(created) == 1 and read(created[0] / "results.json")["Passed"]
    receipt = stage / (prefix + "-inspection-receipt-" + identifier + ".json")
    receipt.write_text(json.dumps({"Passed": passed, "ExitCode": result.returncode, "InputsUnchanged": unchanged,
        "Sources": sources, "Binaries": binaries, "ReportSha256": digest(report),
        "Inspection": str(created[0]) if len(created) == 1 else None, "Log": str(log)}, indent=2))
    print(log.read_text(errors="replace")[-3000:], flush=True)
    print("Inspection receipt:", receipt, flush=True)
    if not passed:
        raise RuntimeError("Office publication inspection failed; retain the complete comparison evidence.")


if __name__ == "__main__":
    main()
