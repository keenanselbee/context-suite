"""Opt-in disposable Office export, independent validation and application copy publication."""

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
    parser.add_argument("--cleanup-only", action="store_true", help="Exercise one obstructed profile cleanup and retry without launching Office.")
    parser.add_argument("--office-engine", type=Path)
    parser.add_argument("--pdf-engine", type=Path)
    parser.add_argument("--pdf-renderer", type=Path)
    parser.add_argument("--retained-worker", type=Path, help="Reuse a scratch worker after matching its top-level files to the fresh build; readers verify their full pinned runtimes.")
    parser.add_argument("--fixtures", type=Path, required=True)
    args = parser.parse_args()
    if not args.create_disposable_profiles:
        parser.error("Explicit disposable Office profile authorization is required.")
    if args.retained_worker is None and not all((args.office_engine, args.pdf_engine, args.pdf_renderer)):
        parser.error("Choose all three engine directories or a retained scratch worker.")
    if args.retained_worker is not None and any((args.office_engine, args.pdf_engine, args.pdf_renderer)):
        parser.error("A retained worker uses its own engine directories; omit separate engines.")
    root = Path(__file__).resolve().parents[2]
    fixtures = args.fixtures.resolve(strict=True)
    fixtures.relative_to(root / ".codex-temp")
    originals = {str(path): digest(path) for path in fixtures.iterdir() if path.is_file()}
    stage = root / ".codex-temp/office-execution" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("Office execution evidence:", stage, flush=True)
    paths = {Path(__file__).resolve()}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/ContextSuite.Worker", "src/Shared",
                      "proprietary/src/ContextSuite.Private", "tests/ContextSuite.Core.ContractTests"):
        paths.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Version.props", "global.json", "tools/Require-Private.targets")
                 if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(paths)}
    for label, project in (("worker", "src/ContextSuite.Worker/ContextSuite.Worker.csproj"),
                           ("contracts", "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj")):
        build = subprocess.run(["dotnet", "build", str(root / project), "-c", "Release", "--no-restore", "--verbosity", "quiet"], cwd=root, capture_output=True)
        (stage / (label + "-build.log")).write_bytes(build.stdout + build.stderr)
        if build.returncode:
            raise RuntimeError("Build failed before profile creation: " + label)
    built = root / "artifacts/managed/bin/ContextSuite.Worker/Release/net10.0-windows"
    if args.retained_worker is not None:
        worker = args.retained_worker.resolve(strict=True)
        worker.relative_to(root / ".codex-temp/office-execution")
        if worker.name != "ContextSuite.Worker.exe":
            raise RuntimeError("Unexpected retained worker name.")
        worker_root = worker.parent
        actual = {path.name: digest(path) for path in worker_root.iterdir() if path.is_file()}
        expected = {path.name: digest(path) for path in built.iterdir() if path.is_file()}
        if actual != expected:
            raise RuntimeError("Retained worker differs from the current build; prepare a fresh worker.")
    else:
        worker_root = stage / "worker"
        worker_root.mkdir()
        for path in built.iterdir():
            if path.is_file():
                shutil.copy2(path, worker_root / path.name)
    engines = {}
    for name, folder in (("office-engine", args.office_engine), ("pdf-engine", args.pdf_engine), ("pdf-renderer", args.pdf_renderer)):
        if args.retained_worker is not None:
            continue
        folder = folder.resolve(strict=True)
        folder.relative_to(root)
        print("Copying isolated", name, flush=True)
        shutil.copytree(folder, worker_root / name)
        for path in folder.rglob("*"):
            if path.is_file():
                expected = digest(path)
                if digest(worker_root / name / path.relative_to(folder)) != expected:
                    raise RuntimeError("Scratch engine copy differs from selected source.")
                engines[str(path)] = expected
    host = root / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    binaries.update({str(path): digest(path) for path in worker_root.rglob("*") if path.is_file()})
    receipt = {"Sources": sources, "Fixtures": originals, "Engines": engines, "Binaries": binaries}
    (stage / "inputs.json").write_text(json.dumps(receipt, indent=2), encoding="utf-8")
    if any(digest(root / name) != expected for name, expected in sources.items()):
        raise RuntimeError("Source drift before execution.")
    print("Starting one disposable profile cleanup/retry check." if args.cleanup_only else
          "Starting five disposable Office exports and application publication checks.", flush=True)
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        run = subprocess.run([str(host), "--office-execution-cleanup" if args.cleanup_only else "--office-execution", str(worker_root / "ContextSuite.Worker.exe"), str(fixtures), str(stage / "contracts")],
                             cwd=root, stdout=output, stderr=error)
    unchanged = all(digest(root / name) == expected for name, expected in sources.items()) and all(
        digest(Path(name)) == expected for group in (originals, engines, binaries) for name, expected in group.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": run.returncode, "InputsUnchanged": unchanged}), encoding="utf-8")
    print((stage / "stdout.log").read_text(encoding="utf-8", errors="replace"), end="", flush=True)
    if run.returncode or not unchanged:
        raise RuntimeError("Office execution failed; inspect retained context journals and profile cleanup before retrying: " + str(stage))
    report = json.loads((stage / "contracts/results.json").read_text(encoding="utf-8"))
    if not report.get("Passed") or len(report.get("Profiles", [])) != (1 if args.cleanup_only else 5) or not all(profile["Removed"] for profile in report["Profiles"]):
        raise RuntimeError("Missing complete execution and cleanup evidence.")


if __name__ == "__main__":
    main()
