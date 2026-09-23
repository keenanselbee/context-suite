"""Opt-in production Office launcher limits; a tiny authored helper, no Office engine rebuild."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--create-disposable-profile", action="store_true")
    args = parser.parse_args()
    if not args.create_disposable_profile:
        parser.error("Explicit disposable profile authorization is required.")
    if os.name != "nt":
        raise RuntimeError("Windows x64 is required.")
    root = Path(__file__).resolve().parents[2]
    identity = uuid.uuid4().hex
    evidence = root / ".codex-temp/office-resources" / identity
    stage = root / ".codex-temp/office-isolation" / identity
    source = evidence / "source"
    source.mkdir(parents=True)
    print("Office resource evidence:", evidence, flush=True)
    for name in ("Probe.cpp", "CMakeLists.txt"):
        (source / name).write_bytes((root / "tools/office-engine/ResourceProbe" / name).read_bytes())
    inputs = {Path(__file__).resolve(), *(root / "tools/office-engine/ResourceProbe").iterdir()}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/Shared", "proprietary/src/ContextSuite.Private",
                      "proprietary/tests/ContextSuite.Pdf.ContractTests", "proprietary/tests/ContextSuite.Image.ContractTests"):
        inputs.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj")
                      and not {"bin", "obj"}.intersection(path.parts))
    inputs.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                                         "Version.props", "global.json", "tools/Require-Private.targets") if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(inputs)}
    (evidence / "inputs.json").write_text(json.dumps(sources, indent=2) + "\n", encoding="utf-8")
    vswhere = Path(os.environ["ProgramFiles(x86)"]) / "Microsoft Visual Studio/Installer/vswhere.exe"
    visual_studio = subprocess.check_output([str(vswhere), "-latest", "-products", "*", "-requires",
        "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"], text=True).strip()
    cmake = Path(visual_studio) / "Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"
    project = root / "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj"
    build = evidence / "build"
    commands = [
        ["dotnet", "build", str(project), "-c", "Release", "--no-restore", "--verbosity", "quiet"],
        [str(cmake), "-S", str(source), "-B", str(build), "-G", "Visual Studio 18 2026", "-A", "x64", "-DCMAKE_SYSTEM_VERSION=10.0.26100.0"],
        [str(cmake), "--build", str(build), "--config", "Release", "--", "/m:2", "/p:ImportDirectoryBuildProps=false",
         "/p:ImportDirectoryBuildTargets=false", "/verbosity:minimal"]]
    for index, command in enumerate(commands):
        result = subprocess.run(command, cwd=root, capture_output=True)
        (evidence / f"build-{index}.log").write_bytes(result.stdout + result.stderr)
        if result.returncode:
            raise RuntimeError(f"Build failed before profile creation: {evidence}")
    if any(digest(root / name) != expected for name, expected in sources.items()):
        raise RuntimeError("Sources changed during build; no profile was created.")
    probe = build / "bin/Release/ContextSuite.Office.ResourceProbe.exe"
    host = root / "artifacts/managed/bin/ContextSuite.Pdf.ContractTests/Release/net10.0/ContextSuite.Pdf.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    binaries[str(probe)] = digest(probe)
    (evidence / "build.json").write_text(json.dumps({"Stage": str(stage), "Binaries": binaries, "Commands": commands}, indent=2) + "\n", encoding="utf-8")
    with (evidence / "stdout.log").open("wb") as output, (evidence / "stderr.log").open("wb") as error:
        run = subprocess.run([str(host), "--office-resources", str(probe), binaries[str(probe)], str(stage)],
                             cwd=root, stdout=output, stderr=error)
    unchanged = all(digest(root / name) == value for name, value in sources.items()) and all(
        digest(Path(name)) == value for name, value in binaries.items())
    (evidence / "exit.json").write_text(json.dumps({"ExitCode": run.returncode, "InputsUnchanged": unchanged}) + "\n")
    print((evidence / "stdout.log").read_text(encoding="utf-8", errors="replace"), end="", flush=True)
    if run.returncode or not unchanged:
        raise RuntimeError("Resource checks failed; inspect retained results and profile cleanup before retrying.")
    result = json.loads((stage / "results.json").read_text(encoding="utf-8"))
    expected = ["initial-control", *(name for mode in ("memory", "aggregate", "process", "stdout", "stderr") for name in (mode, "after-" + mode))]
    if not result.get("Passed") or [case["Name"] for case in result["Cases"]] != expected:
        raise RuntimeError("Missing complete resource enforcement evidence.")


if __name__ == "__main__":
    main()
