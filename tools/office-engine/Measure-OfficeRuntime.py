"""Measure pinned runtime verification without launching Office or creating Windows profiles."""

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
    parser.add_argument("--package", type=Path, required=True, help="office-engine directory in a retained scratch worker.")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    package = args.package.resolve(strict=True)
    package.relative_to(root / ".codex-temp/office-execution")
    stage = root / ".codex-temp/office-runtime" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("Office runtime timing evidence:", stage, flush=True)
    paths = {Path(__file__).resolve(), root / "proprietary/tests/ContextSuite.Image.ContractTests/PngFixture.cs"}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/Shared",
                      "proprietary/src/ContextSuite.Private", "proprietary/tests/ContextSuite.Pdf.ContractTests"):
        paths.update(path for path in (root / directory).rglob("*") if
                     path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                                         "Version.props", "global.json", "tools/Require-Private.targets") if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(paths)}
    project = root / "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj"
    build = subprocess.run(["dotnet", "build", str(project), "-c", "Release", "--no-restore", "--verbosity", "quiet"],
                           cwd=root, capture_output=True)
    (stage / "build.log").write_bytes(build.stdout + build.stderr)
    if build.returncode:
        raise RuntimeError("Runtime timing build failed: " + str(stage))
    host = root / "artifacts/managed/bin/ContextSuite.Pdf.ContractTests/Release/net10.0/ContextSuite.Pdf.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    binaries.update({str(package / name): digest(package / name) for name in ("ContextSuite.OfficeHost.exe", "runtime-files.txt")})
    if any(digest(root / name) != expected for name, expected in sources.items()):
        raise RuntimeError("Timing sources changed during build.")
    (stage / "inputs.json").write_text(json.dumps({"Sources": sources, "Binaries": binaries}, indent=2))
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        run = subprocess.run([str(host), "--office-runtime-timing", str(package), str(stage / "contracts")],
                             cwd=root, stdout=output, stderr=error)
    unchanged = all(digest(root / name) == expected for name, expected in sources.items()) and all(
        digest(Path(name)) == expected for name, expected in binaries.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": run.returncode, "InputsUnchanged": unchanged}))
    print((stage / "stdout.log").read_text(errors="replace"), end="", flush=True)
    if run.returncode or not unchanged:
        raise RuntimeError("Runtime timing failed; inspect retained evidence: " + str(stage))
    report = json.loads((stage / "contracts/results.json").read_text())
    if not report["Passed"] or report["Files"] != 19332 or report["Bytes"] != 1517294910:
        raise RuntimeError("Incomplete runtime timing evidence.")


if __name__ == "__main__":
    main()
