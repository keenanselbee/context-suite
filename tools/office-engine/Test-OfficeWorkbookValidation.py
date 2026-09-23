"""Check calculated-workbook validation using retained native copies; no native export or profile is created."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshots", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    retained = args.snapshots.resolve(strict=True)
    retained.relative_to(root / ".codex-temp/office-engine")
    manifest = retained / "office-evaluation.json"
    report = json.loads(manifest.read_bytes())
    if report.get("Mode") != "ExcelNativeDateSnapshots":
        raise ValueError("Use the retained six native calculated-date snapshots.")
    expected = {(f"Excel dates {base}.xlsx", mode) for base in ("1900-default", "1900-explicit", "1904")
                for mode in ("calc-always", "calc-never")}
    fixtures = {str(manifest): digest(manifest)}
    for item in report["Results"]:
        name, mode = item["Source"], item["ProfileStyle"]
        expected.remove((name, mode))
        path = retained / (Path(name).stem + "-" + mode) / name
        actual = digest(path)
        if actual != item["SnapshotSha256"].upper():
            raise ValueError("Retained calculated workbook differs from its receipt.")
        fixtures[str(path)] = actual
    if expected:
        raise ValueError("All six retained snapshots are required.")
    stage = root / ".codex-temp/office-workbook-validation" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("Calculated-workbook evidence:", stage, flush=True)
    inputs = {Path(__file__).resolve()}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/Shared", "proprietary/src/ContextSuite.Private",
                      "proprietary/tests/ContextSuite.Pdf.ContractTests", "proprietary/tests/ContextSuite.Image.ContractTests"):
        inputs.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj")
                      and not {"bin", "obj"}.intersection(path.parts))
    inputs.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Version.props", "global.json", "tools/Require-Private.targets")
                  if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(inputs)}
    (stage / "inputs.json").write_text(json.dumps({"Sources": sources, "Fixtures": fixtures}, indent=2) + "\n", encoding="utf-8")
    build = subprocess.run(["dotnet", "build", str(root / "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj"),
                            "-c", "Release", "--no-restore", "--verbosity", "quiet"], cwd=root, capture_output=True)
    (stage / "build.log").write_bytes(build.stdout + build.stderr)
    if build.returncode:
        raise RuntimeError("Workbook validation harness build failed; see build.log.")
    host = root / "artifacts/managed/bin/ContextSuite.Pdf.ContractTests/Release/net10.0/ContextSuite.Pdf.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    (stage / "binaries.json").write_text(json.dumps(binaries, indent=2) + "\n", encoding="utf-8")
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        run = subprocess.run([str(host), "--office-workbook-evidence", str(retained), str(stage / "contracts")],
                             cwd=root, stdout=output, stderr=error)
    unchanged = all(digest(root / name) == value for name, value in sources.items()) and all(
        digest(Path(name)) == value for group in (fixtures, binaries) for name, value in group.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": run.returncode, "InputsUnchanged": unchanged}) + "\n")
    print((stage / "stdout.log").read_text(encoding="utf-8", errors="replace"), end="", flush=True)
    if run.returncode or not unchanged:
        raise RuntimeError("Workbook validation failed or inputs changed; retain the evidence.")
    result = json.loads((stage / "contracts/results.json").read_text(encoding="utf-8"))
    if not result.get("Passed") or len(result["Checks"]) != 105:
        raise RuntimeError("Missing complete workbook validation evidence.")


if __name__ == "__main__":
    main()
