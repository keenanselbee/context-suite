"""Validate retained Office exports with pinned PDF readers; no Office profile or export is created."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import shutil
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pdf-engine", type=Path, required=True)
    parser.add_argument("--pdf-renderer", type=Path, required=True)
    parser.add_argument("--exports", type=Path, required=True)
    parser.add_argument("--worker", action="store_true", help="Build a matching scratch worker and test actual client dispatch and cancellation.")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    exports = args.exports.resolve(strict=True)
    exports.relative_to(root / ".codex-temp")
    report = json.loads(exports.read_text(encoding="utf-8-sig"))
    if not report.get("Passed") or len(report.get("Reports", [])) != 3:
        raise RuntimeError("Expected three passed retained Office exports.")
    fixtures = {str(exports): digest(exports)}
    for row in report["Reports"]:
        work = row["Work"]
        context = Path(work["DirectoryPath"]).resolve(strict=True)
        context.relative_to(root / ".codex-temp")
        for file, expected in ((context / "input" / ("source." + work["Format"]), work["SourceSha256"]),
                               (context / "output/candidate.pdf", row["Candidate"]["Completion"]["OutputSha256"])):
            actual = digest(file)
            if actual != expected.upper():
                raise RuntimeError("Retained export differs from its receipt.")
            fixtures[str(file)] = actual
    stage = root / ".codex-temp/office-pdf" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("Office PDF validation evidence:", stage, flush=True)
    inputs = {Path(__file__).resolve()}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/ContextSuite.Worker", "src/Shared", "proprietary/src/ContextSuite.Private",
                      "proprietary/tests/ContextSuite.Pdf.ContractTests", "proprietary/tests/ContextSuite.Image.ContractTests"):
        inputs.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj")
                      and not {"bin", "obj"}.intersection(path.parts))
    inputs.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Version.props", "global.json", "tools/Require-Private.targets")
                  if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(inputs)}
    engines = {str(path): digest(path) for folder in (args.pdf_engine.resolve(strict=True), args.pdf_renderer.resolve(strict=True))
               for path in folder.iterdir() if path.is_file()}
    (stage / "inputs.json").write_text(json.dumps({"Sources": sources, "Fixtures": fixtures, "Engines": engines}, indent=2), encoding="utf-8")
    build = subprocess.run(["dotnet", "build", str(root / "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj"),
                            "-c", "Release", "--no-restore", "--verbosity", "quiet"], cwd=root, capture_output=True)
    (stage / "build.log").write_bytes(build.stdout + build.stderr)
    if build.returncode:
        raise RuntimeError("Validator harness build failed; see build.log.")
    worker = None
    if args.worker:
        build = subprocess.run(["dotnet", "build", str(root / "src/ContextSuite.Worker/ContextSuite.Worker.csproj"),
                                "-c", "Release", "--no-restore", "--verbosity", "quiet"], cwd=root, capture_output=True)
        (stage / "worker-build.log").write_bytes(build.stdout + build.stderr)
        if build.returncode:
            raise RuntimeError("Matching scratch worker build failed; see worker-build.log.")
        directory = stage / "worker"
        directory.mkdir()
        for path in (root / "artifacts/managed/bin/ContextSuite.Worker/Release/net10.0-windows").iterdir():
            if path.is_file():
                shutil.copy2(path, directory / path.name)
        shutil.copytree(args.pdf_engine, directory / "pdf-engine")
        shutil.copytree(args.pdf_renderer, directory / "pdf-renderer")
        worker = directory / "ContextSuite.Worker.exe"
    host = root / "artifacts/managed/bin/ContextSuite.Pdf.ContractTests/Release/net10.0/ContextSuite.Pdf.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    if worker is not None:
        binaries.update({str(path): digest(path) for path in worker.parent.rglob("*") if path.is_file()})
    (stage / "binaries.json").write_text(json.dumps(binaries, indent=2), encoding="utf-8")
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        command = [str(host), "--office-pdf-worker-validation", str(worker)] if worker is not None else [
            str(host), "--office-pdf-validation", str(args.pdf_engine.resolve()), str(args.pdf_renderer.resolve())]
        run = subprocess.run(command + [str(exports), str(stage / "contracts")], cwd=root, stdout=output, stderr=error)
    unchanged = all(digest(root / name) == expected for name, expected in sources.items()) and all(
        digest(Path(name)) == expected for group in (fixtures, engines, binaries) for name, expected in group.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": run.returncode, "InputsUnchanged": unchanged}), encoding="utf-8")
    print((stage / "stdout.log").read_text(encoding="utf-8", errors="replace"), end="", flush=True)
    if run.returncode or not unchanged:
        raise RuntimeError("Office PDF validation failed or inputs changed; inspect retained evidence.")
    result = json.loads((stage / "contracts/results.json").read_text(encoding="utf-8"))
    if not result.get("Passed") or not result.get("Checks"):
        raise RuntimeError("Missing complete validation result.")


if __name__ == "__main__":
    main()
