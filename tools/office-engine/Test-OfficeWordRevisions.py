"""Export thirteen authored Word revision controls through the actual application command."""

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
    parser.add_argument("--create-disposable-profiles", action="store_true")
    parser.add_argument("--worker", type=Path, required=True)
    args = parser.parse_args()
    if not args.create_disposable_profiles:
        parser.error("Explicit disposable native-profile authorization is required.")
    root = Path(__file__).resolve().parents[2]
    worker = args.worker.resolve(strict=True)
    worker.relative_to(root / ".codex-temp/office-execution")
    stage = root / ".codex-temp/office-execution" / uuid.uuid4().hex
    stage.mkdir()
    print("Word revision application evidence:", stage, flush=True)
    files = {Path(__file__).resolve(), root / "tools/office-engine/Probe/WordRevisionFixtures.cs",
             root / "tools/office-engine/Probe/WordRevisionStructureFixtures.cs"}
    for directory in ("src", "proprietary/src", "tests/ContextSuite.Core.ContractTests"):
        files.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj", ".xaml")
                     and not {"bin", "obj"}.intersection(path.parts))
    files.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                                          "Version.props", "global.json", "tools/Require-Private.targets") if (root / name).is_file())
    sources = {str(path): digest(path) for path in sorted(files)}
    for label, project in (("worker", "src/ContextSuite.Worker/ContextSuite.Worker.csproj"),
                           ("contracts", "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj")):
        build = subprocess.run(["dotnet", "build", str(root / project), "-c", "Release", "--no-restore", "--verbosity", "quiet"],
                               cwd=root, capture_output=True)
        (stage / (label + "-build.log")).write_bytes(build.stdout + build.stderr)
        if build.returncode:
            raise RuntimeError("Build failed before native-profile creation: " + label)
    built = root / "artifacts/managed/bin/ContextSuite.Worker/Release/net10.0-windows"
    assert {path.name: digest(path) for path in worker.parent.iterdir() if path.is_file()} == {
        path.name: digest(path) for path in built.iterdir() if path.is_file()}, "Stage a worker matching the current build"
    host = root / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
    binaries = {str(path): digest(path) for path in sorted(set(worker.parent.rglob("*")).union(host.parent.iterdir())) if path.is_file()}
    (stage / "inputs.json").write_text(json.dumps({"Sources": sources, "Binaries": binaries, "Worker": str(worker)}, indent=2))
    assert all(digest(Path(name)) == expected for name, expected in sources.items()), "Source drift before launch"
    print("Starting thirteen disposable Word exports; independent PDF inspection follows separately.", flush=True)
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        result = subprocess.run([str(host), "--office-word-revisions", str(worker), str(stage / "contracts")],
                                cwd=root, stdout=output, stderr=error)
    unchanged = all(digest(Path(name)) == expected for group in (sources, binaries) for name, expected in group.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": result.returncode, "InputsUnchanged": unchanged}))
    print((stage / "stdout.log").read_text(errors="replace"), end="", flush=True)
    if result.returncode or not unchanged:
        print((stage / "stderr.log").read_text(errors="replace")[-4000:], flush=True)
        raise RuntimeError("Word revision execution failed; retain context/profile evidence: " + str(stage))
    report = json.loads((stage / "contracts/results.json").read_text())
    assert report["Passed"] and len(report["Profiles"]) == 13
    assert all(profile["Removed"] and profile["ContextRetired"] for profile in report["Profiles"])


if __name__ == "__main__":
    main()
