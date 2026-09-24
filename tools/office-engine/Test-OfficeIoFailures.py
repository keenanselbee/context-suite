"""Exercise real Office output-creation denial and following exports with disposable profiles."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--create-disposable-profiles", action="store_true")
    parser.add_argument("--office-engine", type=Path, required=True)
    parser.add_argument("--pdf-engine", type=Path, required=True)
    parser.add_argument("--pdf-renderer", type=Path, required=True)
    parser.add_argument("--fixtures", type=Path, required=True)
    args = parser.parse_args()
    if not args.create_disposable_profiles:
        parser.error("Explicit disposable profile authorization is required.")
    root = Path(__file__).resolve().parents[2]
    fixtures = args.fixtures.resolve(strict=True)
    fixtures.relative_to(root / ".codex-temp")
    originals = {}
    for extension in ("docx", "xlsx", "pptx"):
        selected = list(fixtures.glob("*." + extension))
        if len(selected) != 1:
            raise ValueError("Provide exactly one ordinary fixture for each Office family.")
        originals[str(selected[0])] = digest(selected[0])
    engines = {}
    for folder in (args.office_engine, args.pdf_engine, args.pdf_renderer):
        folder.resolve(strict=True).relative_to(root / ".codex-temp")
        engines.update({str(path.resolve()): digest(path) for path in folder.rglob("*") if path.is_file()})
    stage = root / ".codex-temp/office-io" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("Office I/O evidence:", stage, flush=True)
    paths = {Path(__file__).resolve()}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/ContextSuite.Worker", "src/Shared",
                      "proprietary/src/ContextSuite.Private", "proprietary/tests/ContextSuite.Pdf.ContractTests",
                      "proprietary/tests/ContextSuite.Image.ContractTests"):
        paths.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj")
                     and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
        "Version.props", "global.json", "tools/Require-Private.targets", "tools/office-engine/payload-candidate.json",
        "tools/office-engine/source-runtime-candidate.json") if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(paths)}
    inputs = {"Sources": sources, "Fixtures": originals, "Engines": engines}
    (stage / "inputs.json").write_text(json.dumps(inputs, indent=2) + "\n", encoding="utf-8")
    for label, project in (("contracts", "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj"),
                           ("worker", "src/ContextSuite.Worker/ContextSuite.Worker.csproj")):
        build = subprocess.run(["dotnet", "build", str(root / project), "-c", "Release", "--no-restore",
                                "--verbosity", "quiet", "-maxcpucount:1"], cwd=root, capture_output=True)
        (stage / (label + "-build.log")).write_bytes(build.stdout + build.stderr)
        if build.returncode:
            raise RuntimeError("Build failed before profile creation: " + label)
    directory = stage / "worker"
    directory.mkdir()
    for path in (root / "artifacts/managed/bin/ContextSuite.Worker/Release/net10.0-windows").iterdir():
        if path.is_file():
            shutil.copy2(path, directory / path.name)
    for name, folder in (("office-engine", args.office_engine), ("pdf-engine", args.pdf_engine), ("pdf-renderer", args.pdf_renderer)):
        shutil.copytree(folder, directory / name)
        if any(digest(path) != digest(directory / name / path.relative_to(folder)) for path in folder.rglob("*") if path.is_file()):
            raise RuntimeError("Engine copy differs from its selected source.")
    host = root / "artifacts/managed/bin/ContextSuite.Pdf.ContractTests/Release/net10.0/ContextSuite.Pdf.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    binaries.update({str(path): digest(path) for path in directory.rglob("*") if path.is_file()})
    inputs["Binaries"] = binaries
    (stage / "inputs.json").write_text(json.dumps(inputs, indent=2) + "\n", encoding="utf-8")
    if any(digest(root / name) != value for name, value in sources.items()) or any(
            digest(Path(name)) != value for group in (originals, engines) for name, value in group.items()):
        raise RuntimeError("Inputs changed before profile creation.")
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        run = subprocess.run([str(host), "--office-io", str(directory / "ContextSuite.Worker.exe"), str(fixtures), str(stage / "contracts")],
                             cwd=root, stdout=output, stderr=error)
    unchanged = all(digest(root / name) == value for name, value in sources.items()) and all(
        digest(Path(name)) == value for group in (originals, engines, binaries) for name, value in group.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": run.returncode, "InputsUnchanged": unchanged}) + "\n")
    print((stage / "stdout.log").read_text(encoding="utf-8", errors="replace"), end="", flush=True)
    if run.returncode or not unchanged:
        raise RuntimeError("Office I/O checks failed; inspect retained evidence and profile cleanup before retrying.")
    result = json.loads((stage / "contracts/results.json").read_text(encoding="utf-8"))
    if not result.get("Passed") or len(result["Profiles"]) != 9 or len(result["Failures"]) != 6 or len(result["Reports"]) != 3:
        raise RuntimeError("Missing complete Office I/O evidence.")


if __name__ == "__main__":
    main()
