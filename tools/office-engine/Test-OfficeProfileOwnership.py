"""Opt-in owned profile/grant tests; never installs or executes Office itself."""

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
    root = Path(__file__).resolve().parents[2]
    if os.name != "nt":
        raise RuntimeError("Windows x64 is required.")
    project = root / "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj"
    if not project.is_file():
        raise RuntimeError("The separate private checkout and its contract tests are required.")
    identity = uuid.uuid4().hex
    evidence = root / ".codex-temp/office-owner" / identity
    stage = root / ".codex-temp/office-isolation" / identity
    source = evidence / "source"
    source.mkdir(parents=True)
    for name in ["Probe.cpp", "CMakeLists.txt"]:
        (source / name).write_bytes((root / "tools/office-engine/ProcessProbe" / name).read_bytes())
    names = ["src/ContextSuite.Application/Infrastructure/OfficeSandboxOwner.cs",
             "src/ContextSuite.Application/Infrastructure/OfficeOwnershipJournal.cs",
             "src/ContextSuite.Application/Infrastructure/WorkerProcessJob.cs",
             "src/ContextSuite.Application/Infrastructure/PublicationFiles.cs",
             "proprietary/src/ContextSuite.Private/Office/OfficeJob.cs",
             "proprietary/src/ContextSuite.Private/Office/OfficeSandboxProcess.cs",
             "proprietary/tests/ContextSuite.Pdf.ContractTests/OfficeOwnerContracts.cs",
             "proprietary/tests/ContextSuite.Pdf.ContractTests/Program.cs",
             "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj"]
    inputs = {name: digest(root / name) for name in names}
    vswhere = Path(os.environ["ProgramFiles(x86)"]) / "Microsoft Visual Studio/Installer/vswhere.exe"
    visual_studio = subprocess.check_output([str(vswhere), "-latest", "-products", "*", "-requires",
        "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"], text=True).strip()
    cmake = Path(visual_studio) / "Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"
    build = evidence / "build"
    commands = [
        ["dotnet", "build", str(project), "-c", "Release", "--no-restore", "--verbosity", "quiet"],
        [str(cmake), "-S", str(source), "-B", str(build), "-G", "Visual Studio 18 2026", "-A", "x64", "-DCMAKE_SYSTEM_VERSION=10.0.26100.0"],
        [str(cmake), "--build", str(build), "--config", "Release", "--", "/m", "/p:ImportDirectoryBuildProps=false",
         "/p:ImportDirectoryBuildTargets=false", "/verbosity:minimal"]]
    for index, command in enumerate(commands):
        result = subprocess.run(command, cwd=root, capture_output=True)
        (evidence / f"build-{index}.log").write_bytes(result.stdout + result.stderr)
        if result.returncode:
            raise RuntimeError(f"Build failed before profile creation: {evidence}")
    if any(digest(root / name) != expected for name, expected in inputs.items()):
        raise RuntimeError("Sources changed during the build; no profile was created.")
    probe = build / "bin/Release/ContextSuite.Office.ProcessProbe.exe"
    managed = root / "artifacts/managed/bin/ContextSuite.Pdf.ContractTests/Release/net10.0/ContextSuite.Pdf.ContractTests.exe"
    receipt = {"stage": str(stage), "probe": str(probe), "probeSha256": digest(probe), "source": inputs,
               "nativeSource": {path.name: digest(path) for path in source.iterdir()},
               "managed": str(managed), "managedFiles": {path.name: digest(path) for path in managed.parent.iterdir() if path.is_file()}}
    (evidence / "build.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print("Office profile ownership evidence:", evidence, flush=True)
    result = subprocess.run([str(managed), "--office-profile-owner", str(probe), receipt["probeSha256"], str(stage)], cwd=root, capture_output=True)
    (evidence / "stdout.log").write_bytes(result.stdout)
    (evidence / "stderr.log").write_bytes(result.stderr)
    print((result.stdout + result.stderr).decode(errors="replace"), end="", flush=True)
    if result.returncode:
        raise RuntimeError(f"Ownership test failed. Inspect retained profile name and cleanup before retrying: {stage}")
    results = json.loads((stage / "results.json").read_text())
    if not results["Passed"]:
        raise RuntimeError("Missing successful ownership evidence.")
    print("Verified ownership checks:", len(results["Checks"]), flush=True)


if __name__ == "__main__":
    main()
