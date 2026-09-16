"""Test actual application startup after killed Office preparation and a following Word export."""

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
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--case", choices=("all", "preparation-before", "preparation-partial", "preparation-retiring", "preparation-locked"), default="all")
    parser.add_argument("--layout", choices=("full", "compact"), default="full")
    args = parser.parse_args()
    if not args.create_disposable_profiles:
        parser.error("The following Word export requires explicit disposable native profile authorization.")
    root = Path(__file__).resolve().parents[2]
    worker = args.worker.resolve(strict=True)
    fixture = args.fixture.resolve(strict=True)
    worker.relative_to(root / ".codex-temp")
    fixture.relative_to(root / ".codex-temp")
    stage = root / ".codex-temp/office-app-preparation" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("Application preparation run:", stage, flush=True)
    sources = {Path(__file__).resolve(), fixture}
    for directory in ("src", "tests/ContextSuite.Application.TestHost", "proprietary/src"):
        sources.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj", ".xaml")
                       and not {"bin", "obj"}.intersection(path.parts))
    sources.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Version.props", "global.json")
                   if (root / name).is_file())
    source_hashes = {str(path): digest(path) for path in sorted(sources)}
    build = subprocess.run(["dotnet", "build", str(root / "tests/ContextSuite.Application.TestHost/ContextSuite.Application.TestHost.csproj"),
                            "-c", "Release", "--no-restore", "--verbosity", "quiet"], cwd=root, capture_output=True)
    (stage / "build.log").write_bytes(build.stdout + build.stderr)
    if build.returncode:
        raise RuntimeError("Application test-host build failed before launch.")
    host = root / "artifacts/managed/bin/ContextSuite.Application.TestHost/Release/net10.0-windows/ContextSuite.Application.TestHost.exe"
    binaries = {str(path): digest(path) for path in sorted(set(worker.parent.rglob("*")).union(host.parent.iterdir())) if path.is_file()}
    inputs = {"Sources": source_hashes, "Binaries": binaries, "Worker": str(worker), "Fixture": str(fixture)}
    (stage / "inputs.json").write_text(json.dumps(inputs, indent=2))
    assert all(digest(Path(name)) == expected for name, expected in source_hashes.items()), "Source drift before launch"
    print("Worker and app hashes recorded; launching isolated lifecycle cases.", flush=True)
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        result = subprocess.run([str(host), "--office-preparation-lifecycle", str(worker), str(fixture), args.case, args.layout], cwd=root, stdout=output, stderr=error)
    unchanged = all(digest(Path(name)) == expected for mapping in (source_hashes, binaries) for name, expected in mapping.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": result.returncode, "InputsUnchanged": unchanged}))
    print((stage / "stdout.log").read_text(errors="replace"), end="", flush=True)
    if result.returncode or not unchanged:
        print((stage / "stderr.log").read_text(errors="replace")[-4000:], flush=True)
        raise RuntimeError("Lifecycle failed; inspect retained context/profile evidence before retrying: " + str(stage))


if __name__ == "__main__":
    main()
