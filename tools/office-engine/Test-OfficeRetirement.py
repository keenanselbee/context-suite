"""Verify restart retirement with disposable native profiles, without an Office renderer."""

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
    parser.add_argument("--fixture", type=Path, required=True)
    args = parser.parse_args()
    if not args.create_disposable_profiles:
        parser.error("Explicit disposable native profile authorization is required.")
    root = Path(__file__).resolve().parents[2]
    fixture = args.fixture.resolve(strict=True)
    fixture.relative_to(root / ".codex-temp")
    stage = root / ".codex-temp/office-retirement" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("Office restart retirement evidence:", stage, flush=True)
    paths = {Path(__file__).resolve()}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/Shared", "tests/ContextSuite.Core.ContractTests"):
        paths.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj", ".xaml")
                     and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Version.props", "global.json")
                 if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(paths)}
    fixture_hash = digest(fixture)
    build = subprocess.run(["dotnet", "build", str(root / "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj"),
                            "-c", "Release", "--no-restore", "--verbosity", "quiet"], cwd=root, capture_output=True)
    (stage / "build.log").write_bytes(build.stdout + build.stderr)
    if build.returncode:
        raise RuntimeError("Build failed before native profile creation.")
    host = root / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    (stage / "inputs.json").write_text(json.dumps({"Sources": sources, "Binaries": binaries, "Fixture": str(fixture), "FixtureSha256": fixture_hash}, indent=2))
    if any(digest(root / name) != expected for name, expected in sources.items()):
        raise RuntimeError("Source drift before native profile tests.")
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        result = subprocess.run([str(host), "--office-retirement-native", str(stage), str(fixture)], cwd=root, stdout=output, stderr=error)
    unchanged = digest(fixture) == fixture_hash and all(digest(root / name) == expected for name, expected in sources.items()) and all(
        digest(Path(name)) == expected for name, expected in binaries.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": result.returncode, "InputsUnchanged": unchanged}))
    print((stage / "stdout.log").read_text(errors="replace"), end="", flush=True)
    if result.returncode or not unchanged:
        raise RuntimeError("Retirement failed; inspect the retained profile/context evidence before retrying: " + str(stage))
    report = json.loads((stage / "office-retirement-recovery.json").read_text())
    if not report["Passed"] or len(report["Profiles"]) != 7 or not all(item["Native"] and item["Removed"] and item["ContextRetired"] for item in report["Profiles"]):
        raise RuntimeError("Missing complete native retirement evidence.")


if __name__ == "__main__":
    main()
